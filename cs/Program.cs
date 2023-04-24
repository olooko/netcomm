using System;
using System.Threading;

using xyz.olooko.comm.netcomm;


namespace comm 
{
    internal class Program 
    {
        static void Main(string[] args) 
        { 
            Thread thread1 = new Thread(new ThreadStart(UdpSocketProc));
            thread1.IsBackground = true;
            thread1.Start();

            Thread.Sleep(1000);

            Thread thread2 = new Thread(new ThreadStart(TcpServerProc));
            thread2.IsBackground = true;
            thread2.Start();

            Thread.Sleep(1000);

            Thread thread3 = new Thread(new ThreadStart(TcpClientProc));
            thread3.IsBackground = true;
            thread3.Start();

            thread1.Join();
            thread2.Join();
            thread3.Join();
        }

        static void UdpSocketProc()
        {
            CSocketAddress address = new CSocketAddress("127.0.0.1", 10010);
            UdpSocket udpsocket = NetworkComm.UdpCast(address);

            if (udpsocket.Available)
            {
                Console.WriteLine(string.Format("UdpSocket Started. {0}", udpsocket.LocalAddress));
                udpsocket.SetReceivedCallback(CSocketReceivedCallback);

                CSocketDataArgs args = new CSocketDataArgs();
                args.Add(new CInteger(-256));
                args.Add(new CBoolean(true));
                args.Add(new CString("Hello"));
                args.Add(new CFloat(-1.1));
                args.Add(new CByteArray(new byte[] { 0x41, 0x42, 0x43 }));

                CSocketSendData data = new CSocketSendData(0x88, args);

                if (data.BuildResult == CSocketSendDataBuildResult.Successful)
                {
                    while (true)
                    {
                        udpsocket.Send(data, address);
                        Thread.Sleep(5000);
                    }
                }
            }
        }

        static void TcpServerProc() 
        {
            TcpServer tcpserver = NetworkComm.TcpListen(new CSocketAddress("127.0.0.1", 10010));
            Console.WriteLine("TcpServer Started.");

            if (tcpserver.Running)
                tcpserver.SetAcceptCallback(TcpServerAcceptCallback);
        }

        static void TcpClientProc() 
        {
            TcpSocket tcpsocket = NetworkComm.TcpConnect(new CSocketAddress("127.0.0.1", 10010));

            if (tcpsocket.Available)
            {
                Console.WriteLine(string.Format("TcpClient Started. {0}", tcpsocket.LocalAddress));
                tcpsocket.SetReceivedCallback(CSocketReceivedCallback);

                CSocketDataArgs args = new CSocketDataArgs();
                args.Add(new CInteger(-256));
                args.Add(new CBoolean(true));
                args.Add(new CString("Hello"));
                args.Add(new CFloat(-1.1));
                args.Add(new CByteArray(new byte[] { 0x41, 0x42, 0x43 }));

                CSocketSendData data = new CSocketSendData(0x88, args);

                if (data.BuildResult == CSocketSendDataBuildResult.Successful)
                {
                    while (true)
                    {
                        if (tcpsocket.Connected)
                            tcpsocket.Send(data);
                        else
                            break;

                        Thread.Sleep(5000);
                    }
                }
            }
        }

        static void TcpServerAcceptCallback(TcpSocket tcpsocket)
        {
            if (tcpsocket.Available)
            {
                Console.WriteLine(string.Format("TcpClient Accepted. {0}", tcpsocket.RemoteAddress));
                tcpsocket.SetReceivedCallback(CSocketReceivedCallback);
            }
        }

        static void CSocketReceivedCallback(CSocket socket, CSocketReceivedData data) 
        {
            if (data.Result == CSocketReceivedDataResult.Completed) 
            {
                if (data.Command == 0x88)
                {
                    var a1 = data.Args[0] as CInteger;
                    var a2 = data.Args[1] as CBoolean;
                    var a3 = data.Args[2] as CString; 
                    var a4 = data.Args[3] as CFloat;
                    var a5 = data.Args.At(4) as CByteArray;

                    string protocol = string.Empty;
                    if (socket.ProtocolType == CSocketProtocolType.Tcp)
                        protocol = "TCP";
                    else if (socket.ProtocolType == CSocketProtocolType.Udp)
                        protocol = "UDP";

                    string output = string.Format("{0} {1} ({2}, {3}, {4}, {5}, [{6}])", 
                        protocol, data.RemoteAddress, a1, a2, a3, a4, a5);

                    Console.WriteLine(output);
                }
            }
            else if (data.Result == CSocketReceivedDataResult.Interrupted)
            {
                Console.WriteLine("Interrupted");
            }
            else if (data.Result == CSocketReceivedDataResult.ParsingError) 
            {
                Console.WriteLine("Parsing-Error");
            }
            else if (data.Result == CSocketReceivedDataResult.Closed) 
            {
                Console.WriteLine("Close");
                socket.Close();
            }
        }
    }
}
