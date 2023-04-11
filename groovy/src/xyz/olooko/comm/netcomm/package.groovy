package xyz.olooko.comm.netcomm

import java.nio.ByteBuffer

class CBoolean extends IDataType {
    CBoolean(boolean value) {
        _value = value
    }

    @Override
    String toString() {
        return _value.toString()
    }
}

class CByteArray extends IDataType {
    CByteArray(byte[] value) {
        _value = value
    }

    @Override
    String toString() {
        String s = ""
        for (byte b: _value) {
            if (s != "") s += ","
            s += String.format("0x%02X", b & 0xFF)
        }
        return s
    }
}

class CFloat extends IDataType {
    CFloat(double value) {
        _value = value
    }
}

class CInteger extends IDataType {
    CInteger(long value) {
        _value = value
    }
}

class CString extends IDataType {
    CString(String value) {
        _value = value
    }

    @Override
    String toString() {
        return _value
    }
}

abstract class IDataType {
    protected def _value

    def getValue() {
        return _value
    }

    String toString() {
        _value.toString()
    }
}

abstract class CSocket {
    protected def _socket
    protected def _data
    protected def _result
    protected def _localAddress
    protected def _protocol

    def getAvailable() {
        return _socket != null
    }

    def getLocalAddress() {
        return _localAddress
    }

    def getProtocolType() {
        return _protocol
    }

    CSocket(s, protocolType) {
        _data = new CSocketData()
        _protocol = protocolType
        _result = CSocketDataManipulationResult.NoData
        _localAddress = new CSocketAddress("0.0.0.0", 0)
        _socket = s

        if (available) {
            def address = (InetSocketAddress)_socket.localSocketAddress
            _localAddress = new CSocketAddress(address.address, address.port)
        }
    }


    def close() {
        if (available) {
            try {
                _socket.close()
            }
            catch (Exception e) {}
        }
    }

    def setReceivedCallback(callback) {
        if (available) {
            Thread.start {
                def buffer = new byte[4096]
                def remoteAddress = new CSocketAddress("0.0.0.0", 0)

                while (true) {
                    def bytesTransferred = 0

                    if (_protocol == CSocketProtocolType.Tcp) {
                        try {
                            bytesTransferred = _socket.inputStream.read(buffer)
                        }
                        catch (Exception e) {
                        }
                        remoteAddress = new CSocketAddress((InetSocketAddress) _socket.remoteSocketAddress)
                    }
                    else if (_protocol == CSocketProtocolType.Udp) {
                        try {
                            def packet = new DatagramPacket(buffer, buffer.length)
                            _socket.receive(packet)

                            remoteAddress = new CSocketAddress((InetSocketAddress) packet.socketAddress)
                            bytesTransferred = packet.length
                        }
                        catch (Exception e) {
                        }
                    }

                    if (bytesTransferred > 0) {
                        _data.Append(buffer, bytesTransferred)

                        while (true) {
                            _result = _data.Manipulate()
                            if (_result == CSocketDataManipulationResult.Completed) {
                                callback.call(this, new CSocketReceivedData(_data.command, _data.args, CSocketReceivedDataResult.Completed, remoteAddress))
                                continue
                            } else if (_result == CSocketDataManipulationResult.ParsingError) {
                                callback.call(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress))
                                return
                            } else if (_result == CSocketDataManipulationResult.InProgress) {
                                final def paramAddress = remoteAddress
                                Thread.start {
                                    sleep(15000)
                                    if (_result == CSocketDataManipulationResult.InProgress)
                                        callback.call(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, paramAddress))
                                }
                                break
                            } else if (_result == CSocketDataManipulationResult.NoData) {
                                break
                            }
                        }
                        continue
                    } else {
                        callback.call(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.Closed, remoteAddress))
                        break
                    }
                }
            }
        }
    }

    protected def send(data, address) {
        if (available) {
            try {
                if (_protocol == CSocketProtocolType.Tcp) {
                    _socket.outputStream.write(data.bytes, 0, data.length)
                    _socket.outputStream.flush()
                }
                else if (_protocol == CSocketProtocolType.Udp) {
                    def packet = new DatagramPacket(data.bytes, data.length, address.inetAddress, address.port)
                    _socket.send(packet)
                }
            }
            catch (Exception e) {}
        }
    }
}

class CSocketAddress {
    private InetAddress _inetAddress
    private String _host
    private int _port

    def getInetAddress() {
        return _inetAddress
    }

    def getHost() {
        return _host
    }

    def getPort() {
        return _port
    }

    CSocketAddress(String host, port) {
        _port = port
        try {
            _inetAddress = InetAddress.getByName(host)
            _host = host
        }
        catch (Exception e) {}
    }

    CSocketAddress(InetAddress inetAddress, port) {
        _inetAddress = inetAddress
        _host = inetAddress.getHostAddress()
        _port = port
    }

    CSocketAddress(inetSocketAddress) {
        _inetAddress = inetSocketAddress.getAddress()
        _host = _inetAddress.getHostAddress()
        _port = inetSocketAddress.getPort()
    }

    String toString() {
        return String.format("%s:%d", _host, _port)
    }
}

class CSocketData {
    private def _command
    private def _args
    private def _data
    private def _datalen
    private def _datapos
    private def _step
    private def _checksum
    private def _textlen

    def getArgs() {
        return _args
    }

    def getCommand() {
        return _command & 0xFF
    }

    CSocketData() {
        _command = 0x00
        _args = new CSocketDataArgs()
        _data = ByteBuffer.allocate(0)
        _datalen = 0
        _datapos = 0
        _checksum = 0x00
        _step = CSocketDataParsingStep.SOH
        _textlen = 0
    }

    def Append(buffer, bytesTransferred) {
        if (_data.capacity() < _datalen + bytesTransferred) {
            _data = ByteBuffer.allocate(_datalen + bytesTransferred).put(_data)
            _data.position(_datalen)
        }

        _data.put(buffer, 0, bytesTransferred)
        _datalen += bytesTransferred
    }

    def Manipulate() {
        while (true) {
            def datalen = _datalen - _datapos

            switch (_step) {
                case CSocketDataParsingStep.SOH:
                    if (datalen > 0) {
                        if (_data.get(_datapos) == 0x01) {
                            _datapos += 1
                            _step = CSocketDataParsingStep.OTL
                            continue
                        }
                        else {
                            return CSocketDataManipulationResult.ParsingError
                        }
                    }
                    break

                case CSocketDataParsingStep.OTL:
                    if (datalen > 0) {
                        if (Arrays.asList((byte)0x11, (byte)0x12, (byte)0x14).contains(_data.get(_datapos))) {
                            def a = getArgLength(datalen)

                            if (a.argL >= 0) {
                                _textlen = a.argL
                                _datapos += 1 + a.size
                                _step = CSocketDataParsingStep.STX
                                continue
                            }
                        }
                        else {
                            return CSocketDataManipulationResult.ParsingError
                        }
                    }
                    break

                case CSocketDataParsingStep.STX:
                    if (datalen > 0) {
                        if (_data.get(_datapos) == 0x02) {
                            _datapos += 1
                            _step = CSocketDataParsingStep.ETX
                            continue
                        }
                        else {
                            return CSocketDataManipulationResult.ParsingError
                        }
                    }
                    break

                case CSocketDataParsingStep.ETX:
                    if (datalen > _textlen) {
                        if (_data.get(_datapos + _textlen) == 0x03) {
                            def textfpos = _datapos

                            _command = _data.get(textfpos)
                            _args.clear()
                            _datapos += 1

                            while (_datapos < _textlen + textfpos) {
                                def sz = 0
                                def argL = 0

                                if (Arrays.asList((byte)0x31, (byte)0x32, (byte)0x34, (byte)0x38).contains(_data.get(_datapos))) {
                                    sz = (int)(_data.get(_datapos) & 0x0F)
                                    def i = 0
                                    switch (sz) {
                                        case 1: i = (long)_data.get(_datapos + 1); break
                                        case 2: i = (long)_data.getShort(_datapos + 1); break
                                        case 4: i = (long)_data.getInt(_datapos + 1); break
                                        case 8: i = (long)_data.getLong(_datapos + 1); break
                                    }
                                    _args.add(new CInteger(i))
                                }
                                else if (Arrays.asList((byte)0x54, (byte)0x58).contains(_data.get(_datapos))) {
                                    sz = (int)(_data.get(_datapos) & 0x0F)
                                    def f = 0.0
                                    switch (sz) {
                                        case 4: f = (double)_data.getFloat(_datapos + 1); break
                                        case 8: f = _data.getDouble(_datapos + 1); break
                                    }
                                    _args.add(new CFloat(f))
                                }
                                else if (Arrays.asList((byte)0x71).contains(_data.get(_datapos))) {
                                    sz = 1
                                    _args.add(new CBoolean((_data.get(_datapos + 1) == 0) ? false : true))
                                }
                                else if (Arrays.asList((byte)0x91, (byte)0x92, (byte)0x94).contains(_data.get(_datapos))) {
                                    def a = getArgLength(datalen)
                                    sz = a.size
                                    argL = a.argL

                                    _args.add(new CString(new String(_data.array(), _datapos + 1 + sz, argL)))
                                    _datapos += argL
                                }
                                else if (Arrays.asList((byte)0xB1, (byte)0xB2, (byte)0xB4).contains(_data.get(_datapos))) {
                                    def a = getArgLength(datalen)
                                    sz = a.size
                                    argL = a.argL

                                    def ba = new byte[argL]
                                    System.arraycopy(_data.array(), _datapos + 1 + sz, ba, 0, argL)

                                    _args.add(new CByteArray(ba))
                                    _datapos += argL

                                } else {
                                    return CSocketDataManipulationResult.ParsingError
                                }
                                _datapos += 1 + sz
                            }

                            _checksum = 0x00
                            for (def i = textfpos; i < textfpos + _textlen; i++)
                                _checksum ^= _data.get(i)

                            _datapos += 1
                            _step = CSocketDataParsingStep.CHK
                            continue
                        }
                        else {
                            return CSocketDataManipulationResult.ParsingError
                        }
                    }
                    break

                case CSocketDataParsingStep.CHK:
                    if (datalen > 0) {
                        if (_data.get(_datapos) == _checksum) {
                            _datapos += 1
                            _step = CSocketDataParsingStep.EOT
                            continue
                        }
                        else {
                            return CSocketDataManipulationResult.ParsingError
                        }
                    }
                    break

                case CSocketDataParsingStep.EOT:
                    if (datalen > 0) {
                        if (_data.get(_datapos) == 0x04) {
                            _datapos += 1
                            _datalen -= _datapos

                            _data = ByteBuffer.allocate(_datalen).put(_data.array(), _datapos, _datalen)

                            _datapos = 0
                            _checksum = 0x00
                            _step = CSocketDataParsingStep.SOH
                            _textlen = 0

                            return CSocketDataManipulationResult.Completed
                        }
                        else {
                            return CSocketDataManipulationResult.ParsingError
                        }
                    }
                    break
            }

            if (_datalen == 0) {
                return CSocketDataManipulationResult.NoData
            }

            return CSocketDataManipulationResult.InProgress
        }
    }

    private def getArgLength(datalen) {
        def sz = (int)(_data.get(_datapos) & 0x0F)
        def argL = -1

        if (datalen > sz) {
            switch (sz) {
                case 1: argL = (int)_data.get(_datapos + 1); break
                case 2: argL = (int)_data.getShort(_datapos + 1); break
                case 4: argL = (int)_data.getInt(_datapos + 1); break
            }
        }
        return new CSocketDataArgLength(sz, argL)
    }
}

class CSocketDataArgLength {
    private def _sz
    private def _argL

    def getSize() {
        return _sz
    }

    def getArgL() {
        return _argL
    }

    CSocketDataArgLength(sz, argL) {
        _sz = sz
        _argL = argL
    }
}

class CSocketDataArgs {
    private def _list

    def getLength() {
        return _list.size()
    }

    CSocketDataArgs() {
        _list = new ArrayList<IDataType>()
    }

    def add(valueType) {
        _list.add(valueType)
    }

    def at(index) {
        return _list.get(index)
    }

    def clear() {
        _list.clear()
    }
}

enum CSocketDataManipulationResult {
    Completed, InProgress, NoData, ParsingError
}

enum CSocketDataParsingStep {
    SOH, OTL, STX, ETX, CHK, EOT
}

enum CSocketProtocolType {
    Tcp, Udp
}

class CSocketReceivedData {
    private def _command
    private def _args
    private def _result
    private def _address

    def getArgs() {
        return _args
    }

    def getCommand() {
        return _command & 0xFF
    }

    def getRemoteAddress() {
        return _address
    }

    def getResult() {
        return _result
    }

    CSocketReceivedData(command, args, result, address) {
        _command = (byte)(command & 0xFF)
        _args = args
        _result = result
        _address = address
    }
}

enum CSocketReceivedDataResult {
    Closed, Completed, Interrupted, ParsingError
}

class CSocketDataStream extends ByteArrayOutputStream {
    def getBuffer() {
        return super.buf
    }

    def getCount() {
        return super.count
    }
}

class CSocketSendData {
    private static final int ARG_MAXLEN = 0x7FFFFF - 5
    private static final int TXT_MAXLEN = Integer.MAX_VALUE - 10

    private def _command
    private def _args
    private def _bytes
    private def _result

    def getArgs() {
        return _args
    }

    def getBuildResult() {
        return _result
    }

    def getBytes() {
        return _bytes
    }

    def getCommand() {
        return _command & 0xFF
    }

    def getLength() {
        return _bytes.length
    }

    CSocketSendData(command, args) {
        _result = CSocketSendDataBuildResult.NoData

        if (command < 0x00 || command > 0xFF) {
            _result = CSocketSendDataBuildResult.CommandValueOverflowError
            return
        }

        _command = (byte)(command & 0xFF)
        _args = args

        def textds = new CSocketDataStream()

        textds.write(new byte[] { _command }, 0, 1)

        for (def n = 0; n < args.length; n++) {
            def arg = args.at(n)

            switch (arg.class.simpleName) {
                case "CInteger":
                    def i = ((CInteger)arg).value

                    if (Byte.MIN_VALUE <= i && i <= Byte.MAX_VALUE) {
                        textds.write(new byte[] { (byte)0x31 }, 0, 1)
                        textds.write(ByteBuffer.allocate(1).put((byte)i).array(), 0, 1)
                    }
                    else if (Short.MIN_VALUE <= i && i <= Short.MAX_VALUE) {
                        textds.write(new byte[] { (byte)0x32 }, 0, 1)
                        textds.write(ByteBuffer.allocate(2).putShort((short)i).array(), 0, 2)
                    }
                    else if (Integer.MIN_VALUE <= i && i <= Integer.MAX_VALUE) {
                        textds.write(new byte[] { (byte)0x34 }, 0, 1)
                        textds.write(ByteBuffer.allocate(4).putInt((int)i).array(), 0, 4)
                    }
                    else {
                        textds.write(new byte[] { (byte)0x38 }, 0, 1)
                        textds.write(ByteBuffer.allocate(8).putLong((long)i).array(), 0, 8)
                    }
                    break

                case "CFloat":
                    def f = ((CFloat)arg).value
                    if (Math.abs(f) <= Float.MAX_VALUE) {
                        textds.write(new byte[] { (byte)0x54 }, 0, 1)
                        textds.write(ByteBuffer.allocate(4).putFloat((float)f).array(), 0, 4)
                    }
                    else {
                        textds.write(new byte[] { (byte)0x58 }, 0, 1)
                        textds.write(ByteBuffer.allocate(8).putDouble(f).array(), 0, 8)
                    }
                    break

                case "CBoolean":
                    textds.write(new byte[] { (byte)0x71 }, 0, 1)
                    textds.write(ByteBuffer.allocate(1).put((byte)(((CBoolean)arg).getValue()?1:0)).array(), 0, 1)
                    break

                case "CString":
                    def s = ((CString)arg).value.bytes
                    if (s.length <= ARG_MAXLEN) {
                        if (s.length <= Byte.MAX_VALUE) {
                            textds.write(new byte[] { (byte)0x91 }, 0, 1)
                            textds.write(ByteBuffer.allocate(1).put((byte)s.length).array(), 0, 1)
                        }
                        else if (s.length <= Short.MAX_VALUE) {
                            textds.write(new byte[] { (byte)0x92 }, 0, 1)
                            textds.write(ByteBuffer.allocate(2).putShort((short)s.length).array(), 0, 2)
                        }
                        else {
                            textds.write(new byte[] { (byte)0x94 }, 0, 1)
                            textds.write(ByteBuffer.allocate(4).putInt((int)s.length).array(), 0, 4)
                        }
                        textds.write(s, 0, s.length)
                    }
                    else {
                        _result = CSocketSendDataBuildResult.StringLengthOverflowError
                        return
                    }
                    break

                case "CByteArray":
                    def b = ((CByteArray)arg).value
                    if (b.length <= ARG_MAXLEN) {
                        if (b.length <= Byte.MAX_VALUE) {
                            textds.write(new byte[] { (byte)0xB1 }, 0, 1)
                            textds.write(ByteBuffer.allocate(1).put((byte)b.length).array(), 0, 1)
                        }
                        else if (b.length <= Short.MAX_VALUE) {
                            textds.write(new byte[] { (byte)0xB2 }, 0, 1)
                            textds.write(ByteBuffer.allocate(2).putShort((short)b.length).array(), 0, 2)
                        }
                        else {
                            textds.write(new byte[] { (byte)0xB4 }, 0, 1)
                            textds.write(ByteBuffer.allocate(4).putInt((int)b.length).array(), 0, 4)
                        }
                        textds.write(b, 0, b.length)
                    }
                    else {
                        _result = CSocketSendDataBuildResult.ByteArrayLengthOverflowError
                        return
                    }
                    break

                default:
                    _result = CSocketSendDataBuildResult.DataTypeNotImplementedError
                    return
            }
        }

        def textlen = textds.count

        def otl = 0
        if (textlen <= Byte.MAX_VALUE) otl = 2
        else if (textlen <= Short.MAX_VALUE) otl = 3
        else if (textlen <= Integer.MAX_VALUE) otl = 5

        //SOH(1)+OTL(v)+STX(1)+TXT(v)+ETX(1)+CHK(1)+EOT(1)
        def data = new byte[1 + otl + 1 + textlen + 1 + 1 + 1]
        def datapos = 0

        if (textlen <= TXT_MAXLEN) {
            data[datapos] = 0x01
            datapos += 1

            if (textlen <= Byte.MAX_VALUE) {
                data[datapos] = 0x11
                datapos += 1

                System.arraycopy(ByteBuffer.allocate(1).put((byte)textlen).array(), 0, data, datapos, 1)
                datapos += 1
            }
            else if (textlen <= Short.MAX_VALUE) {
                data[datapos] = 0x12
                datapos += 1

                System.arraycopy(ByteBuffer.allocate(2).putShort((short)textlen).array(), 0, data, datapos, 2)
                datapos += 2
            }
            else {
                data[datapos] = 0x14
                datapos += 1

                System.arraycopy(ByteBuffer.allocate(4).putInt((int)textlen).array(), 0, data, datapos, 4)
                datapos += 4
            }

            data[datapos] = 0x02
            datapos += 1

            def text = textds.buffer

            System.arraycopy(text, 0, data, datapos, textlen)
            datapos += textlen

            data[datapos] = 0x03
            datapos += 1

            def checksum = 0x00

            for (int i = 0; i < textlen; i++)
                checksum ^= text[i]

            data[datapos] = checksum
            datapos += 1

            data[datapos] = 0x04
            //datapos += 1
        }
        else {
            _result = CSocketSendDataBuildResult.DataTotalLengthOverflowError
            return
        }

        _bytes = data
        _result = CSocketSendDataBuildResult.Successful
    }
}

enum CSocketSendDataBuildResult {
    ByteArrayLengthOverflowError,
    CommandValueOverflowError,
    DataTotalLengthOverflowError,
    DataTypeNotImplementedError,
    NoData,
    StringLengthOverflowError,
    Successful
}

class TcpServer {
    private def _server

    TcpServer(ServerSocket s) {
        _server = s
    }

    def getRunning() {
        return _server != null
    }

    def close() {
        try {
            _server.close()
            _server = null
        }
        catch (Exception e) {}
    }

    def setAcceptCallback(callback) {
        Thread.start {
            while (running) {
                Socket s = null
                try {
                    s = _server.accept()
                }
                catch (Exception e) {}
                callback.call(new TcpSocket(s))
            }
        }
    }
}

class TcpSocket extends CSocket {
    private def _remoteAddress

    def getConnected() {
        return available && _socket.connected
    }

    def getRemoteAddress() {
        return _remoteAddress
    }

    TcpSocket(s) {
        super(s, CSocketProtocolType.Tcp)

        _remoteAddress = new CSocketAddress("0.0.0.0", 0)

        if (s != null) {
            def address = (InetSocketAddress)s.remoteSocketAddress
            _remoteAddress = new CSocketAddress(address.address, address.port)
        }
    }

    def send(data) {
        super.send(data, null)
    }
}

class UdpSocket extends CSocket {
    UdpSocket(s) {
        super(s, CSocketProtocolType.Udp)
    }

    def send(data, address) {
        super.send(data, address)
    }
}

class NetworkComm {
    def static TcpConnect(address) {
        def s = null
        try {
            s = new Socket(address.host, address.port)
        }
        catch (Exception e) {
        }
        return new TcpSocket(s)
    }

    def static TcpListen(address) {
        def s = null
        try {
            s = new ServerSocket(address.port, 0, address.inetAddress)
        }
        catch (Exception e) {
        }
        return new TcpServer(s)
    }

    def static UdpCast(address) {
        def s = null
        try {
            s = new DatagramSocket(address.port, address.inetAddress)
        }
        catch (Exception e) {
        }
        return new UdpSocket(s)
    }
}


