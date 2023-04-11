require "socket"

class IDataType
    def value
        return @value
    end
    def initialize(value)
        @value = value
    end   
    def to_s
        return @value.to_s
    end 
end

class CBoolean < IDataType
end

class CByteArray < IDataType
    def to_s
        s = ""
        ba = @value.unpack("C*")
        for b in ba
            if (s != "")
                s += ","
            end
            s += "0x%02X" % b
        end
        return s
    end 
end

class CFloat < IDataType
end

class CInteger < IDataType
end

class CString < IDataType
    def to_s
        return @value
    end 
end

class CSocket
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
        abstract_class()
        @data = CSocketData.new()
        @protocol = protocolType
        @socket = socket
        @connected = false
        @result = CSocketDataManipulationResult::NoData
        @localAddress = CSocketAddress.new("0.0.0.0", 0)
        if available
            address = @socket.local_address()
            @localAddress = CSocketAddress.new(address.ip_address, address.ip_port)
        end
    end
    def abstract_class()
        raise "abstract class"
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
        if @protocol == CSocketProtocolType::Tcp
            length = @socket.send(data.bytes[bytesTransferred..-1], 0)
            @connected = length > 0 ? true : false
        elsif @protocol == CSocketProtocolType::Udp
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
            if @protocol == CSocketProtocolType::Tcp
                buffer = @socket.recv(4096)
                if buffer.length > 0
                    @connected = true
                else
                    @connected = false
                end
                address = @socket.remote_address()                
            elsif @protocol == CSocketProtocolType::Udp
                data = @socket.recvfrom(4096, 0)
                buffer = data[0]
                address = data[1]
            end
            remoteAddress = CSocketAddress.new(address.ip_address, address.ip_port)
            if buffer.length > 0
                @data.append(buffer)
                while true
                    @result = @data.manipulate()
                    if @result == CSocketDataManipulationResult::Completed
                        callback.call(self, CSocketReceivedData.new(@data.command, @data.args, CSocketReceivedDataResult::Completed, remoteAddress))
                        next
                    elsif @result == CSocketDataManipulationResult::ParsingError
                        callback.call(self, CSocketReceivedData.new(0X00, CSocketDataArgs.new(), CSocketReceivedDataResult::ParsingError, remoteAddress))
                        return
                    elsif @result == CSocketDataManipulationResult::InProgress
                        Thread.new {
                            sleep 15
                            if @result == CSocketDataManipulationResult::InProgress
                                callback.call(self, CSocketReceivedData.new(0X00, CSocketDataArgs.new(), CSocketReceivedDataResult::Interrupted, remoteAddress))   
                            end   
                        }
                        break
                    elsif @result == CSocketDataManipulationResult::NoData
                        break
                    end
                end
                next
            else
                callback.call(self, CSocketReceivedData.new(0x00, CSocketDataArgs.new(), CSocketReceivedDataResult::Closed, remoteAddress))
                break
            end
        end
    end      
end

class CSocketAddress
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

class CSocketData
    def args
        @args
    end
    def command
        @command
    end
    def initialize()
        @command = 0x00
        @args = CSocketDataArgs.new()         
        @data = ""
        @datapos = 0
        @checksum = 0x00
        @step = CSocketDataParsingStep::SOH
        @textlen = 0  
    end
    def append(buffer)
        @data << buffer
    end
    def manipulate()
        while true
            datalen = @data.length - @datapos
            case @step
            when CSocketDataParsingStep::SOH
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == 0x01                       
                        @datapos += 1
                        @step = CSocketDataParsingStep::OTL
                        next
                    else 
                        return CSocketDataManipulationResult::ParsingError
                    end
                end
            when CSocketDataParsingStep::OTL
                if datalen > 0
                    if [0x11, 0x12, 0x14].include?(@data[@datapos].unpack("C")[0])
                        a = getArgLength(datalen)
                        sz = a.size
                        @textlen = a.argLength
                        if @textlen >= 0
                            @datapos += 1 + sz
                            @step = CSocketDataParsingStep::STX
                            next
                        end
                    else 
                        return CSocketDataManipulationResult::ParsingError
                    end
                end
            when CSocketDataParsingStep::STX
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == 0x02
                        @datapos += 1
                        @step = CSocketDataParsingStep::ETX
                        next
                    else
                        return CSocketDataManipulationResult::ParsingError
                    end
                end
            when CSocketDataParsingStep::ETX       
                if datalen > @textlen
                    if @data[@datapos + @textlen].unpack("C")[0] == 0x03
                        textfpos = @datapos
                        @command = @data[textfpos].unpack("C")[0]
                        @args.clear()
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
                                @args.add(CInteger.new(@data[(@datapos + 1)..(@datapos + sz)].unpack(fmt)[0]))
                            elsif [0x54, 0x58].include?(argH)
                                sz = argH & 0x0F
                                if sz == 4
                                    fmt = "g"
                                elsif sz == 8
                                    fmt = "G"
                                end
                                @args.add(CFloat.new(@data[(@datapos + 1)..(@datapos + sz)].unpack(fmt)[0]))
                            elsif [0x71].include?(argH)
                                sz = 1
                                bool = false
                                if @data[@datapos + 1].unpack("C")[0] == 1
                                    bool = true
                                end
                                @args.add(CBoolean.new(bool))
                            elsif [0x91, 0x92, 0x94].include?(argH)
                                a = getArgLength(datalen)
                                sz = a.size
                                argL = a.argLength
                                @args.add(CString.new(@data[(@datapos + 1 + sz)..(@datapos + sz + argL)]))
                                @datapos += argL
                            elsif [0xB1, 0xB2, 0xB4].include?(argH)
                                a = getArgLength(datalen)
                                sz = a.size
                                argL = a.argLength
                                @args.add(CByteArray.new(@data[(@datapos + 1 + sz)..(@datapos + sz + argL)]))
                                @datapos += argL
                            else
                                return CSocketDataManipulationResult::ParsingError
                            end
                            @datapos += 1 + sz 
                        end  
                        @checksum = 0x00
                        for b in @data[textfpos..(textfpos + @textlen - 1)].unpack("C*")
                            @checksum ^= b 
                        end
                        @datapos += 1
                        @step = CSocketDataParsingStep::CHK
                        next                                                        
                    else
                        return CSocketDataManipulationResult::ParsingError
                    end
                end
            when CSocketDataParsingStep::CHK                       
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == @checksum
                        @datapos += 1
                        @step = CSocketDataParsingStep::EOT
                        next
                    else
                        return CSocketDataManipulationResult::ParsingError
                    end
                end
            when CSocketDataParsingStep::EOT 
                if datalen > 0
                    if @data[@datapos].unpack("C")[0] == 0x04
                        @datapos += 1
                        @data = @data[@datapos..-1]
                        @datapos = 0
                        @checksum = 0x00
                        @step = CSocketDataParsingStep::SOH
                        @textlen = 0  
                        return CSocketDataManipulationResult::Completed
                    else
                        return CSocketDataManipulationResult::ParsingError
                    end
                end
            end
            if @data.length == 0
                return CSocketDataManipulationResult::NoData
            end       
            return CSocketDataManipulationResult::InProgress
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
        return CSocketDataArgLength.new(sz, argL)               
    end    
end

class CSocketDataArgLength
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

class CSocketDataArgs 
    include Enumerable
    def each(&block)
        @list.each(&block) 
    end
    def [](index)
        @list[index]
    end    
    def length
        @list.length
    end    
    def initialize()
        @list = []
    end
    def add(arg)
        @list.append(arg)
    end
    def at(index)
        @list[index]
    end    
    def clear()
        @list.clear()
    end
end

module CSocketDataManipulationResult
    Completed = "Completed"
    InProgress = "InProgress"
    NoData = "NoData"
    ParsingError = "ParsingError"
end

module CSocketDataParsingStep
    SOH = "SOH"
    OTL = "OTL"
    STX = "STX"
    ETX = "ETX"
    CHK = "CHK"
    EOT = "EOT"
end

module CSocketProtocolType
    Tcp = "Tcp"
    Udp = "Udp"
end

class CSocketReceivedData
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

module CSocketReceivedDataResult
    Closed = "Closed"    
    Completed = "Completed"
    Interrupted = "Interrupted"
    ParsingError = "ParsingError"
end

class CSocketSendData
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
        @result = CSocketSendDataBuildResult::NoData
        if command < 0x00 || command > 0xFF
            @result = CSocketSendDataBuildResult::CommandValueOverflowError
            return
        end
        @command = command
        @args = args
        text = [command].pack("C")
        for arg in @args
            case arg.class.to_s
            when "CInteger"
                i = arg.value
                if -128 <= i && i <= 127
                    text << [0x31].pack("C") << [i].pack("c")
                elsif -32768 <= i && i <= 32767
                    text << [0x32].pack("C") << [i].pack("s>")
                elsif -2147483648 <= i && i <= 2147483647
                    text << [0x34].pack("C") << [i].pack("l>")
                else
                    text << [0x38].pack("C") << [i].pack("q>")
                end
            when "CFloat"
                f = arg.value
                if f.abs() <= 3.40282347e+38
                    text << [0x54].pack("C") << [f].pack("g")
                else
                    text << [0x58].pack("C") << [f].pack("G")
                end
            when "CBoolean"
                b = arg.value
                text << [0x71].pack("C") << [(b == true ? 1 : 0)].pack("c")
            when "CString"
                s = arg.value.encode("UTF-8")
                argL = s.length
                if argL <= ARG_MAXLEN
                    if argL <= 127
                        text << [0x91].pack("C") << [argL].pack("c")
                    elsif argL <= 32767
                        text << [0x92].pack("C") << [argL].pack("s>")
                    else
                        text << [0x94].pack("C") << [argL].pack("l>")
                    end
                    text << s
                else
                    @result = CSocketSendDataBuildResult::StringLengthOverflowError
                    return
                end
            when "CByteArray"
                ba = arg.value
                argL = ba.length
                if argL <= ARG_MAXLEN
                    if argL <= 127
                        text << [0xB1].pack("C") << [argL].pack("c")
                    elsif argL <= 32767
                        text << [0xB2].pack("C") << [argL].pack("s>")
                    else
                        text << [0xB4].pack("C") << [argL].pack("l>") 
                    end    
                    text << ba
                else
                    @result = CSocketSendDataBuildResult::ByteArrayLengthOverflowError
                    return
                end
            else
                @result = CSocketSendDataBuildResult::DataTypeNotImplementedError
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
            @result = CSocketSendDataBuildResult::DataTotalLengthOverflowError
            return
        end
        @bytes = data
        @result = CSocketSendDataBuildResult::Successful
    end
end

module CSocketSendDataBuildResult
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

class TcpSocket < CSocket
    def connected
        available && getConnected()   
    end
    def remoteAddress
        @remoteAddress
    end
    def initialize(s)
        super(s, CSocketProtocolType::Tcp)
        setConnected(s != nil)
        @remoteAddress = CSocketAddress.new("0.0.0.0", 0)
        if s != nil 
            address = s.remote_address()
            @remoteAddress = CSocketAddress.new(address.ip_address, address.ip_port)
        end
    end
    def send(data)
        _send(data, nil)
    end
    def abstract_class()
        false
    end    
end
    
class UdpSocket < CSocket
    def initialize(s)
        super(s, CSocketProtocolType::Udp)
    end
    def send(data, address)
        _send(data, address)
    end
    def abstract_class()
        false
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

