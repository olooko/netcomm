#include "netcomm.h"

#include <QNetworkDatagram>
#include <QtConcurrent>
#include <QtEndian>
#include <QtMath>

CBoolean::CBoolean(bool value)
{
    _value = value;
}

bool CBoolean::getValue()
{
    return _value;
}

QString CBoolean::toString()
{
    return _value ? "true" : "false";
}

DataType CBoolean::getType()
{
    return DataType::Boolean;
}

CByteArray::CByteArray(QByteArray value)
{
    _value = value;
}

QByteArray CByteArray::getValue()
{
    return _value;
}

QString CByteArray::toString()
{
    QString s = QString("");
    QByteArray ba = _value;
    for (qsizetype i = 0; i < ba.size(); i++) {
        if (!s.isEmpty()) s += ",";
        s += "0x" + QString::number((quint8)ba[i], 16);
    }
    return s;
}

DataType CByteArray::getType()
{
    return DataType::ByteArray;
}

CFloat::CFloat(double value)
{
    _value = value;
}

double CFloat::getValue()
{
    return _value;
}

QString CFloat::toString()
{
    return QString("%1").arg(_value);
}

DataType CFloat::getType()
{
    return DataType::Float;
}

CInteger::CInteger(qint64 value)
{
    _value = value;
}

qint64 CInteger::getValue()
{
    return _value;
}

QString CInteger::toString()
{
    return QString("%1").arg(_value);
}

DataType CInteger::getType()
{
    return DataType::Integer;
}

CString::CString(QString value)
{
    _value = value;
}

QString CString::getValue()
{
    return _value;
}

QString CString::toString()
{
    return _value;
}

DataType CString::getType()
{
    return DataType::String;
}

CSocketAddress::CSocketAddress()
{
}

CSocketAddress::CSocketAddress(QString host, quint16 port)
{
    _host = host;
    _port = port;
}

CSocketAddress::CSocketAddress(QHostAddress host, quint16 port)
{
    _host = host.toString();
    _port = port;
}

QString CSocketAddress::getHost()
{
    return _host;
}

quint16 CSocketAddress::getPort()
{
    return _port;
}

QString CSocketAddress::toString()
{
    return QString("%1:%2").arg(_host).arg(_port);
}

CSocketDataArgLength::CSocketDataArgLength(qsizetype sz, qsizetype argL)
{
    _sz = sz;
    _argL = argL;
}

qsizetype CSocketDataArgLength::getSize()
{
    return _sz;
}

qsizetype CSocketDataArgLength::getArgLength()
{
    return _argL;
}

CSocketDataArgs::CSocketDataArgs()
{

}

void CSocketDataArgs::add(IDataType *arg)
{
    _list.append(arg);
}

IDataType* CSocketDataArgs::at(int index)
{
    return _list.at(index);
}

void CSocketDataArgs::clear()
{
    _list.clear();
}

qsizetype CSocketDataArgs::getLength()
{
    return _list.length();
}

CSocketData::CSocketData()
{
    _datapos = 0;
    _step = CSocketDataParsingStep::SOH;
}

void CSocketData::append(QByteArray buffer)
{
    _data.append(buffer);
}

CSocketDataManipulationResult CSocketData::manipulate()
{
    while (true)
    {
        qsizetype datalen = _data.length() - _datapos;

        switch (_step)
        {
            case CSocketDataParsingStep::SOH:
                if (datalen > 0)
                {
                    if (_data[_datapos] == 0x01)
                    {
                        _datapos += 1;
                        _step = CSocketDataParsingStep::OTL;
                        continue;
                    }
                    else
                        return CSocketDataManipulationResult::ParsingError;
                }
                break;

            case CSocketDataParsingStep::OTL:
                if (datalen > 0)
                {
                    QList<quint8> list = { 0x11, 0x12, 0x14 };

                    if (list.contains((quint8)_data[_datapos]))
                    {
                        CSocketDataArgLength a = getArgLength(datalen);

                        if (a.getArgLength() >= 0)
                        {
                            _textlen = a.getArgLength();
                            _datapos += 1 + a.getSize();
                            _step = CSocketDataParsingStep::STX;
                            continue;
                        }
                    }
                    else
                        return CSocketDataManipulationResult::ParsingError;
                }
                break;

            case CSocketDataParsingStep::STX:
                if (datalen > 0)
                {
                    if (_data[_datapos] == 0x02)
                    {
                        _datapos += 1;
                        _step = CSocketDataParsingStep::ETX;
                        continue;
                    }
                    else
                        return CSocketDataManipulationResult::ParsingError;
                }
                break;

            case CSocketDataParsingStep::ETX:
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
                                qint64 i = 0;
                                switch (sz)
                                {
                                    case 1: i = (qint64)_data[_datapos + 1]; break;
                                    case 2: i = (qint64)qFromBigEndian<qint16>(_data.constData() + _datapos + 1); break;
                                    case 4: i = (qint64)qFromBigEndian<qint32>(_data.constData() + _datapos + 1); break;
                                    case 8: i = qFromBigEndian<qint64>(_data.constData() + _datapos + 1); break;
                                }
                                _args.add(new CInteger(i));
                            }
                            else if (list_f.contains((quint8)_data[_datapos]))
                            {
                                sz = (qsizetype)(_data[_datapos] & 0x0F);
                                double f = 0.0;
                                switch (sz)
                                {
                                    case 4: f = (double)qFromBigEndian<float>(_data.constData() + _datapos + 1); break;
                                    case 8: f = qFromBigEndian<double>(_data.constData() + _datapos + 1); break;
                                }
                                _args.add(new CFloat(f));
                            }
                            else if (list_b.contains((quint8)_data[_datapos]))
                            {
                                sz = 1;
                                _args.add(new CBoolean((bool)_data[_datapos + 1]));
                            }
                            else if (list_s.contains((quint8)_data[_datapos]))
                            {
                                CSocketDataArgLength a = getArgLength(datalen);

                                _args.add(new CString(QString(_data.mid(_datapos + 1 + a.getSize(), a.getArgLength()))));

                                _datapos += a.getArgLength();
                                sz = a.getSize();
                            }
                            else if (list_ba.contains((quint8)_data[_datapos]))
                            {
                                CSocketDataArgLength a = getArgLength(datalen);

                                QByteArray ba = _data.mid(_datapos + 1 + a.getSize(), a.getArgLength());
                                _args.add(new CByteArray(ba));

                                _datapos += a.getArgLength();
                                sz = a.getSize();
                            }
                            else
                            {
                                return CSocketDataManipulationResult::ParsingError;
                            }
                            _datapos += 1 + sz;
                        }

                        _checksum = 0x00;
                        for (int i = textfpos; i < textfpos + _textlen; i++)
                            _checksum ^= _data[i];

                        _datapos += 1;
                        _step = CSocketDataParsingStep::CHK;
                        continue;
                    }
                    else
                        return CSocketDataManipulationResult::ParsingError;
                }
                break;

            case CSocketDataParsingStep::CHK:
                if (datalen > 0)
                {
                    if (_data[_datapos] == _checksum)
                    {
                        _datapos += 1;
                        _step = CSocketDataParsingStep::EOT;
                        continue;
                    }
                    else
                        return CSocketDataManipulationResult::ParsingError;
                }
                break;

            case CSocketDataParsingStep::EOT:
                if (datalen > 0)
                {
                    if (_data[_datapos] == 0x04)
                    {
                        _datapos += 1;
                        _data.remove(0, _datapos);

                        _datapos = 0;
                        _checksum = 0x00;
                        _step = CSocketDataParsingStep::SOH;
                        _textlen = 0;

                        return CSocketDataManipulationResult::Completed;
                    }
                    else
                        return CSocketDataManipulationResult::ParsingError;
                }
                break;
        }

        if (_data.length() == 0)
            return CSocketDataManipulationResult::NoData;

        return CSocketDataManipulationResult::InProgress;
    }
}

CSocketDataArgs CSocketData::getArgs()
{
    return _args;
}

quint8 CSocketData::getCommand()
{
    return _command;
}

CSocketDataArgLength CSocketData::getArgLength(qsizetype datalen)
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

    return CSocketDataArgLength(sz, argL);
}

CSocketReceivedData::CSocketReceivedData(quint8 command, CSocketDataArgs args, CSocketReceivedDataResult result, CSocketAddress address)
{
    _command = command;
    _args = args;
    _result = result;
    _address = address;
}

CSocketDataArgs CSocketReceivedData::getArgs()
{
    return _args;
}

quint8 CSocketReceivedData::getCommand()
{
    return _command;
}

CSocketAddress CSocketReceivedData::getRemoteAddress()
{
    return _address;
}

CSocketReceivedDataResult CSocketReceivedData::getResult()
{
    return _result;
}

CSocketSendData::CSocketSendData(quint8 command, CSocketDataArgs args)
{
    _result = CSocketSendDataBuildResult::NoData;

    //if (command < 0 || command > 255)
    //{
    //    _result = CSocketSendDataBuildResult::CommandValueOverflowError;
    //    return;
    //}

    _command = command;
    _args = args;

    QByteArray text;
    QDataStream textds(&text, QIODevice::ReadWrite);
    textds.setByteOrder(QDataStream::LittleEndian);

    textds << command;

    for (qsizetype i = 0; i < _args.getLength(); i++)
    {
        IDataType* arg = _args.at(i);

        switch (arg->getType())
        {
        case DataType::Integer:
            {
                qint64 i = ((CInteger *)arg)->getValue();

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

        case DataType::Float:
            {
                double f = ((CFloat *)arg)->getValue();

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

        case DataType::Boolean:
            textds << (quint8)0x71;
            textds << qToBigEndian(((CBoolean *)arg)->getValue());
            break;

        case DataType::String:
            {
                QByteArray s = ((CString *)arg)->getValue().toUtf8();

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
                    _result = CSocketSendDataBuildResult::StringLengthOverflowError;
                    return;
                }
            }
            break;

        case DataType::ByteArray:
            {
                QByteArray ba = ((CByteArray *)arg)->getValue();

                if (ba.length() <= ARG_MAXLEN)
                {
                    if (ba.length() <= std::numeric_limits<qint8>::max())
                    {
                        textds << (quint8)0xB1;
                        textds << qToBigEndian((qint8)ba.length());
                    }
                    else if (ba.length() <= std::numeric_limits<qint16>::max())
                    {
                        textds << (quint8)0xB2;
                        textds << qToBigEndian((qint16)ba.length());
                    }
                    else
                    {
                        textds << (quint8)0xB4;
                        textds << qToBigEndian((qint32)ba.length());
                    }

                    textds.writeRawData(ba.constData(), ba.length());
                }
                else
                {
                    _result = CSocketSendDataBuildResult::ByteArrayLengthOverflowError;
                    return;
                }
            }
            break;

        default:
            _result = CSocketSendDataBuildResult::DataTypeNotImplementedError;
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
        _result = CSocketSendDataBuildResult::DataTotalLengthOverflowError;
        return;
    }

    _bytes = data;
    _result = CSocketSendDataBuildResult::Successful;
}

CSocketDataArgs CSocketSendData::getArgs()
{
    return _args;
}

CSocketSendDataBuildResult CSocketSendData::getBuildResult()
{
    return _result;
}

QByteArray CSocketSendData::getBytes()
{
    return _bytes;
}

quint8 CSocketSendData::getCommand()
{
    return _command;
}

qsizetype CSocketSendData::getLength()
{
    return _bytes.length();
}

CSocket::CSocket(QAbstractSocket* s, CSocketProtocolType protocol)
{
    _socket = s;

    _protocol = protocol;
    _localAddress = CSocketAddress("0.0.0.0", 0);

    if (isAvailable())
    {
        _socket->setParent(nullptr);
        _socket->moveToThread(this);
        _localAddress = CSocketAddress(_socket->localAddress().toString(), _socket->localPort());
    }
}

bool CSocket::isAvailable()
{
    return _socket != nullptr;
}

CSocketAddress CSocket::getLocalAddress()
{ 
    return _localAddress;
}

CSocketProtocolType CSocket::getProtocoltype()
{
    return _protocol;
}

void CSocket::close()
{
    if (isAvailable())
        _socket->close();
}

void CSocket::setReceivedCallback(CSocketReceivedCallback callback)
{
    if (isAvailable())
    {
        _callback = callback;
        start();
    }
}

void CSocket::run()
{
    char* buffer = new char[4096];

    QHostAddress address;
    quint16 port;

    while (true)
    {
        qint64 bytesTransferred = 0;

        if (_protocol == CSocketProtocolType::Tcp)
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
        else if (_protocol == CSocketProtocolType::Udp)
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

        CSocketAddress remoteAddress = CSocketAddress(address, port);

        if (bytesTransferred > 0)
        {
            _data.append(QByteArray(buffer, bytesTransferred));

            while (true)
            {
                _result = _data.manipulate();

                if (_result == CSocketDataManipulationResult::Completed)
                {
                    _callback(this, CSocketReceivedData(_data.getCommand(), _data.getArgs(), CSocketReceivedDataResult::Completed, remoteAddress));
                    continue;
                }
                else if (_result == CSocketDataManipulationResult::ParsingError)
                {
                    _callback(this, CSocketReceivedData(0x00, CSocketDataArgs(), CSocketReceivedDataResult::ParsingError, remoteAddress));
                    return;
                }
                else if (_result == CSocketDataManipulationResult::InProgress)
                {
                    _timeout = QtConcurrent::run(checkInterruptedTimeout, this, 15000, remoteAddress);
                    break;
                }
                else if (_result == CSocketDataManipulationResult::NoData)
                {
                    break;
                }
            }

            continue;
        }
        else
        {
            _callback(this, CSocketReceivedData(0x00, CSocketDataArgs(), CSocketReceivedDataResult::Closed, remoteAddress));
            return;
        }
    }
}

void CSocket::send(CSocketSendData data, CSocketAddress address)
{
    if (isAvailable())
    {
        sendProc(data, address, 0);
    }
}

void CSocket::sendProc(CSocketSendData data, CSocketAddress address, qsizetype bytesTransferred)
{
    qint64 length = 0;

    if (_protocol == CSocketProtocolType::Tcp)
    {
        QTcpSocket* s = (QTcpSocket*)_socket;

        const char *psdata = data.getBytes().constData() + bytesTransferred;
        qint64 len = data.getLength() - bytesTransferred;

        length = s->write(psdata, len);
        s->waitForBytesWritten();
    }
    else if (_protocol == CSocketProtocolType::Udp)
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

void CSocket::checkInterruptedTimeout(CSocket* s, int milliseconds, CSocketAddress address)
{
    QThread::msleep(milliseconds);

    if (s->_result == CSocketDataManipulationResult::InProgress)
    {
        s->_callback(s, CSocketReceivedData(0x00, CSocketDataArgs(), CSocketReceivedDataResult::Interrupted, address));
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

TcpSocket::TcpSocket(QAbstractSocket* s) : CSocket(s, CSocketProtocolType::Tcp)
{
    _remoteAddress = CSocketAddress("0.0.0.0", 0);

    if (s != nullptr)
        _remoteAddress = CSocketAddress(s->peerAddress(), s->peerPort());
}

bool TcpSocket::isConnected()
{
    return isAvailable() && (_socket->state() == QAbstractSocket::ConnectedState);
}

CSocketAddress TcpSocket::getRemoteAddress()
{
    return _remoteAddress;
}

void TcpSocket::send(CSocketSendData data)
{
    CSocketAddress address;
    CSocket::send(data, address);
}

UdpSocket::UdpSocket(QAbstractSocket* s) : CSocket(s, CSocketProtocolType::Udp)
{
}

void UdpSocket::send(CSocketSendData data, CSocketAddress address)
{
    CSocket::send(data, address);
}

TcpSocket* TcpConnect(CSocketAddress address)
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

TcpServer* TcpListen(CSocketAddress address)
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

UdpSocket* UdpCast(CSocketAddress address)
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
