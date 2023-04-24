package xyz.olooko.comm.netcomm

import java.net._
import java.nio.ByteBuffer
import scala.collection.mutable.ArrayBuffer

class CBoolean(private var _value: Boolean) extends IDataType {
    def value: Boolean = _value

    override def getDataType: DataType.Value = DataType.CBoolean
    override def toString: String = _value.toString
}

class CByteArray(private var _value: Array[Byte]) extends IDataType {
    def value: Array[Byte] = _value

    override def getDataType: DataType.Value = DataType.CByteArray
    override def toString: String = {
        var s = ""
        for (b <- _value) {
            if (s ne "") s = s.concat(",")
            s = s.concat("0x%02X".format(b & 0xFF))
        }
        s
    }
}

class CFloat(private var _value: Double) extends IDataType {
    def value: Double = _value

    override def getDataType: DataType.Value = DataType.CFloat
    override def toString: String = _value.toString
}

class CInteger(private var _value: Long) extends IDataType {
    def value: Long = _value

    override def getDataType: DataType.Value = DataType.CInteger
    override def toString: String = _value.toString
}

class CString(private var _value: String) extends IDataType {
    def value: String = _value

    override def getDataType: DataType.Value = DataType.CString
    override def toString: String = _value
}

object DataType extends Enumeration {
    type DataType = Value
    val CBoolean, CByteArray, CFloat, CInteger, CString = Value
}

trait IDataType {
    def getDataType: DataType.Value
    def toString: String
}

abstract class CSocket extends Runnable {
    protected var _socket: Socket = null
    protected var _dgram: DatagramSocket = null
    private var _data: CSocketData = null
    private var _callback: CSocketReceivedCallback = null
    private var _result: CSocketDataManipulationResult.Value = null
    private var _localAddress: CSocketAddress = null
    private var _protocol: CSocketProtocolType.Value = null

    def available: Boolean = {
        if (_protocol eq CSocketProtocolType.Tcp) return _socket != null
        else if (_protocol eq CSocketProtocolType.Udp) return _dgram != null
        false
    }

    def localAddress: CSocketAddress = _localAddress

    def protocolType: CSocketProtocolType.Value = _protocol

    def this(s: Socket, protocolType: CSocketProtocolType.Value) {
        this()
        initialize(s, null, protocolType)
    }

    def this(d: DatagramSocket, protocolType: CSocketProtocolType.Value) {
        this()
        initialize(null, d, protocolType)
    }

    private def initialize(s: Socket, d: DatagramSocket, protocolType: CSocketProtocolType.Value): Unit = {
        _socket = s
        _dgram = d
        _data = new CSocketData()
        _protocol = protocolType
        _result = CSocketDataManipulationResult.NoData
        _localAddress = new CSocketAddress("0.0.0.0", 0)
        if (available) if (_protocol eq CSocketProtocolType.Tcp) {
            val address = _socket.getLocalSocketAddress.asInstanceOf[InetSocketAddress]
            _localAddress = new CSocketAddress(address.getAddress, address.getPort)
        }
        else if (_protocol eq CSocketProtocolType.Udp) {
            val address = _dgram.getLocalSocketAddress.asInstanceOf[InetSocketAddress]
            _localAddress = new CSocketAddress(address.getAddress, address.getPort)
        }
    }

    def close(): Unit = {
        if (available) try if (_protocol eq CSocketProtocolType.Tcp) _socket.close
        else if (_protocol eq CSocketProtocolType.Udp) _dgram.close
        catch {
            case e: Exception =>
        }
    }

    def setReceivedCallback(callback: CSocketReceivedCallback): Unit = {
        if (available) {
            _callback = callback
            val t = new Thread(this)
            t.start()
        }
    }

    protected def _send(data: CSocketSendData, address: CSocketAddress): Unit = {
        if (available) try if (_protocol eq CSocketProtocolType.Tcp) {
            _socket.getOutputStream.write(data.bytes, 0, data.length)
            _socket.getOutputStream.flush
        }
        else if (_protocol eq CSocketProtocolType.Udp) {
            val packet = new DatagramPacket(data.bytes, data.length, address.inetAddress, address.port)
            _dgram.send(packet)
        }
        catch {
            case e: Exception =>
        }
    }

    override def run(): Unit = {
        val buffer = new Array[Byte](4096)
        var remoteAddress = new CSocketAddress("0.0.0.0", 0)
        while (true) {
            var bytesTransferred = 0
            if (_protocol eq CSocketProtocolType.Tcp) {
                try bytesTransferred = _socket.getInputStream.read(buffer)
                catch {
                    case e: Exception =>
                }
                remoteAddress = new CSocketAddress(_socket.getRemoteSocketAddress.asInstanceOf[InetSocketAddress])
            }
            else if (_protocol eq CSocketProtocolType.Udp) try {
                val packet = new DatagramPacket(buffer, buffer.length)
                _dgram.receive(packet)
                remoteAddress = new CSocketAddress(packet.getSocketAddress.asInstanceOf[InetSocketAddress])
                bytesTransferred = packet.getLength
            } catch {
                case e: Exception =>
            }
            if (bytesTransferred > 0) {
                _data.append(buffer, bytesTransferred)
                var looping = true
                while (looping) {
                    _result = _data.manipulate
                    if (_result eq CSocketDataManipulationResult.Completed) {
                        _callback.callMethod(this, new CSocketReceivedData(_data.command, _data.args, CSocketReceivedDataResult.Completed, remoteAddress))
                    }
                    else if (_result eq CSocketDataManipulationResult.ParsingError) {
                        _callback.callMethod(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress))
                        return
                    }
                    else if (_result eq CSocketDataManipulationResult.InProgress) {
                        val paramAddress = remoteAddress
                        val t = new Thread(() => {
                            try Thread.sleep(15000)
                            catch {
                                case e: Exception =>
                            }
                            if (_result eq CSocketDataManipulationResult.InProgress)
                                _callback.callMethod(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, paramAddress))
                        })
                        t.start()
                        looping = false
                    }
                    else if (_result eq CSocketDataManipulationResult.NoData) {
                        looping = false
                    }
                }
            }
            else {
                _callback.callMethod(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.Closed, remoteAddress))
                return
            }
        }
    }
}

class CSocketAddress {
    private var _inetAddress: InetAddress = null
    private var _host: String = null
    private var _port = 0

    def inetAddress: InetAddress = _inetAddress

    def host: String = _host

    def port: Int = _port

    def this(host: String, port: Int) {
        this()
        _port = port
        try {
            _inetAddress = InetAddress.getByName(host)
            _host = host
        } catch {
            case e: Exception =>

        }
    }

    def this(inetAddress: InetAddress, port: Int) {
        this()
        _inetAddress = inetAddress
        _host = inetAddress.getHostAddress
        _port = port
    }

    def this(inetSocketAddress: InetSocketAddress) {
        this()
        _inetAddress = inetSocketAddress.getAddress
        _host = _inetAddress.getHostAddress
        _port = inetSocketAddress.getPort
    }

    override def toString: String = "%s:%d".format(_host, _port)
}

class CSocketData {
    private var _command: Byte = 0x00
    private var _args: CSocketDataArgs = new CSocketDataArgs()
    private var _data: ByteBuffer = ByteBuffer.allocate(0)
    private var _datalen = 0
    private var _datapos = 0
    private var _step: CSocketDataParsingStep.Value = CSocketDataParsingStep.SOH
    private var _checksum = 0x00
    private var _textlen = 0

    def args: CSocketDataArgs = _args

    def command: Int = _command & 0xFF

    def append(buffer: Array[Byte], bytesTransferred: Int): Unit = {
        if (_data.capacity < _datalen + bytesTransferred) {
            _data = ByteBuffer.allocate(_datalen + bytesTransferred).put(_data)
            _data.position(_datalen)
        }
        _data.put(buffer, 0, bytesTransferred)
        _datalen += bytesTransferred
    }

    def manipulate: CSocketDataManipulationResult.Value = {
        var continue: Boolean = false

        while (true) {
            val datalen = _datalen - _datapos
            _step match {
                case CSocketDataParsingStep.SOH =>
                    if (datalen > 0) {
                        if (_data.get(_datapos) == 0x01) {
                            _datapos += 1
                            _step = CSocketDataParsingStep.OTL
                            continue = true
                        }
                        else return CSocketDataManipulationResult.ParsingError
                    }

                case CSocketDataParsingStep.OTL =>
                    if (datalen > 0) {
                        if (Array(0x11.toByte, 0x12.toByte, 0x14.toByte).contains(_data.get(_datapos))) {
                            val a = getArgLength(datalen)
                            if (a.getArgL >= 0) {
                                _textlen = a.getArgL
                                _datapos += 1 + a.getSize
                                _step = CSocketDataParsingStep.STX
                                continue = true
                            }
                        }
                        else return CSocketDataManipulationResult.ParsingError
                    }

                case CSocketDataParsingStep.STX =>
                    if (datalen > 0) {
                        if (_data.get(_datapos) == 0x02) {
                            _datapos += 1
                            _step = CSocketDataParsingStep.ETX
                            continue = true
                        }
                        else return CSocketDataManipulationResult.ParsingError
                    }

                case CSocketDataParsingStep.ETX =>
                    if (datalen > _textlen) {
                        if (_data.get(_datapos + _textlen) == 0x03) {
                            val textfpos = _datapos
                            _command = _data.get(textfpos)
                            _args.clear()
                            _datapos += 1
                            while (_datapos < _textlen + textfpos) {
                                var sz = 0
                                var argL = 0
                                if (Array(0x31.toByte, 0x32.toByte, 0x34.toByte, 0x38.toByte).contains(_data.get(_datapos))) {
                                    sz = (_data.get(_datapos) & 0x0F)
                                    var i: Long = 0
                                    sz match {
                                        case 1 =>
                                            i = _data.get(_datapos + 1).asInstanceOf[Long]

                                        case 2 =>
                                            i = _data.getShort(_datapos + 1).asInstanceOf[Long]

                                        case 4 =>
                                            i = _data.getInt(_datapos + 1).asInstanceOf[Long]

                                        case 8 =>
                                            i = _data.getLong(_datapos + 1)

                                    }
                                    _args.add(new CInteger(i))
                                }
                                else if (Array(0x54.toByte, 0x58.toByte).contains(_data.get(_datapos))) {
                                    sz = (_data.get(_datapos) & 0x0F)
                                    var f = 0.0
                                    sz match {
                                        case 4 =>
                                            f = _data.getFloat(_datapos + 1).asInstanceOf[Double]

                                        case 8 =>
                                            f = _data.getDouble(_datapos + 1)

                                    }
                                    _args.add(new CFloat(f))
                                }
                                else if (Array(0x71.toByte).contains(_data.get(_datapos))) {
                                    sz = 1
                                    _args.add(new CBoolean(if (_data.get(_datapos + 1) == 0) false
                                    else true))
                                }
                                else if (Array(0x91.toByte, 0x92.toByte, 0x94.toByte).contains(_data.get(_datapos))) {
                                    val a = getArgLength(datalen)
                                    sz = a.getSize
                                    argL = a.getArgL
                                    _args.add(new CString(new String(_data.array, _datapos + 1 + sz, argL)))
                                    _datapos += argL
                                }
                                else if (Array(0xB1.toByte, 0xB2.toByte, 0xB4.toByte).contains(_data.get(_datapos))) {
                                    val a = getArgLength(datalen)
                                    sz = a.getSize
                                    argL = a.getArgL
                                    val ba = new Array[Byte](argL)
                                    System.arraycopy(_data.array, _datapos + 1 + sz, ba, 0, argL)
                                    _args.add(new CByteArray(ba))
                                    _datapos += argL
                                }
                                else return CSocketDataManipulationResult.ParsingError
                                _datapos += 1 + sz
                            }
                            _checksum = 0x00
                            for (i <- textfpos until textfpos + _textlen) {
                                _checksum ^= _data.get(i)
                            }
                            _datapos += 1
                            _step = CSocketDataParsingStep.CHK
                            continue = true
                        }
                        else return CSocketDataManipulationResult.ParsingError
                    }

                case CSocketDataParsingStep.CHK =>
                    if (datalen > 0) {
                        if (_data.get(_datapos) == _checksum) {
                            _datapos += 1
                            _step = CSocketDataParsingStep.EOT
                            continue = true
                        }
                        else return CSocketDataManipulationResult.ParsingError
                    }

                case CSocketDataParsingStep.EOT =>
                    if (datalen > 0) {
                        if (_data.get(_datapos) == 0x04) {
                            _datapos += 1
                            _datalen -= _datapos
                            _data = ByteBuffer.allocate(_datalen).put(_data.array, _datapos, _datalen)
                            _datapos = 0
                            _checksum = 0x00
                            _step = CSocketDataParsingStep.SOH
                            _textlen = 0
                            return CSocketDataManipulationResult.Completed
                        }
                        else return CSocketDataManipulationResult.ParsingError
                    }
            }

            if (!continue) {
                if (_datalen == 0)
                    return CSocketDataManipulationResult.NoData
                return CSocketDataManipulationResult.InProgress
            }
        }
        return CSocketDataManipulationResult.NoData
    }

    private def getArgLength(datalen: Int): CSocketDataArgLength = {
        val sz = _data.get(_datapos) & 0x0F
        var argL = -1
        if (datalen > sz) sz match {
            case 1 =>
                argL = _data.get(_datapos + 1).asInstanceOf[Int]

            case 2 =>
                argL = _data.getShort(_datapos + 1).asInstanceOf[Int]

            case 4 =>
                argL = _data.getInt(_datapos + 1)
        }
        new CSocketDataArgLength(sz, argL)
    }
}

class CSocketDataArgLength(private var _sz: Int, private var _argL: Int) {
    def getSize: Int = _sz

    def getArgL: Int = _argL
}

class CSocketDataArgs {
    private val _list: ArrayBuffer[IDataType] = new ArrayBuffer[IDataType]

    def getLength: Int = _list.size

    def add(valueType: IDataType): Unit = {
        _list.append(valueType)
    }

    def at(index: Int): IDataType = _list(index)

    def clear(): Unit = {
        _list.clear
    }
}

object CSocketDataManipulationResult extends Enumeration {
    type CSocketDataManipulationResult = Value
    val Completed, InProgress, NoData, ParsingError = Value
}

object CSocketDataParsingStep extends Enumeration {
    type CSocketDataParsingStep = Value
    val SOH, OTL, STX, ETX, CHK, EOT = Value
}

object CSocketProtocolType extends Enumeration {
    type CSocketProtocolType = Value
    val Tcp, Udp = Value
}

trait CSocketReceivedCallback {
    def callMethod(socket: CSocket, data: CSocketReceivedData): Unit
}

class CSocketReceivedData(cmd: Int, private var _args: CSocketDataArgs, private var _result: CSocketReceivedDataResult.Value, private var _address: CSocketAddress) {
    private val _command: Byte = (cmd & 0xFF).toByte
    def args: CSocketDataArgs = _args

    def command: Int = _command & 0xFF

    def remoteAddress: CSocketAddress = _address

    def result: CSocketReceivedDataResult.Value = _result
}

object CSocketReceivedDataResult extends Enumeration {
    type CSocketReceivedDataResult = Value
    val Closed, Completed, Interrupted, ParsingError = Value
}

object CSocketSendData {
    private val ARG_MAXLEN = 0x7FFFFF - 5
    private val TXT_MAXLEN = Int.MaxValue - 10
}

class CSocketSendData(cmd: Int, private var _args: CSocketDataArgs) {
    private var _bytes: Array[Byte] = null
    private var _result: CSocketSendDataBuildResult.Value = CSocketSendDataBuildResult.NoData
    private val _command: Byte = (cmd & 0xFF).toByte

    def args: CSocketDataArgs = _args

    def buildResult: CSocketSendDataBuildResult.Value = _result

    def bytes: Array[Byte] = _bytes

    def command: Int = _command & 0xFF

    def length: Int = _bytes.length

    initialize(command)

    private def initialize(command: Int): Unit = {
        if (command < 0x00 || command > 0xFF) {
            _result = CSocketSendDataBuildResult.CommandValueOverflowError
            return
        }

        val text = new ArrayBuffer[Byte]
        text.append(_command)
        var n = 0
        while (n < _args.getLength) {
            val arg = _args.at(n)
            arg.getDataType match {
                case DataType.CInteger =>
                    val i = arg.asInstanceOf[CInteger].value
                    if (Byte.MinValue <= i && i <= Byte.MaxValue) {
                        text.append(0x31.toByte)
                        text.appendAll(ByteBuffer.allocate(1).put(i.toByte).array)
                    }
                    else if (Short.MinValue <= i && i <= Short.MaxValue) {
                        text.append(0x32.toByte)
                        text.appendAll(ByteBuffer.allocate(2).putShort(i.toShort).array)
                    }
                    else if (Int.MinValue <= i && i <= Int.MaxValue) {
                        text.append(0x34.toByte)
                        text.appendAll(ByteBuffer.allocate(4).putInt(i.toInt).array)
                    }
                    else {
                        text.append(0x38.toByte)
                        text.appendAll(ByteBuffer.allocate(8).putLong(i.toLong).array)
                    }

                case DataType.CFloat =>
                    val f = arg.asInstanceOf[CFloat].value
                    if (Math.abs(f) <= Float.MaxValue) {
                        text.append(0x54.toByte)
                        text.appendAll(ByteBuffer.allocate(4).putFloat(f.toFloat).array)
                    }
                    else {
                        text.append(0x58.toByte)
                        text.appendAll(ByteBuffer.allocate(8).putDouble(f).array)
                    }

                case DataType.CBoolean =>
                    val b: Byte = if (arg.asInstanceOf[CBoolean].value) 1 else 0
                    text.append(0x71.toByte)
                    text.appendAll(ByteBuffer.allocate(1).put(b).array)

                case DataType.CString =>
                    val s = arg.asInstanceOf[CString].value.getBytes
                    if (s.length <= CSocketSendData.ARG_MAXLEN) {
                        if (s.length <= Byte.MaxValue) {
                            text.append(0x91.toByte)
                            text.appendAll(ByteBuffer.allocate(1).put(s.length.toByte).array)
                        }
                        else if (s.length <= Short.MaxValue) {
                            text.append(0x92.toByte)
                            text.appendAll(ByteBuffer.allocate(2).putShort(s.length.toShort).array)
                        }
                        else {
                            text.append(0x94.toByte)
                            text.appendAll(ByteBuffer.allocate(4).putInt(s.length.toInt).array)
                        }
                        text.appendAll(s)
                    }
                    else {
                        _result = CSocketSendDataBuildResult.StringLengthOverflowError
                        return
                    }

                case DataType.CByteArray =>
                    val ba = arg.asInstanceOf[CByteArray].value
                    if (ba.length <= CSocketSendData.ARG_MAXLEN) {
                        if (ba.length <= Byte.MaxValue) {
                            text.append(0xB1.toByte)
                            text.appendAll(ByteBuffer.allocate(1).put(ba.length.toByte).array)
                        }
                        else if (ba.length <= Short.MaxValue) {
                            text.append(0xB2.toByte)
                            text.appendAll(ByteBuffer.allocate(2).putShort(ba.length.toShort).array)
                        }
                        else {
                            text.append(0xB4.toByte)
                            text.appendAll(ByteBuffer.allocate(4).putInt(ba.length.toInt).array)
                        }
                        text.appendAll(ba)
                    }
                    else {
                        _result = CSocketSendDataBuildResult.ByteArrayLengthOverflowError
                        return
                    }

                case _ =>
                    _result = CSocketSendDataBuildResult.DataTypeNotImplementedError
                    return
            }

            n += 1
        }
        val textlen: Int = text.length
        val data = ArrayBuffer[Byte]()
        if (textlen <= CSocketSendData.TXT_MAXLEN) {
            data.append(0x01.toByte)
            if (textlen <= Byte.MaxValue) {
                data.append(0x11.toByte)
                data.appendAll(ByteBuffer.allocate(1).put(textlen.toByte).array)
            }
            else if (textlen <= Short.MaxValue) {
                data.append(0x12.toByte)
                data.appendAll(ByteBuffer.allocate(2).putShort(textlen.toShort).array)
            }
            else {
                data.append(0x14.toByte)
                data.appendAll(ByteBuffer.allocate(4).putInt(textlen.toInt).array)
            }
            data.append(0x02.toByte)
            data.appendAll(text)
            data.append(0x03.toByte)
            var checksum: Byte = 0x00
            for (i <- 0 until textlen) {
                checksum = (checksum ^ text(i)).toByte
            }
            data.append(checksum)
            data.append(0x04.toByte)
        }
        else {
            _result = CSocketSendDataBuildResult.DataTotalLengthOverflowError
            return
        }

        _bytes = data.toArray
        _result = CSocketSendDataBuildResult.Successful
    }
}

object CSocketSendDataBuildResult extends Enumeration {
    type CSocketSendDataBuildResult = Value
    val ByteArrayLengthOverflowError, CommandValueOverflowError, DataTotalLengthOverflowError, DataTypeNotImplementedError, NoData, StringLengthOverflowError, Successful = Value
}

class TcpServer(private var _server: ServerSocket) extends Runnable {
    private var _callback: TcpServerAcceptCallback = null

    def running: Boolean = _server != null

    def close(): Unit = {
        try {
            _server.close
            _server = null
        } catch {
            case e: Exception =>
        }
    }

    def setAcceptCallback(callback: TcpServerAcceptCallback): Unit = {
        _callback = callback
        val t = new Thread(this)
        t.start()
    }

    override def run(): Unit = {
        if (_callback == null) return
        while (running) {
            var s: Socket = null
            try s = _server.accept
            catch {
                case e: Exception =>
            }
            _callback.callMethod(new TcpSocket(s))
        }
    }
}

trait TcpServerAcceptCallback {
    def callMethod(tcpsocket: TcpSocket): Unit
}

class TcpSocket(s: Socket) extends CSocket(s, CSocketProtocolType.Tcp) {
    private var _remoteAddress: CSocketAddress = new CSocketAddress("0.0.0.0", 0)
    if (s != null) {
        val address = s.getRemoteSocketAddress.asInstanceOf[InetSocketAddress]
        _remoteAddress = new CSocketAddress(address.getAddress, address.getPort)
    }

    def connected: Boolean = available && s.isConnected

    def remoteAddress: CSocketAddress = _remoteAddress

    def send(data: CSocketSendData): Unit = {
        super._send(data, null)
    }
}

class UdpSocket(s: DatagramSocket) extends CSocket(s, CSocketProtocolType.Udp) {
    def send(data: CSocketSendData, address: CSocketAddress): Unit = {
        super._send(data, address)
    }
}

object NetworkComm {
    def TcpConnect(address: CSocketAddress): TcpSocket = {
        var s: Socket = null
        try s = new Socket(address.host, address.port)
        catch {
            case e: Exception =>
        }
        new TcpSocket(s)
    }

    def TcpListen(address: CSocketAddress): TcpServer = {
        var s: ServerSocket = null
        try s = new ServerSocket(address.port, 0, address.inetAddress)
        catch {
            case e: Exception =>
        }
        new TcpServer(s)
    }

    def UdpCast(address: CSocketAddress): UdpSocket = {
        var s: DatagramSocket = null
        try s = new DatagramSocket(address.port, address.inetAddress)
        catch {
            case e: Exception =>
        }
        new UdpSocket(s)
    }
}