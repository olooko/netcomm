import xyz.olooko.comm.netcomm._


class UdpSocketThread(private var _callback: CSocketReceivedCallback) extends Thread {
    override def run(): Unit = {
        val address = new CSocketAddress("127.0.0.1", 10010)
        val udpsocket = NetworkComm.UdpCast(address)
        if (udpsocket.available) {
            println("UdpSocket Started. %s".format(udpsocket.localAddress))
            udpsocket.setReceivedCallback(_callback)
            val args = new CSocketDataArgs
            args.add(new CInteger(-256))
            args.add(new CBoolean(true))
            args.add(new CString("Hello"))
            args.add(new CFloat(-1.1))
            args.add(new CByteArray(Array[Byte](0x41, 0x42, 0x43)))
            val data = new CSocketSendData(0x88, args)
            if (data.buildResult eq CSocketSendDataBuildResult.Successful) {
                while (true) {
                    udpsocket.send(data, address)
                    Thread.sleep(5000)
                }
            }
        }
    }
}

class TcpServerThread(private var _callback: TcpServerAcceptCallback) extends Thread {
    override def run(): Unit = {
        val tcpserver = NetworkComm.TcpListen(new CSocketAddress("127.0.0.1", 10010))
        println("TcpServer Started.")
        if (tcpserver.running) tcpserver.setAcceptCallback(_callback)
    }
}

class TcpClientThread(private var _callback: CSocketReceivedCallback) extends Thread {
    override def run(): Unit = {
        val tcpsocket = NetworkComm.TcpConnect(new CSocketAddress("127.0.0.1", 10010))
        if (tcpsocket.available) {
            println("TcpClient Started. %s".format(tcpsocket.localAddress))
            tcpsocket.setReceivedCallback(_callback)
            val args = new CSocketDataArgs
            args.add(new CInteger(-256))
            args.add(new CBoolean(true))
            args.add(new CString("Hello"))
            args.add(new CFloat(-1.1))
            args.add(new CByteArray(Array[Byte](0x41, 0x42, 0x43)))
            val data = new CSocketSendData(0x88, args)
            if (data.buildResult eq CSocketSendDataBuildResult.Successful) {
                while (true) {
                    if (tcpsocket.connected) tcpsocket.send(data)
                    else return
                    Thread.sleep(5000)
                }
            }
        }
    }
}

object Main {
    def main(args: Array[String]): Unit = {
        val receivedCallback = new CSocketReceivedCallback() {
            def callMethod(socket: CSocket, data: CSocketReceivedData): Unit = {
                if (data.result eq CSocketReceivedDataResult.Completed) {
                    if (data.command == 0x88) {
                        val args = data.args
                        val a1 = args.at(0).asInstanceOf[CInteger]
                        val a2 = args.at(1).asInstanceOf[CBoolean]
                        val a3 = args.at(2).asInstanceOf[CString]
                        val a4 = args.at(3).asInstanceOf[CFloat]
                        val a5 = args.at(4).asInstanceOf[CByteArray]
                        var protocol = ""
                        if (socket.protocolType eq CSocketProtocolType.Tcp) protocol = "TCP"
                        else if (socket.protocolType eq CSocketProtocolType.Udp) protocol = "UDP"
                        val output = "%s %s (%s, %s, %s, %s, [%s])".format(protocol, data.remoteAddress, a1, a2, a3, a4, a5)
                        println(output)
                    }
                    else if (data.result eq CSocketReceivedDataResult.Interrupted) println("Interrupted")
                    else if (data.result eq CSocketReceivedDataResult.ParsingError) println("Parsing-Error")
                    else if (data.result eq CSocketReceivedDataResult.Closed) {
                        println("Close")
                        socket.close()
                    }
                }
            }
        }
        val acceptCallback = new TcpServerAcceptCallback() {
            def callMethod(tcpsocket: TcpSocket): Unit = {
                if (tcpsocket.available) {
                    println("TcpClient Accepted. %s".format(tcpsocket.remoteAddress))
                    tcpsocket.setReceivedCallback(receivedCallback)
                }
            }
        }
        val thread1 = new UdpSocketThread(receivedCallback)
        thread1.start()
        Thread.sleep(1000)

        val thread2 = new TcpServerThread(acceptCallback)
        thread2.start()
        Thread.sleep(1000)

        val thread3 = new TcpClientThread(receivedCallback)
        thread3.start()

        thread1.join()
        thread2.join()
        thread3.join()
    }
}

