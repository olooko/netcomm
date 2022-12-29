package xyz.olooko.comm.netcomm;

import java.net.InetAddress;
import java.net.InetSocketAddress;

public class NetSocketAddress 
{
    private InetAddress _inetAddress;
    private String _host;
    private int _port;

    public InetAddress getInetAddress() 
    {
        return _inetAddress;
    }
    
    public String getHost() 
    {
        return _host;
    }    
    
    public int getPort() 
    { 
        return _port;
    }

    public NetSocketAddress(String host, int port) 
    {
        _port = port;

        try {
            _inetAddress = InetAddress.getByName(host);
            _host = host;
        } 
        catch (Exception e) {}
    }

    public NetSocketAddress(InetAddress inetAddress, int port) 
    {
        _inetAddress = inetAddress;
        _host = inetAddress.getHostAddress();
        _port = port;
    }    

    public NetSocketAddress(InetSocketAddress inetSocketAddress) 
    {
        _inetAddress = inetSocketAddress.getAddress();
        _host = _inetAddress.getHostAddress();
        _port = inetSocketAddress.getPort();
    }  
    
    @Override
	public String toString() 
    {
		return String.format("%s:%d", _host, _port);
	}    
}
