package xyz.olooko.comm.netcomm;

import java.net.DatagramSocket;

public class UdpSocket extends CSocket
{
    public UdpSocket(DatagramSocket s) 
    { 
        super(s, CSocketProtocolType.Udp);
    }

    public void send(CSocketSendData data, CSocketAddress address)
    {
        super.send(data, address);
    }    
}