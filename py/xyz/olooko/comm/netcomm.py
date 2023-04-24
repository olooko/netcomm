from enum import Enum
from abc import *
import socket
import struct
import threading
import time


class DataType(Enum):
    CBoolean = 0
    CByteArray = 1
    CFloat = 2
    CInteger = 3
    CString = 4


class IDataType:
    @property
    def value(self):
        return self.__value

    def __init__(self, value):
        self.__value = value

    def getDataType(self):
        return DataType.CString
    
    def __str__(self):
        return str(self.__value)
    
    
class CBoolean(IDataType):
    def getDataType(self):
        return DataType.CBoolean


class CByteArray(IDataType):
    def getDataType(self):
        return DataType.CByteArray
        
    def __str__(self):
        s = ''
        ba = self._IDataType__value
        for b in ba:
            if (s != ''): s += ','
            s += '0x%02X' % b
        return s


class CFloat(IDataType):
    def getDataType(self):
        return DataType.CFloat
    

class CInteger(IDataType):
    def getDataType(self):
        return DataType.CInteger
    

class CString(IDataType):
    def getDataType(self):
        return DataType.CString
    

class CSocket(metaclass=ABCMeta):
    @property
    def available(self):
        return self.__socket != None

    @property
    def localAddress(self):
        return self.__localAddress    

    @property
    def protocolType(self):
        return self.__protocol           

    def __init__(self, socket, protocolType):
        self.__data = CSocketData()
        self.__protocol = protocolType
        self.__socket = socket
        self.__connected = False
        self.__result = CSocketDataManipulationResult.NoData
        self.__localAddress = CSocketAddress('0.0.0.0', 0)
        if self.available:
            address = self.__socket.getsockname()
            self.__localAddress = CSocketAddress(address[0], address[1])            

    def close(self):
        if self.available:
            self.__socket.close()

    def setReceivedCallback(self, callback):
        if self.available:  
            t = threading.Thread(target=self.__receiveProc, args=(callback,))
            t.start()

    def __send(self, data, address):
        if self.available:        
            self.__sendProc(data, address, 0)

    def __sendProc(self, data, address, bytesTransferred):
        length = 0
        if self.__protocol == CSocketProtocolType.Tcp:
            length = self.__socket.send(data.bytes[bytesTransferred:])
            self.__connected = True if length > 0 else False
        elif self.__protocol == CSocketProtocolType.Udp:
            length = self.__socket.sendto(data.bytes[bytesTransferred:], (address.host, address.port))
        if length > 0:
            bytesTransferred += length
            if bytesTransferred < len(data.bytes):
                self.__sendProc(data, address, bytesTransferred) 

    def __receiveProc(self, callback):
        while True:
            buffer = None
            remoteAddress = None
            if self.__protocol == CSocketProtocolType.Tcp:
                buffer = self.__socket.recv(4096)
                if len(buffer) > 0: 
                    self.__connected = True
                else:
                    self.__connected = False
                address = self.__socket.getpeername()
                remoteAddress = CSocketAddress(address[0], address[1])
            elif self.__protocol == CSocketProtocolType.Udp:
                buffer, address = self.__socket.recvfrom(4096)
                remoteAddress = CSocketAddress(address[0], address[1])
            if len(buffer) > 0:
                self.__data.append(buffer)
                while True:
                    self.__result = self.__data.manipulate()
                    if self.__result == CSocketDataManipulationResult.Completed:
                        callback(self, CSocketReceivedData(self.__data.command, self.__data.args, CSocketReceivedDataResult.Completed, remoteAddress))
                        continue
                    elif self.__result == CSocketDataManipulationResult.ParsingError:
                        callback(self, CSocketReceivedData(0X00, CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress))
                        return
                    elif self.__result == CSocketDataManipulationResult.InProgress:
                        t = threading.Thread(target=self.__checkInterruptedTimeout, args=(self, 15000, callback, remoteAddress,))
                        t.start()
                        break
                    elif self.__result == CSocketDataManipulationResult.NoData:
                        break
                continue
            else:
                callback(self, CSocketReceivedData(0x00, CSocketDataArgs(), CSocketReceivedDataResult.Closed, remoteAddress))
                break

    @staticmethod
    def __checkInterruptedTimeout(s, milliseconds, callback, address):
        time.sleep(float(milliseconds) / 1000)
        if s.__result == CSocketDataManipulationResult.InProgress:
            callback(s, CSocketReceivedData(0X00, CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, address))         


class CSocketAddress:
    @property
    def host(self):
        return self.__host

    @property
    def port(self):
        return self.__port

    def __init__(self, host, port):
        self.__host = host
        self.__port = port

    def __str__(self):
        return '%s:%d' % (self.__host, self.__port)


class CSocketData:
    @property
    def args(self):
        return self.__args

    @property
    def command(self):
        return self.__command

    def __init__(self):
        self.__command = 0x00
        self.__args = CSocketDataArgs()         
        self.__data = bytearray()
        self.__datapos = 0
        self.__checksum = 0x00
        self.__step = CSocketDataParsingStep.SOH
        self.__textlen = 0  

    def append(self, buffer):
        self.__data.extend(buffer)

    def manipulate(self):
        while True:
            datalen = len(self.__data) - self.__datapos
            if self.__step == CSocketDataParsingStep.SOH:
                if datalen > 0:
                    if self.__data[self.__datapos] == 0x01:
                        self.__datapos += 1
                        self.__step = CSocketDataParsingStep.OTL
                        continue
                    else: 
                        return CSocketDataManipulationResult.ParsingError
            elif self.__step == CSocketDataParsingStep.OTL:
                if datalen > 0:
                    if self.__data[self.__datapos] in [0x11, 0x12, 0x14]:
                        a = self.__getArgLength(datalen)
                        sz = a.size
                        self.__textlen = a.argLength
                        if self.__textlen >= 0:
                            self.__datapos += 1 + sz
                            self.__step = CSocketDataParsingStep.STX
                            continue
                    else: 
                        return CSocketDataManipulationResult.ParsingError
            elif self.__step == CSocketDataParsingStep.STX:
                if datalen > 0:
                    if self.__data[self.__datapos] == 0x02:
                        self.__datapos += 1
                        self.__step = CSocketDataParsingStep.ETX
                        continue
                    else: 
                        return CSocketDataManipulationResult.ParsingError
            elif self.__step == CSocketDataParsingStep.ETX:
                if datalen > self.__textlen:
                    if self.__data[self.__datapos + self.__textlen] == 0x03:
                        textfpos = self.__datapos
                        self.__command = self.__data[textfpos]
                        self.__args.clear()
                        self.__datapos += 1
                        while self.__datapos < self.__textlen + textfpos:  
                            fmt = ''                              
                            sz = 0
                            if self.__data[self.__datapos] in [0x31, 0x32, 0x34, 0x38]:
                                sz = self.__data[self.__datapos] & 0x0F
                                if sz == 1: fmt = 'b'
                                elif sz == 2: fmt = '>h'
                                elif sz == 4: fmt = '>i'
                                elif sz == 8: fmt = '>q'
                                self.__args.add(CInteger(struct.unpack(fmt, self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0]))
                            elif self.__data[self.__datapos] in [0x54, 0x58]:
                                sz = self.__data[self.__datapos] & 0x0F
                                if sz == 4: fmt = '>f'
                                elif sz == 8: fmt = '>d'
                                self.__args.add(CFloat(struct.unpack(fmt, self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0]))
                            elif self.__data[self.__datapos] in [0x71]:
                                sz = 1
                                self.__args.add(CBoolean(struct.unpack('?', self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0]))
                            elif self.__data[self.__datapos] in [0x91, 0x92, 0x94]:
                                a = self.__getArgLength(datalen)
                                sz = a.size
                                argL = a.argLength
                                self.__args.add(CString(self.__data[self.__datapos + 1 + sz: self.__datapos + 1 + sz + argL].decode('utf-8')))
                                self.__datapos += argL
                            elif self.__data[self.__datapos] in [0xB1, 0xB2, 0xB4]:
                                a = self.__getArgLength(datalen)
                                sz = a.size
                                argL = a.argLength
                                self.__args.add(CByteArray(self.__data[self.__datapos + 1 + sz: self.__datapos + 1 + sz + argL]))
                                self.__datapos += argL
                            else:
                                return CSocketDataManipulationResult.ParsingError
                            self.__datapos += 1 + sz   
                        self.__checksum = 0x00
                        for b in self.__data[textfpos:textfpos+self.__textlen]: self.__checksum ^= b 
                        self.__datapos += 1
                        self.__step = CSocketDataParsingStep.CHK
                        continue                                                        
                    else:
                        return CSocketDataManipulationResult.ParsingError
            elif self.__step == CSocketDataParsingStep.CHK:              
                if datalen > 0:
                    if self.__data[self.__datapos] == self.__checksum:
                        self.__datapos += 1
                        self.__step = CSocketDataParsingStep.EOT
                        continue
                    else: 
                        return CSocketDataManipulationResult.ParsingError
            elif self.__step == CSocketDataParsingStep.EOT: 
                if datalen > 0:
                    if self.__data[self.__datapos] == 0x04:
                        self.__datapos += 1
                        del self.__data[:self.__datapos]
                        self.__datapos = 0
                        self.__checksum = 0x00
                        self.__step = CSocketDataParsingStep.SOH
                        self.__textlen = 0 
                        return CSocketDataManipulationResult.Completed
                    else: 
                        return CSocketDataManipulationResult.ParsingError
            if len(self.__data) == 0:
                return CSocketDataManipulationResult.NoData       
            return CSocketDataManipulationResult.InProgress

    def __getArgLength(self, datalen):
        sz = self.__data[self.__datapos] & 0x0F
        fmt = ''
        argL = -1
        if sz == 1: fmt = 'b'
        elif sz == 2: fmt = '>h'
        elif sz == 4: fmt = '>i'
        if datalen > sz:
            argL = struct.unpack(fmt, self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0]
        return CSocketDataArgLength(sz, argL)    


class CSocketDataArgLength:
    @property
    def size(self):
        return self.__sz

    @property
    def argLength(self):
        return self.__argL

    def __init__(self, sz, argL):
        self.__sz = sz
        self.__argL = argL


class CSocketDataArgs:
    @property
    def length(self):
        return len(self.__list)
        
    def __init__(self):
        self.__list = []

    def __getitem__(self, index): 
        return self.__list[index]      

    def add(self, arg):
        self.__list.append(arg)

    def at(self, index):
        return self.__list[index] 

    def clear(self):
        self.__list.clear()


class CSocketDataManipulationResult(Enum):
    Completed = 0    
    InProgress = 1
    NoData = 2
    ParsingError = 3


class CSocketDataParsingStep(Enum):
    SOH = 0
    OTL = 1
    STX = 2
    ETX = 3
    CHK = 4
    EOT = 5


class CSocketProtocolType(Enum):
    Tcp = 0
    Udp = 1


class CSocketReceivedData:
    @property
    def args(self):
        return self.__args

    @property
    def command(self):
        return self.__command

    @property
    def remoteAddress(self):
        return self.__remoteAddress

    @property
    def result(self):
        return self.__result

    def __init__(self, command, args, result, address):
        self.__command = command
        self.__args = args  
        self.__result = result
        self.__remoteAddress = address


class CSocketReceivedDataResult(Enum):
    Closed = 0    
    Completed = 1
    Interrupted = 2
    ParsingError = 3


class CSocketSendData:
    @property
    def args(self):
        return self.__args

    @property
    def buildResult(self):
        return self.__result   

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
        ARG_MAXLEN = 0x7FFFFF - 5
        TXT_MAXLEN = 0x7FFFFFFF - 10
        self.__result = CSocketSendDataBuildResult.NoData
        if command < 0x00 or command > 0xFF:
            self.__result = CSocketSendDataBuildResult.CommandValueOverflowError
            return
        self.__command = command
        self.__args = args
        text = bytearray([command])
        for arg in self.__args:
            argType = arg.getDataType()
            if argType == DataType.CInteger:
                i = arg.value
                if -128 <= i and i <= 127:
                    text.append(0x31)
                    text.extend(bytearray(struct.pack('b', i)))
                elif -32768 <= i and i <= 32767:
                    text.append(0x32)
                    text.extend(bytearray(struct.pack('>h', i)))
                elif -2147483648 <= i and i <= 2147483647:
                    text.append(0x34)
                    text.extend(bytearray(struct.pack('>i', i)))
                else:
                    text.append(0x38)
                    text.extend(bytearray(struct.pack('>q', i)))
            elif argType == DataType.CFloat:
                f = arg.value
                if abs(f) <= 3.40282347e+38:
                    text.append(0x54) 
                    text.extend(bytearray(struct.pack('>f', f)))
                else:
                    text.append(0x58) 
                    text.extend(bytearray(struct.pack('>d', f)))
            elif argType == DataType.CBoolean:
                b = arg.value
                text.append(0x71) 
                text.extend(bytearray(struct.pack('?', b)))
            elif argType == DataType.CString:
                s = arg.value
                s = s.encode('utf-8')
                argL = len(s)
                if argL <= ARG_MAXLEN:
                    if argL <= 127:
                        text.append(0x91)
                        text.extend(bytearray(struct.pack('b', argL)))
                    elif argL <= 32767:
                        text.append(0x92)
                        text.extend(bytearray(struct.pack('>h', argL)))
                    else:
                        text.append(0x94)
                        text.extend(bytearray(struct.pack('>i', argL)))
                    text.extend(bytearray(s))
                else:
                    self.__result = CSocketSendDataBuildResult.StringLengthOverflowError
                    return
            elif argType == DataType.CByteArray:
                ba = arg.value
                argL = len(ba)
                if argL <= ARG_MAXLEN:
                    if argL <= 127:
                        text.append(0xB1)
                        text.extend(bytearray(struct.pack('b', argL)))
                    elif argL <= 32767:
                        text.append(0xB2)
                        text.extend(bytearray(struct.pack('>h', argL)))
                    else:
                        text.append(0xB4)
                        text.extend(bytearray(struct.pack('>i', argL)))     
                    text.extend(ba)
                else:
                    self.__result = CSocketSendDataBuildResult.ByteArrayLengthOverflowError
                    return
            else:
                self.__result = CSocketSendDataBuildResult.DataTypeNotImplementedError
                return
        data = bytearray([0x01]) 
        textlen = len(text)
        if textlen <= TXT_MAXLEN:
            if textlen <= 127:
                data.append(0x11) 
                data.extend(bytearray(struct.pack('b', textlen)))
            elif textlen <= 32767:
                data.append(0x12) 
                data.extend(bytearray(struct.pack('>h', textlen)))
            else:
                data.append(0x14) 
                data.extend(bytearray(struct.pack('>i', textlen)))
            data.append(0x02) 
            data.extend(text)
            data.append(0x03) 
            checksum = 0x00
            for b in text: checksum ^= b 
            data.append(checksum) 
            data.append(0x04) 
        else:
            self.__result = CSocketSendDataBuildResult.DataTotalLengthOverflowError
            return
        self.__bytes = data
        self.__result = CSocketSendDataBuildResult.Successful


class CSocketSendDataBuildResult(Enum):
    ByteArrayLengthOverflowError = 0
    CommandValueOverflowError = 1
    DataTotalLengthOverflowError = 2
    DataTypeNotImplementedError = 3
    NoData = 4
    StringLengthOverflowError = 5 
    Successful = 6


class TcpServer:
    @property
    def running(self):
        return self.__server != None

    def __init__(self, s):
        self.__server = s

    def close(self):
        self.__server.close()
        self.__server = None

    def setAcceptCallback(self, callback):
        t = threading.Thread(target=self.__acceptProc, args=(callback,))
        t.start()

    def __acceptProc(self, callback):
        while self.running:
            s = None
            try:
                s, _ = self.__server.accept()
            except:
                pass
            callback(TcpSocket(s))


class TcpSocket(CSocket):
    @property
    def connected(self):
        return self.available and self._CSocket__connected   

    @property
    def remoteAddress(self):
        return self.__remoteAddress

    def __init__(self, s):
        CSocket.__init__(self, s, CSocketProtocolType.Tcp)
        self._CSocket__connected = s != None
        self.__remoteAddress = CSocketAddress('0.0.0.0', 0)
        if s != None:
            address = s.getpeername()
            self.__remoteAddress = CSocketAddress(address[0], address[1])           

    def send(self, data):
        self._CSocket__send(data, None)


class UdpSocket(CSocket):
    def __init__(self, s):
        CSocket.__init__(self, s, CSocketProtocolType.Udp)

    def send(self, data, address):
        self._CSocket__send(data, address)
        

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
