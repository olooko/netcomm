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
    /// NetSocketReceivedDataResult
    /// </summary>
    public enum NetSocketReceivedDataResult
    {
        Closed, Completed, Interrupted, ParsingError
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
                switch (sz) 
                {
                    case 1: argL = (int)data[datapos + 1]; break;
                    case 2: argL = (int)BitConverter.ToInt16(data, datapos + 1); break;
                    case 4: argL = (int)BitConverter.ToInt32(data, datapos + 1); break;
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

                                        if ((new List<byte> { 0x31, 0x32, 0x34, 0x38 }).Contains(_data[_datapos])) 
                                        {
                                            sz = (int)(_data[_datapos] & 0x0F);

                                            switch (sz) 
                                            {
                                                case 1: _args.Add((sbyte)_data[_datapos + 1]); break;
                                                case 2: _args.Add(BitConverter.ToInt16(_data, _datapos + 1)); break;
                                                case 4: _args.Add(BitConverter.ToInt32(_data, _datapos + 1)); break;
                                                case 8: _args.Add(BitConverter.ToInt64(_data, _datapos + 1)); break;
                                            }
                                        } 
                                        else if ((new List<byte> { 0x54, 0x58 }).Contains(_data[_datapos])) 
                                        {
                                            sz = (int)(_data[_datapos] & 0x0F);
                                            
                                            switch (sz) 
                                            {
                                                case 4: _args.Add(BitConverter.ToSingle(_data, _datapos + 1)); break;
                                                case 8: _args.Add(BitConverter.ToDouble(_data, _datapos + 1)); break;
                                            }
                                        } 
                                        else if ((new List<byte> { 0x71 }).Contains(_data[_datapos])) 
                                        {
                                            sz = 1;
                                            _args.Add(BitConverter.ToBoolean(_data, _datapos + 1));
                                        } 
                                        else if ((new List<byte> { 0x91, 0x92, 0x94 }).Contains(_data[_datapos])) 
                                        {
                                            var a = GetArgLength(_data, datalen, _datapos);

                                            _args.Add(Encoding.UTF8.GetString(_data, _datapos + 1 + a.Size, a.ArgLength));
                                            
                                            _datapos += a.ArgLength;
                                            sz = a.Size;
                                        } 
                                        else if ((new List<byte> { 0xB1, 0xB2, 0xB4 }).Contains(_data[_datapos])) 
                                        {
                                            var a = GetArgLength(_data, datalen, _datapos);

                                            byte[] ba = new byte[a.ArgLength];

                                            Buffer.BlockCopy(_data, _datapos + 1 + a.Size, ba, 0, a.ArgLength);
                                            _args.Add(ba);
                                            
                                            _datapos += a.ArgLength;
                                            sz = a.Size;
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
    /// NetSocketSendData 
    /// </summary>
    public class NetSocketSendData
    {
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

        public NetSocketSendData(byte command, object[] args)
        {
            _command = command;
            _args = args;

            MemoryStream textms = new MemoryStream();

            textms.Write(new byte[] { command }, 0, 1);

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
                                // 0011 0001
                                textms.Write(new byte[] { 0x31 }, 0, 1);
                                textms.Write(BitConverter.GetBytes((sbyte)i), 0, 1);
                            }
                            else if (Convert.ToInt64(short.MinValue) <= i && i <= Convert.ToInt64(short.MaxValue))
                            {
                                // 0011 0010
                                textms.Write(new byte[] { 0x32 }, 0, 1);
                                textms.Write(BitConverter.GetBytes((short)i), 0, 2);
                            }
                            else if (Convert.ToInt64(int.MinValue) <= i && i <= Convert.ToInt64(int.MaxValue))
                            {
                                // 0011 0100
                                textms.Write(new byte[] { 0x34 }, 0, 1);
                                textms.Write(BitConverter.GetBytes((int)i), 0, 4);
                            }
                            else
                            {
                                // 0011 1000
                                textms.Write(new byte[] { 0x38 }, 0, 1);
                                textms.Write(BitConverter.GetBytes(i), 0, 8);
                            }
                        }
                        break;

                    case "Decimal":
                    case "Single":
                    case "Double":
                        {
                            double f = Convert.ToDouble(arg);

                            if (Convert.ToDouble(float.MinValue) <= f && f <= Convert.ToDouble(float.MaxValue))
                            {
                                // 0101 0100
                                textms.Write(new byte[] { 0x54 }, 0, 1);
                                textms.Write(BitConverter.GetBytes((float)f), 0, 4);
                            }
                            else
                            {
                                // 0101 1000
                                textms.Write(new byte[] { 0x58 }, 0, 1);
                                textms.Write(BitConverter.GetBytes(f), 0, 8);
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

                            if (s.Length <= int.MaxValue)
                            {
                                if (s.Length <= 0x7F)
                                {
                                    // 1001 0001
                                    textms.Write(new byte[] { 0x91 }, 0, 1);
                                    textms.Write(BitConverter.GetBytes(Convert.ToSByte(s.Length)), 0, 1);
                                }
                                else if (s.Length <= 0x7FFF)
                                {
                                    // 1001 0010
                                    textms.Write(new byte[] { 0x92 }, 0, 1);
                                    textms.Write(BitConverter.GetBytes(Convert.ToInt16(s.Length)), 0, 2);
                                }
                                else if (s.Length <= 0x7FFFFFFF)
                                {
                                    // 1001 0100
                                    textms.Write(new byte[] { 0x94 }, 0, 1);
                                    textms.Write(BitConverter.GetBytes(Convert.ToInt32(s.Length)), 0, 4);
                                }

                                textms.Write(s, 0, s.Length);
                            }
                            else
                                throw new OverflowException("String is too large");
                        }
                        break;

                    case "Byte[]":
                        {
                            byte[] b = (byte[])arg;

                            if (b.Length <= int.MaxValue)
                            {
                                if (b.Length <= 0x7F)
                                {
                                    // 1011 0001
                                    textms.Write(new byte[] { 0xB1 }, 0, 1);
                                    textms.Write(BitConverter.GetBytes(Convert.ToSByte(b.Length)), 0, 1);
                                }
                                else if (b.Length <= 0x7FFF)
                                {
                                    // 1011 0010
                                    textms.Write(new byte[] { 0xB2 }, 0, 1);
                                    textms.Write(BitConverter.GetBytes(Convert.ToInt16(b.Length)), 0, 2);
                                }
                                else if (b.Length <= 0x7FFFFFFF)
                                {
                                    // 1011 0100
                                    textms.Write(new byte[] { 0xB4 }, 0, 1);
                                    textms.Write(BitConverter.GetBytes(Convert.ToInt32(b.Length)), 0, 4);
                                }

                                textms.Write(b, 0, b.Length);
                            }
                            else
                                throw new OverflowException("Byte[] is too large");
                        }
                        break;

                    default:
                        throw new NotImplementedException(String.Format("type {0} is not implemented", arg.GetType().Name));
                }
            }

            int textlen = (int)textms.Position;

            int otl = 0;
            if (textlen <= 0x7F) otl = 2;
            else if (textlen <= 0x7FFF) otl = 3;
            else if (textlen <= 0x7FFFFFFF) otl = 5;

            //SOH(1)+OTL(v)+STX(1)+TXT(v)+ETX(1)+CHK(1)+EOT(1)
            byte[] data = new byte[1 + otl + 1 + textlen + 1 + 1 + 1];
            int datapos = 0;

            if (textlen <= int.MaxValue)
            {
                // start of header
                data[datapos] = 0x01; datapos += 1;

                if (textlen <= 0x7F)
                {
                    // 0001 0001
                    data[datapos] = 0x11; datapos += 1;
                    Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToSByte(textlen)), 0, data, datapos, 1); datapos += 1;
                }
                else if (textlen <= 0x7FFF)
                {
                    // 0001 0010
                    data[datapos] = 0x12; datapos += 1;
                    Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToInt16(textlen)), 0, data, datapos, 2); datapos += 2;
                }
                else if (textlen <= 0x7FFFFFFF)
                {
                    // 0001 0100
                    data[datapos] = 0x14; datapos += 1;
                    Buffer.BlockCopy(BitConverter.GetBytes(Convert.ToInt32(textlen)), 0, data, datapos, 4); datapos += 4;
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
                data[datapos] = 0x04; datapos += 1;
                textms.Close();
            }
            else
            {
                textms.Close();
                throw new OverflowException("text is too large");
            }

            _bytes = data;
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
        private const int BUF_SZ = 4096;
        private const int INTRPT_TM = 4000;

    
        private byte[] _buffer;
        private NetSocketData _data;
        private Socket _socket;

        NetSocketDataManipulationResult _result;

        public bool Available 
        {
            get { return _socket != null; }
        }

        private NetSocketAddress _localAddr;
        public NetSocketAddress LocalAddress
        {
            get { return _localAddr; }
        }

        private NetSocketProtocolType _protocol;
        public NetSocketProtocolType ProtocolType
        {
            get { return _protocol; }
        }

        public NetSocket(Socket s, NetSocketProtocolType protocol) 
        {
            _data = new NetSocketData();
            _buffer = new byte[BUF_SZ];
            _protocol = protocol;
            _socket = s;

            _result = NetSocketDataManipulationResult.NoData;

            IPEndPoint iep = _socket.LocalEndPoint as IPEndPoint;
            _localAddr = new NetSocketAddress(iep.Address, iep.Port);
        }

        public void Close() 
        {
            _socket.Close();
        }

        protected void Send(NetSocketSendData data, NetSocketAddress address)
        {
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
            Thread t = new Thread(new ParameterizedThreadStart(ReceiveProc));
            t.IsBackground = true;
            t.Start(callback);
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
                            CheckInterruptedTimeout(INTRPT_TM, callback, remoteAddress);
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

        private async void CheckInterruptedTimeout(int milliseconds, Action<NetSocket, NetSocketReceivedData> callback, NetSocketAddress address)
        {
            await Task.Delay(milliseconds);

            if (_result == NetSocketDataManipulationResult.InProgress)
                callback(this, new NetSocketReceivedData(0x00, new object[] { }, NetSocketReceivedDataResult.Interrupted, address));
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

        public TcpSocket Accept() 
        {
            Socket s = null;
            try
            {
                s = _server.Accept();
            }
            catch { }
            return new TcpSocket(s);
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
        private Socket _socket;

        public bool Connected
        {
            get { return Available && _socket.Connected; }
        }

        private NetSocketAddress _address;
        public NetSocketAddress RemoteAddress
        {
            get { return _address; }
        }

        public TcpSocket(Socket s)
            : base(s, NetSocketProtocolType.Tcp) 
        {
            _socket = s;

            IPEndPoint iep = s.RemoteEndPoint as IPEndPoint;
            _address = new NetSocketAddress(iep.Address, iep.Port);
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
