import xyz.olooko.comm.netcomm.*

fun receivedCallback(socket: CSocket, data: CSocketReceivedData) {
    if (data.result === CSocketReceivedDataResult.Completed) {
        if (data.command == 0x88) {
            val args: CSocketDataArgs = data.args
            val a1: CInteger = args.at(0) as CInteger
            val a2: CBoolean = args.at(1) as CBoolean
            val a3: CString = args.at(2) as CString
            val a4: CFloat = args.at(3) as CFloat
            val a5: CByteArray = args.at(4) as CByteArray
            var protocol = ""
            if (socket.protocolType === CSocketProtocolType.Tcp) {
                protocol = "TCP"
            } else if (socket.protocolType === CSocketProtocolType.Udp) {
                protocol = "UDP"
            }
            val output: String = String.format(
                "%s %s (%s, %s, %s, %s, [%s])",
                protocol, data.remoteAddress, a1, a2, a3, a4, a5
            )
            println(output)
        }
    } else if (data.result === CSocketReceivedDataResult.Interrupted) {
        println("Interrupted")
    } else if (data.result === CSocketReceivedDataResult.ParsingError) {
        println("Parsing-Error")
    } else if (data.result === CSocketReceivedDataResult.Closed) {
        println("Close")
        socket.close()
    }
}

fun acceptCallback(tcpsocket: TcpSocket) {
    if (tcpsocket.available) {
        println(String.format("TcpClient Accepted. %s", tcpsocket.remoteAddress))
        tcpsocket.setReceivedCallback(::receivedCallback)
    }
}

fun main() {
    val thread1 = Thread {
        val address: CSocketAddress = CSocketAddress("127.0.0.1", 10010)
        val udpsocket: UdpSocket = UdpCast(address)
        if (udpsocket.available) {
            println(String.format("UdpSocket Started. %s", udpsocket.localAddress))
            udpsocket.setReceivedCallback(::receivedCallback)
            val args = CSocketDataArgs()
            args.add(CInteger(-256))
            args.add(CBoolean(true))
            args.add(CString("Hello"))
            args.add(CFloat(-1.1))
            args.add(CByteArray(byteArrayOf(0x41, 0x42, 0x43)))
            val data = CSocketSendData(0x88, args)
            if (data.buildResult === CSocketSendDataBuildResult.Successful) {
                while (true) {
                    udpsocket.send(data, address)
                    Thread.sleep(5000)
                }
            }
        }
    }
    thread1.start()

    Thread.sleep(1000)

    val thread2 = Thread {
        val tcpserver: TcpServer = TcpListen(CSocketAddress("127.0.0.1", 10010))
        println("TcpServer Started.")
        if (tcpserver.running) {
            tcpserver.setAcceptCallback(::acceptCallback)
        }
    }
    thread2.start()

    Thread.sleep(1000)

    val thread3 = Thread {
        val tcpsocket: TcpSocket = TcpConnect(CSocketAddress("127.0.0.1", 10010))
        if (tcpsocket.available) {
            println(String.format("TcpClient Started. %s", tcpsocket.localAddress))
            tcpsocket.setReceivedCallback(::receivedCallback)
            val args = CSocketDataArgs()
            args.add(CInteger(-256))
            args.add(CBoolean(true))
            args.add(CString("Hello"))
            args.add(CFloat(-1.1))
            args.add(CByteArray(byteArrayOf(0x41, 0x42, 0x43)))
            val data = CSocketSendData(0x88, args)
            if (data.buildResult === CSocketSendDataBuildResult.Successful) {
                while (true) {
                    if (tcpsocket.connected) {
                        tcpsocket.send(data)
                    } else break
                    Thread.sleep(5000)
                }
            }
        }
    }
    thread3.start()

    thread1.join()
    thread2.join()
    thread3.join()
}
