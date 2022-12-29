import sys
import threading
import time
import logging
import logging.handlers
from xyz.olooko.comm.netcomm import NetSocketAddress, NetSocketSendData
from xyz.olooko.comm.netcomm import NetSocketProtocolType, NetSocketReceivedDataResult, NetSocketSendDataBuildResult
from xyz.olooko.comm.netcomm import TcpListen, TcpConnect, UdpCast


def UdpSocketProc():
    udpsocket = UdpCast(NetSocketAddress('127.0.0.1', 10010))
    if udpsocket.available:
        logging.info('NetworkComm.UdpSocket Started. %s' % udpsocket.localAddress)
        udpsocket.setReceivedCallback(NetSocketReceivedCallback)
        data = NetSocketSendData(0x88, [-256, True, 'Hello', -1.1, bytearray([0x41,0x42,0x43])])
        if data.buildResult == NetSocketSendDataBuildResult.Successful:
            while True:
                udpsocket.send(data, NetSocketAddress('127.0.0.1', 10010))
                time.sleep(5)


def TcpServerProc():
    tcpserver = TcpListen(NetSocketAddress('127.0.0.1', 10010))
    logging.info('NetworkComm.TcpServer Started.')
    if tcpserver.running:
        tcpserver.setAcceptCallback(TcpServerAcceptCallback)


def TcpClientProc():
    tcpsocket = TcpConnect(NetSocketAddress('127.0.0.1', 10010))
    if tcpsocket.available:
        logging.info('NetworkComm.TcpClient Started. %s' % tcpsocket.localAddress)
        tcpsocket.setReceivedCallback(NetSocketReceivedCallback)
        data = NetSocketSendData(0x88, [-256, True, 'Hello', -1.1, bytearray([0x41,0x42,0x43])])
        if data.buildResult == NetSocketSendDataBuildResult.Successful:        
            while True:
                if tcpsocket.connected:  
                    tcpsocket.send(data)
                else: 
                    break
                time.sleep(5) 


def TcpServerAcceptCallback(tcpsocket):
    if tcpsocket.available:
        logging.info('NetworkComm.TcpClient Accepted. %s' % tcpsocket.remoteAddress)
        tcpsocket.setReceivedCallback(NetSocketReceivedCallback)


def NetSocketReceivedCallback(socket, data):
    if data.result == NetSocketReceivedDataResult.Completed:
        if data.command == 0x88:
            a1 = data.args[0]
            a2 = data.args[1]
            a3 = data.args[2]
            a4 = data.args[3]          
            a5 = ''
            ba = data.args[4]
            for b in ba:
                if (a5 != ''): a5 += ','
                a5 += '0x%02X' % b
            protocol = ''
            if socket.protocolType == NetSocketProtocolType.Tcp:
                protocol = 'TCP'
            elif socket.protocolType == NetSocketProtocolType.Udp:
                protocol = 'UDP'
            logging.info('%s %s (%d, %r, %s, %f, [%s])' % (protocol, data.remoteAddress, a1, a2, a3, a4, a5))
    elif data.result == NetSocketReceivedDataResult.Interrupted:
        logging.info('Interrupted')
    elif data.result == NetSocketReceivedDataResult.ParsingError:
        logging.info('Parsing-Error')
    elif data.result == NetSocketReceivedDataResult.Closed:
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
    thread1.isDaemon = True
    thread1.start()

    time.sleep(1) 

    thread2 = threading.Thread(target=TcpServerProc, args=())
    thread2.isDaemon = True
    thread2.start()

    time.sleep(1) 

    thread3 = threading.Thread(target=TcpClientProc, args=())
    thread3.isDaemon = True
    thread3.start()

    thread1.join()
    thread2.join()
    thread3.join()
