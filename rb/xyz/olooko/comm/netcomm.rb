require "socket"

class ByteArray
    def data
        @data
    end
    def length
        @data.length
    end
    def initialize(source)
        @data = source.pack("C*")
    end    
end

module NetSocketDataParsingStep
    SOH = "SOH"
    OTL = "OTL"
    STX = "STX"
    ETX = "ETX"
    CHK = "CHK"
    EOT = "EOT"
end

module NetSocketDataManipulationResult
    Completed = "Completed"
    InProgress = "InProgress"
    NoData = "NoData"
    ParsingError = "ParsingError"
end

class NetSocketDataArgLength
    def sz
        @sz
    end
    def argL
        @argL
    end
    def initialize(sz, argL)
        @sz = sz
        @argL = argL
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
            if @step == NetSocketDataParsingStep::SOH
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == 0x01                       
                        @datapos += 1
                        @step = NetSocketDataParsingStep::OTL
                        next
                    else 
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            elsif @step == NetSocketDataParsingStep::OTL
                if datalen > 0
                    if [0x11, 0x12, 0x14].include?(@data[@datapos].unpack("C")[0])
                        a = getArgLength(@data[@datapos..-1])
                        sz = a.sz
                        @textlen = a.argL
                        if @textlen >= 0
                            @datapos += 1 + sz
                            @step = NetSocketDataParsingStep::STX
                            next
                        end
                    else 
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            elsif @step == NetSocketDataParsingStep::STX
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == 0x02
                        @datapos += 1
                        @step = NetSocketDataParsingStep::ETX
                        next
                    else
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            elsif @step == NetSocketDataParsingStep::ETX
                if datalen > @textlen
                    if @data[@datapos + @textlen].unpack("C")[0] == 0x03
                        begin
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
                                    if @data[@datapos + 1] == 1
                                        bool = true
                                    end
                                    @args.append(bool)
                                elsif [0x91, 0x92, 0x94].include?(argH)
                                    a = getArgLength(@data[@datapos..-1])
                                    sz = a.sz
                                    argL = a.argL
                                    @args.append(@data[(@datapos + 1 + sz)..(@datapos + sz + argL)])
                                    @datapos += argL
                                elsif [0xB1, 0xB2, 0xB4].include?(argH)
                                    a = getArgLength(@data[@datapos..-1])
                                    sz = a.sz
                                    argL = a.argL
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
                        rescue 
                            return NetSocketDataManipulationResult::ParsingError
                        end
                    else
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            elsif @step == NetSocketDataParsingStep::CHK                       
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == @checksum
                        @datapos += 1
                        @step = NetSocketDataParsingStep::EOT
                        next
                    else
                        return NetSocketDataManipulationResult::ParsingError
                    end
                end
            elsif @step == NetSocketDataParsingStep::EOT 
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
    def getArgLength(data)
        sz = data[0].unpack("C")[0] & 0x0F
        fmt = ""
        argL = -1
        if sz == 1 
            fmt = "c"
        elsif sz == 2 
            fmt = "s>"
        elsif sz == 4 
            fmt = "l>"
        end
        if data.length > sz
            argL = data[1..sz].unpack(fmt)[0]
        end
        return NetSocketDataArgLength.new(sz, argL)               
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
end

module NetSocketProtocolType
    Tcp = "Tcp"
    Udp = "Udp"
end

module NetSocketReceivedDataResult
    Closed = "Closed"    
    Completed = "Completed"
    Interrupted = "Interrupted"
    ParsingError = "ParsingError"
end

class NetSocketReceivedData
    def command
        @command
    end
    def args
        @args
    end
    def result
        @result
    end
    def remoteAddress
        @remote_address
    end
    def initialize(command, args, result, address)
        @command = command
        @args = args  
        @result = result
        @remote_address = address
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

class NetSocketSendData
    ARG_MAXLEN = 0x7FFFFF - 5
    TXT_MAXLEN = 0x7FFFFFFF - 10
    def args
        @args
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
    def buildResult
        @result       
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
                    # 0011 0001
                    text << [0x31].pack("C") << [arg].pack("c")
                elsif -32768 <= arg && arg <= 32767
                    # 0011 0010
                    text << [0x32].pack("C") << [arg].pack("s>")
                elsif -2147483648 <= arg && arg <= 2147483647
                    # 0011 0100
                    text << [0x34].pack("C") << [arg].pack("l>")
                else
                    # 0011 1000
                    text << [0x38].pack("C") << [arg].pack("q>")
                end
            when "Float"
                if arg.abs() <= 3.40282347e+38
                    # 0101 0100
                    text << [0x54].pack("C") << [arg].pack("g")
                else
                    # 0101 1000
                    text << [0x58].pack("C") << [arg].pack("G")
                end
            when "TrueClass"
                # 0111 0001
                text << [0x71].pack("C") << [1].pack("c")
            when "FalseClass"
                # 0111 0001
                text << [0x71].pack("C") << [0].pack("c")
            when "String"
                arg = arg.encode("UTF-8")
                argL = arg.length
                if argL <= ARG_MAXLEN
                    if argL <= 127
                        # 1001 0001
                        text << [0x91].pack("C") << [argL].pack("c")
                    elsif argL <= 32767
                        # 1001 0010
                        text << [0x92].pack("C") << [argL].pack("s>")
                    else
                        # 1001 0100
                        text << [0x94].pack("C") << [argL].pack("l>")
                    end
                    text << arg
                else
                    @result = NetSocketSendDataBuildResult.StringLengthOverflowError
                    return
                end
            when "ByteArray"
                argL = arg.length
                if argL <= ARG_MAXLEN
                    if argL <= 127
                        # 1011 0001
                        text << [0xB1].pack("C") << [argL].pack("c")
                    elsif argL <= 32767
                        # 1011 0010
                        text << [0xB2].pack("C") << [argL].pack("s>")
                    else
                        # 1011 0100
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
        # start of header
        data = [0x01].pack("C")
        textlen = text.length
        if textlen <= TXT_MAXLEN
            if textlen <= 127
                # 0001 0001
                data << [0x11].pack("C") << [textlen].pack("c")
            elsif textlen <= 32767
                # 0001 0010
                data << [0x12].pack("C") << [textlen].pack("s>")
            else
                # 0001 0100
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

class NetSocket
    def available
        return @socket != nil
    end
    def localAddress
        @local_address    
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
        @local_address = NetSocketAddress.new("0.0.0.0", 0)
        if available
            address = @socket.local_address().getnameinfo()
            @local_address = NetSocketAddress.new(address[0], address[1])
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
                receive_proc(callback)
            }
        end
    end    
    private
    def send_proc(data, address, bytes_transferred)
        length = 0
        if @protocol == NetSocketProtocolType::Tcp
            length = @socket.send(data.bytes[bytes_transferred..-1], 0)
            if length > 0 
                @connected = true
            else
                @connected = false
            end
        elsif @protocol == NetSocketProtocolType::Udp
            length = @socket.send(data.bytes[bytes_transferred..-1], 0, Socket.pack_sockaddr_in(address.port, address.host))
        end
        if length > 0
            bytes_transferred += length
            if bytes_transferred < data.length
                send_proc(data, address, bytes_transferred) 
            end
        end
    end     
    def receive_proc(callback)
        while true
            remote_address = nil
            if @protocol == NetSocketProtocolType::Tcp
                buffer = @socket.recv(4096)
                if buffer.length > 0
                    @connected = true
                else
                    @connected = false
                end
                address = @socket.remote_address().getnameinfo()
                remote_address = NetSocketAddress.new(address[0], address[1])
            elsif @protocol == NetSocketProtocolType::Udp
                data = @socket.recvfrom(4096, 0)
                buffer = data[0]
                address = data[1].getnameinfo()
                remote_address = NetSocketAddress.new(address[0], address[1])
            end
            if buffer.length > 0
                @data.append(buffer)
                while true
                    @result = @data.manipulate()
                    if @result == NetSocketDataManipulationResult::Completed
                        callback.call(self, NetSocketReceivedData.new(@data.command, @data.args, NetSocketReceivedDataResult::Completed, remote_address))
                        next
                    elsif @result == NetSocketDataManipulationResult::ParsingError
                        callback.call(self, NetSocketReceivedData.new(0X00, [], NetSocketReceivedDataResult::ParsingError, remote_address))
                        return
                    elsif @result == NetSocketDataManipulationResult::InProgress
                        Thread.new {
                            sleep 15
                            if @result == NetSocketDataManipulationResult::InProgress
                                callback.call(self, NetSocketReceivedData.new(0X00, [], NetSocketReceivedDataResult::Interrupted, remote_address))   
                            end   
                        }
                        break
                    elsif @result == NetSocketDataManipulationResult::NoData
                        break
                    end
                end
                next
            else
                callback.call(self, NetSocketReceivedData.new(0x00, [], NetSocketReceivedDataResult::Closed, remote_address))
                break
            end
        end
    end
    protected
    def getConnected()
        @connected
    end
    def setConnected(connected)
        @connected = connected
    end    
    def p_send(data, address)
        if available  
            send_proc(data, address, 0)
        end
    end       
end

class TcpServer
    def started
        return @server != nil
    end
    def initialize(s)
        @server = s
    end
    def setAcceptCallback(callback)
        Thread.new {
            while @server != nil
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
    def close()
        @server.close()
        @server = nil
    end    
end

class TcpSocket < NetSocket
    def connected
        available && getConnected()   
    end
    def remoteAddress
        @remote_address
    end
    def initialize(s)
        super(s, NetSocketProtocolType::Tcp)
        setConnected(s != nil)
        @remote_address = NetSocketAddress.new("0.0.0.0", 0)
        if s != nil 
            address = s.remote_address().getnameinfo()
            @remote_address = NetSocketAddress.new(address[0], address[1])
        end
    end
    def send(data)
        p_send(data, nil)
    end
end
    
class UdpSocket < NetSocket
    def initialize(s)
        super(s, NetSocketProtocolType::Udp)
    end
    def send(data, address)
        p_send(data, address)
    end
end               

def TcpConnect(address)
    s = Socket.new(Socket::AF_INET, Socket::SOCK_STREAM)
    begin
        s.connect(Socket.pack_sockaddr_in(address.port, address.host))
    rescue
        s = nil
    end
    return TcpSocket.new(s)
end

def TcpListen(address)
    s = Socket.new(Socket::AF_INET, Socket::SOCK_STREAM)
    begin
        s.bind(Socket.pack_sockaddr_in(address.port, address.host))
        s.listen(1024)
    rescue
        s = nil
    end
    return TcpServer.new(s)
end

def UdpCast(address)
    s = Socket.new(Socket::AF_INET, Socket::SOCK_DGRAM)
    begin
        s.bind(Socket.pack_sockaddr_in(address.port, address.host))
    rescue
        s = nil
    end
    return UdpSocket.new(s)
end

