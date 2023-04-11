import xyz.olooko.comm.netcomm.*

def CSocketReceivedCallback = { socket, data ->
    {
        if (data.result == CSocketReceivedDataResult.Completed) {
            if (data.command == 0x88) {
                def args = data.args

                var a1 = (CInteger)args.at(0)
                var a2 = (CBoolean)args.at(1)
                var a3 = (CString)args.at(2)
                var a4 = (CFloat)args.at(3)
                var a5 = (CByteArray)args.at(4)

                String protocol = ""
                if (socket.protocolType == CSocketProtocolType.Tcp)
                    protocol = "TCP"
                else if (socket.protocolType == CSocketProtocolType.Udp)
                    protocol = "UDP"

                def output = String.format("%s %s (%s, %s, %s, %s, [%s])",
                        protocol, data.remoteAddress, a1, a2, a3, a4, a5)

                println(output)
            }
        }
        else if (data.result == CSocketReceivedDataResult.Interrupted) {
            println("Interrupted")
        }
        else if (data.result == CSocketReceivedDataResult.ParsingError) {
            println("Parsing-Error")
        }
        else if (data.result == CSocketReceivedDataResult.Closed) {
            println("Close")
            socket.close()
        }
    }
}

def TcpServerAcceptCallback = { tcpsocket ->
    {
        if (tcpsocket.available) {
            println(String.format("TcpClient Accepted. %s", tcpsocket.remoteAddress))
            tcpsocket.setReceivedCallback(CSocketReceivedCallback)
        }
    }
}

Thread.start {
    def address = new CSocketAddress("127.0.0.1", 10010)
    def udpsocket = NetworkComm.UdpCast(address)

    if (udpsocket.available) {
        println(String.format("UdpSocket Started. %s", udpsocket.localAddress))
        udpsocket.setReceivedCallback(CSocketReceivedCallback)

        def args = new CSocketDataArgs()
        args.add(new CInteger(-256))
        args.add(new CBoolean(true))
        args.add(new CString("Hello"))
        args.add(new CFloat(-1.1))
        args.add(new CByteArray(new byte[] { 0x41, 0x42, 0x43 }))

        def data = new CSocketSendData(0x88, args)
        if (data.buildResult == CSocketSendDataBuildResult.Successful) {
            while (true) {
                udpsocket.send(data, address)
                sleep(5000)
            }
        }
    }
}

sleep(1000)

Thread.start {
    def tcpserver = NetworkComm.TcpListen(new CSocketAddress("127.0.0.1", 10010))
    println("TcpServer Started.")

    if (tcpserver.running) {
        tcpserver.setAcceptCallback(TcpServerAcceptCallback)
    }
}

sleep(1000)

Thread.start {
    def tcpsocket = NetworkComm.TcpConnect(new CSocketAddress("127.0.0.1", 10010))

    if (tcpsocket.available) {
        println(String.format("TcpClient Started. %s", tcpsocket.localAddress))
        tcpsocket.setReceivedCallback(CSocketReceivedCallback)

        def args = new CSocketDataArgs()
        args.add(new CInteger(-256))
        args.add(new CBoolean(true))
        args.add(new CString("Hello"))
        args.add(new CFloat(-1.1))
        args.add(new CByteArray(new byte[] { 0x41, 0x42, 0x43 }))

        def data = new CSocketSendData(0x88, args)
        if (data.buildResult == CSocketSendDataBuildResult.Successful) {
            while (true) {
                if (tcpsocket.connected) {
                    tcpsocket.send(data)
                } else
                    break

                sleep(5000)
            }
        }
    }
}








