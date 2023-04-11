package xyz.olooko.comm.netcomm;

import java.util.ArrayList;

public class CSocketDataArgs
{
    private ArrayList<IDataType> _list;
    
    public int getLength() {
        return _list.size();
    }

    public CSocketDataArgs() {
        _list = new ArrayList<IDataType>();
    }

    public void add(IDataType valueType) {
        _list.add(valueType);
    }

    public IDataType at(int index) {
        return _list.get(index);
    }

    public void clear() {
        _list.clear();
    }  
}
