Imports System
Imports System.Collections
Imports System.Collections.Generic
Imports System.Diagnostics.Eventing.Reader
Imports System.IO
Imports System.Net
Imports System.Net.Sockets
Imports System.Runtime.CompilerServices
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Xml.Linq

Namespace xyz.olooko.comm.netcomm
    Public Class CBoolean
        Implements IDataType
        Public ReadOnly Property Value As Boolean

        Public Sub New(ByVal value As Boolean)
            Me.Value = value
        End Sub

        Public Function GetDataType() As DataType Implements IDataType.GetDataType
            Return DataType.CBoolean
        End Function

        Public Overrides Function ToString() As String Implements IDataType.ToString
            Return Me.Value.ToString()
        End Function
    End Class

    Public Class CByteArray
        Implements IDataType

        Public ReadOnly Property Value As Byte()

        Public Sub New(ByVal value As Byte())
            Me.Value = value
        End Sub

        Public Function GetDataType() As DataType Implements IDataType.GetDataType
            Return DataType.CByteArray
        End Function

        Public Overrides Function ToString() As String Implements IDataType.ToString
            Return "0x" & BitConverter.ToString(Me.Value).Replace("-", ",0x")
        End Function
    End Class

    Public Class CFloat
        Implements IDataType

        Public ReadOnly Property Value As Double

        Public Sub New(ByVal value As Double)
            Me.Value = value
        End Sub
        Public Function GetDataType() As DataType Implements IDataType.GetDataType
            Return DataType.CFloat
        End Function

        Public Overrides Function ToString() As String Implements IDataType.ToString
            Return Me.Value.ToString()
        End Function
    End Class

    Public Class CInteger
        Implements IDataType

        Public ReadOnly Property Value As Long

        Public Sub New(ByVal value As Long)
            Me.Value = value
        End Sub
        Public Function GetDataType() As DataType Implements IDataType.GetDataType
            Return DataType.CInteger
        End Function

        Public Overrides Function ToString() As String Implements IDataType.ToString
            Return Me.Value.ToString()
        End Function
    End Class

    Public Class CString
        Implements IDataType

        Public ReadOnly Property Value As String

        Public Sub New(ByVal value As String)
            Me.Value = value
        End Sub
        Public Function GetDataType() As DataType Implements IDataType.GetDataType
            Return DataType.CString
        End Function

        Public Overrides Function ToString() As String Implements IDataType.ToString
            Return Me.Value
        End Function
    End Class

    Public Enum DataType
        CBoolean
        CByteArray
        CFloat
        CInteger
        CString
    End Enum

    Public Interface IDataType
        Function GetDataType() As DataType
        Function ToString() As String
    End Interface

    Public MustInherit Class CSocket
        Protected _socket As Socket
        Private _data As CSocketData
        Private _result As CSocketDataManipulationResult
        Private _localAddress As CSocketAddress
        Private _protocol As CSocketProtocolType

        Public ReadOnly Property Available As Boolean
            Get
                Return _socket IsNot Nothing
            End Get
        End Property

        Public ReadOnly Property LocalAddress As CSocketAddress
            Get
                Return _localAddress
            End Get
        End Property

        Public ReadOnly Property ProtocolType As CSocketProtocolType
            Get
                Return _protocol
            End Get
        End Property

        Public Sub New(ByVal s As Socket, ByVal protocol As CSocketProtocolType)
            _socket = s
            _data = New CSocketData()
            _protocol = protocol
            _result = CSocketDataManipulationResult.NoData
            _localAddress = New CSocketAddress("0,0.0.0", 0)

            If Me.Available Then
                Dim iep As IPEndPoint = TryCast(_socket.LocalEndPoint, IPEndPoint)
                _localAddress = New CSocketAddress(iep.Address, iep.Port)
            End If
        End Sub

        Public Sub Close()
            If Me.Available Then _socket.Close()
        End Sub

        Public Sub SetReceivedCallback(ByVal callback As Action(Of CSocket, CSocketReceivedData))
            If Me.Available Then
                Dim t As Thread = New Thread(New ParameterizedThreadStart(AddressOf ReceiveProc))
                t.IsBackground = True
                t.Start(callback)
            End If
        End Sub

        Protected Sub Send(ByVal data As CSocketSendData, ByVal address As CSocketAddress)
            If Me.Available Then SendProc(data, address, 0)
        End Sub

        Private Sub SendProc(ByVal data As CSocketSendData, ByVal address As CSocketAddress, ByVal bytesTransferred As Integer)
            Dim length As Integer = 0

            If _protocol = CSocketProtocolType.Tcp Then
                length = _socket.Send(data.Bytes, bytesTransferred, data.Length - bytesTransferred, SocketFlags.None)
            ElseIf _protocol = CSocketProtocolType.Udp Then
                Dim iep As IPEndPoint = New IPEndPoint(address.IPAddress, address.Port)
                length = _socket.SendTo(data.Bytes, bytesTransferred, data.Length - bytesTransferred, SocketFlags.None, iep)
            End If

            If length > 0 Then
                bytesTransferred += length
                If bytesTransferred < data.Length Then SendProc(data, address, bytesTransferred)
            End If
        End Sub

        Private Sub ReceiveProc(ByVal state As Object)
            If state Is Nothing Then Return
            Dim callback As Action(Of CSocket, CSocketReceivedData) = CType(state, Action(Of CSocket, CSocketReceivedData))
            Dim buffer As Byte() = New Byte(4095) {}

            While True
                Dim bytesTransferred As Integer = 0
                Dim remoteAddress As CSocketAddress = New CSocketAddress("0.0.0.0", 0)

                If _protocol = CSocketProtocolType.Tcp Then
                    bytesTransferred = _socket.Receive(buffer)
                    Dim iep As IPEndPoint = TryCast(_socket.RemoteEndPoint, IPEndPoint)
                    remoteAddress = New CSocketAddress(iep.Address, iep.Port)
                ElseIf _protocol = CSocketProtocolType.Udp Then
                    Dim ep As EndPoint = CType((New IPEndPoint(IPAddress.Any, 0)), EndPoint)
                    bytesTransferred = _socket.ReceiveFrom(buffer, ep)
                    Dim iep As IPEndPoint = TryCast(ep, IPEndPoint)
                    remoteAddress = New CSocketAddress(iep.Address, iep.Port)
                End If

                If bytesTransferred > 0 Then
                    _data.Append(buffer, bytesTransferred)

                    While True
                        _result = CType(_data.Manipulate(), CSocketDataManipulationResult)

                        If _result = CSocketDataManipulationResult.Completed Then
                            callback(Me, New CSocketReceivedData(_data.Command, _data.Args, CSocketReceivedDataResult.Completed, remoteAddress))
                            Continue While
                        ElseIf _result = CSocketDataManipulationResult.ParsingError Then
                            callback(Me, New CSocketReceivedData(&H0, New CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress))
                            Return
                        ElseIf _result = CSocketDataManipulationResult.InProgress Then
                            CheckInterruptedTimeout(Me, 15000, callback, remoteAddress)
                            Exit While
                        ElseIf _result = CSocketDataManipulationResult.NoData Then
                            Exit While
                        End If
                    End While

                    Continue While
                Else
                    callback(Me, New CSocketReceivedData(&H0, New CSocketDataArgs(), CSocketReceivedDataResult.Closed, remoteAddress))
                    Return
                End If
            End While
        End Sub

        Private Shared Async Sub CheckInterruptedTimeout(ByVal s As CSocket, ByVal milliseconds As Integer, ByVal callback As Action(Of CSocket, CSocketReceivedData), ByVal address As CSocketAddress)
            Await Task.Delay(milliseconds)
            If s._result = CSocketDataManipulationResult.InProgress Then callback(s, New CSocketReceivedData(&H0, New CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, address))
        End Sub
    End Class

    Public Class CSocketAddress
        Private _ipaddress As IPAddress
        Private _host As String
        Private _port As Integer

        Public ReadOnly Property IPAddress As IPAddress
            Get
                Return _ipaddress
            End Get
        End Property

        Public ReadOnly Property Host As String
            Get
                Return _host.Replace("::ffff:", "")
            End Get
        End Property

        Public ReadOnly Property Port As Integer
            Get
                Return _port
            End Get
        End Property

        Public Sub New(ByVal host As String, ByVal port As Integer)
            _ipaddress = GetIPAddress(host)
            _host = host
            _port = port
        End Sub

        Public Sub New(ByVal ipaddress As IPAddress, ByVal port As Integer)
            _ipaddress = ipaddress
            _host = ipaddress.ToString()
            _port = port
        End Sub

        Private Shared Function GetIPAddress(ByVal host As String) As IPAddress
            Dim ipaddress As IPAddress = Nothing

            If Not IPAddress.TryParse(host, ipaddress) Then

                If ipaddress IsNot Nothing Then
                    Dim hostEntry As IPHostEntry = Dns.GetHostEntry(host)

                    For Each ip As IPAddress In hostEntry.AddressList
                        If ip.AddressFamily = AddressFamily.InterNetwork Then ipaddress = ip
                    Next
                End If
            End If

            Return ipaddress
        End Function

        Public Overrides Function ToString() As String
            Return String.Format("{0}:{1}", Me.Host, Me.Port)
        End Function
    End Class

    Public Class CSocketData
        Private _command As Byte
        Private _args As CSocketDataArgs
        Private _data As Byte()
        Private _datalen As Integer
        Private _datapos As Integer
        Private _checksum As Byte
        Private _step As CSocketDataParsingStep
        Private _textlen As Integer

        Public ReadOnly Property Args As CSocketDataArgs
            Get
                Return _args
            End Get
        End Property

        Public ReadOnly Property Command As Byte
            Get
                Return _command
            End Get
        End Property

        Public Sub New()
            _command = &H0
            _args = New CSocketDataArgs()
            _data = New Byte(-1) {}
            _datalen = 0
            _datapos = 0
            _checksum = &H0
            _step = CSocketDataParsingStep.SOH
            _textlen = 0
        End Sub

        Public Sub Append(ByVal buffer As Byte(), ByVal bytesTransferred As Integer)
            If _data.Length < _datalen + bytesTransferred Then Array.Resize(_data, _datalen + bytesTransferred)
            System.Buffer.BlockCopy(buffer, 0, _data, _datalen, bytesTransferred)
            _datalen += bytesTransferred
        End Sub

        Public Function Manipulate() As CSocketDataManipulationResult
            Dim buffer As Byte()

            While True
                Dim datalen As Integer = _datalen - _datapos

                Select Case _step
                    Case CSocketDataParsingStep.SOH

                        If datalen > 0 Then

                            If _data(_datapos) = &H1 Then
                                _datapos += 1
                                _step = CSocketDataParsingStep.OTL
                                Continue While
                            Else
                                Return CSocketDataManipulationResult.ParsingError
                            End If
                        End If

                    Case CSocketDataParsingStep.OTL

                        If datalen > 0 Then

                            If (New List(Of Byte) From {
                                &H11,
                                &H12,
                                &H14
                            }).Contains(_data(_datapos)) Then
                                Dim a As CSocketDataArgLength = GetArgLength(datalen)

                                If a.ArgL >= 0 Then
                                    _textlen = a.ArgL
                                    _datapos += 1 + a.Size
                                    _step = CSocketDataParsingStep.STX
                                    Continue While
                                End If
                            Else
                                Return CSocketDataManipulationResult.ParsingError
                            End If
                        End If

                    Case CSocketDataParsingStep.STX

                        If datalen > 0 Then

                            If _data(_datapos) = &H2 Then
                                _datapos += 1
                                _step = CSocketDataParsingStep.ETX
                                Continue While
                            Else
                                Return CSocketDataManipulationResult.ParsingError
                            End If
                        End If

                    Case CSocketDataParsingStep.ETX

                        If datalen > _textlen Then

                            If _data(_datapos + _textlen) = &H3 Then
                                Dim textfpos As Integer = _datapos
                                _command = _data(textfpos)
                                _args.Clear()
                                _datapos += 1

                                While _datapos < _textlen + textfpos
                                    Dim sz As Integer = 0
                                    Dim argL As Integer = 0

                                    If (New List(Of Byte) From {
                                        &H31,
                                        &H32,
                                        &H34,
                                        &H38
                                    }).Contains(_data(_datapos)) Then
                                        sz = CInt((_data(_datapos) And &HF))
                                        buffer = New Byte(sz - 1) {}
                                        System.Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz)
                                        If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                                        Dim i As Long = 0

                                        Select Case sz
                                            Case 1
                                                i = CLng(IIf(buffer(0) < 128, buffer(0), buffer(0) - 256))
                                            Case 2
                                                i = CLng(BitConverter.ToInt16(buffer, 0))
                                            Case 4
                                                i = CLng(BitConverter.ToInt32(buffer, 0))
                                            Case 8
                                                i = BitConverter.ToInt64(buffer, 0)
                                        End Select

                                        _args.Add(New CInteger(i))
                                    ElseIf (New List(Of Byte) From {
                                        &H54,
                                        &H58
                                    }).Contains(_data(_datapos)) Then
                                        sz = CInt((_data(_datapos) And &HF))
                                        buffer = New Byte(sz - 1) {}
                                        System.Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz)
                                        If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                                        Dim f As Double = 0.0

                                        Select Case sz
                                            Case 4
                                                f = CDbl(BitConverter.ToSingle(buffer, 0))
                                            Case 8
                                                f = BitConverter.ToDouble(buffer, 0)
                                        End Select

                                        _args.Add(New CFloat(f))
                                    ElseIf (New List(Of Byte) From {
                                        &H71
                                    }).Contains(_data(_datapos)) Then
                                        sz = 1
                                        buffer = New Byte(sz - 1) {}
                                        System.Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz)
                                        If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                                        _args.Add(New CBoolean(BitConverter.ToBoolean(buffer, 0)))
                                    ElseIf (New List(Of Byte) From {
                                        &H91,
                                        &H92,
                                        &H94
                                    }).Contains(_data(_datapos)) Then
                                        Dim a As CSocketDataArgLength = GetArgLength(datalen)
                                        sz = a.Size
                                        argL = a.ArgL
                                        _args.Add(New CString(Encoding.UTF8.GetString(_data, _datapos + 1 + sz, argL)))
                                        _datapos += argL
                                    ElseIf (New List(Of Byte) From {
                                        &HB1,
                                        &HB2,
                                        &HB4
                                    }).Contains(_data(_datapos)) Then
                                        Dim a As CSocketDataArgLength = GetArgLength(datalen)
                                        sz = a.Size
                                        argL = a.ArgL
                                        Dim ba As Byte() = New Byte(argL - 1) {}
                                        System.Buffer.BlockCopy(_data, _datapos + 1 + sz, ba, 0, argL)
                                        _args.Add(New CByteArray(ba))
                                        _datapos += argL
                                    Else
                                        Return CSocketDataManipulationResult.ParsingError
                                    End If

                                    _datapos += 1 + sz
                                End While

                                _checksum = &H0

                                For i As Integer = textfpos To textfpos + _textlen - 1
                                    _checksum = _checksum Xor _data(i)
                                Next

                                _datapos += 1
                                _step = CSocketDataParsingStep.CHK
                                Continue While
                            Else
                                Return CSocketDataManipulationResult.ParsingError
                            End If
                        End If

                    Case CSocketDataParsingStep.CHK

                        If datalen > 0 Then

                            If _data(_datapos) = _checksum Then
                                _datapos += 1
                                _step = CSocketDataParsingStep.EOT
                                Continue While
                            Else
                                Return CSocketDataManipulationResult.ParsingError
                            End If
                        End If

                    Case CSocketDataParsingStep.EOT

                        If datalen > 0 Then

                            If _data(_datapos) = &H4 Then
                                _datapos += 1
                                _datalen -= _datapos
                                System.Buffer.BlockCopy(_data, _datapos, _data, 0, _datalen)
                                _datapos = 0
                                _checksum = &H0
                                _step = CSocketDataParsingStep.SOH
                                _textlen = 0
                                Return CSocketDataManipulationResult.Completed
                            Else
                                Return CSocketDataManipulationResult.ParsingError
                            End If
                        End If
                End Select

                If _datalen = 0 Then Return CSocketDataManipulationResult.NoData
                Return CSocketDataManipulationResult.InProgress
            End While
            Return CSocketDataManipulationResult.NoData
        End Function

        Private Function GetArgLength(ByVal datalen As Integer) As CSocketDataArgLength
            Dim sz As Integer = CInt((_data(_datapos) And &HF))
            Dim argL As Integer = -1

            If datalen > sz Then
                Dim buffer As Byte() = New Byte(sz - 1) {}
                System.Buffer.BlockCopy(_data, _datapos + 1, buffer, 0, sz)
                If BitConverter.IsLittleEndian Then Array.Reverse(buffer)

                Select Case sz
                    Case 1
                        argL = CInt(buffer(0))
                    Case 2
                        argL = CInt(BitConverter.ToInt16(buffer, 0))
                    Case 4
                        argL = CInt(BitConverter.ToInt32(buffer, 0))
                End Select
            End If

            Return New CSocketDataArgLength(sz, argL)
        End Function
    End Class

    Public Class CSocketDataArgLength
        Public ReadOnly Property Size As Integer
        Public ReadOnly Property ArgL As Integer

        Public Sub New(ByVal sz As Integer, ByVal argL As Integer)
            Me.Size = sz
            Me.ArgL = argL
        End Sub
    End Class

    Public Class CSocketDataArgs
        Implements IEnumerable

        Private _list As List(Of IDataType)

        Default Public ReadOnly Property Item(ByVal index As Integer) As IDataType
            Get
                Return _list(index)
            End Get
        End Property

        Public ReadOnly Property Length As Integer
            Get
                Return _list.Count
            End Get
        End Property

        Public Sub New()
            _list = New List(Of IDataType)()
        End Sub

        Public Sub Add(ByVal arg As IDataType)
            _list.Add(arg)
        End Sub

        Public Function At(ByVal index As Integer) As IDataType
            Return _list(index)
        End Function

        Public Sub Clear()
            _list.Clear()
        End Sub

        Public Function GetEnumerator() As IEnumerator Implements IEnumerable.GetEnumerator
            Return _list.GetEnumerator()
        End Function
    End Class

    Public Enum CSocketDataManipulationResult
        Completed
        InProgress
        NoData
        ParsingError
    End Enum

    Public Enum CSocketDataParsingStep
        SOH
        OTL
        STX
        ETX
        CHK
        EOT
    End Enum

    Public Enum CSocketProtocolType
        Tcp
        Udp
    End Enum

    Public Class CSocketReceivedData
        Private _command As Byte
        Private _args As CSocketDataArgs
        Private _address As CSocketAddress
        Private _result As CSocketReceivedDataResult

        Public ReadOnly Property Args As CSocketDataArgs
            Get
                Return _args
            End Get
        End Property

        Public ReadOnly Property Command As Byte
            Get
                Return _command
            End Get
        End Property

        Public ReadOnly Property RemoteAddress As CSocketAddress
            Get
                Return _address
            End Get
        End Property

        Public ReadOnly Property Result As CSocketReceivedDataResult
            Get
                Return _result
            End Get
        End Property

        Public Sub New(ByVal command As Byte, ByVal args As CSocketDataArgs, ByVal result As CSocketReceivedDataResult, ByVal address As CSocketAddress)
            _command = command
            _args = args
            _address = address
            _result = result
        End Sub
    End Class

    Public Enum CSocketReceivedDataResult
        Closed
        Completed
        Interrupted
        ParsingError
    End Enum

    Public Class CSocketSendData
        Private Const ARG_MAXLEN As Integer = &H7FFFFF - 5
        Private Const TXT_MAXLEN As Integer = Integer.MaxValue - 10
        Private _result As CSocketSendDataBuildResult
        Private _args As CSocketDataArgs
        Private _bytes As Byte()
        Private _command As Byte

        Public ReadOnly Property Args As CSocketDataArgs
            Get
                Return _args
            End Get
        End Property

        Public ReadOnly Property BuildResult As CSocketSendDataBuildResult
            Get
                Return _result
            End Get
        End Property

        Public ReadOnly Property Bytes As Byte()
            Get
                Return _bytes
            End Get
        End Property

        Public ReadOnly Property Command As Byte
            Get
                Return _command
            End Get
        End Property

        Public ReadOnly Property Length As Integer
            Get
                Return _bytes.Length
            End Get
        End Property

        Public Sub New(ByVal command As Byte, ByVal args As CSocketDataArgs)
            _result = CSocketSendDataBuildResult.NoData

            If command < &H0 OrElse command > &HFF Then
                _result = CSocketSendDataBuildResult.CommandValueOverflowError
                Return
            End If

            _command = command
            _args = args
            _bytes = New Byte(-1) {}
            Dim textms As MemoryStream = New MemoryStream()
            textms.Write(New Byte() {command}, 0, 1)
            Dim buffer As Byte()

            For Each arg As IDataType In _args

                Select Case arg.GetDataType()
                    Case DataType.CInteger
                        Dim i As Long = (TryCast(arg, CInteger)).Value

                        If Convert.ToInt64(SByte.MinValue) <= i AndAlso i <= Convert.ToInt64(SByte.MaxValue) Then
                            buffer = BitConverter.GetBytes(CSByte(i))
                            textms.Write(New Byte() {&H31}, 0, 1)
                            textms.Write(buffer, 0, 1)
                        ElseIf Convert.ToInt64(Short.MinValue) <= i AndAlso i <= Convert.ToInt64(Short.MaxValue) Then
                            buffer = BitConverter.GetBytes(CShort(i))
                            If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                            textms.Write(New Byte() {&H32}, 0, 1)
                            textms.Write(buffer, 0, 2)
                        ElseIf Convert.ToInt64(Integer.MinValue) <= i AndAlso i <= Convert.ToInt64(Integer.MaxValue) Then
                            buffer = BitConverter.GetBytes(CInt(i))
                            If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                            textms.Write(New Byte() {&H34}, 0, 1)
                            textms.Write(buffer, 0, 4)
                        Else
                            buffer = BitConverter.GetBytes(i)
                            If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                            textms.Write(New Byte() {&H38}, 0, 1)
                            textms.Write(buffer, 0, 8)
                        End If

                    Case DataType.CFloat
                        Dim f As Double = (TryCast(arg, CFloat)).Value

                        If Math.Abs(f) <= Convert.ToDouble(Single.MaxValue) Then
                            buffer = BitConverter.GetBytes(CSng(f))
                            If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                            textms.Write(New Byte() {&H54}, 0, 1)
                            textms.Write(buffer, 0, 4)
                        Else
                            buffer = BitConverter.GetBytes(f)
                            If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                            textms.Write(New Byte() {&H58}, 0, 1)
                            textms.Write(buffer, 0, 8)
                        End If

                    Case DataType.CBoolean
                        textms.Write(New Byte() {&H71}, 0, 1)
                        textms.Write(BitConverter.GetBytes((TryCast(arg, CBoolean)).Value), 0, 1)
                    Case DataType.CString
                        Dim s As Byte() = Encoding.UTF8.GetBytes((TryCast(arg, CString)).Value)

                        If s.Length <= ARG_MAXLEN Then
                            If s.Length <= SByte.MaxValue Then
                                buffer = BitConverter.GetBytes(Convert.ToSByte(s.Length))
                                textms.Write(New Byte() {&H91}, 0, 1)
                                textms.Write(buffer, 0, 1)
                            ElseIf s.Length <= Short.MaxValue Then
                                buffer = BitConverter.GetBytes(Convert.ToInt16(s.Length))
                                If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                                textms.Write(New Byte() {&H92}, 0, 1)
                                textms.Write(buffer, 0, 2)
                            Else
                                buffer = BitConverter.GetBytes(Convert.ToInt32(s.Length))
                                If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                                textms.Write(New Byte() {&H94}, 0, 1)
                                textms.Write(buffer, 0, 4)
                            End If

                            textms.Write(s, 0, s.Length)
                        Else
                            _result = CSocketSendDataBuildResult.StringLengthOverflowError
                            Return
                        End If

                    Case DataType.CByteArray
                        Dim ba As Byte() = (TryCast(arg, CByteArray)).Value

                        If ba.Length <= ARG_MAXLEN Then

                            If ba.Length <= SByte.MaxValue Then
                                buffer = BitConverter.GetBytes(Convert.ToSByte(ba.Length))
                                textms.Write(New Byte() {&HB1}, 0, 1)
                                textms.Write(buffer, 0, 1)
                            ElseIf ba.Length <= Short.MaxValue Then
                                buffer = BitConverter.GetBytes(Convert.ToInt16(ba.Length))
                                If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                                textms.Write(New Byte() {&HB2}, 0, 1)
                                textms.Write(buffer, 0, 2)
                            Else
                                buffer = BitConverter.GetBytes(Convert.ToInt32(ba.Length))
                                If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                                textms.Write(New Byte() {&HB4}, 0, 1)
                                textms.Write(buffer, 0, 4)
                            End If

                            textms.Write(ba, 0, ba.Length)
                        Else
                            _result = CSocketSendDataBuildResult.ByteArrayLengthOverflowError
                            Return
                        End If

                    Case Else
                        _result = CSocketSendDataBuildResult.DataTypeNotImplementedError
                        Return
                End Select
            Next

            Dim textlen As Integer = CInt(textms.Position)
            Dim otl As Integer = 0

            If textlen <= SByte.MaxValue Then
                otl = 2
            ElseIf textlen <= Short.MaxValue Then
                otl = 3
            ElseIf textlen <= Integer.MaxValue Then
                otl = 5
            End If

            Dim data As Byte() = New Byte(1 + otl + 1 + textlen + 1 + 1 + 1 - 1) {}
            Dim datapos As Integer = 0

            If textlen <= TXT_MAXLEN Then
                data(datapos) = &H1
                datapos += 1

                If textlen <= SByte.MaxValue Then
                    buffer = BitConverter.GetBytes(Convert.ToSByte(textlen))
                    data(datapos) = &H11
                    datapos += 1
                    System.Buffer.BlockCopy(buffer, 0, data, datapos, 1)
                    datapos += 1
                ElseIf textlen <= Short.MaxValue Then
                    buffer = BitConverter.GetBytes(Convert.ToInt16(textlen))
                    If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                    data(datapos) = &H12
                    datapos += 1
                    System.Buffer.BlockCopy(buffer, 0, data, datapos, 2)
                    datapos += 2
                Else
                    buffer = BitConverter.GetBytes(Convert.ToInt32(textlen))
                    If BitConverter.IsLittleEndian Then Array.Reverse(buffer)
                    data(datapos) = &H14
                    datapos += 1
                    System.Buffer.BlockCopy(buffer, 0, data, datapos, 4)
                    datapos += 4
                End If

                data(datapos) = &H2
                datapos += 1
                textms.Flush()
                Dim text As Byte() = textms.GetBuffer()
                System.Buffer.BlockCopy(text, 0, data, datapos, textlen)
                datapos += textlen
                data(datapos) = &H3
                datapos += 1
                Dim checksum As Byte = &H0

                For i As Integer = 0 To textlen - 1
                    checksum = checksum Xor text(i)
                Next

                data(datapos) = checksum
                datapos += 1
                data(datapos) = &H4
                datapos += 1
                textms.Close()
            Else
                textms.Close()
                _result = CSocketSendDataBuildResult.DataTotalLengthOverflowError
                Return
            End If

            _bytes = data
            _result = CSocketSendDataBuildResult.Successful
        End Sub
    End Class

    Public Enum CSocketSendDataBuildResult
        ByteArrayLengthOverflowError
        CommandValueOverflowError
        DataTotalLengthOverflowError
        DataTypeNotImplementedError
        NoData
        StringLengthOverflowError
        Successful
    End Enum

    Public Class TcpServer
        Private _server As Socket

        Public ReadOnly Property Running As Boolean
            Get
                Return _server IsNot Nothing
            End Get
        End Property

        Public Sub New(ByVal s As Socket)
            _server = s
        End Sub

        Public Sub Close()
            _server.Close()
            _server = Nothing
        End Sub

        Public Sub SetAcceptCallback(ByVal callback As Action(Of TcpSocket))
            Dim t As Thread = New Thread(New ParameterizedThreadStart(AddressOf AcceptProc))
            t.IsBackground = True
            t.Start(callback)
        End Sub

        Private Sub AcceptProc(ByVal state As Object)
            If state Is Nothing Then Return
            Dim callback As Action(Of TcpSocket) = CType(state, Action(Of TcpSocket))

            While Me.Running
                Dim s As Socket = Nothing

                Try
                    s = _server.Accept()
                Catch
                End Try

                callback(New TcpSocket(s))
            End While
        End Sub
    End Class

    Public Class TcpSocket
        Inherits CSocket

        Private _remoteAddress As CSocketAddress

        Public ReadOnly Property Connected As Boolean
            Get
                Return Available AndAlso _socket.Connected
            End Get
        End Property

        Public ReadOnly Property RemoteAddress As CSocketAddress
            Get
                Return _remoteAddress
            End Get
        End Property

        Public Sub New(ByVal s As Socket)
            MyBase.New(s, CSocketProtocolType.Tcp)
            _remoteAddress = New CSocketAddress("0.0.0.0", 0)

            If s IsNot Nothing Then
                Dim iep As IPEndPoint = TryCast(s.RemoteEndPoint, IPEndPoint)
                _remoteAddress = New CSocketAddress(iep.Address, iep.Port)
            End If
        End Sub

        Public Overloads Sub Send(ByVal data As CSocketSendData)
            MyBase.Send(data, Nothing)
        End Sub
    End Class

    Public Class UdpSocket
        Inherits CSocket

        Public Sub New(ByVal s As Socket)
            MyBase.New(s, CSocketProtocolType.Udp)
        End Sub

        Public Overloads Sub Send(ByVal data As CSocketSendData, ByVal address As CSocketAddress)
            MyBase.Send(data, address)
        End Sub
    End Class

    Public Class NetworkComm
        Public Shared Function TcpConnect(ByVal address As CSocketAddress) As TcpSocket
            Dim s As Socket = New Socket(SocketType.Stream, ProtocolType.Tcp)

            Try
                s.Connect(New IPEndPoint(address.IPAddress, address.Port))
            Catch
                s = Nothing
            End Try

            Return New TcpSocket(s)
        End Function

        Public Shared Function TcpListen(ByVal address As CSocketAddress) As TcpServer
            Dim s As Socket = New Socket(SocketType.Stream, ProtocolType.Tcp)

            Try
                s.Bind(New IPEndPoint(address.IPAddress, address.Port))
                s.Listen(0)
            Catch
                s = Nothing
            End Try

            Return New TcpServer(s)
        End Function

        Public Shared Function UdpCast(ByVal address As CSocketAddress) As UdpSocket
            Dim s As Socket = New Socket(SocketType.Dgram, ProtocolType.Udp)

            Try
                s.Bind(New IPEndPoint(address.IPAddress, address.Port))
            Catch
                s = Nothing
            End Try

            Return New UdpSocket(s)
        End Function
    End Class
End Namespace

