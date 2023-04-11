package xyz.olooko.comm.netcomm;

public interface CSocketReceivedCallback
{
    void callMethod(CSocket socket, CSocketReceivedData data);
}