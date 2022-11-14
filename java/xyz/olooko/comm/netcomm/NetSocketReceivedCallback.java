package xyz.olooko.comm.netcomm;

public interface NetSocketReceivedCallback {
    void callbackMethod(NetSocket socket, NetSocketReceivedData data);
}