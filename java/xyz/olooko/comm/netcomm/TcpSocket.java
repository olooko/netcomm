package xyz.olooko.comm.netcomm;

import java.net.InetSocketAddress;
import java.net.Socket;

public class TcpSocket extends NetSocket {
    
    private Socket _socket;

    public Boolean isConnected() {
        return isAvailable() && _socket.isConnected();
    }

    private NetSocketAddress _remoteAddress;
    public NetSocketAddress getRemoteAddress() {
        return _remoteAddress;
    }  

    public TcpSocket(Socket s) { 
        super(s, NetSocketProtocolType.Tcp);

        _socket = s;

        InetSocketAddress address = (InetSocketAddress)s.getRemoteSocketAddress();
        _remoteAddress = new NetSocketAddress(address.getAddress(), address.getPort());        
    }

    public void send(NetSocketSendData data) {
        super.send(data, null);
    }
}

