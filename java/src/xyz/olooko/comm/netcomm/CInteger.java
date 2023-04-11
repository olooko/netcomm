package xyz.olooko.comm.netcomm;

public class CInteger implements IDataType
{
    private Long _value;

    public long getValue() {
        return _value;
    }

    public CInteger(long value) {
        _value = value;
    }

    @Override
    public String toString() {
        return _value.toString();
    }
}
