package xyz.olooko.comm.netcomm

import java.net.*
import java.nio.ByteBuffer
import kotlin.math.abs

class CBoolean(value: Boolean) : IDataType {
    private val _value: Boolean
    val value get() = _value

    init {
        _value = value
    }

    override fun toString(): String {
        return _value.toString()
    }
}

class CByteArray(value: ByteArray) : IDataType {
    private val _value: ByteArray
    val value get() = _value

    init {
        _value = value
    }

    override fun toString(): String {
        var s: String = ""
        for (b in _value) {
            if (s !== "") s += ","
            s += String.format("0x%02X", b.toInt() and 0xFF)
        }
        return s
    }
}

class CFloat(value: Double) : IDataType {
    private val _value: Double
    val value get() = _value

    init {
        _value = value
    }

    override fun toString(): String {
        return _value.toString()
    }
}

class CInteger(value: Long) : IDataType {
    private val _value: Long
    val value get() = _value

    init {
        _value = value
    }

    override fun toString(): String {
        return _value.toString()
    }
}

class CString(value: String) : IDataType {
    private val _value: String
    val value get() = _value

    init {
        _value = value
    }

    override fun toString(): String {
        return _value
    }
}

interface IDataType {

    override fun toString(): String
}

open class CSocket(s: Socket?, d: DatagramSocket?, protocolType: CSocketProtocolType) {
    protected var _socket: Socket? = null
    private var _dgram: DatagramSocket? = null
    private var _data: CSocketData
    private var _result: CSocketDataManipulationResult
    private var _localAddress: CSocketAddress
    private var _protocol: CSocketProtocolType

    val available: Boolean get() {
        if (_protocol === CSocketProtocolType.Tcp) {
            return _socket != null
        } else if (_protocol === CSocketProtocolType.Udp) {
            return _dgram != null
        }
        return false
    }

    val localAddress: CSocketAddress get() {
        return _localAddress
    }

    val protocolType: CSocketProtocolType get() {
        return _protocol
    }

    init {
        _socket = s
        _dgram = d
        _data = CSocketData()
        _protocol = protocolType
        _result = CSocketDataManipulationResult.NoData
        _localAddress = CSocketAddress("0.0.0.0", 0)

        if (available) {
            if (_protocol === CSocketProtocolType.Tcp) {
                val address: InetSocketAddress = _socket!!.localSocketAddress as InetSocketAddress
                _localAddress = CSocketAddress(address.address, address.port)
            } else if (_protocol === CSocketProtocolType.Udp) {
                val address: InetSocketAddress = _dgram!!.localSocketAddress as InetSocketAddress
                _localAddress = CSocketAddress(address.address, address.port)
            }
        }
    }

    constructor(s: Socket?, protocolType: CSocketProtocolType)
            : this(s, null, protocolType) {
    }

    constructor(d: DatagramSocket?, protocolType: CSocketProtocolType)
            : this(null, d, protocolType) {
    }

    fun close() {
        if (available) {
            if (_protocol === CSocketProtocolType.Tcp) {
                _socket!!.close()
            } else if (_protocol === CSocketProtocolType.Udp) {
                _dgram!!.close()
            }
        }
    }

    fun setReceivedCallback(callback: (CSocket, CSocketReceivedData) -> Unit) {
        if (available) {
            val t = Thread {
                val buffer = ByteArray(4096)
                var remoteAddress = CSocketAddress("0.0.0.0", 0)
                while (true) {
                    var bytesTransferred = 0

                    if (_protocol === CSocketProtocolType.Tcp) {
                        try {
                            bytesTransferred = _socket!!.getInputStream().read(buffer)
                        } catch (e: Exception) {
                        }
                        remoteAddress = CSocketAddress(_socket!!.remoteSocketAddress as InetSocketAddress)
                    } else if (_protocol === CSocketProtocolType.Udp) {
                        try {
                            val packet = DatagramPacket(buffer, buffer.size)
                            _dgram!!.receive(packet)
                            remoteAddress = CSocketAddress(packet.socketAddress as InetSocketAddress)
                            bytesTransferred = packet.length
                        } catch (e: Exception) {
                        }
                    }
                    if (bytesTransferred > 0) {
                        _data.Append(buffer, bytesTransferred)
                        while (true) {
                            _result = _data.Manipulate()
                            if (_result === CSocketDataManipulationResult.Completed) {
                                callback(this, CSocketReceivedData(_data.command, _data.args, CSocketReceivedDataResult.Completed, remoteAddress!!))
                                continue
                            } else if (_result === CSocketDataManipulationResult.ParsingError) {
                                callback(this, CSocketReceivedData(0x00, CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress!!))
                                return@Thread
                            } else if (_result === CSocketDataManipulationResult.InProgress) {
                                val t = Thread {
                                    Thread.sleep(15000)
                                    if (_result === CSocketDataManipulationResult.InProgress) callback(this, CSocketReceivedData(0x00, CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, remoteAddress!!))
                                }
                                t.start()
                                break
                            } else if (_result === CSocketDataManipulationResult.NoData) {
                                break
                            }
                        }
                        continue
                    } else {
                        callback(this, CSocketReceivedData(0x00, CSocketDataArgs(), CSocketReceivedDataResult.Closed, remoteAddress!!))
                        break
                    }
                }
            }
            t.start()
        }
    }

    protected open fun send(data: CSocketSendData, address: CSocketAddress?) {
        if (available) {
            if (_protocol === CSocketProtocolType.Tcp) {
                _socket!!.getOutputStream().write(data.bytes, 0, data.length)
                _socket!!.getOutputStream().flush()
            } else if (_protocol === CSocketProtocolType.Udp) {
                val packet = DatagramPacket(data.bytes, data.length, address!!.inetAddress, address!!.port)
                _dgram!!.send(packet)
            }
        }
    }
}

class CSocketAddress(host: String, port: Int) {
    private var _inetAddress: InetAddress
    private var _host: String
    private var _port: Int

    val inetAddress: InetAddress? get() {
        return _inetAddress
    }

    val host: String get() {
        return _host
    }

    val port: Int get() {
        return _port
    }

    init  {
        _port = port
        _inetAddress = InetAddress.getByName(host)
        _host = host
    }

    constructor(inetAddress: InetAddress, port: Int)
            : this(inetAddress.hostAddress, port) {
    }

    constructor(iNetSocketAddress: InetSocketAddress)
            : this(iNetSocketAddress.address.hostAddress, iNetSocketAddress.port) {
    }

    override fun toString(): String {
        return String.format("%s:%d", _host, _port)
    }
}

class CSocketData {
    private var _command: Byte = 0x00
    private val _args: CSocketDataArgs
    private var _data: ByteBuffer
    private var _datalen: Int
    private var _datapos: Int
    private var _step: CSocketDataParsingStep
    private var _checksum: Byte
    private var _textlen: Int

    val args: CSocketDataArgs get() {
        return _args
    }

    val command: Int get() {
        return _command.toInt() and 0xFF
    }

    init {
        _args = CSocketDataArgs()
        _data = ByteBuffer.allocate(0)
        _datalen = 0
        _datapos = 0
        _checksum = 0x00
        _step = CSocketDataParsingStep.SOH
        _textlen = 0
    }

    fun Append(buffer: ByteArray, bytesTransferred: Int) {
        if (_data.capacity() < _datalen + bytesTransferred) {
            _data = ByteBuffer.allocate(_datalen + bytesTransferred).put(_data)
            _data.position(_datalen)
        }
        _data.put(buffer, 0, bytesTransferred)
        _datalen += bytesTransferred
    }

    fun Manipulate(): CSocketDataManipulationResult {
        while (true) {
            val datalen = _datalen - _datapos
            when (_step) {
                CSocketDataParsingStep.SOH -> if (datalen > 0) {
                    return if (_data.get(_datapos) == 0x01.toByte()) {
                        _datapos += 1
                        _step = CSocketDataParsingStep.OTL
                        continue
                    } else {
                        CSocketDataManipulationResult.ParsingError
                    }
                }

                CSocketDataParsingStep.OTL -> if (datalen > 0) {
                    if (listOf(0x11.toByte(), 0x12.toByte(), 0x14.toByte()).contains(_data.get(_datapos))) {
                        val a: CSocketDataArgLength = getArgLength(datalen)
                        if (a.argL >= 0) {
                            _textlen = a.argL
                            _datapos += 1 + a.size
                            _step = CSocketDataParsingStep.STX
                            continue
                        }
                    } else {
                        return CSocketDataManipulationResult.ParsingError
                    }
                }

                CSocketDataParsingStep.STX -> if (datalen > 0) {
                    return if (_data.get(_datapos) == 0x02.toByte()) {
                        _datapos += 1
                        _step = CSocketDataParsingStep.ETX
                        continue
                    } else {
                        CSocketDataManipulationResult.ParsingError
                    }
                }

                CSocketDataParsingStep.ETX -> if (datalen > _textlen) {
                    return if (_data.get(_datapos + _textlen) == 0x03.toByte()) {
                        val textfpos = _datapos
                        _command = _data.get(textfpos)
                        _args.clear()
                        _datapos += 1
                        while (_datapos < _textlen + textfpos) {
                            var sz = 0
                            var argL = 0
                            if (listOf(0x31.toByte(), 0x32.toByte(), 0x34.toByte(), 0x38.toByte()).contains(_data.get(_datapos))) {
                                sz = (_data.get(_datapos).toInt() and 0x0F)
                                var i: Long = 0
                                when (sz) {
                                    1 -> i = _data.get(_datapos + 1).toLong()
                                    2 -> i = _data.getShort(_datapos + 1).toLong()
                                    4 -> i = _data.getInt(_datapos + 1).toLong()
                                    8 -> i = _data.getLong(_datapos + 1)
                                }
                                _args.add(CInteger(i))
                            } else if (listOf(0x54.toByte(), 0x58.toByte()).contains(_data.get(_datapos))) {
                                sz = (_data.get(_datapos).toInt() and 0x0F)
                                var f = 0.0
                                when (sz) {
                                    4 -> f = _data.getFloat(_datapos + 1).toDouble()
                                    8 -> f = _data.getDouble(_datapos + 1)
                                }
                                _args.add(CFloat(f))
                            } else if (listOf(0x71.toByte()).contains(_data.get(_datapos))) {
                                sz = 1
                                _args.add(CBoolean(_data.get(_datapos + 1).toInt() != 0))
                            } else if (listOf(0x91.toByte(), 0x92.toByte(), 0x94.toByte()).contains(_data.get(_datapos))) {
                                val a: CSocketDataArgLength = getArgLength(datalen)
                                sz = a.size
                                argL = a.argL
                                _args.add(CString(String(_data.array(), _datapos + 1 + sz, argL)))
                                _datapos += argL
                            } else if (listOf(0xB1.toByte(), 0xB2.toByte(), 0xB4.toByte()).contains(_data.get(_datapos))) {
                                val a: CSocketDataArgLength = getArgLength(datalen)
                                sz = a.size
                                argL = a.argL
                                val ba = ByteArray(argL)
                                System.arraycopy(_data.array(), _datapos + 1 + sz, ba, 0, argL)
                                _args.add(CByteArray(ba))
                                _datapos += argL
                            } else {
                                return CSocketDataManipulationResult.ParsingError
                            }
                            _datapos += 1 + sz
                        }
                        _checksum = 0x00
                        var i = textfpos
                        while (i < textfpos + _textlen) {
                            _checksum = (_checksum.toInt() xor _data.get(i).toInt()).toByte()
                            i++
                        }
                        _datapos += 1
                        _step = CSocketDataParsingStep.CHK
                        continue
                    } else {
                        CSocketDataManipulationResult.ParsingError
                    }
                }

                CSocketDataParsingStep.CHK -> if (datalen > 0) {
                    return if (_data.get(_datapos) == _checksum) {
                        _datapos += 1
                        _step = CSocketDataParsingStep.EOT
                        continue
                    } else {
                        CSocketDataManipulationResult.ParsingError
                    }
                }

                CSocketDataParsingStep.EOT -> if (datalen > 0) {
                    return if (_data.get(_datapos) == 0x04.toByte()) {
                        _datapos += 1
                        _datalen -= _datapos
                        _data = ByteBuffer.allocate(_datalen).put(_data.array(), _datapos, _datalen)
                        _datapos = 0
                        _checksum = 0x00
                        _step = CSocketDataParsingStep.SOH
                        _textlen = 0
                        CSocketDataManipulationResult.Completed
                    } else {
                        CSocketDataManipulationResult.ParsingError
                    }
                }
            }
            return if (_datalen == 0) {
                CSocketDataManipulationResult.NoData
            } else CSocketDataManipulationResult.InProgress
        }
    }

    private fun getArgLength(datalen: Int): CSocketDataArgLength {
        val sz = (_data.get(_datapos).toInt() and 0x0F)
        var argL = -1
        if (datalen > sz) {
            when (sz) {
                1 -> argL = _data.get(_datapos + 1).toInt()
                2 -> argL = _data.getShort(_datapos + 1).toInt()
                4 -> argL = _data.getInt(_datapos + 1).toInt()
            }
        }
        return CSocketDataArgLength(sz, argL)
    }
}

class CSocketDataArgLength(private val _sz: Int, private val _argL: Int) {
    val size: Int get() {
        return _sz
    }

    val argL: Int get() {
        return _argL
    }
}

class CSocketDataArgs {
    private val _list: MutableList<IDataType> = mutableListOf()

    val length: Int get() {
        return _list.size
    }

    fun add(valueType: IDataType) {
        _list.add(valueType)
    }

    fun at(index: Int): IDataType {
        return _list[index]
    }

    fun clear() {
        _list.clear()
    }
}

enum class CSocketDataManipulationResult {
    Completed,
    InProgress,
    NoData,
    ParsingError
}

enum class CSocketDataParsingStep {
    SOH,
    OTL,
    STX,
    ETX,
    CHK,
    EOT
}

enum class CSocketProtocolType {
    Tcp,
    Udp
}

class CSocketReceivedData(command: Int, args: CSocketDataArgs, result: CSocketReceivedDataResult, address: CSocketAddress) {
    private val _command: Byte
    private val _args: CSocketDataArgs
    private val _result: CSocketReceivedDataResult
    private val _address: CSocketAddress

    val args: CSocketDataArgs get() {
        return _args
    }

    val command: Int get() {
        return _command.toInt() and 0xFF
    }

    val remoteAddress: CSocketAddress get() {
        return _address
    }

    val result: CSocketReceivedDataResult get() {
        return _result
    }

    init {
        _command = (command and 0xFF).toByte()
        _args = args
        _result = result
        _address = address
    }
}

enum class CSocketReceivedDataResult {
    Closed,
    Completed,
    Interrupted,
    ParsingError
}

class CSocketSendData(command: Int, args: CSocketDataArgs) {
    private var _command: Byte
    private var _args: CSocketDataArgs
    private var _bytes: ByteArray
    private var _result: CSocketSendDataBuildResult

    val args: CSocketDataArgs get() {
        return _args
    }

    val buildResult: CSocketSendDataBuildResult get() {
        return _result
    }

    val bytes: ByteArray get() {
        return _bytes
    }

    val command: Int get() {
        return _command.toInt() and 0xFF
    }

    val length: Int get() {
        return _bytes.size
    }

    init {
        _bytes = ByteArray(0)
        _result = CSocketSendDataBuildResult.NoData
        _command = (command and 0xFF).toByte()
        _args = args

        if (command in 0x00..0xFF) {
            build()
        }
        else {
            _result = CSocketSendDataBuildResult.CommandValueOverflowError
        }
    }

    private fun build() {
        var text: ByteArray = byteArrayOf(_command)
        for (n in 0 until _args.length) {
            val arg: IDataType = _args.at(n)
            when (arg.javaClass.kotlin.simpleName) {
                "CInteger" -> {
                    val i: Long = (arg as CInteger).value
                    if (Byte.MIN_VALUE <= i && i <= Byte.MAX_VALUE) {
                        text += 0x31.toByte()
                        text += i.toByte()
                    } else if (Short.MIN_VALUE <= i && i <= Short.MAX_VALUE) {
                        text += 0x32.toByte()
                        text += ByteBuffer.allocate(2).putShort(i.toShort()).array()
                    } else if (Integer.MIN_VALUE <= i && i <= Integer.MAX_VALUE) {
                        text += 0x34.toByte()
                        text += ByteBuffer.allocate(4).putInt(i.toInt()).array()
                    } else {
                        text += 0x38.toByte()
                        text += ByteBuffer.allocate(8).putLong(i).array()
                    }
                }
                "CFloat" -> {
                    val f: Double = (arg as CFloat).value
                    if (abs(f) <= Float.MAX_VALUE) {
                        text += 0x54.toByte()
                        text += ByteBuffer.allocate(4).putFloat(f.toFloat()).array()
                    } else {
                        text += 0x58.toByte()
                        text += ByteBuffer.allocate(8).putDouble(f).array()
                    }
                }
                "CBoolean" -> {
                    text += 0x71.toByte()
                    text += ByteBuffer.allocate(1).put((if ((arg as CBoolean).value) 1 else 0).toByte()).array()
                }
                "CString" -> {
                    val s: ByteArray = (arg as CString).value.toByteArray()
                    if (s.size <= ARG_MAXLEN) {
                        if (s.size <= Byte.MAX_VALUE) {
                            text += 0x91.toByte()
                            text += s.size.toByte()
                        } else if (s.size <= Short.MAX_VALUE) {
                            text += 0x92.toByte()
                            text += ByteBuffer.allocate(2).putShort(s.size.toShort()).array()
                        } else {
                            text += 0x94.toByte()
                            text += ByteBuffer.allocate(4).putInt(s.size).array()
                        }
                        text += s
                    } else {
                        _result = CSocketSendDataBuildResult.StringLengthOverflowError
                        return
                    }
                }
                "CByteArray" -> {
                    val b: ByteArray = (arg as CByteArray).value
                    if (b.size <= ARG_MAXLEN) {
                        if (b.size <= Byte.MAX_VALUE) {
                            text += 0xB1.toByte()
                            text += b.size.toByte()
                        } else if (b.size <= Short.MAX_VALUE) {
                            text += 0xB2.toByte()
                            text += ByteBuffer.allocate(2).putShort(b.size.toShort()).array()
                        } else {
                            text += 0xB4.toByte()
                            text += ByteBuffer.allocate(4).putInt(b.size).array()
                        }
                        text += b
                    } else {
                        _result = CSocketSendDataBuildResult.ByteArrayLengthOverflowError
                        return
                    }
                }
                else -> {
                    _result = CSocketSendDataBuildResult.DataTypeNotImplementedError
                    return
                }
            }
        }
        val textlen = text.size

        var data: ByteArray = byteArrayOf()

        if (textlen <= TXT_MAXLEN) {
            data += 0x01.toByte()
            if (textlen <= Byte.MAX_VALUE) {
                data += 0x11.toByte()
                data += textlen.toByte()
            } else if (textlen <= Short.MAX_VALUE) {
                data += 0x12.toByte()
                data += ByteBuffer.allocate(2).putShort(textlen.toShort()).array()
            } else {
                data += 0x14.toByte()
                data += ByteBuffer.allocate(4).putInt(textlen).array()
            }
            data += 0x02.toByte()
            data += text
            data += 0x03.toByte()
            var checksum: Byte = 0x00
            for (i in 0 until textlen) checksum = (checksum.toInt() xor text.get(i).toInt()).toByte()
            data += checksum
            data += 0x04.toByte()
        } else {
            _result = CSocketSendDataBuildResult.DataTotalLengthOverflowError
            return
        }
        _bytes = data
        _result = CSocketSendDataBuildResult.Successful
    }

    companion object {
        private const val ARG_MAXLEN = 0x7FFFFF - 5
        private const val TXT_MAXLEN: Int = Integer.MAX_VALUE - 10
    }
}

enum class CSocketSendDataBuildResult {
    ByteArrayLengthOverflowError,
    CommandValueOverflowError,
    DataTotalLengthOverflowError,
    DataTypeNotImplementedError,
    NoData,
    StringLengthOverflowError,
    Successful
}

class TcpServer(s: ServerSocket?) {
    private var _server: ServerSocket?

    init {
        _server = s
    }

    val running: Boolean get() {
        return _server != null
    }

    fun close() {
        try {
            _server?.close()
            _server = null
        } catch (e: Exception) {
        }
    }

    fun setAcceptCallback(callback: (TcpSocket) -> Unit) {
        val t = Thread {
            if (callback != null) {
                while (running) {
                    var s: Socket? = null
                    try {
                        s = _server?.accept()
                    } catch (e: Exception) {
                    }
                    callback.invoke(TcpSocket(s))
                }
            }
        }
        t.start()
    }
}

class TcpSocket(s: Socket?) : CSocket(s, CSocketProtocolType.Tcp) {
    private var _remoteAddress: CSocketAddress

    val connected: Boolean get() {
        return available && _socket!!.isConnected
    }

    val remoteAddress: CSocketAddress get() {
        return _remoteAddress
    }

    init {
        _remoteAddress = CSocketAddress("0.0.0.0", 0)
        if (s != null) {
            val address: InetSocketAddress = s.remoteSocketAddress as InetSocketAddress
            _remoteAddress = CSocketAddress(address.address, address.port)
        }
    }

    fun send(data: CSocketSendData) {
        super.send(data, null)
    }
}

class UdpSocket(d: DatagramSocket?) : CSocket(d, CSocketProtocolType.Udp) {
    public override fun send(data: CSocketSendData, address: CSocketAddress?) {
        super.send(data, address)
    }
}

fun TcpConnect(address: CSocketAddress): TcpSocket {
    var s: Socket? = null
    try {
        s = Socket(address.host, address.port)
    } catch (e: Exception) {
    }
    return TcpSocket(s)
}

fun TcpListen(address: CSocketAddress): TcpServer {
    var s: ServerSocket? = null
    try {
        s = ServerSocket(address.port, 0, address.inetAddress)
    } catch (e: Exception) {
    }
    return TcpServer(s)
}

fun UdpCast(address: CSocketAddress): UdpSocket {
    var s: DatagramSocket? = null
    try {
        s = DatagramSocket(address.port, address.inetAddress)
    } catch (e: Exception) {
    }
    return UdpSocket(s)
}

