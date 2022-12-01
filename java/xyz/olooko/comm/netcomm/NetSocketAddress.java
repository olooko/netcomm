package xyz.olooko.comm.netcomm;

import java.net.InetAddress;

public class NetSocketAddress {

    private InetAddress _ipaddress;
    public InetAddress getInetAddress() {
        return _ipaddress;
    }

    private String _host;
    public String getHost() {
        return _host;
    }    

    private int _port;
    public int getPort() { 
        return _port;
    }

    public NetSocketAddress(String host, int port) {
        _port = port;
        try {
            _ipaddress = InetAddress.getByName(host);
            _host = host;
        } catch (Exception e) {}
    }

    public NetSocketAddress(InetAddress ipaddress, int port) {
        _ipaddress = ipaddress;
        _host = ipaddress.getHostAddress();
        _port = port;
    }    
}
