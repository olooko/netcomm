require "./xyz/olooko/comm/netcomm.rb"

def TcpServerAcceptCallback(tcpsocket)
    if tcpsocket.available
        puts("NetworkComm.TcpClient Accepted. %s" % [tcpsocket.remoteAddress])
        tcpsocket.setReceivedCallback(method(:NetSocketReceivedCallback))
    end
end

def NetSocketReceivedCallback(socket, data)
    case data.result
    when NetSocketReceivedDataResult::Completed
        if data.command == 0x88
            a1 = data.args[0]
            a2 = data.args[1]
            a3 = data.args[2]
            a4 = data.args[3]          
            a5 = ""
            ba = data.args[4].unpack("C*")
            for b in ba
                if (a5 != "")
                    a5 += ","
                end
                a5 += "0x%02X" % b
            end
            protocol = ""
            if socket.protocolType == NetSocketProtocolType::Tcp
                protocol = "TCP"
            elsif socket.protocolType == NetSocketProtocolType::Udp
                protocol = "UDP"
            end
            puts("%s %s (%d, %s, %s, %f, [%s])" % [protocol, data.remoteAddress, a1, a2.to_s, a3, a4, a5])
        end
    when NetSocketReceivedDataResult::Interrupted
        puts "Interrupted"
    when NetSocketReceivedDataResult::ParsingError
        puts "Parsing-Error"
    when NetSocketReceivedDataResult::Closed
        puts "Close"
        socket.close()
    end
end

thread1 = Thread.new {
    udpsocket = UdpCast(NetSocketAddress.new("127.0.0.1", 10010))
    if udpsocket.available
        puts("NetworkComm.UdpSocket Started. %s" % [udpsocket.localAddress])
        udpsocket.setReceivedCallback(method(:NetSocketReceivedCallback))
        data = NetSocketSendData.new(0x88, [-256, true, "Hello", -1.1, [0x41, 0x42, 0x43].pack("C*")])
        if data.buildResult == NetSocketSendDataBuildResult::Successful
            while true
                udpsocket.send(data, NetSocketAddress.new("127.0.0.1", 10010))
                sleep 5 
            end
        end
    end 
}

sleep 1

thread2 = Thread.new {
    tcpserver = TcpListen(NetSocketAddress.new("127.0.0.1", 10010))
    puts("NetworkComm.TcpServer Started.")
    if tcpserver.running
        tcpserver.setAcceptCallback(method(:TcpServerAcceptCallback))
    end     
}

sleep 1

thread3 = Thread.new {
    tcpsocket = TcpConnect(NetSocketAddress.new("127.0.0.1", 10010))
    if tcpsocket.available
        puts("NetworkComm.TcpClient Started. %s" % [tcpsocket.localAddress])
        tcpsocket.setReceivedCallback(method(:NetSocketReceivedCallback))
        data = NetSocketSendData.new(0x88, [-256, true, "Hello", -1.1, [0x41, 0x42, 0x43].pack("C*")])
        if data.buildResult == NetSocketSendDataBuildResult::Successful
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
