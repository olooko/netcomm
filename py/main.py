import sys
import threading
import time
import logging
import logging.handlers
from xyz.olooko.comm.netcomm import NetSocketAddress, NetSocketSendData, NetSocketReceivedDataResult, NetSocketSendDataBuildResult
from xyz.olooko.comm.netcomm import TcpListen, TcpConnect, UdpCast


def tcpserver_proc():
    tcpserver = TcpListen(NetSocketAddress('127.0.0.1', 10010))
    logging.info('NetworkComm.TcpServer Started...')

    if tcpserver.started:
        tcpserver.setAcceptCallback(tcpserver_acceptCallback)


def tcpclient_proc():
    tcpsocket = TcpConnect(NetSocketAddress('127.0.0.1', 10010))

    if tcpsocket.available:
        logging.info('NetworkComm.TcpSocket Started...')
        tcpsocket.setReceivedCallback(netsocket_receivedCallback)

        while True:
            if tcpsocket.connected:  
                data = NetSocketSendData(0x88, [-256, True, 'Hello', -1.1, bytearray([0x41,0x42,0x43])])
                if data.buildResult == NetSocketSendDataBuildResult.Successful:
                    tcpsocket.send(data)
            else: 
                break
            time.sleep(5) 


def udpsocket_proc():
    udpsocket = UdpCast(NetSocketAddress('127.0.0.1', 10010))

    if udpsocket.available:
        logging.info('NetworkComm.UdpSocket Started...')
        udpsocket.setReceivedCallback(netsocket_receivedCallback)

        while True:
            data = NetSocketSendData(0x88, [-256, True, 'Hello', -1.1, bytearray([0x41,0x42,0x43])])
            if data.buildResult == NetSocketSendDataBuildResult.Successful:
                udpsocket.send(data, NetSocketAddress('127.0.0.1', 10010))
            time.sleep(5)


def tcpserver_acceptCallback(tcpsocket):
    if tcpsocket.available:
        logging.info('NetworkComm.TcpSocket Accepted')
        tcpsocket.setReceivedCallback(netsocket_receivedCallback)


def netsocket_receivedCallback(socket, data):
    if data.result == NetSocketReceivedDataResult.Completed:
        logging.info("protocol: %s, command: 0x%02X, args: {%s}" % (socket.protocolType, data.command, data.args))
    elif data.result == NetSocketReceivedDataResult.Interrupted:
        logging.info('Interrupted')
    elif data.result == NetSocketReceivedDataResult.ParsingError:
        logging.info('parsing-error')
    elif data.result == NetSocketReceivedDataResult.Closed:
        logging.info('close')
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

    thread1 = threading.Thread(target=tcpserver_proc, args=())
    thread1.isDaemon = True
    thread1.start()

    thread2 = threading.Thread(target=tcpclient_proc, args=())
    thread2.isDaemon = True
    thread2.start()

    thread3 = threading.Thread(target=udpsocket_proc, args=())
    thread3.isDaemon = True
    thread3.start()

    thread1.join()
    thread2.join()
    thread3.join()


