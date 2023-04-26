module xyz.olooko.comm.netcomm

open System
open System.Collections
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks

type DataType = 
    | CBoolean = 0
    | CByteArray = 1
    | CFloat = 2
    | CInteger = 3
    | CString = 4

type IDataType() = 
    abstract member GetDataType : unit -> DataType
    default this.GetDataType() = DataType.CString
    //abstract member ToString : unit -> string
    //default this.ToString() = String.Empty

type CBoolean(value: bool) =
    inherit IDataType()
    let _value = value
    member this.Value with get () = _value
    override this.GetDataType() = DataType.CBoolean
    override this.ToString() = this.Value.ToString()

type CByteArray(value: byte array) =
    inherit IDataType()
    let _value = value
    member this.Value with get () = _value
    override this.GetDataType() = DataType.CByteArray
    override this.ToString() = "0x" + BitConverter.ToString(this.Value).Replace("-", ",0x")

type CFloat(value: float) =
    inherit IDataType()
    let _value = value
    member this.Value with get () = _value
    override this.GetDataType() = DataType.CFloat
    override this.ToString() = this.Value.ToString()

type CInteger(value: int64) =
    inherit IDataType()
    let _value = value
    member this.Value with get () = _value
    override this.GetDataType() = DataType.CInteger
    override this.ToString() = this.Value.ToString()

type CString(value: string) =
    inherit IDataType()
    let _value = value
    member this.Value with get () = _value
    override this.GetDataType() = DataType.CString
    override this.ToString() = this.Value.ToString()

type CSocketAddress(host: string, port: int) =
    let _ipaddress = CSocketAddress.GetIPAddress(host)
    let _host = host
    let mutable _port = port

    member this.IPAddress with get () = _ipaddress
    member this.Host with get () = _host.Replace("::ffff:", "")
    member this.Port with get () = _port

    new(ipaddress: IPAddress, port: int) = CSocketAddress(ipaddress.ToString(), port)

    static member private GetIPAddress(host: string) = 
        let mutable ipaddress = IPAddress.Any
        if not (IPAddress.TryParse(host, &ipaddress)) then
            let hostEntry = Dns.GetHostEntry(host)
            for (ip: IPAddress) in hostEntry.AddressList do  
                if ip.AddressFamily = AddressFamily.InterNetwork then
                    ipaddress <- ip
        ipaddress

    override this.ToString() = 
        String.Format("{0}:{1}", this.Host, this.Port);


type CSocketDataArgs() =
    let _list = List<IDataType>()

    member this.Length with get () = _list.Count
    member this.Item with get(index) = _list[index]

    member this.Add(arg: IDataType) = _list.Add(arg)
    member this.At(index: int) = _list[index]
    member this.Clear() = _list.Clear()

    interface IEnumerable with
        member this.GetEnumerator() = _list.GetEnumerator()


type CSocketDataArgLength(sz: int, argL: int) =
    let _sz = sz
    let _argL = argL

    member this.Size with get () = _sz
    member this.ArgL with get () = _argL


type CSocketDataManipulationResult = 
    | Completed = 0
    | InProgress = 1
    | NoData = 2
    | ParsingError = 3


type CSocketDataParsingStep = 
    | SOH = 0
    | OTL = 1
    | STX = 2
    | ETX = 3
    | CHK = 4
    | EOT = 5


type CSocketData() = 
    let mutable _command = 0x00uy
    let _args = CSocketDataArgs()
    let mutable _data: byte array = Array.zeroCreate 0
    let mutable _datalen = 0
    let mutable _datapos = 0
    let mutable _checksum = 0x00uy
    let mutable _step = CSocketDataParsingStep.SOH
    let mutable _textlen = 0

    member this.Args with get () = _args
    member this.Command with get () = _command

    member this.Append(buffer: byte array, bytesTransferred: int) = 
        if _data.Length < _datalen + bytesTransferred then
            Array.Resize(&_data, _datalen + bytesTransferred)
        Buffer.BlockCopy(buffer, 0, _data, _datalen, bytesTransferred)
        _datalen <- _datalen + bytesTransferred

    member private this.GetArgLength(datalen: int) = 
        let sz = int _data[_datapos] &&& 0x0F
        let mutable argL = -1
        if datalen > sz then
            let buffer: byte array = Array.zeroCreate sz
            Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz);
            if BitConverter.IsLittleEndian then Array.Reverse(buffer);
            match sz with
                | 1 -> argL <- int buffer[0]
                | 2 -> argL <- int (BitConverter.ToInt16(buffer, 0))
                | 4 -> argL <- BitConverter.ToInt32(buffer, 0)
                | _ -> ()
        CSocketDataArgLength(sz, argL)

    member this.Manipulate() =
        let mutable buffer = Array.zeroCreate 0
        let mutable result = CSocketDataManipulationResult.NoData
        let mutable looping = true
        while looping do
            let datalen = _datalen - _datapos    
            match _step with
            | CSocketDataParsingStep.SOH ->
                if datalen > 0 then
                    if _data[_datapos] = 0x01uy then
                        _datapos <- _datapos + 1
                        _step <- CSocketDataParsingStep.OTL
                    else
                        result <- CSocketDataManipulationResult.ParsingError
                        looping <- false 
                else
                    if _datalen = 0 then result <- CSocketDataManipulationResult.NoData
                    else result <- CSocketDataManipulationResult.InProgress
                    looping <- false
            | CSocketDataParsingStep.OTL ->
                if datalen > 0 then
                    if (List<byte>([|0x11uy;0x12uy;0x14uy|])).Contains(_data[_datapos]) then
                        let a = this.GetArgLength(datalen)
                        if a.ArgL >= 0 then
                            _textlen <- a.ArgL
                            _datapos <- _datapos + 1 + a.Size
                            _step <- CSocketDataParsingStep.STX
                    else
                        result <- CSocketDataManipulationResult.ParsingError
                        looping <- false
                else
                    if _datalen = 0 then result <- CSocketDataManipulationResult.NoData
                    else result <- CSocketDataManipulationResult.InProgress
                    looping <- false
            | CSocketDataParsingStep.STX ->
                if datalen > 0 then
                    if _data[_datapos] = 0x02uy then
                        _datapos <- _datapos + 1
                        _step <- CSocketDataParsingStep.ETX
                    else
                        result <- CSocketDataManipulationResult.ParsingError
                        looping <- false
                else
                    if _datalen = 0 then result <- CSocketDataManipulationResult.NoData
                    else result <- CSocketDataManipulationResult.InProgress
                    looping <- false
            | CSocketDataParsingStep.ETX ->
                if datalen > _textlen then
                    if _data[_datapos + _textlen] = 0x03uy then
                        let textfpos = _datapos
                        _command <- _data[textfpos]
                        _args.Clear()
                        _datapos <- _datapos + 1
                        while _datapos < _textlen + textfpos do
                            let mutable sz = 0
                            let mutable argL = 0
                            if (List<byte>([|0x31uy;0x32uy;0x34uy;0x38uy|])).Contains(_data[_datapos]) then
                                sz <- int _data[_datapos] &&& 0x0F
                                buffer <- Array.zeroCreate sz
                                Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz)
                                if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                                let mutable i = 0L
                                match sz with      
                                    | 1 -> i <- int64 (sbyte buffer[0])
                                    | 2 -> i <- int64 (BitConverter.ToInt16(buffer, 0))
                                    | 4 -> i <- int64 (BitConverter.ToInt32(buffer, 0))
                                    | 8 -> i <- BitConverter.ToInt64(buffer, 0)
                                    | _ -> ()
                                _args.Add(CInteger(i))
                            else if (List<byte>([|0x54uy;0x58uy|])).Contains(_data[_datapos]) then   
                                sz <- int _data[_datapos] &&& 0x0F
                                buffer <- Array.zeroCreate sz
                                Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz)
                                if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                                let mutable f = 0.0
                                match sz with
                                    | 4 -> f <- float (BitConverter.ToSingle(buffer, 0))
                                    | 8 -> f <- BitConverter.ToDouble(buffer, 0)
                                    | _ -> ()
                                _args.Add(CFloat(f))
                            else if (List<byte>([|0x71uy|])).Contains(_data[_datapos]) then
                                sz <- 1
                                buffer <- Array.zeroCreate sz
                                Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz)
                                if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                                _args.Add(CBoolean(BitConverter.ToBoolean(buffer, 0)))
                            else if (List<byte>([|0x91uy;0x92uy;0x94uy|])).Contains(_data[_datapos]) then
                                let a = this.GetArgLength(datalen)
                                sz <- a.Size
                                argL <- a.ArgL
                                _args.Add(CString(Encoding.UTF8.GetString(_data, _datapos + 1 + sz, argL)))
                                _datapos <- _datapos + argL
                            else if (List<byte>([|0xB1uy;0xB2uy;0xB4uy|])).Contains(_data[_datapos]) then
                                let a = this.GetArgLength(datalen)
                                sz <- a.Size
                                argL <- a.ArgL
                                let ba = Array.zeroCreate argL
                                Buffer.BlockCopy(_data, _datapos + 1 + sz, ba, 0, argL)
                                _args.Add(CByteArray(ba))
                                _datapos <- _datapos + argL
                            else
                                result <- CSocketDataManipulationResult.ParsingError
                                looping <- false
                            _datapos <- _datapos + 1 + sz
                        _checksum <- 0x00uy
                        for i = textfpos to textfpos + _textlen - 1 do
                            _checksum <- _checksum ^^^ _data[i]
                        _datapos <- _datapos + 1
                        _step <- CSocketDataParsingStep.CHK
                    else
                        result <- CSocketDataManipulationResult.ParsingError
                        looping <- false
                else
                    if _datalen = 0 then result <- CSocketDataManipulationResult.NoData
                    else result <- CSocketDataManipulationResult.InProgress
                    looping <- false
            | CSocketDataParsingStep.CHK ->
                if datalen > 0 then
                    if _data[_datapos] = _checksum then          
                        _datapos <- _datapos + 1
                        _step <- CSocketDataParsingStep.EOT
                    else
                        result <- CSocketDataManipulationResult.ParsingError
                        looping <- false
                else
                    if _datalen = 0 then result <- CSocketDataManipulationResult.NoData
                    else result <- CSocketDataManipulationResult.InProgress
                    looping <- false
            | CSocketDataParsingStep.EOT ->
                if datalen > 0 then
                    if _data[_datapos] = 0x04uy then
                        _datapos <- _datapos + 1
                        _datalen <- _datalen - _datapos
                        Buffer.BlockCopy(_data, _datapos, _data, 0, _datalen)
                        _datapos <- 0
                        _checksum <- 0x00uy
                        _step <- CSocketDataParsingStep.SOH
                        _textlen <- 0
                        result <- CSocketDataManipulationResult.Completed
                    else
                        result <- CSocketDataManipulationResult.ParsingError
                    looping <- false
                else
                    if _datalen = 0 then result <- CSocketDataManipulationResult.NoData
                    else result <- CSocketDataManipulationResult.InProgress
                    looping <- false
            | _ -> ()
        result            


type CSocketProtocolType = 
    | Tcp = 0
    | Udp = 1


type CSocketSendDataBuildResult =
    | ByteArrayLengthOverflowError = 0
    | CommandValueOverflowError = 1
    | DataTotalLengthOverflowError = 2
    | DataTypeNotImplementedError = 3
    | NoData = 4
    | StringLengthOverflowError = 5
    | Successful = 6

type CSocketSendData(command: byte , args: CSocketDataArgs) = 
    let ARG_MAXLEN = 0x7FFFFF - 5
    let TXT_MAXLEN = Int32.MaxValue - 10
    let mutable _result = CSocketSendDataBuildResult.NoData
    let _args = args
    let mutable _bytes = Array.zeroCreate 0 
    let _command = command

    do
        //if _command < 0x00uy || _command > 0xFFuy then     
        //    _result <- CSocketSendDataBuildResult.CommandValueOverflowError
        //    return
        let textms = new MemoryStream()
        textms.Write([|command|], 0, 1)
        let mutable buffer =  Array.zeroCreate 0
        let mutable looping = true
        let mutable i = 0
        while looping && i < _args.Length do
            let arg = _args.At(i)
            match arg.GetDataType() with
            | DataType.CInteger ->
                let i = (arg :?> CInteger).Value
                if Convert.ToInt64(SByte.MinValue) <= i && i <= Convert.ToInt64(SByte.MaxValue) then
                    textms.Write([|0x31uy|], 0, 1)
                    textms.Write([|byte (Convert.ToSByte(i))|], 0, 1)       
                else if Convert.ToInt64(Int16.MinValue) <= i && i <= Convert.ToInt64(Int16.MaxValue) then    
                    buffer <- BitConverter.GetBytes(Convert.ToInt16(i))
                    if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                    textms.Write([|0x32uy|], 0, 1)
                    textms.Write(buffer, 0, 2)
                else if Convert.ToInt64(Int32.MinValue) <= i && i <= Convert.ToInt64(Int32.MaxValue) then  
                    buffer <- BitConverter.GetBytes(Convert.ToInt32(i))
                    if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                    textms.Write([|0x34uy|], 0, 1)
                    textms.Write(buffer, 0, 4)            
                else             
                    buffer <- BitConverter.GetBytes(i)
                    if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                    textms.Write([|0x38uy|], 0, 1)
                    textms.Write(buffer, 0, 8)
            | DataType.CFloat ->
                let f = (arg :?> CFloat).Value
                if Math.Abs(f) <= Convert.ToDouble(Single.MaxValue) then
                    buffer <- BitConverter.GetBytes(Convert.ToSingle(f))
                    if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                    textms.Write([|0x54uy|], 0, 1)
                    textms.Write(buffer, 0, 4)
                else
                    buffer <- BitConverter.GetBytes(f)
                    if BitConverter.IsLittleEndian then Array.Reverse(buffer);
                    textms.Write([|0x58uy|], 0, 1)
                    textms.Write(buffer, 0, 8)
            | DataType.CBoolean ->
                textms.Write([|0x71uy|], 0, 1)
                textms.Write(BitConverter.GetBytes((arg :?> CBoolean).Value), 0, 1)
            | DataType.CString ->
                let s = Encoding.UTF8.GetBytes((arg :?> CString).Value)
                if s.Length <= ARG_MAXLEN then              
                    if s.Length <= Convert.ToInt32(SByte.MaxValue) then                   
                        textms.Write([|0x91uy|], 0, 1)
                        textms.Write([|Convert.ToByte(s.Length)|], 0, 1)                  
                    else if s.Length <= Convert.ToInt32(Int16.MaxValue) then                  
                        buffer <- BitConverter.GetBytes(Convert.ToInt16(s.Length))
                        if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                        textms.Write([|0x92uy|], 0, 1)
                        textms.Write(buffer, 0, 2)                    
                    else                   
                        buffer <- BitConverter.GetBytes(Convert.ToInt32(s.Length))
                        if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                        textms.Write([|0x94uy|], 0, 1)
                        textms.Write(buffer, 0, 4)
                    textms.Write(s, 0, s.Length)               
                else               
                    _result <- CSocketSendDataBuildResult.StringLengthOverflowError
                    looping <- false
            | DataType.CByteArray ->          
                let ba = (arg :?> CByteArray).Value
                if ba.Length <= ARG_MAXLEN then               
                    if ba.Length <= Convert.ToInt32(SByte.MaxValue) then                    
                        textms.Write([|0xB1uy|], 0, 1)
                        textms.Write([|Convert.ToByte(ba.Length)|], 0, 1)                 
                    else if ba.Length <= Convert.ToInt32(Int16.MaxValue) then
                        buffer <- BitConverter.GetBytes(Convert.ToInt16(ba.Length))
                        if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                        textms.Write([|0xB2uy|], 0, 1)
                        textms.Write(buffer, 0, 2)                
                    else                 
                        buffer <- BitConverter.GetBytes(Convert.ToInt32(ba.Length))
                        if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                        textms.Write([|0xB4uy|], 0, 1)
                        textms.Write(buffer, 0, 4)
                    textms.Write(ba, 0, ba.Length)              
                else               
                    _result <- CSocketSendDataBuildResult.ByteArrayLengthOverflowError
                    looping <- false
            | _ ->
                _result <- CSocketSendDataBuildResult.DataTypeNotImplementedError
                looping <- false
            i <- i + 1

        let textlen = Convert.ToInt32(textms.Position)
        let mutable otl = 0
        if textlen <= Convert.ToInt32(SByte.MaxValue) then otl <- 2
        else if textlen <= Convert.ToInt32(Int16.MaxValue) then otl <- 3
        else if textlen <= Int32.MaxValue then otl <- 5
        //SOH(1)+OTL(v)+STX(1)+TXT(v)+ETX(1)+CHK(1)+EOT(1)
        let data = Array.zeroCreate (1 + otl + 1 + textlen + 1 + 1 + 1)
        let mutable datapos = 0
        if textlen <= TXT_MAXLEN then
            data[datapos] <- 0x01uy
            datapos <- datapos + 1
            if textlen <= Convert.ToInt32(SByte.MaxValue) then
                data[datapos] <- 0x11uy
                datapos <- datapos + 1
                Buffer.BlockCopy([|Convert.ToByte(textlen)|], 0, data, datapos, 1)
                datapos <- datapos + 1
            else if textlen <= Convert.ToInt32(Int16.MaxValue) then
                buffer <- BitConverter.GetBytes(Convert.ToInt16(textlen))
                if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                data[datapos] <- 0x12uy
                datapos <- datapos + 1                  
                Buffer.BlockCopy(buffer, 0, data, datapos, 2); 
                datapos <- datapos + 2
            else           
                buffer <- BitConverter.GetBytes(Convert.ToInt32(textlen))
                if BitConverter.IsLittleEndian then Array.Reverse(buffer)
                data[datapos] <- 0x14uy
                datapos <- datapos + 1                    
                Buffer.BlockCopy(buffer, 0, data, datapos, 4)
                datapos <- datapos + 4
            data[datapos] <- 0x02uy
            datapos <- datapos + 1
            textms.Flush()
            let text = textms.GetBuffer()
            Buffer.BlockCopy(text, 0, data, datapos, textlen) 
            datapos <- datapos + textlen
            data[datapos] <- 0x03uy
            datapos <- datapos + 1
            let mutable checksum = 0x00uy         
            for i = 0 to textlen - 1 do checksum <- checksum ^^^ text[i]
            data[datapos] <- checksum 
            datapos <- datapos + 1
            data[datapos] <- 0x04uy
            //datapos <- datapos + 1
            textms.Close()   
            _bytes <- data
            _result <- CSocketSendDataBuildResult.Successful 
        else      
            textms.Close()
            _result <- CSocketSendDataBuildResult.DataTotalLengthOverflowError

    member this.Args with get () = _args
    member this.BuildResult with get () = _result
    member this.Bytes with get () = _bytes
    member this.Command with get () = _command
    member this.Length with get () = _bytes.Length


type CSocketReceivedDataResult = 
    | Closed = 0
    | Completed = 1
    | Interrupted = 2
    | ParsingError = 3


type CSocketReceivedData(command: byte, args: CSocketDataArgs, result: CSocketReceivedDataResult, address: CSocketAddress) = 
    let _command = command
    let _args = args
    let _address = address
    let _result = result

    member this.Args with get () = _args
    member this.Command with get () = _command
    member this.RemoteAddress with get () = _address
    member this.Result with get () = _result


type CSocket(s: Socket, protocol: CSocketProtocolType) =
    let _socket = s
    let _data = CSocketData()
    let mutable _result = CSocketDataManipulationResult.NoData
    let mutable _localAddress = CSocketAddress("0.0.0.0", 0)
    let _protocol = protocol
    let CheckInterruptedTimeout(s: CSocket, milliseconds: int, callback: (CSocket * CSocketReceivedData) -> unit, address: CSocketAddress) =
        async {
            let! result = Task.Delay(milliseconds) |> Async.AwaitTask
            if s.ManipulationResult = CSocketDataManipulationResult.InProgress then
                callback(s, CSocketReceivedData(0x00uy, CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, address))
        }

    do
        if _socket <> null then
            let iep = s.LocalEndPoint :?> IPEndPoint
            _localAddress <- CSocketAddress(iep.Address, iep.Port) 

    member this.Available with get () = _socket <> null
    member this.LocalAddress with get () = _localAddress
    member this.ProtocolType with get () = _protocol
    member private this.ManipulationResult with get () = _result

    member this.Close() = if this.Available then _socket.Close()

    member this.SetReceivedCallback(callback: (CSocket * CSocketReceivedData) -> unit) = 
        if this.Available then 
            let t = Thread(ParameterizedThreadStart(this.ReceiveProc))
            t.IsBackground <- true
            t.Start(callback)

    member this.Send(data: CSocketSendData, address: CSocketAddress) = 
        if this.Available then
            this.SendProc(data, address, 0)

    member private this.SendProc(data: CSocketSendData, address: CSocketAddress, bytesTransferred: int) =
        let mutable length = 0
        let mutable transferred = bytesTransferred
        if _protocol = CSocketProtocolType.Tcp then
            length <- _socket.Send(data.Bytes, transferred, data.Length - transferred, SocketFlags.None)
        else if _protocol = CSocketProtocolType.Udp then
            let iep = IPEndPoint(address.IPAddress, address.Port)
            length <- _socket.SendTo(data.Bytes, transferred, data.Length - transferred, SocketFlags.None, iep)
        if length > 0 then
            transferred <- transferred + length
            if transferred < data.Length then
                this.SendProc(data, address, transferred)

    member private this.ReceiveProc(state: Object) =
        let callback = state :?> ((CSocket * CSocketReceivedData) -> unit)
        let mutable buffer: byte array = Array.zeroCreate 4096
        let mutable returning = false
        while not returning do
            let mutable bytesTransferred = 0
            let mutable remoteAddress = CSocketAddress("0.0.0.0", 0)
            if _protocol = CSocketProtocolType.Tcp then
                bytesTransferred <- _socket.Receive(buffer)
                let iep = _socket.RemoteEndPoint :?> IPEndPoint
                remoteAddress <- CSocketAddress(iep.Address, iep.Port)
            else if _protocol = CSocketProtocolType.Udp then
                let mutable ep = IPEndPoint(IPAddress.Any, 0) :> EndPoint
                bytesTransferred <- _socket.ReceiveFrom(buffer, &ep)
                let iep = ep :?> IPEndPoint
                remoteAddress <- CSocketAddress(iep.Address, iep.Port)
            if bytesTransferred > 0 then
                _data.Append(buffer, bytesTransferred)
                let mutable looping = true
                while looping do
                    _result <- _data.Manipulate()
                    if _result = CSocketDataManipulationResult.Completed then        
                        callback(this, CSocketReceivedData(_data.Command, _data.Args, CSocketReceivedDataResult.Completed, remoteAddress))
                    else if _result = CSocketDataManipulationResult.ParsingError then
                        callback(this, CSocketReceivedData(0x00uy, CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress))
                        looping <- false
                        returning <- true
                    else if _result = CSocketDataManipulationResult.InProgress then     
                        CheckInterruptedTimeout(this, 3000, callback, remoteAddress) |> Async.RunSynchronously
                        looping <- false
                    else if _result = CSocketDataManipulationResult.NoData then
                        looping <- false           
            else
                callback(this, CSocketReceivedData(0x00uy, CSocketDataArgs(), CSocketReceivedDataResult.Closed, remoteAddress))
                returning <- true
 

type TcpSocket(s: Socket) = 
    inherit CSocket(s, CSocketProtocolType.Tcp)
    let mutable _remoteAddress = CSocketAddress("0.0.0.0", 0)
    do
        if s <> null then
            let iep = s.RemoteEndPoint :?> IPEndPoint
            _remoteAddress <- CSocketAddress(iep.Address, iep.Port)   

    member this.Connected with get () = this.Available && s.Connected
    member this.RemoteAddress with get () = _remoteAddress

    member this.Send(data: CSocketSendData) =
        base.Send(data, CSocketAddress("0.0.0.0", 0))


type TcpServer(s: Socket) =
    let _server = s
    member this.Running with get () = _server <> null

    member this.Close() = 
        _server.Close()
        _server = null

    member this.SetAcceptCallback(callback: TcpSocket -> unit) =
        let t = Thread(new ParameterizedThreadStart(this.AcceptProc))
        t.IsBackground <- true
        t.Start(callback)

    member private this.AcceptProc(state: Object) =
        let callback = state :?> (TcpSocket -> unit)
        while this.Running do
            let mutable s: Socket = null
            try 
                s <- _server.Accept()
            with 
                _ -> s <- null

            callback(new TcpSocket(s))


type UdpSocket(s: Socket) =
    inherit CSocket(s, CSocketProtocolType.Udp)
    //member this.Send(data: CSocketSendData, address: CSocketAddress) = ()
        //base.Send(data, address)


type NetworkComm = class 
    static member TcpConnect(address: CSocketAddress) = 
        let mutable s = new Socket(SocketType.Stream, ProtocolType.Tcp)
        try
            s.Connect(IPEndPoint(address.IPAddress, address.Port))
        with
            _ -> s <- null
        TcpSocket(s)

    static member TcpListen(address: CSocketAddress) = 
        let mutable s = new Socket(SocketType.Stream, ProtocolType.Tcp)
        try
            s.Bind(IPEndPoint(address.IPAddress, address.Port))
            s.Listen(0)
        with
            _ -> s <- null
        TcpServer(s)

    static member UdpCast(address: CSocketAddress) =
        let mutable s = new Socket(SocketType.Dgram, ProtocolType.Udp)
        try
            s.Bind(IPEndPoint(address.IPAddress, address.Port))  
        with
            _ -> s <- null
        UdpSocket(s)
end

