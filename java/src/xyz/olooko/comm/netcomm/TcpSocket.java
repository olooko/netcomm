package xyz.olooko.comm.netcomm;

import java.net.InetSocketAddress;
import java.net.Socket;

public class TcpSocket extends CSocket
{
    private CSocketAddress _remoteAddress;

    public Boolean isConnected() 
    {
        return isAvailable() && _socket.isConnected();
    }
    
    public CSocketAddress getRemoteAddress()
    {
        return _remoteAddress;
    }  

    public TcpSocket(Socket s) 
    { 
        super(s, CSocketProtocolType.Tcp);

        _remoteAddress = new CSocketAddress("0.0.0.0", 0);

        if (s != null) 
        {
            InetSocketAddress address = (InetSocketAddress)s.getRemoteSocketAddress();
            _remoteAddress = new CSocketAddress(address.getAddress(), address.getPort());
        }  
    }

    public void send(CSocketSendData data)
    {
        super.send(data, null);
    }
}

