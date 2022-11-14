using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using xyz.olooko.comm.netcomm;

namespace xyz.olooko.comm 
{
    internal class Program 
    {
        static void Main(string[] args) 
        { 
            Thread thread1 = new Thread(new ThreadStart(TcpServerProc));
            thread1.IsBackground = true;
            thread1.Start();

            Thread thread2 = new Thread(new ThreadStart(TcpClientProc));
            thread2.IsBackground = true;
            thread2.Start();

            Thread thread3 = new Thread(new ThreadStart(UdpSocketProc));
            thread3.IsBackground = true;
            thread3.Start();

            thread1.Join();
            thread2.Join();
            thread3.Join();
        }

        static void TcpServerProc() 
        {
            TcpServer tcpserver = NetworkComm.TcpListen(new NetSocketAddress("127.0.0.1", 10010));
            Console.WriteLine("NetworkComm.TcpServer Started...");

            while (tcpserver.Started)
            {
                TcpSocket tcpsocket = tcpserver.Accept();

                if (tcpsocket.Available)
                {
                    Console.WriteLine("NetworkComm.TcpSocket Accepted");
                    tcpsocket.SetReceivedCallback(NetSocketReceivedCallback);
                }
                else
                    break;
            }

            Console.WriteLine("NetworkComm.TcpServer Stopped");
        }

        static void TcpClientProc() 
        {
            TcpSocket tcpsocket = NetworkComm.TcpConnect(new NetSocketAddress("127.0.0.1", 10010));

            if (tcpsocket.Available)
            {
                Console.WriteLine("NetworkComm.TcpSocket Started...");
                tcpsocket.SetReceivedCallback(NetSocketReceivedCallback);

                while (true)
                {
                    if (tcpsocket.Connected)
                    {
                        object[] args = new object[] { -256, true, "Hello", -1.1, new byte[] { 0x41, 0x42, 0x43 } };
                        tcpsocket.Send(new NetSocketSendData(0x88, args));
                    }
                    else
                        break;

                    Thread.Sleep(5 * 1000);
                }
            }
        }

        static void UdpSocketProc() 
        {
            UdpSocket udpsocket = NetworkComm.UdpCast(new NetSocketAddress("127.0.0.1", 10010));

            if (udpsocket.Available)
            {
                Console.WriteLine("NetworkComm.UdpSocket Started...");
                udpsocket.SetReceivedCallback(NetSocketReceivedCallback);

                while (true)
                {
                    object[] args = new object[] { -256, true, "Hello", -1.1, new byte[] { 0x41, 0x42, 0x43 } };
                    udpsocket.Send(new NetSocketSendData(0x88, args), new NetSocketAddress("127.0.0.1", 10010));

                    Thread.Sleep(5 * 1000);
                }
            }
        }

        static void NetSocketReceivedCallback(NetSocket socket, NetSocketReceivedData data) 
        {
            if (data.Result == NetSocketReceivedDataResult.Completed) 
            {
                List<string> args = new List<string>();

                foreach (object o in data.Args) 
                {
                    if (o.GetType().Name == "Byte[]") 
                    {
                        byte[] ba = (byte[])o;
                        StringBuilder sb = new StringBuilder();
                        
                        sb.Append("[");
                        foreach (byte b in ba)
                            sb.AppendFormat(" 0x{0:X2}", b);
                        sb.Append("]");

                        args.Add(sb.ToString());
                    } 
                    else
                        args.Add(o.ToString());
                }

                Console.WriteLine(string.Format("protocol: {0}, command: 0x{1:X2}, args: {2}", socket.ProtocolType, data.Command, string.Format("{{{0}}}", string.Join(",", args))));
            }
            else if (data.Result == NetSocketReceivedDataResult.Interrupted)
            {
                Console.WriteLine("Interrupted");
            }
            else if (data.Result == NetSocketReceivedDataResult.ParsingError) 
            {
                Console.WriteLine("parsing-error");
            }
            else if (data.Result == NetSocketReceivedDataResult.Closed) 
            {
                Console.WriteLine("close");
                socket.Close();
            }
        }
    }
}





