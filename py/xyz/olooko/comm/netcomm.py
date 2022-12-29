from enum import Enum
import socket
import struct
import threading
import time


class NetSocket:
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
        self.__data = NetSocketData()
        self.__protocol = protocolType
        self.__socket = socket
        self.__connected = False
        self.__result = NetSocketDataManipulationResult.NoData
        self.__localAddress = NetSocketAddress('0.0.0.0', 0)
        if self.available:
            address = self.__socket.getsockname()
            self.__localAddress = NetSocketAddress(address[0], address[1])            

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
        if self.__protocol == NetSocketProtocolType.Tcp:
            length = self.__socket.send(data.bytes[bytesTransferred:])
            self.__connected = True if length > 0 else False
        elif self.__protocol == NetSocketProtocolType.Udp:
            length = self.__socket.sendto(data.bytes[bytesTransferred:], (address.host, address.port))
        if length > 0:
            bytesTransferred += length
            if bytesTransferred < len(data.bytes):
                self.__sendProc(data, address, bytesTransferred) 

    def __receiveProc(self, callback):
        while True:
            buffer = None
            remoteAddress = None
            if self.__protocol == NetSocketProtocolType.Tcp:
                buffer = self.__socket.recv(4096)
                if len(buffer) > 0: 
                    self.__connected = True
                else:
                    self.__connected = False
                address = self.__socket.getpeername()
                remoteAddress = NetSocketAddress(address[0], address[1])
            elif self.__protocol == NetSocketProtocolType.Udp:
                buffer, address = self.__socket.recvfrom(4096)
                remoteAddress = NetSocketAddress(address[0], address[1])
            if len(buffer) > 0:
                self.__data.append(buffer)
                while True:
                    self.__result = self.__data.manipulate()
                    if self.__result == NetSocketDataManipulationResult.Completed:
                        callback(self, NetSocketReceivedData(self.__data.command, self.__data.args, NetSocketReceivedDataResult.Completed, remoteAddress))
                        continue
                    elif self.__result == NetSocketDataManipulationResult.ParsingError:
                        callback(self, NetSocketReceivedData(0X00, [], NetSocketReceivedDataResult.ParsingError, remoteAddress))
                        return
                    elif self.__result == NetSocketDataManipulationResult.InProgress:
                        t = threading.Thread(target=self.__checkInterruptedTimeout, args=(self, 15000, callback, remoteAddress,))
                        t.start()
                        break
                    elif self.__result == NetSocketDataManipulationResult.NoData:
                        break
                continue
            else:
                callback(self, NetSocketReceivedData(0x00, [], NetSocketReceivedDataResult.Closed, remoteAddress))
                break

    @staticmethod
    def __checkInterruptedTimeout(s, milliseconds, callback, address):
        time.sleep(float(milliseconds) / 1000)
        if s.__result == NetSocketDataManipulationResult.InProgress:
            callback(s, NetSocketReceivedData(0X00, [], NetSocketReceivedDataResult.Interrupted, address))         


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

    def __str__(self):
        return '%s:%d' % (self.__host, self.__port)


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
                        a = self.__getArgLength(datalen)
                        sz = a.size
                        self.__textlen = a.argLength
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
                                elif sz == 2: fmt = '>h'
                                elif sz == 4: fmt = '>i'
                                elif sz == 8: fmt = '>q'
                                self.__args.append(struct.unpack(fmt, self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0])
                            elif self.__data[self.__datapos] in [0x54, 0x58]:
                                sz = self.__data[self.__datapos] & 0x0F
                                if sz == 4: fmt = '>f'
                                elif sz == 8: fmt = '>d'
                                self.__args.append(struct.unpack(fmt, self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0])
                            elif self.__data[self.__datapos] in [0x71]:
                                sz = 1
                                self.__args.append(struct.unpack('?', self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0])
                            elif self.__data[self.__datapos] in [0x91, 0x92, 0x94]:
                                a = self.__getArgLength(datalen)
                                sz = a.size
                                argL = a.argLength
                                self.__args.append(self.__data[self.__datapos + 1 + sz: self.__datapos + 1 + sz + argL].decode('utf-8'))
                                self.__datapos += argL
                            elif self.__data[self.__datapos] in [0xB1, 0xB2, 0xB4]:
                                a = self.__getArgLength(datalen)
                                sz = a.size
                                argL = a.argLength
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

    def __getArgLength(self, datalen):
        sz = self.__data[self.__datapos] & 0x0F
        fmt = ''
        argL = -1
        if sz == 1: fmt = 'b'
        elif sz == 2: fmt = '>h'
        elif sz == 4: fmt = '>i'
        if datalen > sz:
            argL = struct.unpack(fmt, self.__data[self.__datapos + 1: self.__datapos + 1 + sz])[0]
        return NetSocketDataArgLength(sz, argL)    


class NetSocketDataArgLength:
    @property
    def size(self):
        return self.__sz

    @property
    def argLength(self):
        return self.__argL

    def __init__(self, sz, argL):
        self.__sz = sz
        self.__argL = argL


class NetSocketDataManipulationResult(Enum):
    Completed = 0    
    InProgress = 1
    NoData = 2
    ParsingError = 3


class NetSocketDataParsingStep(Enum):
    SOH = 0
    OTL = 1
    STX = 2
    ETX = 3
    CHK = 4
    EOT = 5


class NetSocketProtocolType(Enum):
    Tcp = 0
    Udp = 1


class NetSocketReceivedData:
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


class NetSocketReceivedDataResult(Enum):
    Closed = 0    
    Completed = 1
    Interrupted = 2
    ParsingError = 3


class NetSocketSendData:
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
        self.__result = NetSocketSendDataBuildResult.NoData
        if command < 0x00 or command > 0xFF:
            self.__result = NetSocketSendDataBuildResult.CommandValueOverflowError
            return
        self.__command = command
        self.__args = args
        text = bytearray([command])
        for arg in args:
            if type(arg) is int:
                if -128 <= arg and arg <= 127:
                    text.append(0x31)
                    text.extend(bytearray(struct.pack('b', arg)))
                elif -32768 <= arg and arg <= 32767:
                    text.append(0x32)
                    text.extend(bytearray(struct.pack('>h', arg)))
                elif -2147483648 <= arg and arg <= 2147483647:
                    text.append(0x34)
                    text.extend(bytearray(struct.pack('>i', arg)))
                else:
                    text.append(0x38)
                    text.extend(bytearray(struct.pack('>q', arg)))
            elif type(arg) is float:
                if abs(arg) <= 3.40282347e+38:
                    text.append(0x54) 
                    text.extend(bytearray(struct.pack('>f', arg)))
                else:
                    text.append(0x58) 
                    text.extend(bytearray(struct.pack('>d', arg)))
            elif type(arg) is bool:
                text.append(0x71) 
                text.extend(bytearray(struct.pack('?', arg)))
            elif type(arg) is str:
                arg = arg.encode('utf-8')
                argL = len(arg)
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
                    text.extend(bytearray(arg))
                else:
                    self.__result = NetSocketSendDataBuildResult.StringLengthOverflowError
                    return
            elif type(arg) is bytearray:
                argL = len(arg)
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
                    text.extend(arg)
                else:
                    self.__result = NetSocketSendDataBuildResult.ByteArrayLengthOverflowError
                    return
            else:
                self.__result = NetSocketSendDataBuildResult.DataTypeNotImplementedError
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
            self.__result = NetSocketSendDataBuildResult.DataTotalLengthOverflowError
            return
        self.__bytes = data
        self.__result = NetSocketSendDataBuildResult.Successful


class NetSocketSendDataBuildResult(Enum):
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


class TcpSocket(NetSocket):
    @property
    def connected(self):
        return self.available and self._NetSocket__connected   

    @property
    def remoteAddress(self):
        return self.__remoteAddress

    def __init__(self, s):
        NetSocket.__init__(self, s, NetSocketProtocolType.Tcp)
        self._NetSocket__connected = s != None
        self.__remoteAddress = NetSocketAddress('0.0.0.0', 0)
        if s != None:
            address = s.getpeername()
            self.__remoteAddress = NetSocketAddress(address[0], address[1])           

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
