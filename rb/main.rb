require "./xyz/olooko/comm/netcomm.rb"

def TcpServerAcceptCallback(tcpsocket)
    if tcpsocket.available
        puts("TcpClient Accepted. %s" % [tcpsocket.remoteAddress])
        tcpsocket.setReceivedCallback(method(:CSocketReceivedCallback))
    end
end

def CSocketReceivedCallback(socket, data)
    case data.result
    when CSocketReceivedDataResult::Completed
        if data.command == 0x88
            a1 = data.args[0]
            a2 = data.args[1]
            a3 = data.args[2]
            a4 = data.args[3]          
            a5 = data.args.at(4)
            protocol = ""
            if socket.protocolType == CSocketProtocolType::Tcp
                protocol = "TCP"
            elsif socket.protocolType == CSocketProtocolType::Udp
                protocol = "UDP"
            end
            puts("%s %s (%s, %s, %s, %s, [%s])" % [protocol, data.remoteAddress, a1, a2, a3, a4, a5])
        end
    when CSocketReceivedDataResult::Interrupted
        puts "Interrupted"
    when CSocketReceivedDataResult::ParsingError
        puts "Parsing-Error"
    when CSocketReceivedDataResult::Closed
        puts "Close"
        socket.close()
    end
end

thread1 = Thread.new {
    address = CSocketAddress.new("127.0.0.1", 10010)
    udpsocket = UdpCast(address)
    if udpsocket.available
        puts("UdpSocket Started. %s" % [udpsocket.localAddress])
        udpsocket.setReceivedCallback(method(:CSocketReceivedCallback))
        args = CSocketDataArgs.new()
        args.add(CInteger.new(-256))
        args.add(CBoolean.new(true))
        args.add(CString.new("Hello"))
        args.add(CFloat.new(-1.1))
        args.add(CByteArray.new([0x41, 0x42, 0x43].pack("C*")))
        data = CSocketSendData.new(0x88, args)
        if data.buildResult == CSocketSendDataBuildResult::Successful
            while true
                udpsocket.send(data, address)
                sleep 5 
            end
        end
    end 
}

sleep 1

thread2 = Thread.new {
    tcpserver = TcpListen(CSocketAddress.new("127.0.0.1", 10010))
    puts("TcpServer Started.")
    if tcpserver.running
        tcpserver.setAcceptCallback(method(:TcpServerAcceptCallback))
    end     
}

sleep 1

thread3 = Thread.new {
    tcpsocket = TcpConnect(CSocketAddress.new("127.0.0.1", 10010))
    if tcpsocket.available
        puts("TcpClient Started. %s" % [tcpsocket.localAddress])
        tcpsocket.setReceivedCallback(method(:CSocketReceivedCallback))
        args = CSocketDataArgs.new()
        args.add(CInteger.new(-256))
        args.add(CBoolean.new(true))
        args.add(CString.new("Hello"))
        args.add(CFloat.new(-1.1))
        args.add(CByteArray.new([0x41, 0x42, 0x43].pack("C*")))
        data = CSocketSendData.new(0x88, args)
        if data.buildResult == CSocketSendDataBuildResult::Successful
            while true
                if tcpsocket.connected
                    tcpsocket.send(data)
                else
                    break
                end
                sleep 5
            end            
        end
    end 
}

thread1.join()
thread2.join()
thread3.join()
