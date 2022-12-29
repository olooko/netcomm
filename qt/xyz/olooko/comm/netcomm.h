#ifndef NETCOMM_H
#define NETCOMM_H

#include <QFuture>
#include <QThread>
#include <QTcpServer>
#include <QTcpSocket>
#include <QUdpSocket>
#include <QVariant>

enum class NetSocketDataManipulationResult
{
    Completed, InProgress, NoData, ParsingError
};

enum class NetSocketDataParsingStep
{
    SOH, OTL, STX, ETX, CHK, EOT
};

enum class NetSocketProtocolType
{
    Tcp, Udp
};

enum class NetSocketReceivedDataResult
{
    Closed, Completed, Interrupted, ParsingError
};

enum class NetSocketSendDataBuildResult
{
    ByteArrayLengthOverflowError,
    CommandValueOverflowError,
    DataTotalLengthOverflowError,
    DataTypeNotImplementedError,
    NoData,
    StringLengthOverflowError,
    Successful
};

class NetSocketAddress
{
public:
    NetSocketAddress();
    NetSocketAddress(QString host, quint16 port);
    NetSocketAddress(QHostAddress host, quint16 port);

    QString getHost();
    quint16 getPort();
    QString toString();

private:
    QString _host;
    quint16 _port;
};

class NetSocketReceivedData
{
public:
    NetSocketReceivedData(quint8 command, QList<QVariant> args, NetSocketReceivedDataResult result, NetSocketAddress address);

    QList<QVariant> getArgs();
    quint8 getCommand();
    NetSocketAddress getRemoteAddress();
    NetSocketReceivedDataResult getResult();

private:
    quint8 _command;
    QList<QVariant> _args;
    NetSocketReceivedDataResult _result;
    NetSocketAddress _address;
};

class NetSocketSendData
{
public:
    NetSocketSendData(quint8 command, QList<QVariant> args);

    QList<QVariant> getArgs();
    NetSocketSendDataBuildResult getBuildResult();
    QByteArray getBytes();
    quint8 getCommand();
    qsizetype getLength();

private:
    const int ARG_MAXLEN = 0x7FFFFF - 5;
    const int TXT_MAXLEN = 0x7FFFFFFF - 10;

    quint8 _command;
    QList<QVariant> _args;
    QByteArray _bytes;
    NetSocketSendDataBuildResult _result;
};

class NetSocketDataArgLength
{
public:
    NetSocketDataArgLength(qsizetype sz, qsizetype argL);

    qsizetype getSize();
    qsizetype getArgLength();

private:
    qsizetype _sz;
    qsizetype _argL;
};

class NetSocketData
{
public:
    NetSocketData();

    void append(QByteArray buffer);
    NetSocketDataManipulationResult manipulate();

    QList<QVariant> getArgs();
    quint8 getCommand();

private:
    quint8 _command;
    QList<QVariant> _args;
    QByteArray _data;
    qsizetype _datapos;
    quint8 _checksum;
    NetSocketDataParsingStep _step;
    qsizetype _textlen;

    NetSocketDataArgLength getArgLength(qsizetype datalen);
};

class NetSocket;
typedef void(*NetSocketReceivedCallback)(NetSocket*, NetSocketReceivedData);

class NetSocket : public QThread
{
    Q_OBJECT
public:
    NetSocket(QAbstractSocket* s, NetSocketProtocolType protocol);

    bool isAvailable();
    NetSocketAddress getLocalAddress();
    NetSocketProtocolType getProtocoltype();

    void close();
    void setReceivedCallback(NetSocketReceivedCallback callback);

protected:
    void run() override;
    void send(NetSocketSendData data, NetSocketAddress address);

    QAbstractSocket* _socket;

private:
    NetSocketProtocolType _protocol;
    NetSocketDataManipulationResult _result;
    NetSocketReceivedCallback _callback;
    NetSocketAddress _localAddress;

    NetSocketData _data;
    QFuture<void> _timeout;

    void sendProc(NetSocketSendData data, NetSocketAddress address, qsizetype bytesTransferred);
    static void checkInterruptedTimeout(NetSocket* s, int seconds, NetSocketAddress address);
};

class TcpSocket : public NetSocket
{
public:
    TcpSocket(QAbstractSocket* s);

    bool isConnected();
    NetSocketAddress getRemoteAddress();

    void send(NetSocketSendData data);

private:
    NetSocketAddress _remoteAddress;
};

class TcpSocket;
typedef void(*TcpServerAcceptCallback)(TcpSocket*);

class TcpServer : public QThread
{
public:
    TcpServer(QTcpServer* s);

    bool isRunning();

    void close();
    void setAcceptCallback(TcpServerAcceptCallback callback);    

protected:
    void run() override;

private:
    QTcpServer* _server;
    TcpServerAcceptCallback _callback;
};

class UdpSocket : public NetSocket
{
public:
    UdpSocket(QAbstractSocket* s);

    void send(NetSocketSendData data, NetSocketAddress address);
};

TcpServer* TcpListen(NetSocketAddress address);

TcpSocket* TcpConnect(NetSocketAddress address);

UdpSocket* UdpCast(NetSocketAddress address);


#endif // NETCOMM_H
