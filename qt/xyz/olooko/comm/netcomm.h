#ifndef NETCOMM_H
#define NETCOMM_H

#include <QFuture>
#include <QThread>
#include <QTcpServer>
#include <QTcpSocket>
#include <QUdpSocket>
#include <QVariant>

class NetSocketAddress
{
public:
    NetSocketAddress();
    NetSocketAddress(QString host, quint16 port);
    NetSocketAddress(QHostAddress host, quint16 port);

    QString getHost();
    quint16 getPort();

private:
    QString _host;
    quint16 _port;

    QByteArray _bytes;
};

enum class NetSocketProtocolType
{
    Tcp, Udp
};

enum class NetSocketDataManipulationResult
{
    Completed, InProgress, NoData, ParsingError
};

enum class NetSocketReceivedDataResult
{
    Closed, Completed, Interrupted, ParsingError
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

enum class NetSocketSendDataBuildResult
{
	ByteArrayOverflowError,
    NoData,
    StringOverflowError,
    Successful,
	TextOverflowError,
    TypeNotImplementedError
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
    quint8 _command;
    QList<QVariant> _args;

    QByteArray _bytes;
    NetSocketSendDataBuildResult _result;
};

enum class NetSocketDataParsingStep
{
    SOH, OTL, STX, ETX, CHK, EOT
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

    QPair<qsizetype, qsizetype> getArgLength(QByteArray data, qsizetype datalen, qsizetype datapos);
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
    virtual void send(NetSocketSendData data, NetSocketAddress address);

private:
    QAbstractSocket* _socket;
    NetSocketProtocolType _protocol;
    NetSocketDataManipulationResult _result;
    NetSocketReceivedCallback _callback;

    char* _buffer;
    NetSocketData _data;

    QFuture<void> _timeout;

    void sendProc(NetSocketSendData data, NetSocketAddress address, int bytesTransferred);
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
    QAbstractSocket* _socket;
};

class TcpServer
{
public:
    TcpServer(QTcpServer* s);

    TcpSocket* accept();
    void close();

    bool isStarted();

private:
    QTcpServer* _server;
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
