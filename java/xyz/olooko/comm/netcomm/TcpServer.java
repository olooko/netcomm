package xyz.olooko.comm.netcomm;

import java.net.ServerSocket;
import java.net.Socket;

public class TcpServer {

    public boolean isStarted() {
        return _server != null;
    }

    private ServerSocket _server;

    public TcpServer(ServerSocket s) { _server = s; }

    public TcpSocket accept() {
        Socket s = null;
        try {
            s = _server.accept();
        } catch (Exception e) {}
        return new TcpSocket(s);
    }

    public void close() {
        try {
            _server.close();
            _server = null;
        } catch (Exception e) {}       
    }
}
