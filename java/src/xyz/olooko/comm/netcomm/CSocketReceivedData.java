package xyz.olooko.comm.netcomm;

public class CSocketReceivedData
{
    private byte _command;
    private CSocketDataArgs _args;
    private CSocketReceivedDataResult _result;
    private CSocketAddress _address;

    public CSocketDataArgs getArgs()
    {
        return _args;
    }
    
    public int getCommand() 
    {
        return _command & 0xFF;
    }
    
    public CSocketAddress getRemoteAddress()
    {
        return _address;
    }
    
    public CSocketReceivedDataResult getResult()
    {
        return _result;
    }

    public CSocketReceivedData(int command, CSocketDataArgs args, CSocketReceivedDataResult result, CSocketAddress address)
    {
        _command = (byte)(command & 0xFF);
        _args = args;
        _result = result;
        _address = address;
    }
}


