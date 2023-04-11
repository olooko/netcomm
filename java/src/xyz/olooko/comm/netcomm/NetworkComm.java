package xyz.olooko.comm.netcomm;

import java.net.DatagramSocket;
import java.net.ServerSocket;
import java.net.Socket;

public class NetworkComm {
    
    public static TcpSocket TcpConnect(CSocketAddress address)
    {
        Socket s = null;

        try {
            s = new Socket(address.getHost(), address.getPort());
        } 
        catch (Exception e) {}

        return new TcpSocket(s);
    }

    public static TcpServer TcpListen(CSocketAddress address)
    {
        ServerSocket s = null;

        try {
            s = new ServerSocket(address.getPort(), 0, address.getInetAddress());
        } 
        catch (Exception e) {}

        return new TcpServer(s);
    }

    public static UdpSocket UdpCast(CSocketAddress address)
    {
        DatagramSocket s = null;

        try {
            s = new DatagramSocket(address.getPort(), address.getInetAddress());
        } 
        catch (Exception e) {}
        
        return new UdpSocket(s);
    }
}


