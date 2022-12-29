#include "netcomm.h"

#include <QNetworkDatagram>
#include <QtConcurrent>
#include <QtEndian>
#include <QtMath>

NetSocketAddress::NetSocketAddress()
{
}

NetSocketAddress::NetSocketAddress(QString host, quint16 port)
{
    _host = host;
    _port = port;
}

NetSocketAddress::NetSocketAddress(QHostAddress host, quint16 port)
{
    _host = host.toString();
    _port = port;
}

QString NetSocketAddress::getHost()
{
    return _host;
}

quint16 NetSocketAddress::getPort()
{
    return _port;
}

QString NetSocketAddress::toString()
{
    return QString("%1:%2").arg(_host).arg(_port);
}

NetSocketReceivedData::NetSocketReceivedData(quint8 command, QList<QVariant> args, NetSocketReceivedDataResult result, NetSocketAddress address)
{
    _command = command;
    _args = args;
    _result = result;
    _address = address;
}

QList<QVariant> NetSocketReceivedData::getArgs()
{
    return _args;
}

quint8 NetSocketReceivedData::getCommand()
{
    return _command;
}

NetSocketAddress NetSocketReceivedData::getRemoteAddress()
{
    return _address;
}

NetSocketReceivedDataResult NetSocketReceivedData::getResult()
{
    return _result;
}

NetSocketSendData::NetSocketSendData(quint8 command, QList<QVariant> args)
{
    _result = NetSocketSendDataBuildResult::NoData;

    if (command < 0 || command > 255)
    {
        _result = NetSocketSendDataBuildResult::CommandValueOverflowError;
        return;
    }

    _command = command;
    _args = args;

    QByteArray text;
    QDataStream textds(&text, QIODevice::ReadWrite);
    textds.setByteOrder(QDataStream::LittleEndian);

    textds << command;

    for (qsizetype i = 0; i < _args.length(); i++)
    {
        const QVariant& arg = _args.at(i);

        switch (arg.typeId())
        {
        case QMetaType::UChar:
        case QMetaType::UShort:
        case QMetaType::UInt:
        case QMetaType::ULong:
        case QMetaType::ULongLong:
        case QMetaType::Char:
        case QMetaType::Short:
        case QMetaType::Int:
        case QMetaType::Long:
        case QMetaType::LongLong:
            {
                qint64 i = arg.toLongLong();

                if (std::numeric_limits<qint8>::min() <= i && i <= std::numeric_limits<qint8>::max())
                {
                    textds << (quint8)0x31;
                    textds << qToBigEndian((qint8)i);
                }
                else if (std::numeric_limits<qint16>::min() <= i && i <= std::numeric_limits<qint16>::max())
                {
                    textds << (quint8)0x32;
                    textds << qToBigEndian((qint16)i);

                }
                else if (std::numeric_limits<qint32>::min() <= i && i <= std::numeric_limits<qint32>::max())
                {
                    textds << (quint8)0x34;
                    textds << qToBigEndian((qint32)i);
                }
                else
                {
                    textds << 0x38;
                    textds << qToBigEndian(i);
                }
            }
            break;

        case QMetaType::Float:
        case QMetaType::Double:
            {
                double f = arg.toDouble();

                if (qFabs(f) <= std::numeric_limits<float>::max())
                {
                    textds << (quint8)0x54;
                    textds.setFloatingPointPrecision(QDataStream::SinglePrecision);
                    textds << qToBigEndian((float)f);
                }
                else
                {
                    textds << (quint8)0x58;
                    textds.setFloatingPointPrecision(QDataStream::QDataStream::DoublePrecision);
                    textds << qToBigEndian(f);
                }
            }
            break;

        case QMetaType::Bool:
            textds << (quint8)0x71;
            textds << qToBigEndian(arg.toBool());
            break;

        case QMetaType::QString:
            {
                QByteArray s = arg.toString().toUtf8();

                if (s.length() <= ARG_MAXLEN)
                {
                    if (s.length() <= std::numeric_limits<qint8>::max())
                    {
                        textds << (quint8)0x91;
                        textds << qToBigEndian((qint8)s.length());
                    }
                    else if (s.length() <= std::numeric_limits<qint16>::max())
                    {
                        textds << (quint8)0x92;
                        textds << qToBigEndian((qint16)s.length());
                    }
                    else
                    {
                        textds << (quint8)0x94;
                        textds << qToBigEndian((qint32)s.length());
                    }

                    textds.writeRawData(s.constData(), s.length());
                }
                else
                {
                    _result = NetSocketSendDataBuildResult::StringLengthOverflowError;
                    return;
                }
            }
            break;

        case QMetaType::QByteArray:
            {
                QByteArray b = arg.toByteArray();

                if (b.length() <= ARG_MAXLEN)
                {
                    if (b.length() <= std::numeric_limits<qint8>::max())
                    {
                        textds << (quint8)0xB1;
                        textds << qToBigEndian((qint8)b.length());
                    }
                    else if (b.length() <= std::numeric_limits<qint16>::max())
                    {
                        textds << (quint8)0xB2;
                        textds << qToBigEndian((qint16)b.length());
                    }
                    else
                    {
                        textds << (quint8)0xB4;
                        textds << qToBigEndian((qint32)b.length());
                    }

                    textds.writeRawData(b.constData(), b.length());
                }
                else
                {
                    _result = NetSocketSendDataBuildResult::ByteArrayLengthOverflowError;
                    return;
                }
            }
            break;

        default:
            _result = NetSocketSendDataBuildResult::DataTypeNotImplementedError;
            return;
        }
    }

    qsizetype textlen = text.length();

    QByteArray data;
    QDataStream datads(&data, QIODevice::ReadWrite);

    if (textlen <= TXT_MAXLEN)
    {
        datads << (quint8)0x01;

        if (textlen <= std::numeric_limits<qint8>::max())
        {
            datads << (quint8)0x11;
            datads << qToBigEndian((qint8)textlen);
        }
        else if (textlen <= std::numeric_limits<qint16>::max())
        {
            datads << (quint8)0x12;
            datads << qToBigEndian((qint16)textlen);
        }
        else
        {
            datads << (quint8)0x14;
            datads << qToBigEndian((qint32)textlen);
        }

        datads << (quint8)0x02;

        datads.writeRawData(text.constData(), text.length());

        datads << (quint8)0x03;

        quint8 checksum = 0x00;
        for (qsizetype i = 0; i < textlen; i++) checksum ^= (quint8)text[i];

        datads << checksum;

        datads << (quint8)0x04;
    }
    else
    {
        _result = NetSocketSendDataBuildResult::DataTotalLengthOverflowError;
        return;
    }

    _bytes = data;
    _result = NetSocketSendDataBuildResult::Successful;
}

QList<QVariant> NetSocketSendData::getArgs()
{
    return _args;
}

NetSocketSendDataBuildResult NetSocketSendData::getBuildResult()
{
    return _result;
}

QByteArray NetSocketSendData::getBytes()
{
    return _bytes;
}

quint8 NetSocketSendData::getCommand()
{
    return _command;
}

qsizetype NetSocketSendData::getLength()
{
    return _bytes.length();
}

NetSocketDataArgLength::NetSocketDataArgLength(qsizetype sz, qsizetype argL)
{
    _sz = sz;
    _argL = argL;
}

qsizetype NetSocketDataArgLength::getSize()
{
    return _sz;
}

qsizetype NetSocketDataArgLength::getArgLength()
{
    return _argL;
}

NetSocketData::NetSocketData()
{
    _datapos = 0;
    _step = NetSocketDataParsingStep::SOH;
}

void NetSocketData::append(QByteArray buffer)
{
    _data.append(buffer);
}

NetSocketDataManipulationResult NetSocketData::manipulate()
{
    while (true)
    {
        qsizetype datalen = _data.length() - _datapos;

        switch (_step)
        {
            case NetSocketDataParsingStep::SOH:
                if (datalen > 0)
                {
                    if (_data[_datapos] == 0x01)
                    {
                        _datapos += 1;
                        _step = NetSocketDataParsingStep::OTL;
                        continue;
                    }
                    else
                        return NetSocketDataManipulationResult::ParsingError;
                }
                break;

            case NetSocketDataParsingStep::OTL:
                if (datalen > 0)
                {
                    QList<quint8> list = { 0x11, 0x12, 0x14 };

                    if (list.contains((quint8)_data[_datapos]))
                    {
                        NetSocketDataArgLength a = getArgLength(datalen);

                        if (a.getArgLength() >= 0)
                        {
                            _textlen = a.getArgLength();
                            _datapos += 1 + a.getSize();
                            _step = NetSocketDataParsingStep::STX;
                            continue;
                        }
                    }
                    else
                        return NetSocketDataManipulationResult::ParsingError;
                }
                break;

            case NetSocketDataParsingStep::STX:
                if (datalen > 0)
                {
                    if (_data[_datapos] == 0x02)
                    {
                        _datapos += 1;
                        _step = NetSocketDataParsingStep::ETX;
                        continue;
                    }
                    else
                        return NetSocketDataManipulationResult::ParsingError;
                }
                break;

            case NetSocketDataParsingStep::ETX:
                if (datalen > _textlen)
                {
                    if (_data[_datapos + _textlen] == 0x03)
                    {
                        qsizetype textfpos = _datapos;

                        _command = _data[textfpos];
                        _args.clear();
                        _datapos += 1;

                        while (_datapos < _textlen + textfpos)
                        {
                            qsizetype sz = 0;

                            QList<quint8> list_i = { 0x31, 0x32, 0x34, 0x38 };
                            QList<quint8> list_f = { 0x54, 0x58 };
                            QList<quint8> list_b = { 0x71 };
                            QList<quint8> list_s = { 0x91, 0x92, 0x94 };
                            QList<quint8> list_ba = { 0xB1, 0xB2, 0xB4 };

                            if (list_i.contains((quint8)_data[_datapos]))
                            {
                                sz = (qsizetype)(_data[_datapos] & 0x0F);

                                switch (sz)
                                {
                                    case 1: _args.append((qint8)_data[_datapos + 1]); break;
                                    case 2: _args.append(qFromBigEndian<qint16>(_data.constData() + _datapos + 1)); break;
                                    case 4: _args.append(qFromBigEndian<qint32>(_data.constData() + _datapos + 1)); break;
                                    case 8: _args.append(qFromBigEndian<qint64>(_data.constData() + _datapos + 1)); break;
                                }
                            }
                            else if (list_f.contains((quint8)_data[_datapos]))
                            {
                                sz = (qsizetype)(_data[_datapos] & 0x0F);

                                switch (sz)
                                {
                                    case 4: _args.append(qFromBigEndian<float>(_data.constData() + _datapos + 1)); break;
                                    case 8: _args.append(qFromBigEndian<double>(_data.constData() + _datapos + 1)); break;
                                }
                            }
                            else if (list_b.contains((quint8)_data[_datapos]))
                            {
                                sz = 1;
                                _args.append((bool)_data[_datapos + 1]);
                            }
                            else if (list_s.contains((quint8)_data[_datapos]))
                            {
                                NetSocketDataArgLength a = getArgLength(datalen);

                                _args.append(QString(_data.mid(_datapos + 1 + a.getSize(), a.getArgLength())));

                                _datapos += a.getArgLength();
                                sz = a.getSize();
                            }
                            else if (list_ba.contains((quint8)_data[_datapos]))
                            {
                                NetSocketDataArgLength a = getArgLength(datalen);

                                QByteArray ba = _data.mid(_datapos + 1 + a.getSize(), a.getArgLength());
                                _args.append(ba);

                                _datapos += a.getArgLength();
                                sz = a.getSize();
                            }
                            else
                            {
                                return NetSocketDataManipulationResult::ParsingError;
                            }
                            _datapos += 1 + sz;
                        }

                        _checksum = 0x00;
                        for (int i = textfpos; i < textfpos + _textlen; i++)
                            _checksum ^= _data[i];

                        _datapos += 1;
                        _step = NetSocketDataParsingStep::CHK;
                        continue;
                    }
                    else
                        return NetSocketDataManipulationResult::ParsingError;
                }
                break;

            case NetSocketDataParsingStep::CHK:
                if (datalen > 0)
                {
                    if (_data[_datapos] == _checksum)
                    {
                        _datapos += 1;
                        _step = NetSocketDataParsingStep::EOT;
                        continue;
                    }
                    else
                        return NetSocketDataManipulationResult::ParsingError;
                }
                break;

            case NetSocketDataParsingStep::EOT:
                if (datalen > 0)
                {
                    if (_data[_datapos] == 0x04)
                    {
                        _datapos += 1;
                        _data.remove(0, _datapos);

                        _datapos = 0;
                        _checksum = 0x00;
                        _step = NetSocketDataParsingStep::SOH;
                        _textlen = 0;

                        return NetSocketDataManipulationResult::Completed;
                    }
                    else
                        return NetSocketDataManipulationResult::ParsingError;
                }
                break;
        }

        if (_data.length() == 0)
            return NetSocketDataManipulationResult::NoData;

        return NetSocketDataManipulationResult::InProgress;
    }
}

QList<QVariant> NetSocketData::getArgs()
{
    return _args;
}

quint8 NetSocketData::getCommand()
{
    return _command;
}

NetSocketDataArgLength NetSocketData::getArgLength(qsizetype datalen)
{
    qsizetype sz = (qsizetype)(_data[_datapos] & 0x0F);
    qsizetype argL = -1;

    if (datalen > sz)
    {
        switch (sz)
        {
            case 1: argL = (qint8)_data[_datapos + 1]; break;
            case 2: argL = qFromBigEndian<qint16>(_data.constData() + _datapos + 1); break;
            case 4: argL = qFromBigEndian<qint32>(_data.constData() + _datapos + 1); break;
        }
    }

    return NetSocketDataArgLength(sz, argL);
}

NetSocket::NetSocket(QAbstractSocket* s, NetSocketProtocolType protocol)
{
    _socket = s;

    _protocol = protocol;
    _localAddress = NetSocketAddress("0.0.0.0", 0);

    if (isAvailable())
    {
        _socket->setParent(nullptr);
        _socket->moveToThread(this);
        _localAddress = NetSocketAddress(_socket->localAddress().toString(), _socket->localPort());
    }
}

bool NetSocket::isAvailable()
{
    return _socket != nullptr;
}

NetSocketAddress NetSocket::getLocalAddress()
{ 
    return _localAddress;
}

NetSocketProtocolType NetSocket::getProtocoltype()
{
    return _protocol;
}

void NetSocket::close()
{
    if (isAvailable())
        _socket->close();
}

void NetSocket::setReceivedCallback(NetSocketReceivedCallback callback)
{
    if (isAvailable())
    {
        _callback = callback;
        start();
    }
}

void NetSocket::run()
{
    char* buffer = new char[4096];

    QHostAddress address;
    quint16 port;

    while (true)
    {
        qint64 bytesTransferred = 0;

        if (_protocol == NetSocketProtocolType::Tcp)
        {
            QTcpSocket* s = (QTcpSocket*)(_socket);

            while (true)
            {
                if(s->waitForReadyRead())
                {
                    bytesTransferred = s->read(buffer, 4096);
                    address = s->peerAddress();
                    port = s->peerPort();
                    break;
                }
            }
        }
        else if (_protocol == NetSocketProtocolType::Udp)
        {
            QUdpSocket* s = (QUdpSocket*)(_socket);

            while (true)
            {
                if (s->hasPendingDatagrams())
                {
                    qint64 maxSize = s->pendingDatagramSize();

                    if (maxSize > 4096)
                        maxSize = 4096;

                    bytesTransferred = s->readDatagram(buffer, maxSize, &address, &port);
                    break;
                }
            }
        }

        NetSocketAddress remoteAddress = NetSocketAddress(address, port);

        if (bytesTransferred > 0)
        {
            _data.append(QByteArray(buffer, bytesTransferred));

            while (true)
            {
                _result = _data.manipulate();

                if (_result == NetSocketDataManipulationResult::Completed)
                {
                    _callback(this, NetSocketReceivedData(_data.getCommand(), _data.getArgs(), NetSocketReceivedDataResult::Completed, remoteAddress));
                    continue;
                }
                else if (_result == NetSocketDataManipulationResult::ParsingError)
                {
                    _callback(this, NetSocketReceivedData(0x00, QList<QVariant>(), NetSocketReceivedDataResult::ParsingError, remoteAddress));
                    return;
                }
                else if (_result == NetSocketDataManipulationResult::InProgress)
                {
                    _timeout = QtConcurrent::run(checkInterruptedTimeout, this, 15000, remoteAddress);
                    break;
                }
                else if (_result == NetSocketDataManipulationResult::NoData)
                {
                    break;
                }
            }

            continue;
        }
        else
        {
            _callback(this, NetSocketReceivedData(0x00, QList<QVariant>(), NetSocketReceivedDataResult::Closed, remoteAddress));
            return;
        }
    }
}

void NetSocket::send(NetSocketSendData data, NetSocketAddress address)
{
    if (isAvailable())
    {
        sendProc(data, address, 0);
    }
}

void NetSocket::sendProc(NetSocketSendData data, NetSocketAddress address, qsizetype bytesTransferred)
{
    qint64 length = 0;

    if (_protocol == NetSocketProtocolType::Tcp)
    {
        QTcpSocket* s = (QTcpSocket*)_socket;

        const char *psdata = data.getBytes().constData() + bytesTransferred;
        qint64 len = data.getLength() - bytesTransferred;

        length = s->write(psdata, len);
        s->waitForBytesWritten();
    }
    else if (_protocol == NetSocketProtocolType::Udp)
    {
        QUdpSocket* s = (QUdpSocket*)_socket;

        QHostAddress remoteAddress;
        remoteAddress.setAddress(address.getHost());
        quint16 port = address.getPort();

        const char *psdata = data.getBytes().constData() + bytesTransferred;
        qint64 len = data.getLength() - bytesTransferred;

        length = s->writeDatagram(psdata, len, remoteAddress, port);
    }

    if (length > 0)
    {
        bytesTransferred += length;

        if (bytesTransferred < data.getLength())
            sendProc(data, address, bytesTransferred);
    }
}

void NetSocket::checkInterruptedTimeout(NetSocket* s, int milliseconds, NetSocketAddress address)
{
    QThread::msleep(milliseconds);

    if (s->_result == NetSocketDataManipulationResult::InProgress)
    {
        s->_callback(s, NetSocketReceivedData(0x00, QList<QVariant>(), NetSocketReceivedDataResult::Interrupted, address));
    }
}

TcpServer::TcpServer(QTcpServer* s)
{
    _server = s;
    _server->moveToThread(this);
}

bool TcpServer::isRunning()
{
    return _server != nullptr;
}

void TcpServer::close()
{
    _server->close();
    _server = nullptr;
}

void TcpServer::setAcceptCallback(TcpServerAcceptCallback callback)
{
    _callback = callback;
    start();
}

void TcpServer::run()
{
    while (isRunning())
    {
        QTcpSocket* s = nullptr;
        try {
            if (_server->waitForNewConnection(-1))
            {
                if (_server->hasPendingConnections())
                {
                    s = _server->nextPendingConnection();
                }
            }
        }
        catch (...) { }

        _callback(new TcpSocket(s));
    }
}

TcpSocket::TcpSocket(QAbstractSocket* s) : NetSocket(s, NetSocketProtocolType::Tcp)
{
    _remoteAddress = NetSocketAddress("0.0.0.0", 0);

    if (s != nullptr)
        _remoteAddress = NetSocketAddress(s->peerAddress(), s->peerPort());
}

bool TcpSocket::isConnected()
{
    return isAvailable() && (_socket->state() == QAbstractSocket::ConnectedState);
}

NetSocketAddress TcpSocket::getRemoteAddress()
{
    return _remoteAddress;
}

void TcpSocket::send(NetSocketSendData data)
{
    NetSocketAddress address;
    NetSocket::send(data, address);
}

UdpSocket::UdpSocket(QAbstractSocket* s) : NetSocket(s, NetSocketProtocolType::Udp)
{
}

void UdpSocket::send(NetSocketSendData data, NetSocketAddress address)
{
    NetSocket::send(data, address);
}

TcpServer* TcpListen(NetSocketAddress address)
{
    QTcpServer* s = new QTcpServer();

    try {
        QHostAddress listenAddress;
        listenAddress.setAddress(address.getHost());
        quint16 port = address.getPort();

        if (!s->listen(listenAddress, port))
            s = nullptr;
    }
    catch (...) {
        s = nullptr;
    }

    return new TcpServer(s);
}


TcpSocket* TcpConnect(NetSocketAddress address)
{
    QAbstractSocket* s = new QTcpSocket();

    QHostAddress connectAddress;
    connectAddress.setAddress(address.getHost());
    quint16 port = address.getPort();

    try {
        s->connectToHost(connectAddress, port);
        if (!s->waitForConnected())
            s = nullptr;
    }
    catch (...) {
        s = nullptr;
    }

    return new TcpSocket(s);
}


UdpSocket* UdpCast(NetSocketAddress address)
{
    QAbstractSocket* s = new QUdpSocket();

    QHostAddress bindAddress;
    bindAddress.setAddress(address.getHost());
    quint16 port = address.getPort();

    try {
        if (!s->bind(bindAddress, port))
            s = nullptr;
    }
    catch (...) {
        s = nullptr;
    }

    return new UdpSocket(s);
}
