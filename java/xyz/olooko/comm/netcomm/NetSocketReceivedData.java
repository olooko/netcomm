package xyz.olooko.comm.netcomm;

public class NetSocketReceivedData 
{
    private byte _command;
    private Object[] _args;
    private NetSocketReceivedDataResult _result;
    private NetSocketAddress _address;

    public Object[] getArgs() 
    {
        return _args;
    }
    
    public int getCommand() 
    {
        return _command & 0xFF;
    }
    
    public NetSocketAddress getRemoteAddress() 
    {
        return _address;
    }
    
    public NetSocketReceivedDataResult getResult() 
    {
        return _result;
    }

    public NetSocketReceivedData(int command, Object[] args, NetSocketReceivedDataResult result, NetSocketAddress address) 
    {
        _command = (byte)(command & 0xFF);
        _args = args;
        _result = result;
        _address = address;
    }
}


