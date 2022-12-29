import 'dart:async';
import 'dart:convert';
import 'dart:core';
import 'dart:ffi';
import 'dart:io';
import 'dart:typed_data';

class NetSocket extends Object 
{  
    var _socket;
    bool _connected = false;

    NetSocketData _data = NetSocketData();
    NetSocketDataManipulationResult _result = NetSocketDataManipulationResult.NoData;
    NetSocketAddress _localAddress = NetSocketAddress('0.0.0.0', 0);
    NetSocketProtocolType _protocol = NetSocketProtocolType.Udp;

    bool get available => _socket != null;
    NetSocketAddress get localAddress => _localAddress; 
    NetSocketProtocolType get protocolType => _protocol; 

    NetSocket(var s, NetSocketProtocolType protocol) 
    {
        _protocol = protocol;
        _socket = s;

        if (this.available)
            _localAddress = NetSocketAddress(_socket.address.host, _socket.port);
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

                if (_protocol == NetSocketProtocolType.Tcp)
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
                else if (_protocol == NetSocketProtocolType.Udp) 
                {
                    Datagram? d = _socket!.receive();
                    if (d == null) return;

                    buffer = d.data;
                    host = d.address.host;
                    port = d.port;
                }

                _receiveProc(buffer, callback, NetSocketAddress(host, port));
            });
        }
    }

    void _receiveProc(Uint8List buffer, Function callback, NetSocketAddress remoteAddress) 
    {
        _data.append(buffer);

        while (true) 
        {
            _result = _data.manipulate();

            if (_result == NetSocketDataManipulationResult.Completed) 
            {
                callback(this, NetSocketReceivedData(_data.command, _data.args, NetSocketReceivedDataResult.Completed, remoteAddress));
                continue;
            }
            else if (_result == NetSocketDataManipulationResult.ParsingError) 
            {
                callback(this, NetSocketReceivedData(0x00, List<Object>.empty(), NetSocketReceivedDataResult.ParsingError, remoteAddress));
                return;
            }
            else if (_result == NetSocketDataManipulationResult.InProgress) 
            {
                var me = this;

                Timer(Duration(seconds: 15), () {
                    if (_result == NetSocketDataManipulationResult.InProgress)
                        callback(me, NetSocketReceivedData(0x00, List<Object>.empty(), NetSocketReceivedDataResult.Interrupted, remoteAddress));
                });
                break;
            }
            else if (_result == NetSocketDataManipulationResult.NoData) 
            {
                break;
            }
        }
    }

    void _send(NetSocketSendData data, NetSocketAddress? address) 
    {
        if (this.available)
            _sendProc(data, address, 0);
    }

    void _sendProc(NetSocketSendData data, NetSocketAddress? address, int bytesTransferred) 
    {
        num length = 0;

        if (_protocol == NetSocketProtocolType.Tcp) 
        {
            length = _socket!.write(data.bytes.sublist(bytesTransferred));

            if (length > 0) 
                _connected = true;
            else 
                _connected = false;
        }
        else if (_protocol == NetSocketProtocolType.Udp)
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

class NetSocketAddress 
{
    String _host = '0.0.0.0';
    int _port = 0;

    String get host => _host;
    int get port => _port;

    NetSocketAddress(String host, int port) 
    {
        _host = host;
        _port = port;
    }

    @override
    String toString() {
        return '${_host}:${_port}';
    }    
}

class NetSocketData 
{
    Uint8List _data = Uint8List(0);
    int _datapos = 0;
    int _checksum = 0x00;
    NetSocketDataParsingStep _step = NetSocketDataParsingStep.SOH;
    int _textlen = 0;
    int _command = 0x00;
    List<Object> _args = [];

    List<Object> get args => _args; 
    int get command => _command;

    void append(Uint8List buffer) 
    {
        BytesBuilder builder = BytesBuilder();
        builder.add(_data);
        builder.add(buffer);

        _data = builder.takeBytes();
    }

    NetSocketDataManipulationResult manipulate() 
    {
        while (true)
        {
            int datalen = _data.length - _datapos;

            switch (_step) 
            {
                case NetSocketDataParsingStep.SOH:
                    if (datalen > 0) 
                    {
                        if (_data[_datapos] == 0x01)
                        {
                            _datapos += 1;
                            _step = NetSocketDataParsingStep.OTL;
                            continue;
                        } 
                        else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case NetSocketDataParsingStep.OTL:
                    if (datalen > 0) 
                    {
                        if (Uint8List.fromList([0x11, 0x12, 0x14]).contains(_data[_datapos])) 
                        {
                            NetSocketDataArgLength a = _getArgLength(datalen);
                            int sz = a.size;
                            int argL = a.argLength;

                            if (argL >= 0) 
                            {
                                _textlen = argL;
                                _datapos += 1 + sz;
                                _step = NetSocketDataParsingStep.STX;
                                continue;
                            }
                        } 
                        else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case NetSocketDataParsingStep.STX:
                    if (datalen > 0) 
                    {
                        if (_data[_datapos] == 0x02) 
                        {
                            _datapos += 1;
                            _step = NetSocketDataParsingStep.ETX;
                            continue;
                        } 
                        else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case NetSocketDataParsingStep.ETX:
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

                                    switch (sz) {
                                        case 1: _args.add(ByteData.sublistView(_data, _datapos + 1).getInt8(0)); break;
                                        case 2: _args.add(ByteData.sublistView(_data, _datapos + 1).getInt16(0)); break;
                                        case 4: _args.add(ByteData.sublistView(_data, _datapos + 1).getInt32(0)); break;
                                        case 8: _args.add(ByteData.sublistView(_data, _datapos + 1).getInt64(0)); break;
                                    }
                                } 
                                else if (Uint8List.fromList([0x54, 0x58]).contains(_data[_datapos])) 
                                {
                                    sz = (_data[_datapos] & 0x0F) as int;

                                    switch (sz) 
                                    {
                                        case 4: _args.add(ByteData.sublistView(_data, _datapos + 1).getFloat32(0)); break;
                                        case 8: _args.add(ByteData.sublistView(_data, _datapos + 1).getFloat64(0)); break;
                                    }
                                } 
                                else if (Uint8List.fromList([0x71]).contains(_data[_datapos])) 
                                {
                                    sz = 1;
                                    _args.add((_data[_datapos + 1] == 1)? true: false);
                                } 
                                else if (Uint8List.fromList([0x91, 0x92, 0x94]).contains(_data[_datapos])) 
                                {
                                    NetSocketDataArgLength a = _getArgLength(datalen);
                                    sz = a.size;
                                    argL = a.argLength;

                                    _args.add(String.fromCharCodes(_data.sublist(_datapos + 1 + sz, _datapos + 1 + sz + argL)));
                                    _datapos += argL; 

                                } 
                                else if (Uint8List.fromList([0xB1, 0xB2, 0xB4]).contains(_data[_datapos])) 
                                {
                                    NetSocketDataArgLength a = _getArgLength(datalen);
                                    sz = a.size;
                                    argL = a.argLength;

                                    _args.add(_data.sublist(_datapos + 1 + sz, _datapos + 1 + sz + argL));
                                    _datapos += argL;
                                } 
                                else {
                                    return NetSocketDataManipulationResult.ParsingError;
                                }
                                _datapos += 1 + sz;
                            }

                            _checksum = 0x00;
                            for (int i = textfpos; i < textfpos + _textlen; i++)
                                _checksum ^= _data[i];

                            _datapos += 1;

                            _step = NetSocketDataParsingStep.CHK;
                            continue;
                        } 
                        else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case NetSocketDataParsingStep.CHK:
                    if (datalen > 0) 
                    {
                        if (_data[_datapos] == _checksum) 
                        {
                            _datapos += 1;
                            _step = NetSocketDataParsingStep.EOT;
                            continue;
                        } 
                        else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case NetSocketDataParsingStep.EOT:
                    if (datalen > 0) 
                    {
                        if (_data[_datapos] == 0x04) 
                        {
                            _datapos += 1;
                            _data = _data.sublist(_datapos);
                            _datapos = 0;
                            _checksum = 0x00;
                            _step = NetSocketDataParsingStep.SOH;
                            _textlen = 0;
                            return NetSocketDataManipulationResult.Completed;
                        } 
                        else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;
            }

            if (_data.length == 0)
                return NetSocketDataManipulationResult.NoData;

            return NetSocketDataManipulationResult.InProgress;
        }
    }

    NetSocketDataArgLength _getArgLength(int datalen) 
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
        return NetSocketDataArgLength(sz, argL);
    }    
}

class NetSocketDataArgLength 
{
    int _sz = 0;
    int _argL = 0;

    int get size => _sz;
    int get argLength => _argL;

    NetSocketDataArgLength(int sz, int argL) 
    {
        _sz = sz;
        _argL = argL;
    }
}

enum NetSocketDataManipulationResult 
{
    Completed, InProgress, NoData, ParsingError
}

enum NetSocketDataParsingStep 
{
    SOH, OTL, STX, ETX, CHK, EOT
}

enum NetSocketProtocolType 
{
    Tcp, Udp
}

class NetSocketReceivedData 
{
    int _command = 0x00;
    List<Object> _args = [];
    NetSocketAddress _address = NetSocketAddress('0.0.0.0', 0);
    NetSocketReceivedDataResult _result = NetSocketReceivedDataResult.Closed;
   
    List<Object> get args => _args;
    int get command => _command;
    NetSocketAddress get remoteAddress => _address;
    NetSocketReceivedDataResult get result => _result;

    NetSocketReceivedData(int command, List<Object> args, NetSocketReceivedDataResult result, NetSocketAddress address) 
    {
        _command = command;
        _args = args;
        _result = result;
        _address = address;
    }
}

enum NetSocketReceivedDataResult 
{
    Closed, Completed, Interrupted, ParsingError
}

class NetSocketSendData 
{
    final int _ARG_MAXLEN = 0x7FFFFF - 5;
    final int _TXT_MAXLEN = 0x7FFFFFFF - 10;

    int _command = 0x00;
    List<Object> _args = [];
    List<int> _bytes = [];
    NetSocketSendDataBuildResult _result = NetSocketSendDataBuildResult.NoData;
    
    List<Object> get args => _args;
    NetSocketSendDataBuildResult get buildResult => _result;
    List<int> get bytes => _bytes;
    int get command => _command;
    int get length => _bytes.length;

    NetSocketSendData(int command, List<Object> args) 
    {
        if (command < 0x00 || command > 0xFF) 
        {
            _result = NetSocketSendDataBuildResult.CommandValueOverflowError;
            return;
        }

        _command = command;
        _args = args;
        List<int> text = [];

        text.add(command as int);

        args.forEach((arg) 
        {
            switch (arg.runtimeType.toString()) 
            {
                case 'int': 
                {
                    int i = arg as int;

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

                case 'double': 
                {
                    double f = arg as double;

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

                case 'bool': 
                {
                    text.add(0x71);
                    text.add(arg as bool ? 1 : 0);
                }
                break;

                case 'String': 
                {
                    List<int> s = utf8.encode(arg as String);

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
                        _result = NetSocketSendDataBuildResult.StringLengthOverflowError;
                        return;
                    }
                }
                break;

                case 'Uint8List': 
                {
                    Uint8List b = arg as Uint8List;

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
                        _result = NetSocketSendDataBuildResult.ByteArrayLengthOverflowError;
                        return;
                    }
                }
                break;

                default: 
                {
                    _result = NetSocketSendDataBuildResult.DataTypeNotImplementedError;
                    return;
                }
            }
        });

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
            _result = NetSocketSendDataBuildResult.DataTotalLengthOverflowError;
            return;
        }

        _bytes = data;
        _result = NetSocketSendDataBuildResult.Successful;
    }
}

enum NetSocketSendDataBuildResult 
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

class TcpSocket extends NetSocket 
{
    NetSocketAddress _remoteAddress = NetSocketAddress('0.0.0.0', 0);

    bool get connected => this.available && _connected;
    NetSocketAddress get remoteAddress => _remoteAddress;

    TcpSocket(RawSocket? s) : super(s, NetSocketProtocolType.Tcp) 
    {
        _connected = (s != null);

        if (s != null)
            _remoteAddress = NetSocketAddress(s!.remoteAddress.host, s!.remotePort);
    }

    void send(NetSocketSendData data) 
    {
        _send(data, null);
    }
}

class UdpSocket extends NetSocket 
{
    UdpSocket(RawDatagramSocket? s) : super(s, NetSocketProtocolType.Udp) 
    {        
    }

    void send(NetSocketSendData data, NetSocketAddress address) 
    {
        _send(data, address);
    }
}

Future<TcpSocket> TcpConnect(NetSocketAddress address) async 
{
    RawSocket? s; 
    
    try {
        s = await RawSocket.connect(InternetAddress(address.host), address.port);
    } 
    catch(e) {
        s = null;
    }
    return new TcpSocket(s);
}

Future<TcpServer> TcpListen(NetSocketAddress address) async 
{
    RawServerSocket? s; 

    try {
        s = await RawServerSocket.bind(InternetAddress(address.host), address.port);
    }
    catch(e) {
        s = null;
    }
    return new TcpServer(s);
}

Future<UdpSocket> UdpCast(NetSocketAddress address) async 
{
    RawDatagramSocket? s;
    
    try {
        s = await RawDatagramSocket.bind(InternetAddress(address.host), address.port);
    } 
    catch(e) {
        s = null;
    }
    return UdpSocket(s);
}

