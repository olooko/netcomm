#include <QCoreApplication>
#include <QFuture>
#include <QDebug>
#include <QtConcurrent>

#include "xyz/olooko/comm/netcomm.h"

void NetSocketReceived(NetSocket* socket, NetSocketReceivedData data)
{
    if (data.getResult() == NetSocketReceivedDataResult::Completed)
    {
        NetSocketProtocolType protocol = socket->getProtocoltype();
        quint8 command = data.getCommand();
        QList<QVariant> args = data.getArgs();

        qInfo() << ((protocol == NetSocketProtocolType::Tcp) ? "Tcp" : "Udp") << command << args;
    }
    else if (data.getResult() == NetSocketReceivedDataResult::Interrupted)
    {
        qInfo() << "Interrupted";
    }
    else if (data.getResult() == NetSocketReceivedDataResult::ParsingError)
    {
        qInfo() << "parsing-error";
    }
    else if (data.getResult() == NetSocketReceivedDataResult::Closed)
    {
        qInfo() << "close";
        socket->close();
    }
}

void TcpServerAccept(TcpSocket* tcpsocket)
{
    if (tcpsocket->isAvailable())
    {
        qInfo() << "NetworkComm.TcpSocket Accepted";
        tcpsocket->setReceivedCallback(NetSocketReceived);
    }
}

void TcpServerThread()
{
    NetSocketAddress address("127.0.0.1", 10010);

    TcpServer* tcpserver = TcpListen(address);
    qInfo() << "NetworkComm.TcpServer Started...";

    if (tcpserver->isStarted())
    {
        tcpserver->setAcceptCallback(TcpServerAccept);
    }
}

void TcpClientThread()
{
    NetSocketAddress address("127.0.0.1", 10010);
    TcpSocket* tcpsocket = TcpConnect(address);

    if (tcpsocket->isAvailable())
    {
        qInfo() << "NetworkComm.TcpSocket Started...";
        tcpsocket->setReceivedCallback(NetSocketReceived);

        while (true)
        {
            if (tcpsocket->isConnected())
            {
                QList<QVariant> args;
                args.append(-256);
                args.append(true);
                args.append(QString("Hello"));
                args.append(-1.1);

                static const char bytes[] = { 0x41, 0x42, 0x43 };
                args.append(QByteArray::fromRawData(bytes, sizeof(bytes)));

                NetSocketSendData data(0x88, args);

                if (data.getBuildResult() == NetSocketSendDataBuildResult::Successful)
                {
                    tcpsocket->send(data);
                }
            }
            else
                break;

            QThread::msleep(5000);
        }
    }
}

void UdpSocketThread()
{
    NetSocketAddress address("127.0.0.1", 10010);
    UdpSocket* udpsocket = UdpCast(address);

    if (udpsocket->isAvailable())
    {
        qInfo() << "NetworkComm.UdpSocket Started...";
        udpsocket->setReceivedCallback(NetSocketReceived);

        while (true)
        {
            QList<QVariant> args;
            args.append(-256);
            args.append(true);
            args.append(QString("Hello"));
            args.append(-1.1);

            static const char bytes[] = { 0x41, 0x42, 0x43 };
            args.append(QByteArray::fromRawData(bytes, sizeof(bytes)));

            NetSocketSendData data(0x88, args);

            if (data.getBuildResult() == NetSocketSendDataBuildResult::Successful)
            {
                NetSocketAddress address("127.0.0.1", 10010);
                udpsocket->send(data, address);
            }

            QThread::msleep(5000);
        }
    }
}

int main(int argc, char *argv[])
{
    QCoreApplication a(argc, argv);

    QFuture<void> f1 = QtConcurrent::run(TcpServerThread);
    QFuture<void> f2 = QtConcurrent::run(TcpClientThread);
    QFuture<void> f3 = QtConcurrent::run(UdpSocketThread);

    f1.waitForFinished();
    f2.waitForFinished();
    f3.waitForFinished();

    return a.exec();
}
