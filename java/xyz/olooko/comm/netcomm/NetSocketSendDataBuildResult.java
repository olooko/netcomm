package xyz.olooko.comm.netcomm;

public enum NetSocketSendDataBuildResult {
	ByteArrayOverflowError, 
    NoData,
    StringOverflowError, 
    Successful,
    TextOverflowError,
    TypeNotImplementedError
}
