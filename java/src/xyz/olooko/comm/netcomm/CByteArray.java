package xyz.olooko.comm.netcomm;

public class CByteArray implements IDataType
{
    private byte[] _value;

    public byte[] getValue() {
        return _value;
    }

    public CByteArray(byte[] value) {
        _value = value;
    }

    @Override
    public DataType getDataType() {
        return DataType.CByteArray;
    }

    @Override
    public String toString() {
        String s = "";
        for (byte b: _value) {
            if (s != "") s += ",";
            s += String.format("0x%02X", b & 0xFF);  
        }
        return s;
    }
}