require "./xyz/olooko/comm/netcomm.rb"

def tcpserver_acceptCallback(tcpsocket)
    if tcpsocket.available
        puts("NetworkComm.TcpSocket Accepted")
        tcpsocket.setReceivedCallback(method(:netsocket_receivedCallback))
    end
end

def netsocket_receivedCallback(socket, data)
    case data.result
    when NetSocketReceivedDataResult::Completed
        puts "protocol: %s, command: 0x%02X, args: {%s}" % [socket.protocolType, data.command, data.args]
    when NetSocketReceivedDataResult::Interrupted
        puts "Interrupted"
    when NetSocketReceivedDataResult::ParsingError
        puts "parsing-error"
    when NetSocketReceivedDataResult::Closed
        puts "close"
        socket.close()
    end
end

thread1 = Thread.new {
    tcpserver = TcpListen(NetSocketAddress.new("127.0.0.1", 10010))
    puts("NetworkComm.TcpServer Started...")
    if tcpserver.started
        tcpserver.setAcceptCallback(method(:tcpserver_acceptCallback))
    end
    while true
        sleep 5
    end        
}

thread2 = Thread.new {
    tcpsocket = TcpConnect(NetSocketAddress.new("203.245.0.226", 10010))
    if tcpsocket.available
        puts("NetworkComm.TcpSocket Started...")
        tcpsocket.setReceivedCallback(method(:netsocket_receivedCallback))
        while true
            if tcpsocket.connected
                data = NetSocketSendData.new(0x88, [-256, true, "Hello", -1.1, ByteArray.new([0x41,0x42,0x43])])
                if data.buildResult == NetSocketSendDataBuildResult::Successful
                    tcpsocket.send(data)
                end
            else
                break
            end
            sleep 5
        end
    end 
}

thread3 = Thread.new {
    udpsocket = UdpCast(NetSocketAddress.new("127.0.0.1", 10010))
    if udpsocket.available
        puts("NetworkComm.UdpSocket Started...")
        udpsocket.setReceivedCallback(method(:netsocket_receivedCallback))
        while true
            data = NetSocketSendData.new(0x88, [-256, true, "Hello", -1.1, ByteArray.new([0x41,0x42,0x43])])
            if data.buildResult == NetSocketSendDataBuildResult::Successful
                udpsocket.send(data, NetSocketAddress.new("127.0.0.1", 10010))
            end
            sleep 5  
        end
    end 
}

thread1.join()
thread2.join()
thread3.join()
