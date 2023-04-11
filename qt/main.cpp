#include <QCoreApplication>
#include <QFuture>
#include <QDebug>
#include <QtConcurrent>

#include "xyz/olooko/comm/netcomm.h"

void CSocketReceived(CSocket* socket, CSocketReceivedData data)
{
    if (data.getResult() == CSocketReceivedDataResult::Completed)
    {
        quint8 command = data.getCommand();
        CSocketDataArgs args = data.getArgs();

        if (command == 0x88)
        {
            CInteger *a1 = (CInteger *)args.at(0);
            CBoolean *a2 = (CBoolean *)args.at(1);
            CString *a3 = (CString *)args.at(2);
            CFloat *a4 = (CFloat *)args.at(3);
            CByteArray *a5 = (CByteArray *)args.at(4);

            QString protocol;
            if (socket->getProtocoltype() == CSocketProtocolType::Tcp)
                protocol = "TCP";
            else if (socket->getProtocoltype() == CSocketProtocolType::Udp)
                protocol = "UDP";

            QString output = QString("%1 %2 (%3, %4, %5, %6, [%7])")
                    .arg(protocol)
                    .arg(data.getRemoteAddress().toString())
                    .arg(a1->toString())
                    .arg(a2->toString())
                    .arg(a3->toString())
                    .arg(a4->toString())
                    .arg(a5->toString());

            qInfo() << output;
        }
    }
    else if (data.getResult() == CSocketReceivedDataResult::Interrupted)
    {
        qInfo() << "Interrupted";
    }
    else if (data.getResult() == CSocketReceivedDataResult::ParsingError)
    {
        qInfo() << "Parsing-Error";
    }
    else if (data.getResult() == CSocketReceivedDataResult::Closed)
    {
        qInfo() << "Close";
        socket->close();
    }
}

void UdpSocketThread()
{
    CSocketAddress address("127.0.0.1", 10010);
    UdpSocket* udpsocket = UdpCast(address);

    if (udpsocket->isAvailable())
    {
        qInfo() << "UdpSocket Started." << udpsocket->getLocalAddress().toString();

        udpsocket->setReceivedCallback(CSocketReceived);

        CSocketDataArgs args;
        args.add(new CInteger(-256));
        args.add(new CBoolean(true));
        args.add(new CString(QString("Hello")));
        args.add(new CFloat(-1.1));

        static const char bytes[] = { 0x41, 0x42, 0x43 };
        args.add(new CByteArray(QByteArray::fromRawData(bytes, sizeof(bytes))));

        CSocketSendData data(0x88, args);

        if (data.getBuildResult() == CSocketSendDataBuildResult::Successful)
        {
            while (true)
            {
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
        qInfo() << "TcpClient Accepted." << tcpsocket->getRemoteAddress().toString();
        tcpsocket->setReceivedCallback(CSocketReceived);
    }
}

void TcpServerThread()
{
    CSocketAddress address("127.0.0.1", 10010);

    TcpServer* tcpserver = TcpListen(address);
    qInfo() << "TcpServer Started.";

    if (tcpserver->isRunning())
    {
        tcpserver->setAcceptCallback(TcpServerAccept);
    }
}

void TcpClientThread()
{
    CSocketAddress address("127.0.0.1", 10010);
    TcpSocket* tcpsocket = TcpConnect(address);

    if (tcpsocket->isAvailable())
    {
        qInfo() << "TcpClient Started." << tcpsocket->getLocalAddress().toString();
        tcpsocket->setReceivedCallback(CSocketReceived);

        CSocketDataArgs args;
        args.add(new CInteger(-256));
        args.add(new CBoolean(true));
        args.add(new CString(QString("Hello")));
        args.add(new CFloat(-1.1));

        static const char bytes[] = { 0x41, 0x42, 0x43 };
        args.add(new CByteArray(QByteArray::fromRawData(bytes, sizeof(bytes))));

        CSocketSendData data(0x88, args);

        if (data.getBuildResult() == CSocketSendDataBuildResult::Successful)
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
