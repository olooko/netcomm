import java.util.ArrayList;

import xyz.olooko.comm.netcomm.NetSocket;
import xyz.olooko.comm.netcomm.NetSocketAddress;
import xyz.olooko.comm.netcomm.NetSocketProtocolType;
import xyz.olooko.comm.netcomm.NetSocketReceivedDataResult;
import xyz.olooko.comm.netcomm.NetSocketReceivedCallback;
import xyz.olooko.comm.netcomm.NetSocketReceivedData;
import xyz.olooko.comm.netcomm.NetSocketSendData;
import xyz.olooko.comm.netcomm.NetSocketSendDataBuildResult;
import xyz.olooko.comm.netcomm.NetworkComm;
import xyz.olooko.comm.netcomm.TcpServer;
import xyz.olooko.comm.netcomm.TcpServerAcceptCallback;
import xyz.olooko.comm.netcomm.TcpSocket;
import xyz.olooko.comm.netcomm.UdpSocket;

class UdpSocketThread extends Thread 
{
    private NetSocketReceivedCallback _callback;

    public UdpSocketThread(NetSocketReceivedCallback callback) {
        _callback = callback;
    }

    @Override
    public void run() 
    {
        UdpSocket udpsocket = NetworkComm.UdpCast(new NetSocketAddress("127.0.0.1", 10010));

        if (udpsocket.isAvailable()) 
        {
            System.out.println(String.format("NetworkComm.UdpSocket Started. %s", udpsocket.getLocalAddress()));
            udpsocket.setReceivedCallback(_callback);

            Object[] args = new Object[] { -256, true, "Hello", -1.1, new byte[] { 0x41, 0x42, 0x43 } };
            NetSocketSendData data = new NetSocketSendData(0x88, args);

            if (data.getBuildResult() == NetSocketSendDataBuildResult.Successful) 
            {
                while (true) 
                {
                    udpsocket.send(data, new NetSocketAddress("127.0.0.1", 10010));
                    
                    try {
                        Thread.sleep(5000);
                    } 
                    catch (Exception e) {}
                }
            }
        }
    }
}

class TcpServerThread extends Thread 
{
    private TcpServerAcceptCallback _callback;

    public TcpServerThread(TcpServerAcceptCallback callback) {
        _callback = callback;
    }

    @Override
    public void run() 
    {
        TcpServer tcpserver = NetworkComm.TcpListen(new NetSocketAddress("127.0.0.1", 10010));
        System.out.println("NetworkComm.TcpServer Started.");

        if (tcpserver.isRunning()) {
            tcpserver.setAcceptCallback(_callback);
        }
    }
}

class TcpClientThread extends Thread 
{
    private NetSocketReceivedCallback _callback;

    public TcpClientThread(NetSocketReceivedCallback callback) {
        _callback = callback;
    }

    @Override
    public void run() 
    {
        TcpSocket tcpsocket = NetworkComm.TcpConnect(new NetSocketAddress("127.0.0.1", 10010));

        if (tcpsocket.isAvailable()) 
        {
            System.out.println(String.format("NetworkComm.TcpClient Started. %s", tcpsocket.getLocalAddress()));
            tcpsocket.setReceivedCallback(_callback);

            Object[] args = new Object[] { -256, true, "Hello", -1.1, new byte[] { 0x41, 0x42, 0x43 } };
            NetSocketSendData data = new NetSocketSendData(0x88, args);

            if (data.getBuildResult() == NetSocketSendDataBuildResult.Successful)
            {
                while (true) 
                {
                    if (tcpsocket.isConnected()) 
                    {
                        tcpsocket.send(data);
                    } else
                        break;
                    
                    try {
                        Thread.sleep(5000);
                    } 
                    catch (Exception e) {}
                }
            }
        } 
    }
}

public class Main 
{
    public static void main(String[] args) 
    {
        NetSocketReceivedCallback receivedCallback = new NetSocketReceivedCallback() 
        {
            @Override
            public void callMethod(NetSocket socket, NetSocketReceivedData data) 
            {
                if (data.getResult() == NetSocketReceivedDataResult.Completed) 
                {
                    if (data.getCommand() == 0x88) 
                    {
                        //long a1 = Long.valueOf(String.valueOf(data.getArgs()[0]));
                        //boolean a2 = (boolean)data.getArgs()[1];
                        //String a3 = (String)data.getArgs()[2];
                        //double a4 = Double.valueOf(String.valueOf(data.getArgs()[3]));
                        var a1 = data.getArgs()[0];
                        var a2 = data.getArgs()[1];
                        var a3 = data.getArgs()[2];
                        var a4 = data.getArgs()[3];

                        String a5 = "";
                        byte[] ba = (byte[])data.getArgs()[4];
                        for (byte b: ba) {
                            if (a5 != "") a5 += ",";
                            a5 += String.format("0x%02X", b & 0xFF);                            
                        }

                        String protocol = "";
                        if (socket.getProtocolType() == NetSocketProtocolType.Tcp)
                            protocol = "TCP";
                        else if (socket.getProtocolType() == NetSocketProtocolType.Udp)
                            protocol = "UDP";
    
                        String output = String.format("%s %s (%d, %b, %s, %f, [%s])", 
                            protocol, data.getRemoteAddress(), a1, a2, a3, a4, a5);
    
                        System.out.println(output);                        
                    }
                } 
                else if (data.getResult() == NetSocketReceivedDataResult.Interrupted) 
                {
                    System.out.println("Interrupted");
                } 
                else if (data.getResult() == NetSocketReceivedDataResult.ParsingError) 
                {
                    System.out.println("Parsing-Error");
                } 
                else if (data.getResult() == NetSocketReceivedDataResult.Closed) 
                {
                    System.out.println("Close");
                    socket.close();
                }
            }
        };

        TcpServerAcceptCallback acceptCallback = new TcpServerAcceptCallback() 
        {
            @Override
            public void callMethod(TcpSocket tcpsocket) 
            {
                if (tcpsocket.isAvailable()) 
                {
                    System.out.println(String.format("NetworkComm.TcpClient Accepted. %s", tcpsocket.getRemoteAddress()));
                    tcpsocket.setReceivedCallback(receivedCallback);
                }
            }
        };

        UdpSocketThread thread1 = new UdpSocketThread(receivedCallback);
        thread1.start();

        try {
            Thread.sleep(1000);
        } 
        catch (Exception e) {}        

        TcpServerThread thread2 = new TcpServerThread(acceptCallback);
        thread2.start();

        try {
            Thread.sleep(1000);
        } 
        catch (Exception e) {}

        TcpClientThread thread3 = new TcpClientThread(receivedCallback);
        thread3.start();


        try {
            thread1.join();
            thread2.join();
            thread3.join();
        } 
        catch (Exception e) {}
    }
}

