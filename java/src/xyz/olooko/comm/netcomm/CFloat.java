package xyz.olooko.comm.netcomm;

public class CFloat implements IDataType
{
    private Double _value;

    public double getValue() {
        return _value;
    }

    public CFloat(double value) {
        _value = value;
    }

    @Override
    public DataType getDataType() {
        return DataType.CFloat;
    }

    @Override
    public String toString() {
        return _value.toString();
    }
}
