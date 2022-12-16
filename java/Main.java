import java.util.ArrayList;

import xyz.olooko.comm.netcomm.NetSocket;
import xyz.olooko.comm.netcomm.NetSocketAddress;
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

class TcpServerThread extends Thread {
    private TcpServerAcceptCallback _callback;
    public TcpServerThread(TcpServerAcceptCallback callback) {
        _callback = callback;
    }

    @Override
    public void run() {
        TcpServer tcpserver = NetworkComm.TcpListen(new NetSocketAddress("127.0.0.1", 10010));
        System.out.println("NetworkComm.TcpServer Started...");

        if (tcpserver.isStarted()) {
            tcpserver.setAcceptCallback(_callback);
        }
    }
}

class TcpClientThread extends Thread {
    private NetSocketReceivedCallback _callback;
    public TcpClientThread(NetSocketReceivedCallback callback) {
        _callback = callback;
    }

    @Override
    public void run() {
        TcpSocket tcpsocket = NetworkComm.TcpConnect(new NetSocketAddress("127.0.0.1", 10010));

        if (tcpsocket.isAvailable()) {
            System.out.println("NetworkComm.TcpSocket Started...");
            tcpsocket.setReceivedCallback(_callback);

            while (true) {
                if (tcpsocket.isConnected()) {
                    Object[] args = new Object[] { -256, true, "Hello", -1.1, new byte[] { 0x41, 0x42, 0x43 } };
                    NetSocketSendData data = new NetSocketSendData((byte)0x88, args);
                    if (data.getBuildResult() == NetSocketSendDataBuildResult.Successful)
                        tcpsocket.send(data);
                } else
                    break;
                
                try {
                    Thread.sleep(5 * 1000);
                } catch (Exception e) {}
            }
        } 
    }
}

class UdpSocketThread extends Thread {
    private NetSocketReceivedCallback _callback;
    public UdpSocketThread(NetSocketReceivedCallback callback) {
        _callback = callback;
    }

    @Override
    public void run() {
        UdpSocket udpsocket = NetworkComm.UdpCast(new NetSocketAddress("127.0.0.1", 10010));

        if (udpsocket.isAvailable()) {
            System.out.println("NetworkComm.UdpSocket Started...");
            udpsocket.setReceivedCallback(_callback);

            while (true) {
                Object[] args = new Object[] { -256, true, "Hello", -1.1, new byte[] { 0x41, 0x42, 0x43 } };
                NetSocketSendData data = new NetSocketSendData((byte)0x88, args);
                if (data.getBuildResult() == NetSocketSendDataBuildResult.Successful)
                    udpsocket.send(data, new NetSocketAddress("127.0.0.1", 10010));
                
                try {
                    Thread.sleep(5 * 1000);
                } catch (Exception e) {}
            }
        }
    }
}

public class Main {
    public static void main(String[] args) {

        NetSocketReceivedCallback receivedCallback = new NetSocketReceivedCallback() {
            @Override
            public void callMethod(NetSocket socket, NetSocketReceivedData data) {
                if (data.getResult() == NetSocketReceivedDataResult.Completed) {
                    ArrayList<String> args = new ArrayList<>();
                    for (int n = 0; n < data.getArgs().length; n++) {
                        Object arg = data.getArgs()[n];
                        if (arg.getClass().getSimpleName().toLowerCase().equals("byte[]")) {
                            byte[] ba = (byte[])arg;
                            StringBuilder sb = new StringBuilder();
                            sb.append("[");
                            for (byte b: ba) {
                                sb.append(String.format(" 0x%02X", b & 0xff));                            
                            }
                            sb.append("]");
                            args.add(sb.toString());
                        } 
                        else
                            args.add(arg.toString());
                    }

                    System.out.println(String.format("protocol: %s, command: 0x%02X, args: {%s}", socket.getProtocolType(), data.getCommand() & 0xFF, String.join(", ", args)));
                } else if (data.getResult() == NetSocketReceivedDataResult.Interrupted) {
                    System.out.println("Interrupted");
                } else if (data.getResult() == NetSocketReceivedDataResult.ParsingError) {
                    System.out.println("parsing-error");
                } else if (data.getResult() == NetSocketReceivedDataResult.Closed) {
                    System.out.println("close");
                    socket.close();
                }
            }
        };

        TcpServerAcceptCallback acceptCallback = new TcpServerAcceptCallback() {
            @Override
            public void callMethod(TcpSocket tcpsocket) {
                if (tcpsocket.isAvailable()) {
                    System.out.println("NetworkComm.TcpSocket Accepted");
                    tcpsocket.setReceivedCallback(receivedCallback);
                }
            }
        };

        TcpServerThread thread1 = new TcpServerThread(acceptCallback);
        thread1.start();

        TcpClientThread thread2 = new TcpClientThread(receivedCallback);
        thread2.start();

        UdpSocketThread thread3 = new UdpSocketThread(receivedCallback);
        thread3.start();

        try {
            thread1.join();
            thread2.join();
            thread3.join();
        } catch (Exception e) 
        {}
    }
}

