import 'dart:async';
import 'dart:convert';
import 'dart:core';
import 'dart:ffi';
import 'dart:io';
import 'dart:typed_data';

enum DataType
{
    CBoolean, CByteArray, CFloat, CInteger, CString
}

abstract class IDataType 
{
    DataType getDataType();
    String toString();
}

class CBoolean implements IDataType 
{
    bool _value = false;
    bool get value => _value;

    CBoolean(bool value)
    {
        _value = value;
    }

    DataType getDataType()
    {
        return DataType.CBoolean;
    }

    String toString()
    {
        return _value.toString();
    }
}

class CByteArray implements IDataType
{
    Uint8List _value = Uint8List(0);
    Uint8List get value => _value;

    CByteArray(Uint8List value)
    {
        _value = value;
    }

    DataType getDataType()
    {
        return DataType.CByteArray;
    }

    String toString()
    {
        String s = "";
        _value.forEach((b) {
            if (s != "") s += ",";
            s += "0x" + b.toRadixString(16);
        });

        return s;
    }    
}

class CFloat implements IDataType
{
    double _value = 0.0;
    double get value => _value;

    CFloat(double value)
    {
        _value = value;
    }

    DataType getDataType()
    {
        return DataType.CFloat;
    }

    String toString()
    {
        return _value.toString();
    }    
}

class CInteger implements IDataType
{
    int _value = 0;
    int get value => _value;

    CInteger(int value)
    {
        _value = value;
    }

    DataType getDataType()
    {
        return DataType.CInteger;
    }

    String toString()
    {
        return _value.toString();
    }    
}

class CString implements IDataType
{
    String _value = '';
    String get value => _value;

    CString(String value)
    {
        _value = value;
    }

    DataType getDataType()
    {
        return DataType.CString;
    }

    String toString()
    {
        return _value;
    }    
}

abstract class CSocket extends Object 
{  
    var _socket;
    bool _connected = false;

    CSocketData _data = CSocketData();
    CSocketDataManipulationResult _result = CSocketDataManipulationResult.NoData;
    CSocketAddress _localAddress = CSocketAddress('0.0.0.0', 0);
    CSocketProtocolType _protocol = CSocketProtocolType.Udp;

    bool get available => _socket != null;
    CSocketAddress get localAddress => _localAddress; 
    CSocketProtocolType get protocolType => _protocol; 

    CSocket(var s, CSocketProtocolType protocol) 
    {
        _protocol = protocol;
        _socket = s;

        if (this.available)
            _localAddress = CSocketAddress(_socket.address.host, _socket.port);
    }

    void close() 
    {
        if (this.available)
            _socket!.close();
    }

    void setReceivedCallback(Function callback) async
    {
        if (this.available) 
        {
            _socket!.listen((RawSocketEvent e) {
                Uint8List buffer = Uint8List(0);
                String host = '0.0.0.0';
                int port = 0;

                if (_protocol == CSocketProtocolType.Tcp)
                {
                    Uint8List? b = _socket!.read();
                    if (b == null) return;

                    if (b.length > 0) 
                        _connected = true;
                    else 
                        _connected = false;

                    buffer = b;
                    host = _socket.remoteAddress.host;
                    port = _socket.remotePort;
                }
                else if (_protocol == CSocketProtocolType.Udp) 
                {
                    Datagram? d = _socket!.receive();
                    if (d == null) return;

                    buffer = d.data;
                    host = d.address.host;
                    port = d.port;
                }

                _receiveProc(buffer, callback, CSocketAddress(host, port));
            });
        }
    }

    void _receiveProc(Uint8List buffer, Function callback, CSocketAddress remoteAddress) 
    {
        _data.append(buffer);

        while (true) 
        {
            _result = _data.manipulate();

            if (_result == CSocketDataManipulationResult.Completed) 
            {
                callback(this, CSocketReceivedData(_data.command, _data.args, CSocketReceivedDataResult.Completed, remoteAddress));
                continue;
            }
            else if (_result == CSocketDataManipulationResult.ParsingError) 
            {
                callback(this, CSocketReceivedData(0x00, CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress));
                return;
            }
            else if (_result == CSocketDataManipulationResult.InProgress) 
            {
                var me = this;

                Timer(Duration(seconds: 15), () {
                    if (_result == CSocketDataManipulationResult.InProgress)
                        callback(me, CSocketReceivedData(0x00, CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, remoteAddress));
                });
                break;
            }
            else if (_result == CSocketDataManipulationResult.NoData) 
            {
                break;
            }
        }
    }

    void _send(CSocketSendData data, CSocketAddress? address) 
    {
        if (this.available)
            _sendProc(data, address, 0);
    }

    void _sendProc(CSocketSendData data, CSocketAddress? address, int bytesTransferred) 
    {
        num length = 0;

        if (_protocol == CSocketProtocolType.Tcp) 
        {
            length = _socket!.write(data.bytes.sublist(bytesTransferred));

            if (length > 0) 
                _connected = true;
            else 
                _connected = false;
        }
        else if (_protocol == CSocketProtocolType.Udp)
        {    
            length = _socket!.send(data.bytes.sublist(bytesTransferred), InternetAddress(address!.host), address!.port);
        }

        if (length > 0)
        {
            bytesTransferred += length as int;

            if (bytesTransferred < data.length)
                _sendProc(data, address, bytesTransferred);
        }
    }
}

class CSocketAddress 
{
    String _host = '0.0.0.0';
    int _port = 0;

    String get host => _host;
    int get port => _port;

    CSocketAddress(String host, int port)
    {
        _host = host;
        _port = port;
    }

    @override
    String toString() {
        return '${_host}:${_port}';
    }    
}

class CSocketData 
{
    Uint8List _data = Uint8List(0);
    int _datapos = 0;
    int _checksum = 0x00;
    CSocketDataParsingStep _step = CSocketDataParsingStep.SOH;
    int _textlen = 0;
    int _command = 0x00;
    CSocketDataArgs _args = CSocketDataArgs();

    CSocketDataArgs get args => _args; 
    int get command => _command;

    void append(Uint8List buffer) 
    {
        BytesBuilder builder = BytesBuilder();
        builder.add(_data);
        builder.add(buffer);

        _data = builder.takeBytes();
    }

    CSocketDataManipulationResult manipulate() 
    {
        while (true)
        {
            int datalen = _data.length - _datapos;

            switch (_step) 
            {
                case CSocketDataParsingStep.SOH:
                    if (datalen > 0) 
                    {
                        if (_data[_datapos] == 0x01)
                        {
                            _datapos += 1;
                            _step = CSocketDataParsingStep.OTL;
                            continue;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case CSocketDataParsingStep.OTL:
                    if (datalen > 0) 
                    {
                        if (Uint8List.fromList([0x11, 0x12, 0x14]).contains(_data[_datapos])) 
                        {
                            CSocketDataArgLength a = _getArgLength(datalen);
                            int sz = a.sz;
                            int argL = a.argL;

                            if (argL >= 0) 
                            {
                                _textlen = argL;
                                _datapos += 1 + sz;
                                _step = CSocketDataParsingStep.STX;
                                continue;
                            }
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case CSocketDataParsingStep.STX:
                    if (datalen > 0) 
                    {
                        if (_data[_datapos] == 0x02) 
                        {
                            _datapos += 1;
                            _step = CSocketDataParsingStep.ETX;
                            continue;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case CSocketDataParsingStep.ETX:
                    if (datalen > _textlen) 
                    {
                        if (_data[_datapos + _textlen] == 0x03) 
                        {
                            int textfpos = _datapos;

                            _command = _data[textfpos];
                            _args.clear();
                            _datapos += 1;

                            while (_datapos < _textlen + textfpos) 
                            {
                                int sz = 0;
                                int argL = 0;

                                if (Uint8List.fromList([0x31, 0x32, 0x34, 0x38]).contains(_data[_datapos]))
                                {
                                    sz = (_data[_datapos] & 0x0F) as int;
                                    int i = 0;
                                    switch (sz) {
                                        case 1: i = ByteData.sublistView(_data, _datapos + 1).getInt8(0); break;
                                        case 2: i = ByteData.sublistView(_data, _datapos + 1).getInt16(0); break;
                                        case 4: i = ByteData.sublistView(_data, _datapos + 1).getInt32(0); break;
                                        case 8: i = ByteData.sublistView(_data, _datapos + 1).getInt64(0); break;
                                    }
                                    _args.add(CInteger(i));
                                } 
                                else if (Uint8List.fromList([0x54, 0x58]).contains(_data[_datapos])) 
                                {
                                    sz = (_data[_datapos] & 0x0F) as int;
                                    double f = 0.0;
                                    switch (sz) 
                                    {
                                        case 4: f = ByteData.sublistView(_data, _datapos + 1).getFloat32(0); break;
                                        case 8: f = ByteData.sublistView(_data, _datapos + 1).getFloat64(0); break;
                                    }
                                    _args.add(CFloat(f));
                                } 
                                else if (Uint8List.fromList([0x71]).contains(_data[_datapos])) 
                                {
                                    sz = 1;
                                    _args.add(CBoolean((_data[_datapos + 1] == 1)? true: false));
                                } 
                                else if (Uint8List.fromList([0x91, 0x92, 0x94]).contains(_data[_datapos])) 
                                {
                                    CSocketDataArgLength a = _getArgLength(datalen);
                                    sz = a.sz;
                                    argL = a.argL;

                                    _args.add(CString(utf8.decode(_data.sublist(_datapos + 1 + sz, _datapos + 1 + sz + argL))));
                                    _datapos += argL; 

                                } 
                                else if (Uint8List.fromList([0xB1, 0xB2, 0xB4]).contains(_data[_datapos])) 
                                {
                                    CSocketDataArgLength a = _getArgLength(datalen);
                                    sz = a.sz;
                                    argL = a.argL;

                                    _args.add(CByteArray(_data.sublist(_datapos + 1 + sz, _datapos + 1 + sz + argL)));
                                    _datapos += argL;
                                } 
                                else {
                                    return CSocketDataManipulationResult.ParsingError;
                                }
                                _datapos += 1 + sz;
                            }

                            _checksum = 0x00;
                            for (int i = textfpos; i < textfpos + _textlen; i++)
                                _checksum ^= _data[i];

                            _datapos += 1;

                            _step = CSocketDataParsingStep.CHK;
                            continue;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case CSocketDataParsingStep.CHK:
                    if (datalen > 0) 
                    {
                        if (_data[_datapos] == _checksum) 
                        {
                            _datapos += 1;
                            _step = CSocketDataParsingStep.EOT;
                            continue;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case CSocketDataParsingStep.EOT:
                    if (datalen > 0) 
                    {
                        if (_data[_datapos] == 0x04) 
                        {
                            _datapos += 1;
                            _data = _data.sublist(_datapos);
                            _datapos = 0;
                            _checksum = 0x00;
                            _step = CSocketDataParsingStep.SOH;
                            _textlen = 0;
                            return CSocketDataManipulationResult.Completed;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;
            }

            if (_data.length == 0)
                return CSocketDataManipulationResult.NoData;

            return CSocketDataManipulationResult.InProgress;
        }
    }

    CSocketDataArgLength _getArgLength(int datalen) 
    {
        int sz = (_data[_datapos] & 0x0F) as int;
        int argL = -1;

        if (datalen > sz) 
        {
            switch (sz) 
            {
                case 1: argL = ByteData.sublistView(_data, _datapos + 1).getInt8(0) as int; break;
                case 2: argL = ByteData.sublistView(_data, _datapos + 1).getInt16(0) as int; break;
                case 4: argL = ByteData.sublistView(_data, _datapos + 1).getInt32(0) as int; break;
            }
        }
        return CSocketDataArgLength(sz, argL);
    }  
}

class CSocketDataArgLength
{
    int _sz = 0;
    int _argL = 0;

    int get sz => _sz;
    int get argL => _argL;

    CSocketDataArgLength(int sz, int argL)
    {
        _sz = sz;
        _argL = argL;
    }
}


class CSocketDataArgs 
{
    List<IDataType> _list = [];

    int get length => _list.length;
    
    operator [](index) => _list[index];

    CSocketDataArgs()
    {
    }

    add(IDataType arg)
    {
        _list.add(arg);
    }

    IDataType at(int index)
    {
        return _list[index];
    }   

    clear()
    {
        _list.clear();
    }
}

enum CSocketDataManipulationResult
{
    Completed,
    InProgress,
    NoData,
    ParsingError
}

enum CSocketDataParsingStep
{
    SOH,
    OTL,
    STX,
    ETX,
    CHK,
    EOT
} 

enum CSocketProtocolType 
{
    Tcp, Udp
}

class CSocketReceivedData 
{
    int _command = 0x00;
    CSocketDataArgs _args = CSocketDataArgs();
    CSocketAddress _address = CSocketAddress('0.0.0.0', 0);
    CSocketReceivedDataResult _result = CSocketReceivedDataResult.Closed;
   
    CSocketDataArgs get args => _args;
    int get command => _command;
    CSocketAddress get remoteAddress => _address;
    CSocketReceivedDataResult get result => _result;

    CSocketReceivedData(int command, CSocketDataArgs args, CSocketReceivedDataResult result, CSocketAddress address) 
    {
        _command = command;
        _args = args;
        _result = result;
        _address = address;
    }
}

enum CSocketReceivedDataResult 
{
    Closed, Completed, Interrupted, ParsingError
}

class CSocketSendData 
{
    final int _ARG_MAXLEN = 0x7FFFFF - 5;
    final int _TXT_MAXLEN = 0x7FFFFFFF - 10;

    int _command = 0x00;
    CSocketDataArgs _args = CSocketDataArgs();
    List<int> _bytes = [];
    CSocketSendDataBuildResult _result = CSocketSendDataBuildResult.NoData;
    
    CSocketDataArgs get args => _args;
    CSocketSendDataBuildResult get buildResult => _result;
    List<int> get bytes => _bytes;
    int get command => _command;
    int get length => _bytes.length;

    CSocketSendData(int command, CSocketDataArgs args) 
    {
        if (command < 0x00 || command > 0xFF) 
        {
            _result = CSocketSendDataBuildResult.CommandValueOverflowError;
            return;
        }

        _command = command;
        _args = args;
        List<int> text = [];

        text.add(command as int);

        for (int n = 0; n < _args.length; n++) 
        {
            IDataType arg = _args[n];

            switch (arg.getDataType()) 
            {
                case DataType.CInteger: 
                {
                    int i = (arg as CInteger).value;

                    if (-128 <= i && i <= 127) 
                    {
                        text.add(0x31);

                        ByteData bd = ByteData(1);
                        bd.setInt8(0, i);
                        text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                    } 
                    else if (-32768 <= i && i <= 32767) 
                    {
                        text.add(0x32);

                        ByteData bd = ByteData(2);
                        bd.setInt16(0, i);
                        text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                    } 
                    else if (-2147483648 <= i && i <= 2147483647) 
                    {
                        text.add(0x34);

                        ByteData bd = ByteData(4);
                        bd.setInt32(0, i);
                        text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                    } 
                    else 
                    {
                        text.add(0x38);

                        ByteData bd = ByteData(8);
                        bd.setInt64(0, i);
                        text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                    }
                }
                break;

                case DataType.CFloat: 
                {
                    double f = (arg as CFloat).value;

                    if (f.abs() <= 3.40282347e+38) 
                    {
                        text.add(0x54);

                        ByteData bd = ByteData(4);
                        bd.setFloat32(0, f);
                        text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                    } 
                    else 
                    {
                        text.add(0x58);

                        ByteData bd = ByteData(8);
                        bd.setFloat64(0, f);
                        text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                    }
                }
                break;

                case DataType.CBoolean: 
                {
                    text.add(0x71);
                    text.add((arg as CBoolean).value ? 1 : 0);
                }
                break;

                case DataType.CString: 
                {
                    List<int> s = utf8.encode((arg as CString).value);

                    if (s.length <= _ARG_MAXLEN) 
                    {
                        if (s.length <= 127)
                        {
                            text.add(0x91);

                            ByteData bd = ByteData(1);
                            bd.setInt8(0, s.length);
                            text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                        } 
                        else if (s.length <= 32767) 
                        {
                            text.add(0x92);

                            ByteData bd = ByteData(2);
                            bd.setInt16(0, s.length);
                            text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                        } 
                        else 
                        {
                            text.add(0x94);

                            ByteData bd = ByteData(4);
                            bd.setInt32(0, s.length);
                            text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                        }
                        text.addAll(s);
                    } 
                    else 
                    {
                        _result = CSocketSendDataBuildResult.StringLengthOverflowError;
                        return;
                    }
                }
                break;

                case DataType.CByteArray: 
                {
                    Uint8List b = (arg as CByteArray).value;

                    if (b.length <= _ARG_MAXLEN) 
                    {
                        if (b.length <= 127) 
                        {
                            text.add(0xB1);

                            ByteData bd = ByteData(1);
                            bd.setInt8(0, b.length);
                            text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                        } 
                        else if (b.length <= 32767) 
                        {
                            text.add(0xB2);

                            ByteData bd = ByteData(2);
                            bd.setInt16(0, b.length);
                            text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                        } 
                        else 
                        {
                            text.add(0xB4);

                            ByteData bd = ByteData(4);
                            bd.setInt32(0, b.length);
                            text.addAll(Int32List.fromList(bd.buffer.asUint8List()));
                        }
                        text.addAll(Int32List.fromList(b));
                    } 
                    else 
                    {
                        _result = CSocketSendDataBuildResult.ByteArrayLengthOverflowError;
                        return;
                    }
                }
                break;

                default: 
                {
                    _result = CSocketSendDataBuildResult.DataTypeNotImplementedError;
                    return;
                }
            }
        }

        int textlen = text.length;
        
        List<int> data = [];

        if (textlen <= _TXT_MAXLEN) 
        {
            data.add(0x01);

            if (textlen <= 127) 
            {
                data.add(0x11);

                ByteData bd = ByteData(1);
                bd.setInt8(0, textlen);
                data.addAll(Int32List.fromList(bd.buffer.asUint8List()));
            } 
            else if (textlen <= 32767) 
            {
                data.add(0x12);

                ByteData bd = ByteData(2);
                bd.setInt16(0, textlen);
                data.addAll(Int32List.fromList(bd.buffer.asUint8List()));
            } 
            else 
            {
                data.add(0x14);

                ByteData bd = ByteData(4);
                bd.setInt32(0, textlen);
                data.addAll(Int32List.fromList(bd.buffer.asUint8List()));
            }

            data.add(0x02);
            data.addAll(text);
            data.add(0x03);
            
            int checksum = 0x00;
            for (int i = 0; i < textlen; i++) 
                checksum ^= text[i];
            
            data.add(checksum);           
            data.add(0x04);
        } 
        else 
        {
            _result = CSocketSendDataBuildResult.DataTotalLengthOverflowError;
            return;
        }

        _bytes = data;
        _result = CSocketSendDataBuildResult.Successful;
    }
}

enum CSocketSendDataBuildResult 
{
    ByteArrayLengthOverflowError,
    CommandValueOverflowError,
    DataTotalLengthOverflowError,
    DataTypeNotImplementedError,
    StringLengthOverflowError,
    NoData,
    Successful
}

class TcpServer 
{
    RawServerSocket? _server;

    bool get running => _server != null;

    TcpServer(RawServerSocket? s) 
    { 
        _server = s; 
    }

    void close() 
    {
        _server?.close();
        _server = null;
    }

    void setAcceptCallback(Function callback) 
    {
        TcpServer me = this;
        _server!.listen((RawSocket s) {
            callback(TcpSocket(s));
        });
    }
}

class TcpSocket extends CSocket 
{
    CSocketAddress _remoteAddress = CSocketAddress('0.0.0.0', 0);

    bool get connected => this.available && _connected;
    CSocketAddress get remoteAddress => _remoteAddress;

    TcpSocket(RawSocket? s) : super(s, CSocketProtocolType.Tcp) 
    {
        _connected = (s != null);

        if (s != null)
            _remoteAddress = CSocketAddress(s!.remoteAddress.host, s!.remotePort);
    }

    void send(CSocketSendData data) 
    {
        _send(data, null);
    }
}

class UdpSocket extends CSocket 
{
    UdpSocket(RawDatagramSocket? s) : super(s, CSocketProtocolType.Udp) 
    {        
    }

    void send(CSocketSendData data, CSocketAddress address) 
    {
        _send(data, address);
    }
}

class NetworkComm 
{
    static Future<TcpSocket> TcpConnect(CSocketAddress address) async 
    {
        RawSocket? s; 
        
        try {
            InternetAddress host = await _getInternetAddress(address.host);
            s = await RawSocket.connect(host, address.port);
        } 
        catch(e) {
            s = null;
        }
        return new TcpSocket(s);
    }

    static Future<TcpServer> TcpListen(CSocketAddress address) async 
    {
        RawServerSocket? s; 

        try {
            InternetAddress host = await _getInternetAddress(address.host);
            s = await RawServerSocket.bind(host, address.port);
        }
        catch(e) {
            s = null;
        }
        return new TcpServer(s);
    }

    static Future<UdpSocket> UdpCast(CSocketAddress address) async 
    {
        RawDatagramSocket? s;
        
        try {
            InternetAddress host = await _getInternetAddress(address.host);
            s = await RawDatagramSocket.bind(host, address.port);
        } 
        catch(e) {
            s = null;
        }
        return UdpSocket(s);
    }

    static Future<InternetAddress> _getInternetAddress(String host) async
    {
        if (null == InternetAddress.tryParse(host)) {
            List<InternetAddress> addresses = await InternetAddress.lookup(host);
            return addresses[0];
        }
        return InternetAddress(host);
    }
}

