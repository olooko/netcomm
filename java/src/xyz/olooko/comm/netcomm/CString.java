package xyz.olooko.comm.netcomm;

public class CString implements IDataType
{
    private String _value;

    public String getValue() {
        return _value;
    }

    public CString(String value) {
        _value = value;
    }

    @Override
    public DataType getDataType() {
        return DataType.CString;
    }

    @Override
    public String toString() {
        return _value;
    }
}
