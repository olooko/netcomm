package xyz.olooko.comm.netcomm;

import java.net.DatagramSocket;

public class UdpSocket extends NetSocket 
{
    public UdpSocket(DatagramSocket s) 
    { 
        super(s, NetSocketProtocolType.Udp); 
    }

    public void send(NetSocketSendData data, NetSocketAddress address) 
    {
        super.send(data, address);
    }    
}