package xyz.olooko.comm.netcomm;

public class CSocketDataArgLength
{
    private int _sz;
    private int _argL;

    public int getSize() 
    {
        return _sz;
    }
    
    public int getArgL() 
    {
        return _argL;
    }

    public CSocketDataArgLength(int sz, int argL)
    {
        _sz = sz;
        _argL = argL;
    }
}
