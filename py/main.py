import sys
import threading
import time
import logging
import logging.handlers
from xyz.olooko.comm.netcomm import CBoolean, CByteArray, CFloat, CInteger, CString
from xyz.olooko.comm.netcomm import CSocketAddress, CSocketDataArgs, CSocketProtocolType, \
                                    CSocketReceivedDataResult, CSocketSendData, CSocketSendDataBuildResult
from xyz.olooko.comm.netcomm import TcpConnect, TcpListen, UdpCast


def UdpSocketProc():
    address = CSocketAddress('127.0.0.1', 10010)
    udpsocket = UdpCast(address)
    if udpsocket.available:
        logging.info('UdpSocket Started. %s' % udpsocket.localAddress)
        udpsocket.setReceivedCallback(CSocketReceivedCallback)
        args = CSocketDataArgs()
        args.add(CInteger(-256))
        args.add(CBoolean(True))
        args.add(CString('Hello'))
        args.add(CFloat(-1.1))
        args.add(CByteArray(bytearray([0x41,0x42,0x43])))
        data = CSocketSendData(0x88, args)
        if data.buildResult == CSocketSendDataBuildResult.Successful:
            while True:
                udpsocket.send(data, address)
                time.sleep(5)


def TcpServerProc():
    tcpserver = TcpListen(CSocketAddress('127.0.0.1', 10010))
    logging.info('TcpServer Started.')
    if tcpserver.running:
        tcpserver.setAcceptCallback(TcpServerAcceptCallback)


def TcpClientProc():
    tcpsocket = TcpConnect(CSocketAddress('127.0.0.1', 10010))
    if tcpsocket.available:
        logging.info('TcpClient Started. %s' % tcpsocket.localAddress)
        tcpsocket.setReceivedCallback(CSocketReceivedCallback)
        args = CSocketDataArgs()
        args.add(CInteger(-256))
        args.add(CBoolean(True))
        args.add(CString('Hello'))
        args.add(CFloat(-1.1))
        args.add(CByteArray(bytearray([0x41,0x42,0x43])))
        data = CSocketSendData(0x88, args)
        if data.buildResult == CSocketSendDataBuildResult.Successful:        
            while True:
                if tcpsocket.connected:  
                    tcpsocket.send(data)
                else: 
                    break
                time.sleep(5) 


def TcpServerAcceptCallback(tcpsocket):
    if tcpsocket.available:
        logging.info('TcpClient Accepted. %s' % tcpsocket.remoteAddress)
        tcpsocket.setReceivedCallback(CSocketReceivedCallback)


def CSocketReceivedCallback(socket, data):
    if data.result == CSocketReceivedDataResult.Completed:
        if data.command == 0x88:
            a1 = data.args[0]
            a2 = data.args[1]
            a3 = data.args[2]
            a4 = data.args[3]  
            a5 = data.args.at(4)     
            protocol = ''
            if socket.protocolType == CSocketProtocolType.Tcp:
                protocol = 'TCP'
            elif socket.protocolType == CSocketProtocolType.Udp:
                protocol = 'UDP'
            logging.info('%s %s (%s, %s, %s, %s, [%s])' % (protocol, data.remoteAddress, a1, a2, a3, a4, a5))
    elif data.result == CSocketReceivedDataResult.Interrupted:
        logging.info('Interrupted')
    elif data.result == CSocketReceivedDataResult.ParsingError:
        logging.info('Parsing-Error')
    elif data.result == CSocketReceivedDataResult.Closed:
        logging.info('Close')
        socket.close()


if __name__ == '__main__':
    timedRotatingFileHandler = logging.handlers.TimedRotatingFileHandler(
        filename='debug.log', 
        when='midnight', 
        interval=1, 
        encoding='utf-8',
    )
    timedRotatingFileHandler.suffix = '%Y%m%d'

    loggingFormat = '[%(asctime)s.%(msecs)03d][%(levelname)s] %(message)s'
    loggingDatefmt = '%Y-%m-%d %H:%M:%S'
    #loggingHandlers = [timedRotatingFileHandler, logging.StreamHandler()]
    loggingHandlers = [logging.StreamHandler()]
    logging.basicConfig(format=loggingFormat, datefmt=loggingDatefmt, level=logging.DEBUG, handlers=loggingHandlers)
    
    tcpclients = []

    thread1 = threading.Thread(target=UdpSocketProc, args=())
    thread1.daemon = True
    thread1.start()

    time.sleep(1) 

    thread2 = threading.Thread(target=TcpServerProc, args=())
    thread2.daemon = True
    thread2.start()

    time.sleep(1) 

    thread3 = threading.Thread(target=TcpClientProc, args=())
    thread3.daemon = True
    thread3.start()

    thread1.join()
    thread2.join()
    thread3.join()
