package xyz.olooko.comm.netcomm;

public interface NetSocketReceivedCallback {
    void callMethod(NetSocket socket, NetSocketReceivedData data);
}