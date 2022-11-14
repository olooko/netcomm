from enum import Enum
import asyncio
import socket
import struct
import threading

INT_MAXVAL = 2147483647
FLOAT_MAXVAL = 3.40282347e+38

BUF_SZ = 4096
INTRPT_TM = 4000


class NetSocketDataParsingStep(Enum):
    SOH = 0
    OTL = 1
    STX = 2
    ETX = 3
    CHK = 4
    EOT = 5


class NetSocketDataManipulationResult(Enum):
    Completed = 0    
    InProgress = 1
    NoData = 2
    ParsingError = 3


class NetSocketData:
    @property
    def args(self):
        return self.__args

    @property
    def command(self):
        return self.__command

    def __init__(self):
        self.__command = 0x00
        self.__args = []         
        self.__data = bytearray()
        self.__datapos = 0
        self.__checksum = 0x00
        self.__step = NetSocketDataParsingStep.SOH
        self.__textlen = 0  

    def __getArgLength(self, data):
        sz = data[0] & 0x0F
        fmt = ''
        argL = -1
        if sz == 1: fmt = 'b'
        elif sz == 2: fmt = 'h'
        elif sz == 4: fmt = 'i'
        if len(data) > sz:
            argL = struct.unpack(fmt, data[1: 1 + sz])[0]
        return (sz, argL)               

    def append(self, buffer):
        self.__data.extend(buffer)

    def manipulate(self):
        while True:
            datalen = len(self.__data) - self.__datapos
            if self.__step == NetSocketDataParsingStep.SOH:
                if datalen > 0:
                    if self.__data[self.__datapos] == 0x01:
                        self.__datapos += 1
                        self.__step = NetSocketDataParsingStep.OTL
                        continue
                    else: 
                        return NetSocketDataManipulationResult.ParsingError
            elif self.__step == NetSocketDataParsingStep.OTL:
                if datalen > 0:
                    if self.__data[self.__datapos] in [0x11, 0x12, 0x14]:
                        sz, self.__textlen = self.__getArgLength(self.__data[self.__datapos:])
                        if self.__textlen >= 0:
                            self.__datapos += 1 + sz
                            self.__step = NetSocketDataParsingStep.STX
                            continue
                    else: 
                        return NetSocketDataManipulationResult.ParsingError
            elif self.__step == NetSocketDataParsingStep.STX:
                if datalen > 0:
                    if self.__data[self.__datapos] == 0x02:
                        self.__datapos += 1
                        self.__step = NetSocketDataParsingStep.ETX
                        continue
                    else: 
                        return NetSocketDataManipulationResult.ParsingError
            elif self.__step == NetSocketDataParsingStep.ETX:
                if datalen > self.__textlen:
                    if self.__data[self.__datapos + self.__textlen] == 0x03:
                        try:
                            textfpos = self.__datapos
                            self.__command = self.__data[textfpos]
                            self.__args = []
                            self.__datapos += 1
                            while self.__datapos < self.__textlen + textfpos:  
                                fmt = ''                              
                                sz = 0
                                if self.__data[self.__datapos] in [0x31, 0x32, 0x34, 0x38]:
                                    sz = self.__data[self.__datapos] & 0x0F
                                    if sz == 1: fmt = 'b'
                                    elif sz == 2: fmt = 'h'
                                    elif sz == 4: fmt = 'i'
                                    elif sz == 8: fmt = 'q'
                                    self.__args.append(struct.unpack(fmt, self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0])
                                elif self.__data[self.__datapos] in [0x54, 0x58]:
                                    sz = self.__data[self.__datapos] & 0x0F
                                    if sz == 4: fmt = 'f'
                                    elif sz == 8: fmt = 'd'
                                    self.__args.append(struct.unpack(fmt, self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0])
                                elif self.__data[self.__datapos] in [0x71]:
                                    sz = 1
                                    self.__args.append(struct.unpack('?', self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0])
                                elif self.__data[self.__datapos] in [0x91, 0x92, 0x94]:
                                    sz, argL = self.__getArgLength(self.__data[self.__datapos:])
                                    self.__args.append(self.__data[self.__datapos + 1 + sz: self.__datapos + 1 + sz + argL].decode('utf-8'))
                                    self.__datapos += argL
                                elif self.__data[self.__datapos] in [0xB1, 0xB2, 0xB4]:
                                    sz, argL = self.__getArgLength(self.__data[self.__datapos:])
                                    self.__args.append(self.__data[self.__datapos + 1 + sz: self.__datapos + 1 + sz + argL])
                                    self.__datapos += argL
                                else:
                                    return NetSocketDataManipulationResult.ParsingError
                                self.__datapos += 1 + sz   
                            self.__checksum = 0x00
                            for b in self.__data[textfpos:textfpos+self.__textlen]: self.__checksum ^= b 
                            self.__datapos += 1
                            self.__step = NetSocketDataParsingStep.CHK
                            continue                                                        
                        except: 
                            return NetSocketDataManipulationResult.ParsingError
                    else:
                        return NetSocketDataManipulationResult.ParsingError
            elif self.__step == NetSocketDataParsingStep.CHK:              
                if datalen > 0:
                    if self.__data[self.__datapos] == self.__checksum:
                        self.__datapos += 1
                        self.__step = NetSocketDataParsingStep.EOT
                        continue
                    else: 
                        return NetSocketDataManipulationResult.ParsingError
            elif self.__step == NetSocketDataParsingStep.EOT: 
                if datalen > 0:
                    if self.__data[self.__datapos] == 0x04:
                        self.__datapos += 1
                        del self.__data[:self.__datapos]
                        self.__datapos = 0
                        self.__checksum = 0x00
                        self.__step = NetSocketDataParsingStep.SOH
                        self.__textlen = 0  
                        return NetSocketDataManipulationResult.Completed
                    else: 
                        return NetSocketDataManipulationResult.ParsingError
            if len(self.__data) == 0:
                return NetSocketDataManipulationResult.NoData       
            return NetSocketDataManipulationResult.InProgress


class NetSocketAddress:
    @property
    def host(self):
        return self.__host

    @property
    def port(self):
        return self.__port

    def __init__(self, host, port):
        self.__host = host
        self.__port = port


class NetSocketProtocolType(Enum):
    Tcp = 0
    Udp = 1


class NetSocketReceivedDataResult(Enum):
    Closed = 0    
    Completed = 1
    Interrupted = 2
    ParsingError = 3


class NetSocketReceivedData:
    @property
    def command(self):
        return self.__command

    @property
    def args(self):
        return self.__args

    @property
    def result(self):
        return self.__result

    @property
    def remoteAddress(self):
        return self.__remote_address

    def __init__(self, command, args, result, address):
        self.__command = command
        self.__args = args  
        self.__result = result
        self.__remote_address = address


class NetSocketSendData:
    @property
    def args(self):
        return self.__args

    @property
    def bytes(self):
        return self.__bytes

    @property
    def command(self):
        return self.__command

    @property
    def length(self):
        return len(self.__bytes)

    def __init__(self, command, args):
        self.__command = command
        self.__args = args
        text = bytearray([command])
        for arg in args:
            if type(arg) is int:
                if -128 <= arg and arg <= 127:
                    # 0011 0001
                    text.append(0x31)
                    text.extend(bytearray(struct.pack('b', arg)))
                elif -32768 <= arg and arg <= 32767:
                    # 0011 0010
                    text.append(0x32)
                    text.extend(bytearray(struct.pack('h', arg)))
                elif -2147483648 <= arg and arg <= 2147483647:
                    # 0011 0100
                    text.append(0x34)
                    text.extend(bytearray(struct.pack('i', arg)))
                else:
                    # 0011 1000
                    text.append(0x38)
                    text.extend(bytearray(struct.pack('q', arg)))
            elif type(arg) is float:
                if abs(arg) <= FLOAT_MAXVAL:
                    # 0101 0100
                    text.append(0x54) 
                    text.extend(bytearray(struct.pack('f', arg)))
                else:
                    # 0101 1000
                    text.append(0x58) 
                    text.extend(bytearray(struct.pack('d', arg)))
            elif type(arg) is bool:
                # 0111 0001
                text.append(0x71) 
                text.extend(bytearray(struct.pack('?', arg)))
            elif type(arg) is str:
                arg = arg.encode('utf-8')
                argL = len(arg)
                if argL <= INT_MAXVAL:
                    if argL <= 0x7F:
                        # 1001 0001
                        text.append(0x91)
                        text.extend(bytearray(struct.pack('b', argL)))
                    elif argL <= 0x7FFF:
                        # 1001 0010
                        text.append(0x92)
                        text.extend(bytearray(struct.pack('h', argL)))
                    elif argL <= 0x7FFFFFFF:
                        # 1001 0100
                        text.append(0x94)
                        text.extend(bytearray(struct.pack('i', argL)))
                    text.extend(bytearray(arg))
                else:
                    raise OverflowError('str is too large')
            elif type(arg) is bytearray:
                argL = len(arg)
                if argL <= INT_MAXVAL:
                    if argL <= 0x7F:
                        # 1011 0001
                        text.append(0xB1)
                        text.extend(bytearray(struct.pack('b', argL)))
                    elif argL <= 0x7FFF:
                        # 1011 0010
                        text.append(0xB2)
                        text.extend(bytearray(struct.pack('h', argL)))
                    elif argL <= 0x7FFFFFFF:
                        # 1011 0100
                        text.append(0xB4)
                        text.extend(bytearray(struct.pack('i', argL)))     
                    text.extend(arg)
                else:
                    raise OverflowError('bytearray is too large')
            else:
                raise NotImplementedError('type %s is not implemented' % type(arg))
        # start of header
        data = bytearray([0x01]) 
        textlen = len(text)
        if textlen <= INT_MAXVAL:
            if textlen <= 0x7F:
                # 0001 0001
                data.append(0x11) 
                data.extend(bytearray(struct.pack('b', textlen)))
            elif textlen <= 0x7FFF:
                # 0001 0010
                data.append(0x12) 
                data.extend(bytearray(struct.pack('h', textlen)))
            elif textlen <= 0x7FFFFFFF:
                # 0001 0100
                data.append(0x14) 
                data.extend(bytearray(struct.pack('i', textlen)))
            # start of text
            data.append(0x02) 
            data.extend(text)
            # end of text
            data.append(0x03) 
            checksum = 0x00
            for b in text: checksum ^= b 
            # checksum of text 
            data.append(checksum) 
            # end of transmission
            data.append(0x04) 
        else:
            raise OverflowError('text is too large')
        self.__bytes = data


class NetSocket:
    @property
    def available(self):
        return self.__socket != None

    @property
    def localAddress(self):
        return self.__local_address    

    @property
    def protocolType(self):
        return self.__protocol           

    def __init__(self, socket, protocolType):
        self.__data = NetSocketData()
        self.__protocol = protocolType
        self.__socket = socket
        address = self.__socket.getsockname()
        self.__local_address = NetSocketAddress(address[0], address[1])
        self.__connected = False
        self.__result = NetSocketDataManipulationResult.NoData

    def close(self):
        self.__socket.close()

    def __send(self, data, address):
        self.__send_proc(data, address, 0)

    def __send_proc(self, data, address, bytes_transferred):
        length = 0
        if self.__protocol == NetSocketProtocolType.Tcp:
            length = self.__socket.send(data.bytes[bytes_transferred:])
            if length > 0: 
                self.__connected = True
            else:
                self.__connected = False
        elif self.__protocol == NetSocketProtocolType.Udp:
            length = self.__socket.sendto(data.bytes[bytes_transferred:], (address.host, address.port))
        if length > 0:
            bytes_transferred += length
            if bytes_transferred < len(data.bytes):
                self.__send_proc(data, address, bytes_transferred) 

    def setReceivedCallback(self, callback):
        t = threading.Thread(target=self.__receive_proc, args=(callback,))
        t.start()

    def __receive_proc(self, callback):
        while True:
            remote_address = None
            if self.__protocol == NetSocketProtocolType.Tcp:
                buffer = self.__socket.recv(BUF_SZ)
                if len(buffer) > 0: 
                    self.__connected = True
                else:
                    self.__connected = False
                address = self.__socket.getpeername()
                remote_address = NetSocketAddress(address[0], address[1])
            elif self.__protocol == NetSocketProtocolType.Udp:
                buffer, address = self.__socket.recvfrom(BUF_SZ)
                remote_address = NetSocketAddress(address[0], address[1])
            if len(buffer) > 0:
                self.__data.append(buffer)
                while True:
                    self.__result = self.__data.manipulate()
                    if self.__result == NetSocketDataManipulationResult.Completed:
                        callback(self, NetSocketReceivedData(self.__data.command, self.__data.args, NetSocketReceivedDataResult.Completed, remote_address))
                        continue
                    elif self.__result == NetSocketDataManipulationResult.ParsingError:
                        callback(self, NetSocketReceivedData(0X00, [], NetSocketReceivedDataResult.ParsingError, remote_address))
                        return
                    elif self.__result == NetSocketDataManipulationResult.InProgress:
                        asyncio.run(self.__check_interrupted_timeout(INTRPT_TM, callback, remote_address))
                        break
                    elif self.__result == NetSocketDataManipulationResult.NoData:
                        break
                continue
            else:
                callback(self, NetSocketReceivedData(0x00, [], NetSocketReceivedDataResult.Closed, remote_address))
                break

    async def __check_interrupted_timeout(self, milliseconds, callback, address):
        await asyncio.sleep(float(milliseconds) / 1000)
        if self.__result == NetSocketDataManipulationResult.InProgress:
            callback(self, NetSocketReceivedData(0X00, [], NetSocketReceivedDataResult.Interrupted, address))         


class TcpServer:
    @property
    def started(self):
        return self.__server != None

    def __init__(self, s):
        self.__server = s

    def accept(self):
        s = None
        try:
            s, _ = self.__server.accept()
        except:
            pass
        return TcpSocket(s)

    def close(self):
        self.__server.close()
        self.__server = None


class TcpSocket(NetSocket):
    @property
    def connected(self):
        return self._NetSocket__connected   

    @property
    def remoteAddress(self):
        return self.__remote_address

    def __init__(self, s):
        NetSocket.__init__(self, s, NetSocketProtocolType.Tcp)
        address = s.getpeername()
        self.__remote_address = NetSocketAddress(address[0], address[1])
        self._NetSocket__connected = s != None

    def send(self, data):
        self._NetSocket__send(data, None)


class UdpSocket(NetSocket):
    def __init__(self, s):
        NetSocket.__init__(self, s, NetSocketProtocolType.Udp)

    def send(self, data, address):
        self._NetSocket__send(data, address)
        

def TcpConnect(address):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        s.connect((address.host, address.port))
    except:
        s = None
    return TcpSocket(s)


def TcpListen(address):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        s.bind((address.host, address.port))
        s.listen()
    except:
        s = None
    return TcpServer(s)


def UdpCast(address):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.bind((address.host, address.port))
    except:
        s = None
    return UdpSocket(s)
