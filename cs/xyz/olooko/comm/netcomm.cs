using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace xyz.olooko.comm.netcomm 
{
    /// <summary>
    /// NetSocketDataParsingStep
    /// </summary>
    public enum NetSocketDataParsingStep
    {
        SOH, OTL, STX, ETX, CHK, EOT
    }

    /// <summary>
    /// NetSocketDataResult
    /// </summary>
    public enum NetSocketDataManipulationResult 
    {
        Completed, InProgress, NoData, ParsingError
    }

    /// <summary>
    /// NetSocketData
    /// </summary>
    public class NetSocketData 
    {
        private byte[] _data;
        private int _datalen;
        private int _datapos;
        private byte _checksum;
        private NetSocketDataParsingStep _step;
        private int _textlen;

        private List<object> _args;
        public object[] Args
        {
            get { return _args.ToArray(); }
        }

        private byte _command;
        public byte Command 
        {
            get { return _command; }
        }

        public NetSocketData() 
        {
            _command = 0x00;
            _args = new List<object>();
            _data = new byte[0];
            _datalen = 0;
            _datapos = 0;
            _checksum = 0x00;
            _step = NetSocketDataParsingStep.SOH;
            _textlen = 0;
        }

        private (int Size, int ArgLength) GetArgLength(byte[] data, int datalen, int datapos) 
        {
            int sz = (int)(data[datapos] & 0x0F);
            int argL = -1;

            if (datalen > sz) 
            {
                byte[] buffer = new byte[sz];
                Buffer.BlockCopy(data, datapos + 1, buffer, 0, sz);

                if (BitConverter.IsLittleEndian)
                    Array.Reverse(buffer);

                switch (sz) 
                {
                    case 1: argL = (int)buffer[0]; break;
                    case 2: argL = (int)BitConverter.ToInt16(buffer, 0); break;
                    case 4: argL = (int)BitConverter.ToInt32(buffer, 0); break;
                }
            }

            return (sz, argL);
        }

        public void Append(byte[] buffer, int bytesTransferred)
        {
            if (_data.Length < _datalen + bytesTransferred)
            {
                Array.Resize(ref _data, _datalen + bytesTransferred);
            }

            Buffer.BlockCopy(buffer, 0, _data, _datalen, bytesTransferred);
            _datalen += bytesTransferred;
        }

        public NetSocketDataManipulationResult Manipulate()
        {
            byte[] buffer;

            while (true) 
            {
                int datalen = _datalen - _datapos;

                switch (_step) 
                {
                    case NetSocketDataParsingStep.SOH:
                        if (datalen > 0) 
                        {
                            if (_data[_datapos] == 0x01) 
                            {
                                _datapos += 1;
                                _step = NetSocketDataParsingStep.OTL;
                                continue;
                            } 
                            else 
                            {
                                return NetSocketDataManipulationResult.ParsingError;
                            }
                        }
                        break;

                    case NetSocketDataParsingStep.OTL:
                        if (datalen > 0) 
                        {
                            if ((new List<byte> { 0x11, 0x12, 0x14 }).Contains(_data[_datapos])) 
                            {
                                var a = GetArgLength(_data, datalen, _datapos);

                                if (a.ArgLength >= 0) 
                                {
                                    _textlen = a.ArgLength;
                                    _datapos += 1 + a.Size;
                                    _step = NetSocketDataParsingStep.STX;
                                    continue;
                                }
                            } 
                            else 
                            {
                                return NetSocketDataManipulationResult.ParsingError;
                            }
                        }
                        break;

                    case NetSocketDataParsingStep.STX:
                        if (datalen > 0) 
                        {
                            if (_data[_datapos] == 0x02) 
                            {
                                _datapos += 1;
                                _step = NetSocketDataParsingStep.ETX;
                                continue;
                            } 
                            else 
                            {
                                return NetSocketDataManipulationResult.ParsingError;
                            }
                        }
                        break;

                    case NetSocketDataParsingStep.ETX:
                        if (datalen > _textlen) 
                        {
                            if (_data[_datapos + _textlen] == 0x03) 
                            {
                                try 
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

                                            switch (sz) 
                                            {
                                                case 1: _args.Add((sbyte)buffer[0]); break;
                                                case 2: _args.Add(BitConverter.ToInt16(buffer, 0)); break;
                                                case 4: _args.Add(BitConverter.ToInt32(buffer, 0)); break;
                                                case 8: _args.Add(BitConverter.ToInt64(buffer, 0)); break;
                                            }
                                        } 
                                        else if ((new List<byte> { 0x54, 0x58 }).Contains(_data[_datapos])) 
                                        {
                                            sz = (int)(_data[_datapos] & 0x0F);

                                            buffer = new byte[sz];
                                            Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz);

                                            if (BitConverter.IsLittleEndian)
                                                Array.Reverse(buffer);

                                            switch (sz) 
                                            {
                                                case 4: _args.Add(BitConverter.ToSingle(buffer, 0)); break;
                                                case 8: _args.Add(BitConverter.ToDouble(buffer, 0)); break;
                                            }
                                        } 
                                        else if ((new List<byte> { 0x71 }).Contains(_data[_datapos])) 
                                        {
                                            sz = 1;

                                            buffer = new byte[sz];
                                            Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz);

                                            if (BitConverter.IsLittleEndian)
                                                Array.Reverse(buffer);

                                            _args.Add(BitConverter.ToBoolean(buffer, 0));
                                        } 
                                        else if ((new List<byte> { 0x91, 0x92, 0x94 }).Contains(_data[_datapos])) 
                                        {
                                            var a = GetArgLength(_data, datalen, _datapos);
                                            sz = a.Size;
                                            argL = a.ArgLength;

                                            _args.Add(Encoding.UTF8.GetString(_data, _datapos + 1 + sz, argL));
                                            _datapos += argL;
                                        } 
                                        else if ((new List<byte> { 0xB1, 0xB2, 0xB4 }).Contains(_data[_datapos])) 
                                        {
                                            var a = GetArgLength(_data, datalen, _datapos);
                                            sz = a.Size;
                                            argL = a.ArgLength;

                                            byte[] ba = new byte[argL];
                                            Buffer.BlockCopy(_data, _datapos + 1 + sz, ba, 0, argL);

                                            _args.Add(ba);
                                            _datapos += argL;
                                        } 
                                        else 
                                        {
                                            return NetSocketDataManipulationResult.ParsingError;
                                        }
                                        _datapos += 1 + sz;
                                    }

                                    _checksum = 0x00;

                                    for (int i = textfpos; i < textfpos + _textlen; i++)
                                        _checksum ^= _data[i];

                                    _datapos += 1;
                                    _step = NetSocketDataParsingStep.CHK;
                                    continue;
                                } 
                                catch 
                                {
                                    return NetSocketDataManipulationResult.ParsingError;
                                }
                            } 
                            else 
                            {
                                return NetSocketDataManipulationResult.ParsingError;
                            }
                        }
                        break;

                    case NetSocketDataParsingStep.CHK:
                        if (datalen > 0) 
                        {
                            if (_data[_datapos] == _checksum) 
                            {
                                _datapos += 1;
                                _step = NetSocketDataParsingStep.EOT;
                                continue;
                            } 
                            else 
                            {
                                return NetSocketDataManipulationResult.ParsingError;
                            }
                        }
                        break;

                    case NetSocketDataParsingStep.EOT:
                        if (datalen > 0)
                        {
                            if (_data[_datapos] == 0x04) 
                            {
                                _datapos += 1;
                                _datalen -= _datapos;

                                Buffer.BlockCopy(_data, _datapos, _data, 0, _datalen);

                                _datapos = 0;
                                _checksum = 0x00;
                                _step = NetSocketDataParsingStep.SOH;
                                _textlen = 0;

                                return NetSocketDataManipulationResult.Completed;
                            } 
                            else 
                            {
                                return NetSocketDataManipulationResult.ParsingError;
                            }
                        }
                        break;
                }

                if (_datalen == 0)
                    return NetSocketDataManipulationResult.NoData;

                return NetSocketDataManipulationResult.InProgress;
            }
        }
    }

    /// <summary>
    /// NetSocketReceivedDataResult
    /// </summary>
    public enum NetSocketReceivedDataResult
    {
        Closed, Completed, Interrupted, ParsingError
    }

    /// <summary>
    /// NetSocketReceivedData 
    /// </summary>
    public class NetSocketReceivedData
    {
        private object[] _args;
        public object[] Args
        {
            get { return _args; }
        }

        private byte _command;
        public byte Command
        {
            get { return _command; }
        }

        private NetSocketAddress _address;
        public NetSocketAddress RemoteAddress
        {
            get { return _address; }
        }

        private NetSocketReceivedDataResult _result;
        public NetSocketReceivedDataResult Result
        {
            get { return _result; }
        }

        public NetSocketReceivedData(byte command, object[] args, NetSocketReceivedDataResult result, NetSocketAddress address)
        {
            _command = command;
            _args = args;
            _result = result;
            _address = address;
        }
    }

    /// <summary>
    /// NetSocketSendDataBuildResult
    /// </summary>
    public enum NetSocketSendDataBuildResult
    {
        ByteArrayLengthOverflowError,
        CommandValueOverflowError,
        DataTotalLengthOverflowError,
        DataTypeNotImplementedError,
        NoData,
        StringLengthOverflowError,
        Successful       
    }

    /// <summary>
    /// NetSocketSendData 
    /// </summary>
    public class NetSocketSendData
    {
        private const int ARG_MAXLEN = 0x7FFFFF - 5;

        private const int TXT_MAXLEN = int.MaxValue - 10;

        private object[] _args;
        public object[] Args
        {
            get { return _args; }
        }

        private byte[] _bytes;
        public byte[] Bytes
        {
            get { return _bytes; }
        }

        private byte _command;
        public byte Command
        {
            get { return _command; }
        }

        public int Length
        {
            get { return _bytes.Length; }
        }

        private NetSocketSendDataBuildResult _result;
        public NetSocketSendDataBuildResult BuildResult
        {
            get { return _result; }
        }

        public NetSocketSendData(byte command, object[] args)
        {
            _result = NetSocketSendDataBuildResult.NoData;

            //if (command < 0 || command > 255)
            //{
            //    _result = NetSocketSendDataBuildResult.CommandValueOverflowError;
            //    return;
            //}

            _command = command;
            _args = args;
            _bytes = new byte[0];
            
            MemoryStream textms = new MemoryStream();
            textms.Write(new byte[] { command }, 0, 1);

            byte[] buffer;

            

            foreach (object arg in args)
            {
                switch (arg.GetType().Name)
                {
                    case "Byte":
                    case "UInt16":
                    case "UInt32":
                    case "UInt64":
                    case "SByte":
                    case "Int16":
                    case "Int32":
                    case "Int64":
                        {
                            long i = Convert.ToInt64(arg);

                            if (Convert.ToInt64(sbyte.MinValue) <= i && i <= Convert.ToInt64(sbyte.MaxValue))
                            {
                                buffer = BitConverter.GetBytes((sbyte)i);

                                // 0011 0001
                                textms.Write(new byte[] { 0x31 }, 0, 1);
                                textms.Write(buffer, 0, 1);
                            }
                            else if (Convert.ToInt64(short.MinValue) <= i && i <= Convert.ToInt64(short.MaxValue))
                            {
                                buffer = BitConverter.GetBytes((short)i);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                // 0011 0010
                                textms.Write(new byte[] { 0x32 }, 0, 1);
                                textms.Write(buffer, 0, 2);
                            }
                            else if (Convert.ToInt64(int.MinValue) <= i && i <= Convert.ToInt64(int.MaxValue))
                            {
                                buffer = BitConverter.GetBytes((int)i);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                // 0011 0100
                                textms.Write(new byte[] { 0x34 }, 0, 1);
                                textms.Write(buffer, 0, 4);
                            }
                            else
                            {
                                buffer = BitConverter.GetBytes(i);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                // 0011 1000
                                textms.Write(new byte[] { 0x38 }, 0, 1);
                                textms.Write(buffer, 0, 8);
                            }
                        }
                        break;

                    case "Decimal":
                    case "Single":
                    case "Double":
                        {
                            double f = Convert.ToDouble(arg);

                            if (Math.Abs(f) <= Convert.ToDouble(float.MaxValue))
                            {
                                buffer = BitConverter.GetBytes((float)f);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                // 0101 0100
                                textms.Write(new byte[] { 0x54 }, 0, 1);
                                textms.Write(buffer, 0, 4);
                            }
                            else
                            {
                                buffer = BitConverter.GetBytes(f);

                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(buffer);

                                // 0101 1000
                                textms.Write(new byte[] { 0x58 }, 0, 1);
                                textms.Write(buffer, 0, 8);
                            }
                        }
                        break;

                    case "Boolean":
                        textms.Write(new byte[] { 0x71 }, 0, 1);
                        textms.Write(BitConverter.GetBytes(Convert.ToBoolean(arg)), 0, 1);
                        break;

                    case "String":
                        {
                            byte[] s = Encoding.UTF8.GetBytes(Convert.ToString(arg));

                            if (s.Length <= ARG_MAXLEN)
                            {
                                if (s.Length <= sbyte.MaxValue)
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToSByte(s.Length));

                                    // 1001 0001
                                    textms.Write(new byte[] { 0x91 }, 0, 1);
                                    textms.Write(buffer, 0, 1);
                                }
                                else if (s.Length <= short.MaxValue)
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToInt16(s.Length));

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(buffer);

                                    // 1001 0010
                                    textms.Write(new byte[] { 0x92 }, 0, 1);
                                    textms.Write(buffer, 0, 2);
                                }
                                else
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToInt32(s.Length));

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(buffer);

                                    // 1001 0100
                                    textms.Write(new byte[] { 0x94 }, 0, 1);
                                    textms.Write(buffer, 0, 4);
                                }

                                textms.Write(s, 0, s.Length);
                            }
                            else
                            {
                                _result = NetSocketSendDataBuildResult.StringLengthOverflowError;
                                return;
                            }
                        }
                        break;

                    case "Byte[]":
                        {
                            byte[] b = (byte[])arg;

                            if (b.Length <= ARG_MAXLEN)
                            {
                                if (b.Length <= sbyte.MaxValue)
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToSByte(b.Length));

                                    // 1011 0001
                                    textms.Write(new byte[] { 0xB1 }, 0, 1);
                                    textms.Write(buffer, 0, 1);
                                }
                                else if (b.Length <= short.MaxValue)
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToInt16(b.Length));

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(buffer);

                                    // 1011 0010
                                    textms.Write(new byte[] { 0xB2 }, 0, 1);
                                    textms.Write(buffer, 0, 2);
                                }
                                else
                                {
                                    buffer = BitConverter.GetBytes(Convert.ToInt32(b.Length));

                                    if (BitConverter.IsLittleEndian)
                                        Array.Reverse(buffer);

                                    // 1011 0100
                                    textms.Write(new byte[] { 0xB4 }, 0, 1);
                                    textms.Write(buffer, 0, 4);
                                }

                                textms.Write(b, 0, b.Length);
                            }
                            else
                            {
                                _result = NetSocketSendDataBuildResult.ByteArrayLengthOverflowError;
                                return;
                            }
                        }
                        break;

                    default:
                        _result = NetSocketSendDataBuildResult.DataTypeNotImplementedError;
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
                // start of header
                data[datapos] = 0x01; datapos += 1;

                if (textlen <= sbyte.MaxValue)
                {
                    buffer = BitConverter.GetBytes(Convert.ToSByte(textlen));

                    // 0001 0001
                    data[datapos] = 0x11; datapos += 1;
                    Buffer.BlockCopy(buffer, 0, data, datapos, 1); datapos += 1;
                }
                else if (textlen <= short.MaxValue)
                {
                    buffer = BitConverter.GetBytes(Convert.ToInt16(textlen));

                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(buffer);

                    // 0001 0010
                    data[datapos] = 0x12; datapos += 1;
                    Buffer.BlockCopy(buffer, 0, data, datapos, 2); datapos += 2;
                }
                else
                {
                    buffer = BitConverter.GetBytes(Convert.ToInt32(textlen));

                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(buffer);

                    // 0001 0100
                    data[datapos] = 0x14; datapos += 1;
                    Buffer.BlockCopy(buffer, 0, data, datapos, 4); datapos += 4;
                }

                // start of text
                data[datapos] = 0x02; datapos += 1;

                // text
                textms.Flush();
                byte[] text = textms.GetBuffer();

                Buffer.BlockCopy(text, 0, data, datapos, textlen); datapos += textlen;

                // end of text
                data[datapos] = 0x03; datapos += 1;

                // checksum of text
                byte checksum = 0x00;
                for (int i = 0; i < textlen; i++) checksum ^= text[i];

                data[datapos] = checksum; datapos += 1;

                // end of transmission
                data[datapos] = 0x04; 
                //datapos += 1;

                textms.Close();
            }
            else
            {
                textms.Close();
                _result = NetSocketSendDataBuildResult.DataTotalLengthOverflowError;
                return;
            }

            _bytes = data;
            _result = NetSocketSendDataBuildResult.Successful;
        }
    }


    /// <summary>
    /// NetSocketAddress 
    /// </summary>
    public class NetSocketAddress
    {
        private IPAddress _ipaddress;
        public IPAddress IPAddress
        {
            get { return _ipaddress; }
        }

        private string _host;
        public string Host 
        { 
            get { return _host; }
        }

        private int _port;
        public int Port 
        { 
            get { return _port; } 
        }

        public NetSocketAddress(string host, int port) 
        {
            _ipaddress = GetIPAddress(host);
            _host = host;
            _port = port;
        }

        public NetSocketAddress(IPAddress ipaddress, int port)
        {
            _ipaddress = ipaddress;
            _host = ipaddress.ToString();
            _port = port;
        }

        private static IPAddress GetIPAddress(string host) 
        {
            IPAddress ipaddress = null;

            if (!IPAddress.TryParse(host, out ipaddress)) 
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(host);
                foreach (IPAddress ip in hostEntry.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipaddress = ip;
                    }
                }
            }

            return ipaddress;
        }
    }

    /// <summary>
    /// NetSocketProtocolType
    /// </summary>
    public enum NetSocketProtocolType
    {
        Tcp, Udp
    }

    /// <summary>
    /// NetSocket 
    /// </summary>
    public class NetSocket 
    {
        private byte[] _buffer;
        private NetSocketData _data;

        protected Socket _socket;

        private NetSocketDataManipulationResult _result;

        public bool Available 
        {
            get { return _socket != null; }
        }

        private NetSocketAddress _localAddress;
        public NetSocketAddress LocalAddress
        {
            get { return _localAddress; }
        }

        private NetSocketProtocolType _protocol;
        public NetSocketProtocolType ProtocolType
        {
            get { return _protocol; }
        }

        public NetSocket(Socket s, NetSocketProtocolType protocol) 
        {
            _data = new NetSocketData();
            _buffer = new byte[4096];
            _protocol = protocol;
            _result = NetSocketDataManipulationResult.NoData;
            _socket = s;

            if (this.Available)
            {
                IPEndPoint iep = _socket.LocalEndPoint as IPEndPoint;
                _localAddress = new NetSocketAddress(iep.Address, iep.Port);
            }
            else
                _localAddress = new NetSocketAddress("0,0.0.0", 0);
        }

        public void Close() 
        {
            if (this.Available)
                _socket.Close();
        }

        protected void Send(NetSocketSendData data, NetSocketAddress address)
        {
            if (this.Available)
                SendProc(data, address, 0);
        }

        private void SendProc(NetSocketSendData data, NetSocketAddress address, int bytesTransferred)
        {
            if (_protocol == NetSocketProtocolType.Tcp)
            {
                bytesTransferred += _socket.Send(data.Bytes, bytesTransferred, data.Length - bytesTransferred, SocketFlags.None);
            }
            else if (_protocol == NetSocketProtocolType.Udp)
            {
                IPEndPoint iep = new IPEndPoint(address.IPAddress, address.Port);
                bytesTransferred += _socket.SendTo(data.Bytes, bytesTransferred, data.Length - bytesTransferred, SocketFlags.None, iep);
            }

            if (bytesTransferred < data.Length)
                SendProc(data, address, bytesTransferred);
        }

        public void SetReceivedCallback(Action<NetSocket, NetSocketReceivedData> callback) 
        {
            if (this.Available)
            {
                Thread t = new Thread(new ParameterizedThreadStart(ReceiveProc));
                t.IsBackground = true;
                t.Start(callback);
            }
        }

        private void ReceiveProc(object state) 
        {
            if (state == null) return;
            Action<NetSocket, NetSocketReceivedData> callback = (Action<NetSocket, NetSocketReceivedData>)state;

            while (true) 
            {
                int bytesTransferred = 0;
                NetSocketAddress remoteAddress = null;

                if (_protocol == NetSocketProtocolType.Tcp) 
                {
                    bytesTransferred = _socket.Receive(_buffer);

                    IPEndPoint iep = _socket.RemoteEndPoint as IPEndPoint;
                    remoteAddress = new NetSocketAddress(iep.Address, iep.Port);
                } 
                else if (_protocol == NetSocketProtocolType.Udp) 
                {
                    EndPoint ep = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
                    bytesTransferred = _socket.ReceiveFrom(_buffer, ref ep);

                    IPEndPoint iep = ep as IPEndPoint;
                    remoteAddress = new NetSocketAddress(iep.Address, iep.Port);
                }

                if (bytesTransferred > 0) 
                {
                    _data.Append(_buffer, bytesTransferred);

                    while (true)
                    {
                        _result = _data.Manipulate();

                        if (_result == NetSocketDataManipulationResult.Completed)
                        {
                            callback(this, new NetSocketReceivedData(_data.Command, _data.Args, NetSocketReceivedDataResult.Completed, remoteAddress));
                            continue;
                        }
                        else if (_result == NetSocketDataManipulationResult.ParsingError)
                        {
                            callback(this, new NetSocketReceivedData(0x00, new object[] { }, NetSocketReceivedDataResult.ParsingError, remoteAddress));
                            return;
                        }
                        else if (_result == NetSocketDataManipulationResult.InProgress)
                        {
                            CheckInterruptedTimeout(this, 15000, callback, remoteAddress);
                            break;
                        }
                        else if (_result == NetSocketDataManipulationResult.NoData)
                        {
                            break;
                        }
                    }

                    continue;
                } 
                else 
                {
                    callback(this, new NetSocketReceivedData(0x00, new object[] {}, NetSocketReceivedDataResult.Closed, remoteAddress));
                    return;
                }
            }
        }

        private static async void CheckInterruptedTimeout(NetSocket s, int milliseconds, Action<NetSocket, NetSocketReceivedData> callback, NetSocketAddress address)
        {
            await Task.Delay(milliseconds);

            if (s._result == NetSocketDataManipulationResult.InProgress)
                callback(s, new NetSocketReceivedData(0x00, new object[] { }, NetSocketReceivedDataResult.Interrupted, address));
        }
    }

    /// <summary>
    /// TcpServer 
    /// </summary>
    public class TcpServer 
    {
        private Socket _server;

        public bool Started
        {
            get { return _server != null; }
        }

        public TcpServer(Socket s) 
        { 
            _server = s; 
        }

        public void SetAcceptCallback(Action<TcpSocket> callback)
        {
            Thread t = new Thread(new ParameterizedThreadStart(AcceptProc));
            t.IsBackground = true;
            t.Start(callback);
        }

        private void AcceptProc(object state)
        {
            if (state == null) return;
            Action<TcpSocket> callback = (Action<TcpSocket>)state;

            while (_server != null)
            {
                Socket s = null;
                try
                {
                    s = _server.Accept();
                }
                catch { }

                callback(new TcpSocket(s));
            }
        }

        public void Close()
        {
            _server.Close();
            _server = null;
        }
    }

    /// <summary>
    /// TcpSocket
    /// </summary>
    public class TcpSocket : NetSocket 
    {
        public bool Connected
        {
            get { return Available && _socket.Connected; }
        }

        private NetSocketAddress _remoteAddress;
        public NetSocketAddress RemoteAddress
        {
            get { return _remoteAddress; }
        }

        public TcpSocket(Socket s)
            : base(s, NetSocketProtocolType.Tcp) 
        {
            IPEndPoint iep = s.RemoteEndPoint as IPEndPoint;
            _remoteAddress = new NetSocketAddress(iep.Address, iep.Port);
        }

        public void Send(NetSocketSendData data)
        {
            base.Send(data, null);
        }
    }

    /// <summary>
    /// UdpSocket 
    /// </summary>
    public class UdpSocket : NetSocket 
    {
        public UdpSocket(Socket s)
            : base(s, NetSocketProtocolType.Udp) 
        {        
        }

        public new void Send(NetSocketSendData data, NetSocketAddress address)
        {
            base.Send(data, address);
        }
    }

    /// <summary>
    /// NetworkComm class creates TcpServer, TcpSocket, UdpSocket classes statically. 
    /// </summary>
    public class NetworkComm 
    {
        public static TcpSocket TcpConnect(NetSocketAddress address) 
        {
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

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

        public static TcpServer TcpListen(NetSocketAddress address)
        {
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                s.Bind(new IPEndPoint(address.IPAddress, address.Port));
                s.Listen(1024);
            }
            catch
            {
                s = null;
            }

            return new TcpServer(s);
        }

        public static UdpSocket UdpCast(NetSocketAddress address) 
        {
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

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
