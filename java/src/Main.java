import xyz.olooko.comm.netcomm.*;

class UdpSocketThread extends Thread 
{
    private CSocketReceivedCallback _callback;

    public UdpSocketThread(CSocketReceivedCallback callback) {
        _callback = callback;
    }

    @Override
    public void run() 
    {
        CSocketAddress address = new CSocketAddress("127.0.0.1", 10010);
        UdpSocket udpsocket = NetworkComm.UdpCast(address);

        if (udpsocket.isAvailable()) 
        {
            System.out.println(String.format("UdpSocket Started. %s", udpsocket.getLocalAddress()));
            udpsocket.setReceivedCallback(_callback);

            CSocketDataArgs args = new CSocketDataArgs();
            args.add(new CInteger(-256));
            args.add(new CBoolean(true));
            args.add(new CString("Hello"));
            args.add(new CFloat(-1.1));
            args.add(new CByteArray(new byte[] { 0x41, 0x42, 0x43 }));

            CSocketSendData data = new CSocketSendData(0x88, args);

            if (data.getBuildResult() == CSocketSendDataBuildResult.Successful)
            {
                while (true) 
                {
                    udpsocket.send(data, address);
                    
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
        TcpServer tcpserver = NetworkComm.TcpListen(new CSocketAddress("127.0.0.1", 10010));
        System.out.println("TcpServer Started.");

        if (tcpserver.isRunning()) {
            tcpserver.setAcceptCallback(_callback);
        }
    }
}

class TcpClientThread extends Thread 
{
    private CSocketReceivedCallback _callback;

    public TcpClientThread(CSocketReceivedCallback callback) {
        _callback = callback;
    }

    @Override
    public void run() 
    {
        TcpSocket tcpsocket = NetworkComm.TcpConnect(new CSocketAddress("127.0.0.1", 10010));

        if (tcpsocket.isAvailable()) 
        {
            System.out.println(String.format("TcpClient Started. %s", tcpsocket.getLocalAddress()));
            tcpsocket.setReceivedCallback(_callback);

            CSocketDataArgs args = new CSocketDataArgs();
            args.add(new CInteger(-256));
            args.add(new CBoolean(true));
            args.add(new CString("Hello"));
            args.add(new CFloat(-1.1));
            args.add(new CByteArray(new byte[] { 0x41, 0x42, 0x43 }));

            CSocketSendData data = new CSocketSendData(0x88, args);

            if (data.getBuildResult() == CSocketSendDataBuildResult.Successful)
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
        CSocketReceivedCallback receivedCallback = new CSocketReceivedCallback()
        {
            @Override
            public void callMethod(CSocket socket, CSocketReceivedData data)
            {
                if (data.getResult() == CSocketReceivedDataResult.Completed)
                {
                    if (data.getCommand() == 0x88) 
                    {
                        CSocketDataArgs args = data.getArgs();

                        var a1 = (CInteger)args.at(0);
                        var a2 = (CBoolean)args.at(1);
                        var a3 = (CString)args.at(2);
                        var a4 = (CFloat)args.at(3);
                        var a5 = (CByteArray)args.at(4);

                        String protocol = "";
                        if (socket.getProtocolType() == CSocketProtocolType.Tcp)
                            protocol = "TCP";
                        else if (socket.getProtocolType() == CSocketProtocolType.Udp)
                            protocol = "UDP";

                        String output = String.format("%s %s (%s, %s, %s, %s, [%s])",
                            protocol, data.getRemoteAddress(), a1, a2, a3, a4, a5);
    
                        System.out.println(output);                        
                    }
                } 
                else if (data.getResult() == CSocketReceivedDataResult.Interrupted)
                {
                    System.out.println("Interrupted");
                } 
                else if (data.getResult() == CSocketReceivedDataResult.ParsingError)
                {
                    System.out.println("Parsing-Error");
                } 
                else if (data.getResult() == CSocketReceivedDataResult.Closed)
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
                    System.out.println(String.format("TcpClient Accepted. %s", tcpsocket.getRemoteAddress()));
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

