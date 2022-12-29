#include <QCoreApplication>
#include <QFuture>
#include <QDebug>
#include <QtConcurrent>

#include "xyz/olooko/comm/netcomm.h"

void NetSocketReceived(NetSocket* socket, NetSocketReceivedData data)
{
    if (data.getResult() == NetSocketReceivedDataResult::Completed)
    {
        quint8 command = data.getCommand();
        QList<QVariant> args = data.getArgs();

        if (command == 0x88)
        {
            qint64 a1 = args[0].toLongLong();
            bool a2 = args[1].toBool();
            QString a3 = args[2].toString();
            double a4 = args[3].toDouble();
            QString a5 = QString("");
            QByteArray ba = args[4].toByteArray();
            for (qsizetype i = 0; i < ba.size(); i++) {
                if (!a5.isEmpty()) a5 += ",";
                a5 += "0x" + QString::number((quint8)ba[i], 16);
            }

            QString protocol;
            if (socket->getProtocoltype() == NetSocketProtocolType::Tcp)
                protocol = "TCP";
            else if (socket->getProtocoltype() == NetSocketProtocolType::Udp)
                protocol = "UDP";

            QString output = QString("%1 %2 (%3, %4, %5, %6, [%7])")
                    .arg(protocol).arg(data.getRemoteAddress().toString())
                    .arg(a1).arg(a2?"true":"false").arg(a3).arg(a4).arg(a5);

            qInfo() << output;
        }
    }
    else if (data.getResult() == NetSocketReceivedDataResult::Interrupted)
    {
        qInfo() << "Interrupted";
    }
    else if (data.getResult() == NetSocketReceivedDataResult::ParsingError)
    {
        qInfo() << "Parsing-Error";
    }
    else if (data.getResult() == NetSocketReceivedDataResult::Closed)
    {
        qInfo() << "Close";
        socket->close();
    }
}

void UdpSocketThread()
{
    NetSocketAddress address("127.0.0.1", 10010);
    UdpSocket* udpsocket = UdpCast(address);

    if (udpsocket->isAvailable())
    {
        qInfo() << "NetworkComm.UdpSocket Started." << udpsocket->getLocalAddress().toString();
        udpsocket->setReceivedCallback(NetSocketReceived);

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
            while (true)
            {
                NetSocketAddress address("127.0.0.1", 10010);
                udpsocket->send(data, address);

                QThread::msleep(5000);
            }
        }
    }
}

void TcpServerAccept(TcpSocket* tcpsocket)
{
    if (tcpsocket->isAvailable())
    {
        qInfo() << "NetworkComm.TcpClient Accepted." << tcpsocket->getRemoteAddress().toString();
        tcpsocket->setReceivedCallback(NetSocketReceived);
    }
}

void TcpServerThread()
{
    NetSocketAddress address("127.0.0.1", 10010);

    TcpServer* tcpserver = TcpListen(address);
    qInfo() << "NetworkComm.TcpServer Started.";

    if (tcpserver->isRunning())
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
        qInfo() << "NetworkComm.TcpClient Started." << tcpsocket->getLocalAddress().toString();
        tcpsocket->setReceivedCallback(NetSocketReceived);

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
            while (true)
            {
                if (tcpsocket->isConnected())
                {
                    tcpsocket->send(data);
                }
                else
                    break;

                QThread::msleep(5000);
            }
        }
    }
}

int main(int argc, char *argv[])
{
    QCoreApplication a(argc, argv);

    QFuture<void> f1 = QtConcurrent::run(UdpSocketThread);

    QThread::msleep(1000);

    QFuture<void> f2 = QtConcurrent::run(TcpServerThread);

    QThread::msleep(1000);

    QFuture<void> f3 = QtConcurrent::run(TcpClientThread);

    f1.waitForFinished();
    f2.waitForFinished();
    f3.waitForFinished();

    return a.exec();
}
