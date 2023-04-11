package netcomm

import (
	"bytes"
	"encoding/binary"
	"fmt"
	"math"
	"net"
	"reflect"
	"strconv"
	"strings"
	"time"
)

type IDataType interface {
	String() string
}

type CBoolean struct {
	value bool
}

func NewCBoolean(value bool) CBoolean {
	return CBoolean{value}
}

func (d CBoolean) GetValue() bool {
	return d.value
}

func (d CBoolean) String() string {
	return fmt.Sprintf("%t", d.value)
}

type CByteArray struct {
	value []byte
}

func NewCByteArray(value []byte) CByteArray {
	return CByteArray{value}
}

func (d CByteArray) GetValue() []byte {
	return d.value
}

func (d CByteArray) String() string {
	s := ""
	ba := d.value
	for i := 0; i < len(ba); i++ {
		if s != "" {
			s += ","
		}
		s += fmt.Sprintf("0x%02X", ba[i])
	}
	return s
}

type CFloat struct {
	value float64
}

func NewCFloat(value float64) CFloat {
	return CFloat{value}
}

func (d CFloat) GetValue() float64 {
	return d.value
}

func (d CFloat) String() string {
	return fmt.Sprintf("%f", d.value)
}

type CInteger struct {
	value int64
}

func NewCInteger(value int64) CInteger {
	return CInteger{value}
}

func (d CInteger) GetValue() int64 {
	return d.value
}

func (d CInteger) String() string {
	return fmt.Sprintf("%d", d.value)
}

type CString struct {
	value string
}

func NewCString(value string) CString {
	return CString{value}
}

func (d CString) GetValue() string {
	return d.value
}

func (d CString) String() string {
	return d.value
}

type CSocket struct {
	conn         net.Conn
	packetConn   net.PacketConn
	data         CSocketData
	protocol     int //CSocketProtocolType
	localAddress CSocketAddress
	connected    bool
	result       int //CSocketDataManipulationResult
}

func newCSocket(s interface{}, protocol int) CSocket {
	netsock := CSocket{}
	netsock.data = NewCSocketData()
	netsock.protocol = protocol
	netsock.localAddress = NewCSocketAddress("0.0.0.0", 0)

	if s != nil {
		if protocol == CSocketProtocolType_Tcp {
			c := s.(net.Conn)
			netsock.conn = c
			if c != nil {
				netsock.localAddress = newCSocketAddressFromString(c.LocalAddr().String())
			}
		} else if protocol == CSocketProtocolType_Udp {
			p := s.(net.PacketConn)
			netsock.packetConn = p
			if p != nil {
				netsock.localAddress = newCSocketAddressFromString(p.LocalAddr().String())
			}
		}
	}
	return netsock
}

func (s CSocket) IsAvailable() bool {
	if s.protocol == CSocketProtocolType_Tcp {
		return s.conn != nil
	} else if s.protocol == CSocketProtocolType_Udp {
		return s.packetConn != nil
	}
	return false
}

func (s CSocket) GetLocalAddress() CSocketAddress {
	return s.localAddress
}

func (s CSocket) GetProtocolType() int {
	return s.protocol
}

func (s CSocket) Close() {
	if s.IsAvailable() {
		if s.protocol == CSocketProtocolType_Tcp {
			s.conn.Close()
		} else if s.protocol == CSocketProtocolType_Udp {
			s.packetConn.Close()
		}
	}
}

func (s CSocket) SetReceivedCallback(callback func(CSocket, CSocketReceivedData)) {
	if s.IsAvailable() {
		go s.receiveProc(callback)
	}
}

func (s CSocket) Send(data CSocketSendData, address CSocketAddress) {
	if s.IsAvailable() {
		s.sendProc(data, address, 0)
	}
}

func (s *CSocket) sendProc(data CSocketSendData, address CSocketAddress, bytesTransferred int) {
	length := 0

	if s.protocol == CSocketProtocolType_Tcp {
		length, _ = s.conn.Write(data.bytes[bytesTransferred:])
		if bytesTransferred > 0 {
			s.connected = true
		} else {
			s.connected = false
		}
	} else if s.protocol == CSocketProtocolType_Udp {
		r, _ := net.ResolveUDPAddr("udp", fmt.Sprintf("%s:%d", address.host, address.port))
		length, _ = s.packetConn.WriteTo(data.bytes[bytesTransferred:], r)
	}

	if length > 0 {
		bytesTransferred += length
		if bytesTransferred < data.GetLength() {
			s.sendProc(data, address, bytesTransferred)
		}
	}
}

func (s *CSocket) receiveProc(callback func(CSocket, CSocketReceivedData)) {
	buffer := make([]byte, 4096)

	for {
		bytesTransferred := 0
		strAddr := "0.0.0.0:0"

		if s.protocol == CSocketProtocolType_Tcp {
			bytesTransferred, _ = s.conn.Read(buffer)
			if bytesTransferred > 0 {
				s.connected = true
			} else {
				s.connected = false
			}
			strAddr = s.conn.RemoteAddr().String()

		} else if s.protocol == CSocketProtocolType_Udp {
			var address net.Addr
			bytesTransferred, address, _ = s.packetConn.ReadFrom(buffer)
			strAddr = address.String()
		}

		remoteAddress := newCSocketAddressFromString(strAddr)

		if bytesTransferred > 0 {
			s.data.Append(buffer, bytesTransferred)

			for {
				s.result = s.data.Manipulate()

				if s.result == CSocketDataManipulationResult_Completed {
					callback(*s, NewCSocketReceivedData(s.data.command, s.data.args, CSocketReceivedDataResult_Completed, remoteAddress))
					continue
				} else if s.result == CSocketDataManipulationResult_ParsingError {
					callback(*s, NewCSocketReceivedData(0x00, NewCSocketDataArgs(), CSocketReceivedDataResult_ParsingError, remoteAddress))
					return
				} else if s.result == CSocketDataManipulationResult_InProgress {
					go checkInterruptedTimeout(s, 15000, callback, remoteAddress)
					break
				} else if s.result == CSocketDataManipulationResult_NoData {
					break
				}
			}
			continue
		} else {
			callback(*s, NewCSocketReceivedData(0x00, NewCSocketDataArgs(), CSocketReceivedDataResult_Closed, remoteAddress))
			break
		}
	}
}

func checkInterruptedTimeout(s *CSocket, milliseconds int, callback func(CSocket, CSocketReceivedData), address CSocketAddress) {
	time.Sleep(time.Duration(milliseconds) * time.Millisecond)

	if s.result == CSocketDataManipulationResult_InProgress {
		callback(*s, NewCSocketReceivedData(0x00, NewCSocketDataArgs(), CSocketReceivedDataResult_Interrupted, address))
	}
}

type CSocketAddress struct {
	host string
	port int
}

func NewCSocketAddress(host string, port int) CSocketAddress {
	return CSocketAddress{host, port}
}

func newCSocketAddressFromString(address string) CSocketAddress {
	ss := strings.Split(address, ":")
	host := strings.Join(ss[:len(ss)-1], ":")
	port, _ := strconv.Atoi(ss[len(ss)-1])
	return CSocketAddress{host, port}
}

func (d CSocketAddress) GetHost() string {
	return d.host
}

func (d CSocketAddress) GetPort() int {
	return d.port
}

func (d CSocketAddress) String() string {
	return fmt.Sprintf("%s:%d", d.GetHost(), d.GetPort())
}

type CSocketData struct {
	command  byte
	args     CSocketDataArgs
	data     []byte
	datapos  int
	checksum byte
	step     int
	textlen  int
}

func NewCSocketData() CSocketData {
	data := CSocketData{}
	data.command = 0x00
	data.args = NewCSocketDataArgs()
	data.data = make([]byte, 0)
	data.datapos = 0
	data.checksum = 0x00
	data.step = CSocketDataParsingStep_SOH
	data.textlen = 0
	return data
}

func (d CSocketData) GetArgs() CSocketDataArgs {
	return d.args
}

func (d CSocketData) GetCommand() byte {
	return d.command
}

func (d CSocketData) getArgLength(datalen int) CSocketDataArgLength {
	sz := int(d.data[d.datapos] & 0x0F)
	argL := -1

	if datalen > sz {
		switch sz {
		case 1:
			argL = int(d.data[d.datapos+1])
		case 2:
			argL = int(binary.BigEndian.Uint16(d.data[d.datapos+1:]))
		case 4:
			argL = int(binary.BigEndian.Uint32(d.data[d.datapos+1:]))
		}
	}
	return NewCSocketDataArgLength(sz, argL)
}

func (d *CSocketData) Append(buffer []byte, bytesTransferred int) {
	d.data = append(d.data, buffer[:bytesTransferred]...)
}

func (d *CSocketData) Manipulate() int {
	for {
		datalen := len(d.data) - d.datapos

		switch d.step {
		case CSocketDataParsingStep_SOH:
			if datalen > 0 {
				if d.data[d.datapos] == 0x01 {
					d.datapos += 1
					d.step = CSocketDataParsingStep_OTL
					continue
				} else {
					return CSocketDataManipulationResult_ParsingError
				}
			}

		case CSocketDataParsingStep_OTL:
			if datalen > 0 {
				switch d.data[d.datapos] {
				case 0x11, 0x12, 0x14:
					a := d.getArgLength(datalen)
					sz := a.GetSize()
					d.textlen = a.GetArgLength()
					if d.textlen >= 0 {
						d.datapos += 1 + sz
						d.step = CSocketDataParsingStep_STX
						continue
					}
				default:
					return CSocketDataManipulationResult_ParsingError
				}
			}

		case CSocketDataParsingStep_STX:
			if datalen > 0 {
				if d.data[d.datapos] == 0x02 {
					d.datapos += 1
					d.step = CSocketDataParsingStep_ETX
					continue
				} else {
					return CSocketDataManipulationResult_ParsingError
				}
			}

		case CSocketDataParsingStep_ETX:
			if datalen > d.textlen {
				if d.data[d.datapos+d.textlen] == 0x03 {
					textfpos := d.datapos
					d.command = d.data[textfpos]
					d.args.Clear()
					d.datapos += 1

					for d.datapos < d.textlen+textfpos {
						sz := 0
						switch d.data[d.datapos] {
						case 0x31, 0x32, 0x34, 0x38:
							var i int64 = 0
							sz = int(d.data[d.datapos] & 0x0F)
							switch sz {
							case 1:
								i = int64(int8(d.data[d.datapos+1]))
							case 2:
								i = int64(int16(binary.BigEndian.Uint16(d.data[d.datapos+1:])))
							case 4:
								i = int64(int32(binary.BigEndian.Uint32(d.data[d.datapos+1:])))
							case 8:
								i = int64(binary.BigEndian.Uint64(d.data[d.datapos+1:]))
							}
							d.args.Add(NewCInteger(i))
						case 0x54, 0x58:
							var f float64 = 0.0
							sz = int(d.data[d.datapos] & 0x0F)
							switch sz {
							case 4:
								f = float64(math.Float32frombits(binary.BigEndian.Uint32(d.data[d.datapos+1:])))
							case 8:
								f = math.Float64frombits(binary.BigEndian.Uint64(d.data[d.datapos+1:]))
							}
							d.args.Add(NewCFloat(f))
						case 0x71:
							sz = 1
							b := true
							if d.data[d.datapos+1] == 0 {
								b = false
							}
							d.args.Add(NewCBoolean(b))
						case 0x91, 0x92, 0x94:
							a := d.getArgLength(datalen)
							sz = a.GetSize()
							argL := a.GetArgLength()
							d.args.Add(NewCString(string(d.data[d.datapos+1+sz : d.datapos+1+sz+argL])))
							d.datapos += argL
						case 0xB1, 0xB2, 0xB4:
							a := d.getArgLength(datalen)
							sz = a.GetSize()
							argL := a.GetArgLength()
							ba := []byte{}
							ba = append(ba, d.data[d.datapos+1+sz:d.datapos+1+sz+argL]...)
							d.args.Add(NewCByteArray(ba))
							d.datapos += argL
						default:
							return CSocketDataManipulationResult_ParsingError
						}
						d.datapos += 1 + sz
					}

					d.checksum = 0x00
					for i := textfpos; i < textfpos+d.textlen; i++ {
						d.checksum ^= d.data[i]
					}

					d.datapos += 1
					d.step = CSocketDataParsingStep_CHK
					continue
				} else {
					return CSocketDataManipulationResult_ParsingError
				}
			}

		case CSocketDataParsingStep_CHK:
			if datalen > 0 {
				if d.data[d.datapos] == d.checksum {
					d.datapos += 1
					d.step = CSocketDataParsingStep_EOT
					continue
				} else {
					return CSocketDataManipulationResult_ParsingError
				}
			}

		case CSocketDataParsingStep_EOT:
			if datalen > 0 {
				if d.data[d.datapos] == 0x04 {
					d.datapos += 1
					d.data = d.data[d.datapos:]
					d.datapos = 0
					d.checksum = 0
					d.step = CSocketDataParsingStep_SOH
					d.textlen = 0
					return CSocketDataManipulationResult_Completed
				} else {
					return CSocketDataManipulationResult_ParsingError
				}
			}
		}

		if len(d.data) == 0 {
			return CSocketDataManipulationResult_NoData
		}
		return CSocketDataManipulationResult_InProgress
	}
}

type CSocketDataArgLength struct {
	sz   int
	argL int
}

func NewCSocketDataArgLength(sz int, argL int) CSocketDataArgLength {
	return CSocketDataArgLength{sz, argL}
}

func (a CSocketDataArgLength) GetSize() int {
	return a.sz
}

func (a CSocketDataArgLength) GetArgLength() int {
	return a.argL
}

type CSocketDataArgs struct {
	list []IDataType
}

func NewCSocketDataArgs() CSocketDataArgs {
	list := make([]IDataType, 0)
	return CSocketDataArgs{list}
}

func (a CSocketDataArgs) GetArgs() []IDataType {
	return a.list
}

func (a CSocketDataArgs) GetLength() int {
	return len(a.list)
}

func (a *CSocketDataArgs) Add(arg IDataType) {
	a.list = append(a.list, arg)
}

func (a CSocketDataArgs) At(index int) IDataType {
	return a.list[index]
}

func (a *CSocketDataArgs) Clear() {
	a.list = make([]IDataType, 0)
}

const (
	CSocketDataManipulationResult_Completed = iota
	CSocketDataManipulationResult_InProgress
	CSocketDataManipulationResult_NoData
	CSocketDataManipulationResult_ParsingError
)

const (
	CSocketDataParsingStep_SOH = iota
	CSocketDataParsingStep_OTL
	CSocketDataParsingStep_STX
	CSocketDataParsingStep_ETX
	CSocketDataParsingStep_CHK
	CSocketDataParsingStep_EOT
)

const (
	CSocketProtocolType_Tcp = iota
	CSocketProtocolType_Udp
)

type CSocketReceivedData struct {
	command byte
	args    CSocketDataArgs
	result  int
	address CSocketAddress
}

func NewCSocketReceivedData(command byte, args CSocketDataArgs, result int, address CSocketAddress) CSocketReceivedData {
	return CSocketReceivedData{command, args, result, address}
}

func (d CSocketReceivedData) GetArgs() CSocketDataArgs {
	return d.args
}

func (d CSocketReceivedData) GetCommand() byte {
	return d.command
}

func (d CSocketReceivedData) GetRemoteAddress() CSocketAddress {
	return d.address
}

func (d CSocketReceivedData) GetResult() int {
	return d.result
}

const (
	CSocketReceivedDataResult_Closed = iota
	CSocketReceivedDataResult_Completed
	CSocketReceivedDataResult_Interrupted
	CSocketReceivedDataResult_ParsingError
)

type CSocketSendData struct {
	command byte
	args    CSocketDataArgs
	bytes   []byte
	result  int
}

func (d CSocketSendData) GetArgs() CSocketDataArgs {
	return d.args
}

func (d CSocketSendData) GetBuildResult() int {
	return d.result
}

func (d CSocketSendData) GetBytes() []byte {
	return d.bytes
}

func (d CSocketSendData) GetCommand() byte {
	return d.command
}

func (d CSocketSendData) GetLength() int {
	return len(d.bytes)
}

func NewCSocketSendData(command byte, args CSocketDataArgs) CSocketSendData {
	ARG_MAXLEN := 0x7FFFFF - 5
	TXT_MAXLEN := math.MaxInt32 - 10

	senddata := CSocketSendData{}
	senddata.command = 0x00
	senddata.args = args
	senddata.bytes = []byte{}
	senddata.result = CSocketSendDataBuildResult_NoData

	//if command < 0x00 || command > 0xFF {
	//	senddata.result = CSocketSendDataBuildResult_CommandValueOverflowError
	//	return senddata
	//}

	text := bytes.Buffer{}
	textlen := 0

	text.Write([]byte{command})
	textlen += 1

	for x := 0; x < args.GetLength(); x++ {
		arg := args.GetArgs()[x]
		argType := reflect.TypeOf(arg).String()
		switch argType {
		case "netcomm.CInteger":
			i := arg.(CInteger).GetValue()
			if math.MinInt8 <= i && i <= math.MaxInt8 {
				text.Write([]byte{byte(0x31)})
				textlen += 1
				binary.Write(&text, binary.BigEndian, int8(i))
				textlen += 1
			} else if math.MinInt16 <= i && i <= math.MaxInt16 {
				text.Write([]byte{byte(0x32)})
				textlen += 1
				binary.Write(&text, binary.BigEndian, int16(i))
				textlen += 2
			} else if math.MinInt32 <= i && i <= math.MaxInt32 {
				text.Write([]byte{byte(0x34)})
				textlen += 1
				binary.Write(&text, binary.BigEndian, int32(i))
				textlen += 4
			} else {
				text.Write([]byte{byte(0x38)})
				textlen += 1
				binary.Write(&text, binary.BigEndian, int64(i))
				textlen += 8
			}

		case "netcomm.CFloat":
			f := arg.(CFloat).GetValue()
			if math.Abs(f) <= math.MaxFloat32 {
				text.Write([]byte{byte(0x54)})
				textlen += 1
				binary.Write(&text, binary.BigEndian, float32(f))
				textlen += 4
			} else {
				text.Write([]byte{byte(0x58)})
				textlen += 1
				binary.Write(&text, binary.BigEndian, f)
				textlen += 8
			}

		case "netcomm.CBoolean":
			text.Write([]byte{byte(0x71)})
			textlen += 1
			binary.Write(&text, binary.BigEndian, arg.(CBoolean).GetValue())
			textlen += 1

		case "netcomm.CString":
			s := []byte(arg.(CString).GetValue())
			if len(s) <= ARG_MAXLEN {
				if len(s) <= math.MaxInt8 {
					text.Write([]byte{byte(0x91)})
					textlen += 1
					binary.Write(&text, binary.BigEndian, int8(len(s)))
					textlen += 1
				} else if len(s) <= math.MaxInt16 {
					text.Write([]byte{byte(0x92)})
					textlen += 1
					binary.Write(&text, binary.BigEndian, int16(len(s)))
					textlen += 2
				} else {
					text.Write([]byte{byte(0x94)})
					textlen += 1
					binary.Write(&text, binary.BigEndian, int32(len(s)))
					textlen += 4
				}
				text.Write(s)
				textlen += len(s)
			} else {
				senddata.result = CSocketSendDataBuildResult_StringLengthOverflowError
				return senddata
			}

		case "netcomm.CByteArray":
			ba := arg.(CByteArray).GetValue()
			if len(ba) <= ARG_MAXLEN {
				if len(ba) <= math.MaxInt8 {
					text.Write([]byte{byte(0xB1)})
					textlen += 1
					binary.Write(&text, binary.BigEndian, int8(len(ba)))
					textlen += 1
				} else if len(ba) <= math.MaxInt16 {
					text.Write([]byte{byte(0xB2)})
					textlen += 1
					binary.Write(&text, binary.BigEndian, int16(len(ba)))
					textlen += 2
				} else {
					text.Write([]byte{byte(0xB4)})
					textlen += 1
					binary.Write(&text, binary.BigEndian, int32(len(ba)))
					textlen += 4
				}
				text.Write(ba)
				textlen += len(ba)
			} else {
				senddata.result = CSocketSendDataBuildResult_ByteArrayLengthOverflowError
				return senddata
			}

		default:
			senddata.result = CSocketSendDataBuildResult_DataTypeNotImplementedError
			return senddata
		}
	}

	textbytes := text.Bytes()

	data := bytes.Buffer{}
	datapos := 0

	if textlen <= TXT_MAXLEN {
		data.Write([]byte{byte(0x01)})
		datapos += 1

		if textlen <= math.MaxInt8 {
			data.Write([]byte{byte(0x11)})
			datapos += 1
			binary.Write(&data, binary.BigEndian, int8(textlen))
			datapos += 1
		} else if textlen <= math.MaxInt16 {
			data.Write([]byte{byte(0x12)})
			datapos += 1
			binary.Write(&data, binary.BigEndian, int16(textlen))
			datapos += 2
		} else {
			data.Write([]byte{byte(0x14)})
			datapos += 1
			binary.Write(&data, binary.BigEndian, int32(textlen))
			datapos += 4
		}

		data.Write([]byte{byte(0x02)})
		datapos += 1

		data.Write(textbytes)
		datapos += textlen

		data.Write([]byte{byte(0x03)})
		datapos += 1

		checksum := byte(0x00)
		for i := 0; i < textlen; i++ {
			checksum ^= textbytes[i]
		}

		data.Write([]byte{byte(checksum)})
		datapos += 1

		data.Write([]byte{byte(0x04)})
		datapos += 1

	} else {
		senddata.result = CSocketSendDataBuildResult_DataTotalLengthOverflowError
		return senddata
	}

	senddata.command = command
	senddata.args = args
	senddata.bytes = data.Bytes()
	senddata.result = CSocketSendDataBuildResult_Successful
	return senddata
}

const (
	CSocketSendDataBuildResult_ByteArrayLengthOverflowError = iota
	CSocketSendDataBuildResult_CommandValueOverflowError
	CSocketSendDataBuildResult_DataTotalLengthOverflowError
	CSocketSendDataBuildResult_DataTypeNotImplementedError
	CSocketSendDataBuildResult_NoData
	CSocketSendDataBuildResult_StringLengthOverflowError
	CSocketSendDataBuildResult_Successful
)

type TcpServer struct {
	server net.Listener
}

func NewTcpServer(l net.Listener) TcpServer {
	return TcpServer{l}
}

func (t TcpServer) IsRunning() bool {
	return t.server != nil
}

func (t *TcpServer) Close() {
	t.server.Close()
	t.server = nil
}

func (t TcpServer) SetAcceptCallback(callback func(TcpSocket)) {
	go t.acceptProc(callback)
}

func (t TcpServer) acceptProc(callback func(TcpSocket)) {
	for t.IsRunning() {
		c, err := t.server.Accept()
		if err != nil {
			c = nil
		}
		callback(NewTcpSocket(c))
	}
}

type TcpSocket struct {
	netsock       CSocket
	remoteAddress CSocketAddress
}

func NewTcpSocket(c net.Conn) TcpSocket {
	s := TcpSocket{}
	s.netsock = newCSocket(c, CSocketProtocolType_Tcp)
	s.netsock.connected = (c != nil)
	s.remoteAddress = NewCSocketAddress("0.0.0.0", 0)
	if c != nil {
		ss := strings.Split(c.RemoteAddr().String(), ":")
		host := strings.Join(ss[:len(ss)-1], ":")
		port, _ := strconv.Atoi(ss[len(ss)-1])
		s.remoteAddress = NewCSocketAddress(host, port)
	}
	return s
}

func (t TcpSocket) IsAvailable() bool {
	return t.netsock.IsAvailable()
}

func (s TcpSocket) IsConnected() bool {
	return s.IsAvailable() && s.netsock.connected
}

func (t TcpSocket) GetLocalAddress() CSocketAddress {
	return t.netsock.GetLocalAddress()
}

func (t TcpSocket) GetProtocolType() int {
	return t.netsock.GetProtocolType()
}

func (t TcpSocket) GetRemoteAddress() CSocketAddress {
	return t.remoteAddress
}

func (t TcpSocket) Close() {
	t.netsock.Close()
}

func (t TcpSocket) SetReceivedCallback(callback func(CSocket, CSocketReceivedData)) {
	t.netsock.SetReceivedCallback(callback)
}

func (t TcpSocket) Send(data CSocketSendData) {
	t.netsock.Send(data, CSocketAddress{})
}

type UdpSocket struct {
	netsock CSocket
}

func NewUdpSocket(p net.PacketConn) UdpSocket {
	s := UdpSocket{}
	s.netsock = newCSocket(p, CSocketProtocolType_Udp)
	return s
}

func (u UdpSocket) IsAvailable() bool {
	return u.netsock.IsAvailable()
}

func (u UdpSocket) GetLocalAddress() CSocketAddress {
	return u.netsock.GetLocalAddress()
}

func (u UdpSocket) GetProtocolType() int {
	return u.netsock.GetProtocolType()
}

func (u UdpSocket) Close() {
	u.netsock.Close()
}

func (u UdpSocket) SetReceivedCallback(callback func(CSocket, CSocketReceivedData)) {
	u.netsock.SetReceivedCallback(callback)
}

func (u UdpSocket) Send(data CSocketSendData, address CSocketAddress) {
	u.netsock.Send(data, address)
}

func TcpConnect(address CSocketAddress) TcpSocket {
	c, err := net.Dial("tcp", fmt.Sprintf("%s:%d", address.host, address.port))
	if err != nil {
		c = nil
	}
	return NewTcpSocket(c)
}

func TcpListen(address CSocketAddress) TcpServer {
	l, err := net.Listen("tcp", fmt.Sprintf("%s:%d", address.host, address.port))
	if err != nil {
		l = nil
	}
	return NewTcpServer(l)
}

func UdpCast(address CSocketAddress) UdpSocket {
	p, err := net.ListenPacket("udp", fmt.Sprintf("%s:%d", address.host, address.port))
	if err != nil {
		p = nil
	}
	return NewUdpSocket(p)
}
