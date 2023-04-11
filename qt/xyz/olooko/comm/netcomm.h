#ifndef NETCOMM_H
#define NETCOMM_H

#include <QFuture>
#include <QThread>
#include <QTcpServer>
#include <QTcpSocket>
#include <QUdpSocket>
#include <QVariant>

enum class DataType
{
    Boolean, ByteArray, Float, Integer, String
};

class IDataType
{
public:
    virtual QString toString() = 0;
    virtual DataType getType() = 0;
};

class CBoolean : public IDataType
{
public:
    CBoolean(bool value);

    bool getValue();
    QString toString();
    DataType getType();

private:
    bool _value;
};

class CByteArray : public IDataType
{
public:
    CByteArray(QByteArray value);

    QByteArray getValue();
    QString toString();
    DataType getType();

private:
    QByteArray _value;
};

class CFloat : public IDataType
{
public:
    CFloat(double value);

    double getValue();
    QString toString();
    DataType getType();

private:
    double _value;
};

class CInteger : public IDataType
{
public:
    CInteger(qint64 value);

    qint64 getValue();
    QString toString();
    DataType getType();

private:
    qint64 _value;
};

class CString : public IDataType
{
public:
    CString(QString value);

    QString getValue();
    QString toString();
    DataType getType();

private:
    QString _value;
};

class CSocketAddress
{
public:
    CSocketAddress();
    CSocketAddress(QString host, quint16 port);
    CSocketAddress(QHostAddress host, quint16 port);

    QString getHost();
    quint16 getPort();
    QString toString();

private:
    QString _host;
    quint16 _port;
};

enum class CSocketDataManipulationResult
{
    Completed, InProgress, NoData, ParsingError
};

enum class CSocketDataParsingStep
{
    SOH, OTL, STX, ETX, CHK, EOT
};

class CSocketDataArgLength
{
public:
    CSocketDataArgLength(qsizetype sz, qsizetype argL);

    qsizetype getSize();
    qsizetype getArgLength();

private:
    qsizetype _sz;
    qsizetype _argL;
};

class CSocketDataArgs
{
public:
    CSocketDataArgs();

    void add(IDataType *arg);
    IDataType* at(int index);
    void clear();

    qsizetype getLength();

private:
    QList<IDataType *> _list;
};

class CSocketData
{
public:
    CSocketData();

    void append(QByteArray buffer);
    CSocketDataManipulationResult manipulate();

    CSocketDataArgs getArgs();
    quint8 getCommand();

private:
    quint8 _command;
    CSocketDataArgs _args;
    QByteArray _data;
    qsizetype _datapos;
    quint8 _checksum;
    CSocketDataParsingStep _step;
    qsizetype _textlen;

    CSocketDataArgLength getArgLength(qsizetype datalen);
};

enum class CSocketReceivedDataResult
{
    Closed, Completed, Interrupted, ParsingError
};

class CSocketReceivedData
{
public:
    CSocketReceivedData(quint8 command, CSocketDataArgs args, CSocketReceivedDataResult result, CSocketAddress address);

    CSocketDataArgs getArgs();
    quint8 getCommand();
    CSocketAddress getRemoteAddress();
    CSocketReceivedDataResult getResult();

private:
    quint8 _command;
    CSocketDataArgs _args;
    CSocketReceivedDataResult _result;
    CSocketAddress _address;
};

enum class CSocketSendDataBuildResult
{
    ByteArrayLengthOverflowError,
    CommandValueOverflowError,
    DataTotalLengthOverflowError,
    DataTypeNotImplementedError,
    NoData,
    StringLengthOverflowError,
    Successful
};

class CSocketSendData
{
public:
    CSocketSendData(quint8 command, CSocketDataArgs args);

    CSocketDataArgs getArgs();
    CSocketSendDataBuildResult getBuildResult();
    QByteArray getBytes();
    quint8 getCommand();
    qsizetype getLength();

private:
    const int ARG_MAXLEN = 0x7FFFFF - 5;
    const int TXT_MAXLEN = 0x7FFFFFFF - 10;

    quint8 _command;
    CSocketDataArgs _args;
    QByteArray _bytes;
    CSocketSendDataBuildResult _result;
};

class CSocket;
typedef void(*CSocketReceivedCallback)(CSocket*, CSocketReceivedData);

enum class CSocketProtocolType
{
    Tcp, Udp
};

class CSocket: public QThread
{
    Q_OBJECT
public:
    CSocket(QAbstractSocket* s, CSocketProtocolType protocol);

    bool isAvailable();
    CSocketAddress getLocalAddress();
    CSocketProtocolType getProtocoltype();

    void close();
    void setReceivedCallback(CSocketReceivedCallback callback);

    virtual bool abstract_class() = 0;

protected:
    void run() override;
    void send(CSocketSendData data, CSocketAddress address);

    QAbstractSocket* _socket;

private:
    CSocketProtocolType _protocol;
    CSocketDataManipulationResult _result;
    CSocketReceivedCallback _callback;
    CSocketAddress _localAddress;

    CSocketData _data;
    QFuture<void> _timeout;

    void sendProc(CSocketSendData data, CSocketAddress address, qsizetype bytesTransferred);
    static void checkInterruptedTimeout(CSocket* s, int seconds, CSocketAddress address);
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

class TcpSocket : public CSocket
{
public:
    TcpSocket(QAbstractSocket* s);

    bool isConnected();
    CSocketAddress getRemoteAddress();

    void send(CSocketSendData data);

    bool abstract_class() { return false; };

private:
    CSocketAddress _remoteAddress;
};

class UdpSocket : public CSocket
{
public:
    UdpSocket(QAbstractSocket* s);

    void send(CSocketSendData data, CSocketAddress address);

    bool abstract_class() { return false; };
};

TcpSocket* TcpConnect(CSocketAddress address);
TcpServer* TcpListen(CSocketAddress address);
UdpSocket* UdpCast(CSocketAddress address);

#endif // NETCOMM_H
