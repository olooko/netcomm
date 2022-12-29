using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using xyz.olooko.comm.netcomm;

namespace xyz.olooko.comm 
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
            UdpSocket udpsocket = NetworkComm.UdpCast(new NetSocketAddress("127.0.0.1", 10010));

            if (udpsocket.Available)
            {
                Console.WriteLine(string.Format("NetworkComm.UdpSocket Started. {0}", udpsocket.LocalAddress));
                udpsocket.SetReceivedCallback(NetSocketReceivedCallback);

                object[] args = new object[] { -256, true, "Hello", -1.1, new byte[] { 0x41, 0x42, 0x43 } };
                NetSocketSendData data = new NetSocketSendData(0x88, args);

                if (data.BuildResult == NetSocketSendDataBuildResult.Successful)
                {
                    while (true)
                    {
                        udpsocket.Send(data, new NetSocketAddress("127.0.0.1", 10010));
                        Thread.Sleep(5000);
                    }
                }
            }
        }

        static void TcpServerProc() 
        {
            TcpServer tcpserver = NetworkComm.TcpListen(new NetSocketAddress("127.0.0.1", 10010));
            Console.WriteLine("NetworkComm.TcpServer Started.");

            if (tcpserver.Running)
                tcpserver.SetAcceptCallback(TcpServerAcceptCallback);
        }

        static void TcpClientProc() 
        {
            TcpSocket tcpsocket = NetworkComm.TcpConnect(new NetSocketAddress("127.0.0.1", 10010));

            if (tcpsocket.Available)
            {
                Console.WriteLine(string.Format("NetworkComm.TcpClient Started. {0}", tcpsocket.LocalAddress));
                tcpsocket.SetReceivedCallback(NetSocketReceivedCallback);

                object[] args = new object[] { -256, true, "Hello", -1.1, new byte[] { 0x41, 0x42, 0x43 } };
                NetSocketSendData data = new NetSocketSendData(0x88, args);

                if (data.BuildResult == NetSocketSendDataBuildResult.Successful)
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
                Console.WriteLine(string.Format("NetworkComm.TcpClient Accepted. {0}", tcpsocket.RemoteAddress));
                tcpsocket.SetReceivedCallback(NetSocketReceivedCallback);
            }
        }

        static void NetSocketReceivedCallback(NetSocket socket, NetSocketReceivedData data) 
        {
            if (data.Result == NetSocketReceivedDataResult.Completed) 
            {
                if (data.Command == 0x88)
                {
                    //long a1 = Convert.ToInt64(data.Args[0]);
                    //bool a2 = (bool)data.Args[1];
                    //string a3 = (string)data.Args[2];
                    //double a4 = Convert.ToDouble(data.Args[3]);
                    var a1 = data.Args[0];
                    var a2 = data.Args[1]; 
                    var a3 = data.Args[2]; 
                    var a4 = data.Args[3];

                    byte[] ba = (byte[])data.Args[4];
                    string a5 = "0x" + BitConverter.ToString(ba).Replace("-", ",0x");

                    string protocol = string.Empty;
                    if (socket.ProtocolType == NetSocketProtocolType.Tcp)
                        protocol = "TCP";
                    else if (socket.ProtocolType == NetSocketProtocolType.Udp)
                        protocol = "UDP";

                    string output = string.Format("{0} {1} ({2}, {3}, {4}, {5}, [{6}])", 
                        protocol, data.RemoteAddress, a1, a2, a3, a4, a5);

                    Console.WriteLine(output);
                }
            }
            else if (data.Result == NetSocketReceivedDataResult.Interrupted)
            {
                Console.WriteLine("Interrupted");
            }
            else if (data.Result == NetSocketReceivedDataResult.ParsingError) 
            {
                Console.WriteLine("Parsing-Error");
            }
            else if (data.Result == NetSocketReceivedDataResult.Closed) 
            {
                Console.WriteLine("Close");
                socket.Close();
            }
        }
    }
}
