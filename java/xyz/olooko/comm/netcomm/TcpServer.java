package xyz.olooko.comm.netcomm;

import java.net.ServerSocket;
import java.net.Socket;

public class TcpServer implements Runnable {

    public boolean isStarted() {
        return _server != null;
    }

    private ServerSocket _server;

    private TcpServerAcceptCallback _callback;

    public TcpServer(ServerSocket s) { _server = s; }

    public void setAcceptCallback(TcpServerAcceptCallback callback) {
        _callback = callback;
        Thread t = new Thread(this);
        t.start();
    }

    @Override
    public void run() {
        if (_callback == null) return;

        while (_server != null) {
            Socket s = null;
            try {
                s = _server.accept();
            } catch (Exception e) {}

            _callback.callMethod(new TcpSocket(s));
        }
    }

    public void close() {
        try {
            _server.close();
            _server = null;
        } catch (Exception e) {}       
    }
}
