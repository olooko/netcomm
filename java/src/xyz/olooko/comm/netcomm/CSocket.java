package xyz.olooko.comm.netcomm;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetSocketAddress;
import java.net.Socket;

public abstract class CSocket implements Runnable
{
    protected Socket _socket;
    protected DatagramSocket _dgram;

    private CSocketData _data;
    private CSocketReceivedCallback _callback;
    private CSocketDataManipulationResult _result;
    
    private CSocketAddress _localAddress;
    private CSocketProtocolType _protocol;

    public Boolean isAvailable() 
    {
        if (_protocol == CSocketProtocolType.Tcp) {
            return _socket != null;
        }
        else if (_protocol == CSocketProtocolType.Udp) {
            return _dgram != null;           
        }
        return false; 
    }

    public CSocketAddress getLocalAddress()
    {
        return _localAddress;
    }  

    public CSocketProtocolType getProtocolType()
    {
        return _protocol;
    }    

    public CSocket(Socket s, CSocketProtocolType protocolType)
    {
        initialize(s, null, protocolType);
    }

    public CSocket(DatagramSocket d, CSocketProtocolType protocolType)
    {
        initialize(null, d, protocolType);
    }

    private void initialize(Socket s, DatagramSocket d, CSocketProtocolType protocolType)
    {
        _socket = s;
        _dgram = d;

        _data = new CSocketData();
        _protocol = protocolType;
        _result = CSocketDataManipulationResult.NoData;
        _localAddress = new CSocketAddress("0.0.0.0", 0);

        if (isAvailable())
        {
            if (_protocol == CSocketProtocolType.Tcp) {
                InetSocketAddress address = (InetSocketAddress)_socket.getLocalSocketAddress();
                _localAddress = new CSocketAddress(address.getAddress(), address.getPort());
            }
            else if (_protocol == CSocketProtocolType.Udp) {
                InetSocketAddress address = (InetSocketAddress)_dgram.getLocalSocketAddress();
                _localAddress = new CSocketAddress(address.getAddress(), address.getPort());
            }
        }
    }

    public void close() 
    {
        if (isAvailable()) 
        {
            try {
                if (_protocol == CSocketProtocolType.Tcp) {
                    _socket.close();
                } 
                else if (_protocol == CSocketProtocolType.Udp) {
                    _dgram.close();
                }  
            } 
            catch (Exception e) {}
        }
    }

    public void setReceivedCallback(CSocketReceivedCallback callback)
     {
        if (isAvailable()) 
        {
            _callback = callback;

            Thread t = new Thread(this);
            t.start();
        }
    }
         
    protected void send(CSocketSendData data, CSocketAddress address)
    {
        if (isAvailable()) 
        {
            try {
                if (_protocol == CSocketProtocolType.Tcp)
                {
                    _socket.getOutputStream().write(data.getBytes(), 0, data.getLength());
                    _socket.getOutputStream().flush();
                } 
                else if (_protocol == CSocketProtocolType.Udp)
                {
                    DatagramPacket packet = new DatagramPacket(data.getBytes(), data.getLength(), address.getInetAddress(), address.getPort());
                    _dgram.send(packet);
                }
            } 
            catch (Exception e) {}
        }
    }  

    @Override
    public void run() 
    {    
        byte[] buffer = new byte[4096];
        CSocketAddress remoteAddress = new CSocketAddress("0.0.0.0", 0);

        while (true) 
        {
            int bytesTransferred = 0;           

            if (_protocol == CSocketProtocolType.Tcp)
            {
                try {
                    bytesTransferred = _socket.getInputStream().read(buffer);
                } 
                catch (Exception e) {}

                remoteAddress = new CSocketAddress((InetSocketAddress)_socket.getRemoteSocketAddress());
            }
            else if (_protocol == CSocketProtocolType.Udp)
            {
                try {
                    DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                    _dgram.receive(packet);

                    remoteAddress = new CSocketAddress((InetSocketAddress)packet.getSocketAddress());

                    bytesTransferred = packet.getLength();
                } 
                catch (Exception e) {}
            }

            if (bytesTransferred > 0) 
            {
                _data.append(buffer, bytesTransferred);
                
                while (true) 
                {
                    _result = _data.manipulate();

                    if (_result == CSocketDataManipulationResult.Completed)
                    {
                        _callback.callMethod(this, new CSocketReceivedData(_data.getCommand(), _data.getArgs(), CSocketReceivedDataResult.Completed, remoteAddress));
                        continue;
                    } 
                    else if (_result == CSocketDataManipulationResult.ParsingError)
                    {
                        _callback.callMethod(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress));
                        return;
                    } 
                    else if (_result == CSocketDataManipulationResult.InProgress)
                    {
                        final CSocketAddress paramAddress = remoteAddress;
                        Thread t = new Thread(() -> {
                            try {
                                Thread.sleep(15000);
                            } 
                            catch (Exception e) {}
                
                            if (_result == CSocketDataManipulationResult.InProgress)
                                _callback.callMethod(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, paramAddress));
                        });
                        t.start();
                        break;
                    } 
                    else if (_result == CSocketDataManipulationResult.NoData)
                    {
                        break;
                    }
                }
                continue;
            } 
            else 
            {
                _callback.callMethod(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.Closed, remoteAddress));
                break;
            }
        }  
    } 
}
