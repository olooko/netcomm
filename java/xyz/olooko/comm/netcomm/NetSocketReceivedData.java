package xyz.olooko.comm.netcomm;

public class NetSocketReceivedData {

    private Object[] _args;
    public Object[] getArgs() {
        return _args;
    }

    private byte _command;
    public byte getCommand() {
        return _command;
    }
   
    private NetSocketAddress _address;
    public NetSocketAddress getRemoteAddress() {
        return _address;
    }

    private NetSocketReceivedDataResult _result;
    public NetSocketReceivedDataResult getResult() {
        return _result;
    }

    public NetSocketReceivedData(byte command, Object[] args, NetSocketReceivedDataResult result, NetSocketAddress address) {
        _command = command;
        _args = args;
        _result = result;
        _address = address;
    }
}


