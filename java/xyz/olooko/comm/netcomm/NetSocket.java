package xyz.olooko.comm.netcomm;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetSocketAddress;
import java.net.Socket;

public class NetSocket implements Runnable {
    
    private static final int BUF_SZ = 4096;
    private static final int INTRPT_TM = 4000;

    private NetSocketReceivedCallback _callback;

    private byte[] _buffer;
    private NetSocketData _data;

    private Socket _socket;
    private DatagramSocket _dgram;

    private NetSocketDataManipulationResult _result;

    public Boolean isAvailable() {
        if (_protocol == NetSocketProtocolType.Tcp)
            return _socket != null;
        else if (_protocol == NetSocketProtocolType.Udp)
            return _dgram != null;           
        return false; 
    }

    private NetSocketAddress _localAddress;
    public NetSocketAddress getLocalAddress() {
        return _localAddress;
    }  

    private NetSocketProtocolType _protocol;
    public NetSocketProtocolType getProtocolType() {
        return _protocol;
    }    

    public NetSocket(Socket s, NetSocketProtocolType protocolType) {
        _buffer = new byte[BUF_SZ];
        _data = new NetSocketData();
        _protocol = protocolType;
        _socket = s;

        _result = NetSocketDataManipulationResult.NoData;

        InetSocketAddress address = (InetSocketAddress)_socket.getLocalSocketAddress();
        _localAddress = new NetSocketAddress(address.getAddress(), address.getPort());
    }

    public NetSocket(DatagramSocket s, NetSocketProtocolType protocolType) {
        _buffer = new byte[BUF_SZ];
        _data = new NetSocketData();
        _protocol = protocolType;
        _dgram = s;

        InetSocketAddress address = (InetSocketAddress)_dgram.getLocalSocketAddress();
        _localAddress = new NetSocketAddress(address.getAddress(), address.getPort());        
    }    

    public void close() {
        try {
            if (_protocol == NetSocketProtocolType.Tcp) {
                _socket.close();
            } else if (_protocol == NetSocketProtocolType.Udp) {
                _dgram.close();
            }  
        } catch (Exception e) {}
    }
     
    protected void send(NetSocketSendData data, NetSocketAddress address) {
        try {
            if (_protocol == NetSocketProtocolType.Tcp) {
                _socket.getOutputStream().write(data.getBytes(), 0, data.getLength());
                _socket.getOutputStream().flush();
            } else if (_protocol == NetSocketProtocolType.Udp) {
				DatagramPacket packet = new DatagramPacket(data.getBytes(), data.getLength(), address.getInetAddress(), address.getPort());
                _dgram.send(packet);
            }
        } catch (Exception e) {}
    }  

    public void setReceivedCallback(NetSocketReceivedCallback callback) {
        _callback = callback;
        Thread t = new Thread(this);
        t.start();
    }

    @Override
    public void run() {
        if (_callback == null) return;

        while (true) {
            int bytesTransferred = 0;
            NetSocketAddress remoteAddress = new NetSocketAddress("0.0.0.0", 0);

            if (_protocol == NetSocketProtocolType.Tcp) {
                try {
                    bytesTransferred = _socket.getInputStream().read(_buffer);
                } catch (Exception e) {}

                InetSocketAddress address = (InetSocketAddress)_socket.getRemoteSocketAddress();
                remoteAddress = new NetSocketAddress(address.getAddress(), address.getPort());                
            }
            else if (_protocol == NetSocketProtocolType.Udp) {
                try {
                    DatagramPacket packet = new DatagramPacket(_buffer, _buffer.length);
                    _dgram.receive(packet);

                    InetSocketAddress address = (InetSocketAddress)packet.getSocketAddress();
                    remoteAddress = new NetSocketAddress(address.getAddress(), address.getPort());

                    bytesTransferred = packet.getLength();
                } catch (Exception e) {
                }
            }

            if (bytesTransferred > 0) {
                
                _data.Append(_buffer, bytesTransferred);

                while (true) {
                    _result = _data.Manipulate();

                    if (_result == NetSocketDataManipulationResult.Completed) {
                        _callback.callbackMethod(this, new NetSocketReceivedData(_data.getCommand(), _data.getArgs(), NetSocketReceivedDataResult.Completed, remoteAddress));
                        continue;
                    } else if (_result == NetSocketDataManipulationResult.ParsingError) {
                        _callback.callbackMethod(this, new NetSocketReceivedData((byte)0x00, new Object[] {}, NetSocketReceivedDataResult.ParsingError, remoteAddress));
                        return;
                    } else if (_result == NetSocketDataManipulationResult.InProgress) {
                        checkInterruptedTimeout(INTRPT_TM, _callback, remoteAddress);
                        break;
                    } else if (_result == NetSocketDataManipulationResult.NoData) {
                        break;
                    }
                }
                continue;
            } else {
                _callback.callbackMethod(this, new NetSocketReceivedData((byte)0x00, new Object[] {}, NetSocketReceivedDataResult.Closed, remoteAddress));
                break;
            }
        }  
    } 
    
    private void checkInterruptedTimeout(int milliseconds, NetSocketReceivedCallback callback, NetSocketAddress address)
    {
        Thread t = new Thread(() -> {
            try {
                Thread.sleep(milliseconds);
            } catch (Exception e) {}

            if (_result == NetSocketDataManipulationResult.InProgress)
                callback.callbackMethod(this, new NetSocketReceivedData((byte)0x00, new Object[] {}, NetSocketReceivedDataResult.Interrupted, address));
        });
        t.start();
    }
  
}
