using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace xyz.olooko.comm.netcomm 
{
    public class CBoolean : IDataType
    {
        public bool Value { get; }

        public CBoolean(bool value)
        {
            this.Value = value;
        }

        public DataType GetDataType()
        {
            return DataType.CBoolean;
        }

        public override string ToString()
        {
            return this.Value.ToString();
        }
    }

    public class CByteArray : IDataType
    {
        public byte[] Value { get; }

        public CByteArray(byte[] value)
        {
            this.Value = value;
        }

        public DataType GetDataType()
        {
            return DataType.CByteArray;
        }

        public override string ToString()
        {
            return "0x" + BitConverter.ToString(this.Value).Replace("-", ",0x");
        }
    }

    public class CFloat : IDataType
    {
        public double Value { get; }

        public CFloat(double value)
        {
            this.Value = value;
        }
        public DataType GetDataType()
        {
            return DataType.CFloat;
        }

        public override string ToString()
        {
            return this.Value.ToString();
        }
    }

    public class CInteger : IDataType
    {
        public long Value { get; }

        public CInteger(long value)
        {
            this.Value = value;
        }

        public DataType GetDataType()
        {
            return DataType.CInteger;
        }

        public override string ToString()
        {
            return this.Value.ToString();
        }
    }

    public class CString : IDataType
    {
        public string Value { get; }

        public CString(string value)
        {
            this.Value = value;
        }
        public DataType GetDataType()
        {
            return DataType.CString;
        }

        public override string ToString()
        {
            return this.Value;
        }
    }

    public enum DataType
    {
        CBoolean, CByteArray, CFloat, CInteger, CString
    }

    public interface IDataType
    {
        DataType GetDataType();
        string ToString();
    }

    public abstract class CSocket
    {
        protected Socket _socket;

        private CSocketData _data;
        private CSocketDataManipulationResult _result;
        private CSocketAddress _localAddress;
        private CSocketProtocolType _protocol;

        public bool Available
        {
            get { return _socket != null; }
        }

        public CSocketAddress LocalAddress
        {
            get { return _localAddress; }
        }

        public CSocketProtocolType ProtocolType
        {
            get { return _protocol; }
        }

        public CSocket(Socket s, CSocketProtocolType protocol)
        {
            _socket = s;

            _data = new CSocketData();
            
            _protocol = protocol;
            _result = CSocketDataManipulationResult.NoData;
            _localAddress = new CSocketAddress("0.0.0.0", 0);

            if (this.Available)
            {
                IPEndPoint iep = _socket.LocalEndPoint as IPEndPoint;
                _localAddress = new CSocketAddress(iep.Address, iep.Port);
            }
        }

        public void Close()
        {
            if (this.Available)
                _socket.Close();
        }

        public void SetReceivedCallback(Action<CSocket, CSocketReceivedData> callback)
        {
            if (this.Available)
            {
                Thread t = new Thread(new ParameterizedThreadStart(ReceiveProc));
                t.IsBackground = true;

                t.Start(callback);
            }
        }

        protected void Send(CSocketSendData data, CSocketAddress address)
        {
            if (this.Available)
                SendProc(data, address, 0);
        }

        private void SendProc(CSocketSendData data, CSocketAddress address, int bytesTransferred)
        {
            int length = 0;

            if (_protocol == CSocketProtocolType.Tcp)
            {
                length = _socket.Send(data.Bytes, bytesTransferred, data.Length - bytesTransferred, SocketFlags.None);
            }
            else if (_protocol == CSocketProtocolType.Udp)
            {
                IPEndPoint iep = new IPEndPoint(address.IPAddress, address.Port);
                length = _socket.SendTo(data.Bytes, bytesTransferred, data.Length - bytesTransferred, SocketFlags.None, iep);
            }

            if (length > 0)
            {
                bytesTransferred += length;

                if (bytesTransferred < data.Length)
                    SendProc(data, address, bytesTransferred);
            }
        }

        private void ReceiveProc(object state)
        {
            if (state == null) return;
            Action<CSocket, CSocketReceivedData> callback = (Action<CSocket, CSocketReceivedData>)state;

            byte[] buffer = new byte[4096];

            while (true)
            {
                int bytesTransferred = 0;
                CSocketAddress remoteAddress = new CSocketAddress("0.0.0.0", 0);

                if (_protocol == CSocketProtocolType.Tcp)
                {
                    bytesTransferred = _socket.Receive(buffer);

                    IPEndPoint iep = _socket.RemoteEndPoint as IPEndPoint;
                    remoteAddress = new CSocketAddress(iep.Address, iep.Port);
                }
                else if (_protocol == CSocketProtocolType.Udp)
                {
                    EndPoint ep = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
                    bytesTransferred = _socket.ReceiveFrom(buffer, ref ep);

                    IPEndPoint iep = ep as IPEndPoint;
                    remoteAddress = new CSocketAddress(iep.Address, iep.Port);
                }

                if (bytesTransferred > 0)
                {
                    _data.Append(buffer, bytesTransferred);

                    while (true)
                    {
                        _result = (CSocketDataManipulationResult)_data.Manipulate();

                        if (_result == CSocketDataManipulationResult.Completed)
                        {
                            callback(this, new CSocketReceivedData(_data.Command, _data.Args, CSocketReceivedDataResult.Completed, remoteAddress));
                            continue;
                        }
                        else if (_result == CSocketDataManipulationResult.ParsingError)
                        {
                            callback(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress));
                            return;
                        }
                        else if (_result == CSocketDataManipulationResult.InProgress)
                        {
                            CheckInterruptedTimeout(this, 15000, callback, remoteAddress);
                            break;
                        }
                        else if (_result == CSocketDataManipulationResult.NoData)
                        {
                            break;
                        }
                    }

                    continue;
                }
                else
                {
                    callback(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.Closed, remoteAddress));
                    return;
                }
            }
        }

        private static async void CheckInterruptedTimeout(CSocket s, int milliseconds, Action<CSocket, CSocketReceivedData> callback, CSocketAddress address)
        {
            await Task.Delay(milliseconds);

            if (s._result == CSocketDataManipulationResult.InProgress)
                callback(s, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, address));
        }
    }

    public class CSocketAddress
    {
        private IPAddress _ipaddress;
        private string _host;
        private int _port;

        public IPAddress IPAddress
        {
            get { return _ipaddress; }
        }

        public string Host
        {
            get { return _host.Replace("::ffff:", ""); }
        }

        public int Port
        {
            get { return _port; }
        }

        public CSocketAddress(string host, int port)
        {
            _ipaddress = GetIPAddress(host);
            _host = host;
            _port = port;
        }

        public CSocketAddress(IPAddress ipaddress, int port)
        {
            _ipaddress = ipaddress;
            _host = ipaddress.ToString();
            _port = port;
        }

        private static IPAddress GetIPAddress(string host)
        {
            IPAddress ipaddress = IPAddress.Any;

            if (!IPAddress.TryParse(host, out ipaddress))
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(host);

                foreach (IPAddress ip in hostEntry.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip;
                    }                
                }
            }

            return ipaddress;
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}", this.Host, this.Port);
        }
    }

    public class CSocketData
    {
        private byte _command;
        private CSocketDataArgs _args;
        private byte[] _data;
        private int _datalen;
        private int _datapos;
        private byte _checksum;
        private CSocketDataParsingStep _step;
        private int _textlen;

        public CSocketDataArgs Args
        {
            get { return _args; }
        }

        public byte Command
        {
            get { return _command; }
        }

        public CSocketData()
        {
            _command = 0x00;
            _args = new CSocketDataArgs();
            _data = new byte[0];
            _datalen = 0;
            _datapos = 0;
            _checksum = 0x00;
            _step = CSocketDataParsingStep.SOH;
            _textlen = 0;
        }

        public void Append(byte[] buffer, int bytesTransferred)
        {
            if (_data.Length < _datalen + bytesTransferred)
                Array.Resize(ref _data, _datalen + bytesTransferred);

            Buffer.BlockCopy(buffer, 0, _data, _datalen, bytesTransferred);
            _datalen += bytesTransferred;
        }

        public CSocketDataManipulationResult Manipulate()
        {
            byte[] buffer;

            while (true)
            {
                int datalen = _datalen - _datapos;

                switch (_step)
                {
                    case CSocketDataParsingStep.SOH:
                        if (datalen > 0)
                        {
                            if (_data[_datapos] == 0x01)
                            {
                                _datapos += 1;
                                _step = CSocketDataParsingStep.OTL;
                                continue;
                            }
                            else
                                return CSocketDataManipulationResult.ParsingError;
                        }
                        break;

                    case CSocketDataParsingStep.OTL:
                        if (datalen > 0)
                        {
                            if ((new List<byte> { 0x11, 0x12, 0x14 }).Contains(_data[_datapos]))
                            {
                                CSocketDataArgLength a = GetArgLength(datalen);

                                if (a.ArgL >= 0)
                                {
                                    _textlen = a.ArgL;
                                    _datapos += 1 + a.Size;
                                    _step = CSocketDataParsingStep.STX;
                                    continue;
                                }
                            }
                            else
                                return CSocketDataManipulationResult.ParsingError;
                        }
                        break;

                    case CSocketDataParsingStep.STX:
                        if (datalen > 0)
                        {
                            if (_data[_datapos] == 0x02)
                            {
                                _datapos += 1;
                                _step = CSocketDataParsingStep.ETX;
                                continue;
                            }
                            else
                                return CSocketDataManipulationResult.ParsingError;
                        }
                        break;

                    case CSocketDataParsingStep.ETX:
                        if (datalen > _textlen)
                        {
                            if (_data[_datapos + _textlen] == 0x03)
                            {
                                int textfpos = _datapos;

                                _command = _data[textfpos];
                                _args.Clear();
                                _datapos += 1;

                                while (_datapos < _textlen + textfpos)
                                {
                                    int sz = 0;
                                    int argL = 0;

                                    if ((new List<byte> { 0x31, 0x32, 0x34, 0x38 }).Contains(_data[_datapos]))
                                    {
                                        sz = (int)(_data[_datapos] & 0x0F);

                                        buffer = new byte[sz];
                                        Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz);

                                        if (BitConverter.IsLittleEndian)
                                            Array.Reverse(buffer);

                                        long i = 0;

                                        switch (sz)
                                        {
                                            case 1: i = (long)(sbyte)buffer[0]; break;
                                            case 2: i = (long)BitConverter.ToInt16(buffer, 0); break;
                                            case 4: i = (long)BitConverter.ToInt32(buffer, 0); break;
                                            case 8: i = BitConverter.ToInt64(buffer, 0); break;
                                        }

                                        _args.Add(new CInteger(i));
                                    }
                                    else if ((new List<byte> { 0x54, 0x58 }).Contains(_data[_datapos]))
                                    {
                                        sz = (int)(_data[_datapos] & 0x0F);

                                        buffer = new byte[sz];
                                        Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz);

                                        if (BitConverter.IsLittleEndian)
                                            Array.Reverse(buffer);

                                        double f = 0.0;

                                        switch (sz)
                                        {
                                            case 4: f = (double)BitConverter.ToSingle(buffer, 0); break;
                                            case 8: f = BitConverter.ToDouble(buffer, 0); break;
                                        }

                                        _args.Add(new CFloat(f));
                                    }
                                    else if ((new List<byte> { 0x71 }).Contains(_data[_datapos]))
                                    {
                                        sz = 1;

                                        buffer = new byte[sz];
                                        Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz);

                                        if (BitConverter.IsLittleEndian)
                                            Array.Reverse(buffer);

                                        _args.Add(new CBoolean(BitConverter.ToBoolean(buffer, 0)));
                                    }
                                    else if ((new List<byte> { 0x91, 0x92, 0x94 }).Contains(_data[_datapos]))
                                    {
                                        CSocketDataArgLength a = GetArgLength(datalen);
                                        sz = a.Size;
                                        argL = a.ArgL;

                                        _args.Add(new CString(Encoding.UTF8.GetString(_data, _datapos + 1 + sz, argL)));
                                        _datapos += argL;
                                    }
                                    else if ((new List<byte> { 0xB1, 0xB2, 0xB4 }).Contains(_data[_datapos]))
                                    {
                                        CSocketDataArgLength a = GetArgLength(datalen);
                                        sz = a.Size;
                                        argL = a.ArgL;

                                        byte[] ba = new byte[argL];
                                        Buffer.BlockCopy(_data, _datapos + 1 + sz, ba, 0, argL);

                                        _args.Add(new CByteArray(ba));
                                        _datapos += argL;
                                    }
                                    else
                                        return CSocketDataManipulationResult.ParsingError;

                                    _datapos += 1 + sz;
                                }

                                _checksum = 0x00;

                                for (int i = textfpos; i < textfpos + _textlen; i++)
                                    _checksum ^= _data[i];

                                _datapos += 1;
                                _step = CSocketDataParsingStep.CHK;
                                continue;
                            }
                            else
                                return CSocketDataManipulationResult.ParsingError;
                        }
                        break;

                    case CSocketDataParsingStep.CHK:
                        if (datalen > 0)
                        {
                            if (_data[_datapos] == _checksum)
                            {
                                _datapos += 1;
                                _step = CSocketDataParsingStep.EOT;
                                continue;
                            }
                            else
                                return CSocketDataManipulationResult.ParsingError;
                        }
                        break;

                    case CSocketDataParsingStep.EOT:
                        if (datalen > 0)
                        {
                            if (_data[_datapos] == 0x04)
                            {
                                _datapos += 1;
                                _datalen -= _datapos;

                                Buffer.BlockCopy(_data, _datapos, _data, 0, _datalen);

                                _datapos = 0;
                                _checksum = 0x00;
                                _step = CSocketDataParsingStep.SOH;
                                _textlen = 0;

                                return CSocketDataManipulationResult.Completed;
                            }
                            else
                                return CSocketDataManipulationResult.ParsingError;
                        }
                        break;
                }

                if (_datalen == 0)
                    return CSocketDataManipulationResult.NoData;

                return CSocketDataManipulationResult.InProgress;
            }
        }

        private CSocketDataArgLength GetArgLength(int datalen)
        {
            int sz = (int)(_data[_datapos] & 0x0F);
            int argL = -1;

            if (datalen > sz)
            {
                byte[] buffer = new byte[sz];
                Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz);

                if (BitConverter.IsLittleEndian)
                    Array.Reverse(buffer);

                switch (sz)
                {
                    case 1: argL = (int)buffer[0]; break;
                    case 2: argL = (int)BitConverter.ToInt16(buffer, 0); break;
                    case 4: argL = (int)BitConverter.ToInt32(buffer, 0); break;
                }
            }

            return new CSocketDataArgLength(sz, argL);
        }
    }

    public class CSocketDataArgLength
    {
        public int Size { get; }
        public int ArgL { get; }

        public CSocketDataArgLength(int sz, int argL)
        {
            this.Size = sz;
            this.ArgL = argL;
        }
    }

    public class CSocketDataArgs : IEnumerable
    {
        private List<IDataType> _list;

        public IDataType this[int index]
        {
            get { return _list[index]; }
        }

        public int Length
        {
            get { return _list.Count; }
        }

        public CSocketDataArgs()
        {
            _list = new List<IDataType>();
        }

        public void Add(IDataType arg)
        {
            _list.Add(arg);
        }

        public IDataType At(int index)
        {
            return _list[index];
        }

        public void Clear()
        {
            _list.Clear();
        }

        public IEnumerator GetEnumerator()
        {
            return _list.GetEnumerator();
        }
    }

    public enum CSocketDataManipulationResult
    {
        Completed, InProgress, NoData, ParsingError
    }

    public enum CSocketDataParsingStep
    {
        SOH, OTL, STX, ETX, CHK, EOT
    }

    public enum CSocketProtocolType
    {
        Tcp, Udp
    }

    public class CSocketReceivedData
    {
        private byte _command;
        private CSocketDataArgs _args;
        private CSocketAddress _address;
        private CSocketReceivedDataResult _result;

        public CSocketDataArgs Args
        {
            get { return _args; }
        }
        
        public byte Command
        {
            get { return _command; }
        }
        
        public CSocketAddress RemoteAddress
        {
            get { return _address; }
        }
        
        public CSocketReceivedDataResult Result
        {
            get { return _result; }
        }

        public CSocketReceivedData(byte command, CSocketDataArgs args, CSocketReceivedDataResult result, CSocketAddress address)
        {
            _command = command;
            _args = args;
            _address = address;
            _result = result;            
        }
    }

    public enum CSocketReceivedDataResult
    {
        Closed, Completed, Interrupted, ParsingError
    }

    public class CSocketSendData
    {
        private const int ARG_MAXLEN = 0x7FFFFF - 5;
        private const int TXT_MAXLEN = int.MaxValue - 10;

        private CSocketSendDataBuildResult _result;
        private CSocketDataArgs _args;
        private byte[] _bytes;        
        private byte _command;

        public CSocketDataArgs Args
        {
            get { return _args; }
        }

        public CSocketSendDataBuildResult BuildResult
        {
            get { return _result; }
        }

        public byte[] Bytes
        {
            get { return _bytes; }
        }
        
        public byte Command
        {
            get { return _command; }
        }

        public int Length
        {
            get { return _bytes.Length; }
        }

        public CSocketSendData(byte command, CSocketDataArgs args)
        {
            _result = CSocketSendDataBuildResult.NoData;

            if (command < 0x00 || command > 0xFF)
            {
                _result = CSocketSendDataBuildResult.CommandValueOverflowError;
                return;
            }

            _command = command;
            _args = args;
            _bytes = new byte[0];
            
            MemoryStream textms = new MemoryStream();
            textms.Write(new byte[] { command }, 0, 1);

            byte[] buffer;

            foreach (IDataType arg in _args)
            {
                switch (arg.GetDataType())
                {
                    case DataType.CInteger:
                        {
                            long i = (arg as CInteger).Value;

                            if (Convert.ToInt64(sbyte.MinValue) <= i && i <= Convert.ToInt64(sbyte.MaxValue))
                            {
                                buffer = BitConverter.GetBytes((sbyte)i);

                                textms.Write(new byte[] { 0x31 }, 0, 1);
                                textms.Write(buffer, 0, 1);
                            }
                            else if (Convert.ToInt64(short.MinValue) <= i && i <= Convert.ToInt64(short.MaxValue))
                            {
                                buffer = BitConverter.GetBytes((short)i);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                textms.Write(new byte[] { 0x32 }, 0, 1);
                                textms.Write(buffer, 0, 2);
                            }
                            else if (Convert.ToInt64(int.MinValue) <= i && i <= Convert.ToInt64(int.MaxValue))
                            {
                                buffer = BitConverter.GetBytes((int)i);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                textms.Write(new byte[] { 0x34 }, 0, 1);
                                textms.Write(buffer, 0, 4);
                            }
                            else
                            {
                                buffer = BitConverter.GetBytes(i);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                textms.Write(new byte[] { 0x38 }, 0, 1);
                                textms.Write(buffer, 0, 8);
                            }
                        }
                        break;

                    case DataType.CFloat:
                        {
                            double f = (arg as CFloat).Value;

                            if (Math.Abs(f) <= Convert.ToDouble(float.MaxValue))
                            {
                                buffer = BitConverter.GetBytes((float)f);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                textms.Write(new byte[] { 0x54 }, 0, 1);
                                textms.Write(buffer, 0, 4);
                            }
                            else
                            {
                                buffer = BitConverter.GetBytes(f);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                textms.Write(new byte[] { 0x58 }, 0, 1);
                                textms.Write(buffer, 0, 8);
                            }
                        }
                        break;

                    case DataType.CBoolean:
                        textms.Write(new byte[] { 0x71 }, 0, 1);
                        textms.Write(BitConverter.GetBytes((arg as CBoolean).Value), 0, 1);
                        break;

                    case DataType.CString:
                        {
                            byte[] s = Encoding.UTF8.GetBytes((arg as CString).Value);

                            if (s.Length <= ARG_MAXLEN)
                            {
                                if (s.Length <= sbyte.MaxValue)
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToSByte(s.Length));

                                    textms.Write(new byte[] { 0x91 }, 0, 1);
                                    textms.Write(buffer, 0, 1);
                                }
                                else if (s.Length <= short.MaxValue)
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToInt16(s.Length));

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(buffer);

                                    textms.Write(new byte[] { 0x92 }, 0, 1);
                                    textms.Write(buffer, 0, 2);
                                }
                                else
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToInt32(s.Length));

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(buffer);

                                    textms.Write(new byte[] { 0x94 }, 0, 1);
                                    textms.Write(buffer, 0, 4);
                                }

                                textms.Write(s, 0, s.Length);
                            }
                            else
                            {
                                _result = CSocketSendDataBuildResult.StringLengthOverflowError;
                                return;
                            }
                        }
                        break;

                    case DataType.CByteArray:
                        {
                            byte[] ba = (arg as CByteArray).Value;

                            if (ba.Length <= ARG_MAXLEN)
                            {
                                if (ba.Length <= sbyte.MaxValue)
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToSByte(ba.Length));

                                    textms.Write(new byte[] { 0xB1 }, 0, 1);
                                    textms.Write(buffer, 0, 1);
                                }
                                else if (ba.Length <= short.MaxValue)
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToInt16(ba.Length));

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(buffer);

                                    textms.Write(new byte[] { 0xB2 }, 0, 1);
                                    textms.Write(buffer, 0, 2);
                                }
                                else
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToInt32(ba.Length));

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(buffer);

                                    textms.Write(new byte[] { 0xB4 }, 0, 1);
                                    textms.Write(buffer, 0, 4);
                                }

                                textms.Write(ba, 0, ba.Length);
                            }
                            else
                            {
                                _result = CSocketSendDataBuildResult.ByteArrayLengthOverflowError;
                                return;
                            }
                        }
                        break;

                    default:
                        _result = CSocketSendDataBuildResult.DataTypeNotImplementedError;
                        return;
                }
            }

            int textlen = (int)textms.Position;

            int otl = 0;
            if (textlen <= sbyte.MaxValue) otl = 2;
            else if (textlen <= short.MaxValue) otl = 3;
            else if (textlen <= int.MaxValue) otl = 5;

            //SOH(1)+OTL(v)+STX(1)+TXT(v)+ETX(1)+CHK(1)+EOT(1)
            byte[] data = new byte[1 + otl + 1 + textlen + 1 + 1 + 1];
            int datapos = 0;

            if (textlen <= TXT_MAXLEN)
            {
                data[datapos] = 0x01; 
                datapos += 1;

                if (textlen <= sbyte.MaxValue)
                {
                    buffer = BitConverter.GetBytes(Convert.ToSByte(textlen));

                    data[datapos] = 0x11; 
                    datapos += 1;

                    Buffer.BlockCopy(buffer, 0, data, datapos, 1); 
                    datapos += 1;
                }
                else if (textlen <= short.MaxValue)
                {
                    buffer = BitConverter.GetBytes(Convert.ToInt16(textlen));

                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(buffer);

                    data[datapos] = 0x12; 
                    datapos += 1;
                    
                    Buffer.BlockCopy(buffer, 0, data, datapos, 2); 
                    datapos += 2;
                }
                else
                {
                    buffer = BitConverter.GetBytes(Convert.ToInt32(textlen));

                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(buffer);

                    data[datapos] = 0x14; 
                    datapos += 1;
                    
                    Buffer.BlockCopy(buffer, 0, data, datapos, 4); 
                    datapos += 4;
                }

                data[datapos] = 0x02; 
                datapos += 1;

                textms.Flush();

                byte[] text = textms.GetBuffer();

                Buffer.BlockCopy(text, 0, data, datapos, textlen); 
                datapos += textlen;

                data[datapos] = 0x03; 
                datapos += 1;

                byte checksum = 0x00;
               
                for (int i = 0; i < textlen; i++) 
                    checksum ^= text[i];

                data[datapos] = checksum; 
                datapos += 1;

                data[datapos] = 0x04; 
                //datapos += 1;

                textms.Close();
            }
            else
            {
                textms.Close();
                _result = CSocketSendDataBuildResult.DataTotalLengthOverflowError;
                return;
            }

            _bytes = data;
            _result = CSocketSendDataBuildResult.Successful;
        }
    }

    public enum CSocketSendDataBuildResult
    {
        ByteArrayLengthOverflowError,
        CommandValueOverflowError,
        DataTotalLengthOverflowError,
        DataTypeNotImplementedError,
        NoData,
        StringLengthOverflowError,
        Successful
    }

    public class TcpServer 
    {
        private Socket _server;

        public bool Running
        {
            get { return _server != null; }
        }

        public TcpServer(Socket s) 
        { 
            _server = s; 
        }

        public void Close()
        {
            _server.Close();
            _server = null;
        }

        public void SetAcceptCallback(Action<TcpSocket> callback)
        {
            Thread t = new Thread(new ParameterizedThreadStart(AcceptProc));
            t.IsBackground = true;

            t.Start(callback);
        }

        private void AcceptProc(object state)
        {
            if (state == null) 
                return;

            Action<TcpSocket> callback = (Action<TcpSocket>)state;

            while (this.Running)
            {
                Socket s = null;

                try 
                {
                    s = _server.Accept();
                }
                catch 
                {
                }

                callback(new TcpSocket(s));
            }
        }
    }

    public class TcpSocket : CSocket 
    {
        private CSocketAddress _remoteAddress;

        public bool Connected
        {
            get { return Available && _socket.Connected; }
        }
        
        public CSocketAddress RemoteAddress
        {
            get { return _remoteAddress; }
        }

        public TcpSocket(Socket s) : base(s, CSocketProtocolType.Tcp) 
        {
            _remoteAddress = new CSocketAddress("0.0.0.0", 0);

            if (s != null)
            {
                IPEndPoint iep = s.RemoteEndPoint as IPEndPoint;
                _remoteAddress = new CSocketAddress(iep.Address, iep.Port);
            }
        }

        public void Send(CSocketSendData data)
        {
            base.Send(data, null);
        }
    }

    public class UdpSocket : CSocket 
    {
        public UdpSocket(Socket s) : base(s, CSocketProtocolType.Udp) 
        {        
        }

        public new void Send(CSocketSendData data, CSocketAddress address)
        {
            base.Send(data, address);
        }
    }

    public class NetworkComm
    {
        public static TcpSocket TcpConnect(CSocketAddress address)
        {
            Socket s = new Socket(SocketType.Stream, ProtocolType.Tcp);

            try
            {
                s.Connect(new IPEndPoint(address.IPAddress, address.Port));
            }
            catch
            {
                s = null;
            }

            return new TcpSocket(s);
        }

        public static TcpServer TcpListen(CSocketAddress address)
        {
            Socket s = new Socket(SocketType.Stream, ProtocolType.Tcp);

            try
            {
                s.Bind(new IPEndPoint(address.IPAddress, address.Port));
                s.Listen(0);
            }
            catch
            {
                s = null;
            }

            return new TcpServer(s);
        }

        public static UdpSocket UdpCast(CSocketAddress address)
        {
            Socket s = new Socket(SocketType.Dgram, ProtocolType.Udp);

            try
            {
                s.Bind(new IPEndPoint(address.IPAddress, address.Port));
            }
            catch
            {
                s = null;
            }

            return new UdpSocket(s);
        }
    }
}
