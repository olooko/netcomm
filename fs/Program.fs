open System
open System.Collections.Generic
open System.Net.Sockets
open System.Text
open System.Threading

open xyz.olooko.comm.netcomm


let CSocketReceivedCallback(socket: CSocket, data: CSocketReceivedData) =
    if data.Result = CSocketReceivedDataResult.Completed then
        if data.Command = 0x88uy then
            let a1 = data.Args[0] :?> CInteger
            let a2 = data.Args[1] :?> CBoolean
            let a3 = data.Args[2]:?> CString
            let a4 = data.Args[3] :?> CFloat
            let a5 = data.Args.At(4) :?> CByteArray

            let mutable protocol = String.Empty
            if socket.ProtocolType = CSocketProtocolType.Tcp then protocol <- "TCP"
            else if socket.ProtocolType = CSocketProtocolType.Udp then protocol <- "UDP"

            let output = String.Format("{0} {1} ({2}, {3}, {4}, {5}, [{6}])", 
                protocol, data.RemoteAddress, a1, a2, a3, a4, a5)

            Console.WriteLine(output)

    else if data.Result = CSocketReceivedDataResult.Interrupted then Console.WriteLine("Interrupted")
    else if data.Result = CSocketReceivedDataResult.ParsingError then Console.WriteLine("Parsing-Error")
    else if data.Result = CSocketReceivedDataResult.Closed then Console.WriteLine("Close"); socket.Close()

        
let TcpServerAcceptCallback(tcpsocket: TcpSocket) =
    if tcpsocket.Available then
        Console.WriteLine(String.Format("NetworkComm.TcpClient Accepted. {0}", tcpsocket.RemoteAddress))
        tcpsocket.SetReceivedCallback(CSocketReceivedCallback)


let UdpSocketProc() = 
    let udpsocket = NetworkComm.UdpCast(CSocketAddress("127.0.0.1", 10010))

    if udpsocket.Available then
        Console.WriteLine(String.Format("NetworkComm.UdpSocket Started. {0}", udpsocket.LocalAddress))
        udpsocket.SetReceivedCallback(CSocketReceivedCallback)

        let args = CSocketDataArgs()
        args.Add(CInteger(-256))
        args.Add(CBoolean(true))
        args.Add(CString("Hello"))
        args.Add(CFloat(-1.1))
        args.Add(CByteArray([|0x41uy;0x42uy;0x43uy|]))

        let data = CSocketSendData(0x88uy, args)

        if data.BuildResult = CSocketSendDataBuildResult.Successful then
            let mutable looping = true
            while looping do
                udpsocket.Send(data, CSocketAddress("127.0.0.1", 10010))
                Thread.Sleep(5000)


let TcpServerProc() = 
    let tcpserver = NetworkComm.TcpListen(CSocketAddress("127.0.0.1", 10010))
    Console.WriteLine("NetworkComm.TcpServer Started.")

    if tcpserver.Running then
        tcpserver.SetAcceptCallback(TcpServerAcceptCallback)


let TcpClientProc() = 
    let tcpsocket = NetworkComm.TcpConnect(CSocketAddress("127.0.0.1", 10010))

    if tcpsocket.Available then
        Console.WriteLine(String.Format("NetworkComm.TcpClient Started. {0}", tcpsocket.LocalAddress))
        tcpsocket.SetReceivedCallback(CSocketReceivedCallback)

        let args = CSocketDataArgs()
        args.Add(CInteger(-256))
        args.Add(CBoolean(true))
        args.Add(CString("Hello"))
        args.Add(CFloat(-1.1))
        args.Add(CByteArray([|0x41uy;0x42uy;0x43uy|]))

        let data = CSocketSendData(0x88uy, args)

        if data.BuildResult = CSocketSendDataBuildResult.Successful then
            let mutable looping = true
            while looping do
                if tcpsocket.Connected then tcpsocket.Send(data)
                else looping <- false
                Thread.Sleep(5000)


let thread1 = Thread(ThreadStart(fun _ -> UdpSocketProc()))
thread1.IsBackground <- true
thread1.Start()

Thread.Sleep(1000)

let thread2 = Thread(ThreadStart(fun _ -> TcpServerProc()))
thread2.IsBackground <- true
thread2.Start()

Thread.Sleep(1000)

let thread3 = Thread(ThreadStart(fun _ -> TcpClientProc()))
thread3.IsBackground <- true
thread3.Start()

thread1.Join()
thread2.Join()
thread3.Join()
