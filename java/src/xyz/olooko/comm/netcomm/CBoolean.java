package xyz.olooko.comm.netcomm;

public class CBoolean implements IDataType
{
    private Boolean _value;

    public boolean getValue() {
        return _value;
    }

    public CBoolean(boolean value) {
        _value = value;
    }

    @Override
    public String toString() {
        return _value.toString();
    }
}
