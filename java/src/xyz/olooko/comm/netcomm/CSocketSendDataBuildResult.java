package xyz.olooko.comm.netcomm;

public enum CSocketSendDataBuildResult
{
    ByteArrayLengthOverflowError, 
    CommandValueOverflowError,
    DataTotalLengthOverflowError,
    DataTypeNotImplementedError,
	NoData,
    StringLengthOverflowError, 
    Successful
}
