package xyz.olooko.comm.netcomm;

public class NetSocketDataArgLength 
{
    private int _sz;
    private int _argL;

    public int getSize() 
    {
        return _sz;
    }
    
    public int getArgLength() 
    {
        return _argL;
    }

    public NetSocketDataArgLength(int sz, int argL) 
    {
        _sz = sz;
        _argL = argL;
    }
}
