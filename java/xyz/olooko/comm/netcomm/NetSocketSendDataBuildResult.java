package xyz.olooko.comm.netcomm;

public enum NetSocketSendDataBuildResult {
    ByteArrayLengthOverflowError, 
    CommandValueOverflowError,
    DataTotalLengthOverflowError,
    DataTypeNotImplementedError,
	NoData,
    StringLengthOverflowError, 
    Successful
}
