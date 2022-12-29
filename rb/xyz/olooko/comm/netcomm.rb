require "socket"

class NetSocket
    def available
        return @socket != nil
    end
    def localAddress
        @localAddress    
    end
    def protocolType
        @protocol           
    end
    def initialize(socket, protocolType)
        @data = NetSocketData.new()
        @protocol = protocolType
        @socket = socket
        @connected = false
        @result = NetSocketDataManipulationResult::NoData
        @localAddress = NetSocketAddress.new("0.0.0.0", 0)
        if available
            address = @socket.local_address()
            @localAddress = NetSocketAddress.new(address.ip_address, address.ip_port)
        end
    end
    def close()
        if available
            @socket.close()
        end
    end
    def setReceivedCallback(callback)
        if available
            Thread.new {
                receiveProc(callback)
            }
        end
    end   
    protected
    def getConnected()
        @connected
    end
    def setConnected(connected)
        @connected = connected
    end    
    def _send(data, address)
        if available  
            sendProc(data, address, 0)
        end
    end      
    private
    def sendProc(data, address, bytesTransferred)
        length = 0
        if @protocol == NetSocketProtocolType::Tcp
            length = @socket.send(data.bytes[bytesTransferred..-1], 0)
            @connected = length > 0 ? true : false
        elsif @protocol == NetSocketProtocolType::Udp
            length = @socket.send(data.bytes[bytesTransferred..-1], 0, Socket.pack_sockaddr_in(address.port, address.host))
        end
        if length > 0
            bytesTransferred += length
            if bytesTransferred < data.length
                sendProc(data, address, bytesTransferred) 
            end
        end
    end     
    def receiveProc(callback)
        buffer = nil  
        address = nil            
        while true
            if @protocol == NetSocketProtocolType::Tcp
                buffer = @socket.recv(4096)
                if buffer.length > 0
                    @connected = true
                else
                    @connected = false
                end
                address = @socket.remote_address()                
            elsif @protocol == NetSocketProtocolType::Udp
                data = @socket.recvfrom(4096, 0)
                buffer = data[0]
                address = data[1]
            end
            remoteAddress = NetSocketAddress.new(address.ip_address, address.ip_port)
            if buffer.length > 0
                @data.append(buffer)
                while true
                    @result = @data.manipulate()
                    if @result == NetSocketDataManipulationResult::Completed
                        callback.call(self, NetSocketReceivedData.new(@data.command, @data.args, NetSocketReceivedDataResult::Completed, remoteAddress))
                        next
                    elsif @result == NetSocketDataManipulationResult::ParsingError
                        callback.call(self, NetSocketReceivedData.new(0X00, [], NetSocketReceivedDataResult::ParsingError, remoteAddress))
                        return
                    elsif @result == NetSocketDataManipulationResult::InProgress
                        Thread.new {
                            sleep 15
                            if @result == NetSocketDataManipulationResult::InProgress
                                callback.call(self, NetSocketReceivedData.new(0X00, [], NetSocketReceivedDataResult::Interrupted, remoteAddress))   
                            end   
                        }
                        break
                    elsif @result == NetSocketDataManipulationResult::NoData
                        break
                    end
                end
                next
            else
                callback.call(self, NetSocketReceivedData.new(0x00, [], NetSocketReceivedDataResult::Closed, remoteAddress))
                break
            end
        end
    end      
end

class NetSocketAddress
    def host
        @host
    end
    def port
        @port
    end
    def initialize(host, port)
        @host = host
        @port = port
    end
    def to_s
        return "%s:%d" % [@host, @port]
    end
end

class NetSocketData
    def args
        @args
    end
    def command
        @command
    end
    def initialize()
        @command = 0x00
        @args = []         
        @data = ""
        @datapos = 0
        @checksum = 0x00
        @step = NetSocketDataParsingStep::SOH
        @textlen = 0  
    end
    def append(buffer)
        @data << buffer
    end
    def manipulate()
        while true
            datalen = @data.length - @datapos
            case @step
            when NetSocketDataParsingStep::SOH
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == 0x01                       
                        @datapos += 1
                        @step = NetSocketDataParsingStep::OTL
                        next
                    else 
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            when NetSocketDataParsingStep::OTL
                if datalen > 0
                    if [0x11, 0x12, 0x14].include?(@data[@datapos].unpack("C")[0])
                        a = getArgLength(datalen)
                        sz = a.size
                        @textlen = a.argLength
                        if @textlen >= 0
                            @datapos += 1 + sz
                            @step = NetSocketDataParsingStep::STX
                            next
                        end
                    else 
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            when NetSocketDataParsingStep::STX
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == 0x02
                        @datapos += 1
                        @step = NetSocketDataParsingStep::ETX
                        next
                    else
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            when NetSocketDataParsingStep::ETX
                if datalen > @textlen
                    if @data[@datapos + @textlen].unpack("C")[0] == 0x03
                        textfpos = @datapos
                        @command = @data[textfpos].unpack("C")[0]
                        @args = []
                        @datapos += 1                            
                        while @datapos < @textlen + textfpos 
                            fmt = ""                           
                            sz = 0
                            argH = @data[@datapos].unpack("C")[0]                                
                            if [0x31, 0x32, 0x34, 0x38].include?(argH)
                                sz = argH & 0x0F
                                if sz == 1 
                                    fmt = "c"
                                elsif sz == 2 
                                    fmt = "s>"
                                elsif sz == 4 
                                    fmt = "l>"
                                elsif sz == 8 
                                    fmt = "q>"
                                end                                    
                                @args.append(@data[(@datapos + 1)..(@datapos + sz)].unpack(fmt)[0])
                            elsif [0x54, 0x58].include?(argH)
                                sz = argH & 0x0F
                                if sz == 4
                                    fmt = "g"
                                elsif sz == 8
                                    fmt = "G"
                                end
                                @args.append(@data[(@datapos + 1)..(@datapos + sz)].unpack(fmt)[0])
                            elsif [0x71].include?(argH)
                                sz = 1
                                bool = false
                                if @data[@datapos + 1].unpack("C")[0] == 1
                                    bool = true
                                end
                                @args.append(bool)
                            elsif [0x91, 0x92, 0x94].include?(argH)
                                a = getArgLength(datalen)
                                sz = a.size
                                argL = a.argLength
                                @args.append(@data[(@datapos + 1 + sz)..(@datapos + sz + argL)])
                                @datapos += argL
                            elsif [0xB1, 0xB2, 0xB4].include?(argH)
                                a = getArgLength(datalen)
                                sz = a.size
                                argL = a.argLength
                                @args.append(ByteArray.new(@data[(@datapos + 1 + sz)..(@datapos + sz + argL)].unpack("C*")))
                                @datapos += argL
                            else
                                return NetSocketDataManipulationResult::ParsingError
                            end
                            @datapos += 1 + sz 
                        end  
                        @checksum = 0x00
                        for b in @data[textfpos..(textfpos + @textlen - 1)].unpack("C*")
                            @checksum ^= b 
                        end
                        @datapos += 1
                        @step = NetSocketDataParsingStep::CHK
                        next                                                        
                    else
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            when NetSocketDataParsingStep::CHK                       
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == @checksum
                        @datapos += 1
                        @step = NetSocketDataParsingStep::EOT
                        next
                    else
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            when NetSocketDataParsingStep::EOT 
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == 0x04
                        @datapos += 1
                        @data = @data[@datapos..-1]
                        @datapos = 0
                        @checksum = 0x00
                        @step = NetSocketDataParsingStep::SOH
                        @textlen = 0  
                        return NetSocketDataManipulationResult::Completed
                    else
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            end
            if @data.length == 0
                return NetSocketDataManipulationResult::NoData
            end       
            return NetSocketDataManipulationResult::InProgress
        end
    end
    private
    def getArgLength(datalen)
        sz = @data[@datapos].unpack("C")[0] & 0x0F
        fmt = ""
        argL = -1
        if sz == 1 
            fmt = "c"
        elsif sz == 2 
            fmt = "s>"
        elsif sz == 4 
            fmt = "l>"
        end
        if datalen > sz
            argL = @data[(@datapos + 1)..(@datapos + sz)].unpack(fmt)[0]
        end
        return NetSocketDataArgLength.new(sz, argL)               
    end    
end

class NetSocketDataArgLength
    def size
        @sz
    end
    def argLength
        @argL
    end
    def initialize(sz, argL)
        @sz = sz
        @argL = argL
    end
end

module NetSocketDataManipulationResult
    Completed = "Completed"
    InProgress = "InProgress"
    NoData = "NoData"
    ParsingError = "ParsingError"
end

module NetSocketDataParsingStep
    SOH = "SOH"
    OTL = "OTL"
    STX = "STX"
    ETX = "ETX"
    CHK = "CHK"
    EOT = "EOT"
end

module NetSocketProtocolType
    Tcp = "Tcp"
    Udp = "Udp"
end

class NetSocketReceivedData
    def args
        @args
    end
    def command
        @command
    end
    def result
        @result
    end
    def remoteAddress
        @remoteAddress
    end
    def initialize(command, args, result, address)
        @command = command
        @args = args  
        @result = result
        @remoteAddress = address
    end
end

module NetSocketReceivedDataResult
    Closed = "Closed"    
    Completed = "Completed"
    Interrupted = "Interrupted"
    ParsingError = "ParsingError"
end

class NetSocketSendData
    ARG_MAXLEN = 0x7FFFFF - 5
    TXT_MAXLEN = 0x7FFFFFFF - 10
    def args
        @args
    end
    def buildResult
        @result       
    end
    def bytes
        @bytes
    end
    def command
        @command
    end
    def length
        @bytes.length
    end
    def initialize(command, args)
        @result = NetSocketSendDataBuildResult::NoData
        if command < 0x00 || command > 0xFF
            @result = NetSocketSendDataBuildResult::CommandValueOverflowError
            return
        end
        @command = command
        @args = args
        text = [command].pack("C")
        for arg in args
            case arg.class.to_s
            when "Integer"
                if -128 <= arg && arg <= 127
                    text << [0x31].pack("C") << [arg].pack("c")
                elsif -32768 <= arg && arg <= 32767
                    text << [0x32].pack("C") << [arg].pack("s>")
                elsif -2147483648 <= arg && arg <= 2147483647
                    text << [0x34].pack("C") << [arg].pack("l>")
                else
                    text << [0x38].pack("C") << [arg].pack("q>")
                end
            when "Float"
                if arg.abs() <= 3.40282347e+38
                    text << [0x54].pack("C") << [arg].pack("g")
                else
                    text << [0x58].pack("C") << [arg].pack("G")
                end
            when "TrueClass"
                text << [0x71].pack("C") << [1].pack("c")
            when "FalseClass"
                text << [0x71].pack("C") << [0].pack("c")
            when "String"
                arg = arg.encode("UTF-8")
                argL = arg.length
                if argL <= ARG_MAXLEN
                    if argL <= 127
                        text << [0x91].pack("C") << [argL].pack("c")
                    elsif argL <= 32767
                        text << [0x92].pack("C") << [argL].pack("s>")
                    else
                        text << [0x94].pack("C") << [argL].pack("l>")
                    end
                    text << arg
                else
                    @result = NetSocketSendDataBuildResult::StringLengthOverflowError
                    return
                end
            when "ByteArray"
                argL = arg.length
                if argL <= ARG_MAXLEN
                    if argL <= 127
                        text << [0xB1].pack("C") << [argL].pack("c")
                    elsif argL <= 32767
                        text << [0xB2].pack("C") << [argL].pack("s>")
                    else
                        text << [0xB4].pack("C") << [argL].pack("l>") 
                    end    
                    text << arg.data
                else
                    @result = NetSocketSendDataBuildResult::ByteArrayLengthOverflowError
                    return
                end
            else
                @result = NetSocketSendDataBuildResult::DataTypeNotImplementedError
                return
            end
        end
        data = [0x01].pack("C")
        textlen = text.length
        if textlen <= TXT_MAXLEN
            if textlen <= 127
                data << [0x11].pack("C") << [textlen].pack("c")
            elsif textlen <= 32767
                data << [0x12].pack("C") << [textlen].pack("s>")
            else
                data << [0x14].pack("C") << [textlen].pack("l>")
            end
            data << [0x02].pack("C") << text << [0x03].pack("C")
            checksum = 0x00
            for b in text.unpack("C*")
                checksum ^= b
            end
            data << [checksum].pack("C") << [0x04].pack("C") 
        else
            @result = NetSocketSendDataBuildResult::DataTotalLengthOverflowError
            return
        end
        @bytes = data
        @result = NetSocketSendDataBuildResult::Successful
    end
end

module NetSocketSendDataBuildResult
    ByteArrayLengthOverflowError = "ByteArrayLengthOverflowError"
    CommandValueOverflowError = "CommandValueOverflowError"
    DataTotalLengthOverflowError = "DataTotalLengthOverflowError"
    DataTypeNotImplementedError = "DataTypeNotImplementedError"
    NoData = "NoData"
    StringLengthOverflowError = "StringLengthOverflowError" 
    Successful = "Successful"
end

class TcpServer
    def running
        return @server != nil
    end
    def initialize(s)
        @server = s
    end
    def close()
        @server.close()
        @server = nil
    end 
    def setAcceptCallback(callback)
        Thread.new {
            while running
                s = nil
                begin
                    s, addrinfo = @server.accept()
                rescue
                    #
                end
                callback.call(TcpSocket.new(s))
            end
        }
    end  
end

class TcpSocket < NetSocket
    def connected
        available && getConnected()   
    end
    def remoteAddress
        @remoteAddress
    end
    def initialize(s)
        super(s, NetSocketProtocolType::Tcp)
        setConnected(s != nil)
        @remoteAddress = NetSocketAddress.new("0.0.0.0", 0)
        if s != nil 
            address = s.remote_address()
            @remoteAddress = NetSocketAddress.new(address.ip_address, address.ip_port)
        end
    end
    def send(data)
        _send(data, nil)
    end
end
    
class UdpSocket < NetSocket
    def initialize(s)
        super(s, NetSocketProtocolType::Udp)
    end
    def send(data, address)
        _send(data, address)
    end
end               

def TcpConnect(address)
    s = Socket.new(Socket::PF_INET, Socket::SOCK_STREAM)
    begin
        s.connect(Socket.pack_sockaddr_in(address.port, address.host))
    rescue
        s = nil
    end
    return TcpSocket.new(s)
end

def TcpListen(address)
    s = Socket.new(Socket::PF_INET, Socket::SOCK_STREAM)
    begin
        s.bind(Socket.pack_sockaddr_in(address.port, address.host))
        s.listen(0)
    rescue
        s = nil
    end
    return TcpServer.new(s)
end

def UdpCast(address)
    s = Socket.new(Socket::PF_INET, Socket::SOCK_DGRAM)
    begin
        s.bind(Socket.pack_sockaddr_in(address.port, address.host))
    rescue
        s = nil
    end
    return UdpSocket.new(s)
end

