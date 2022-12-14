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

type NetSocket struct {
	conn         net.Conn
	packetConn   net.PacketConn
	data         NetSocketData
	protocol     int //NetSocketProtocolType
	localAddress NetSocketAddress
	connected    bool
	result       int //NetSocketDataManipulationResult
}

func NewNetSocket(s interface{}, protocol int) NetSocket {
	netsock := NetSocket{}
	netsock.data = NewNetSocketData()
	netsock.protocol = protocol
	netsock.localAddress = NewNetSocketAddress("0.0.0.0", 0)

	if s != nil {
		if protocol == NetSocketProtocolType_Tcp {
			c := s.(net.Conn)
			netsock.conn = c
			if c != nil {
				netsock.localAddress = newNetSocketAddressFromString(c.LocalAddr().String())
			}
		} else if protocol == NetSocketProtocolType_Udp {
			p := s.(net.PacketConn)
			netsock.packetConn = p
			if p != nil {
				netsock.localAddress = newNetSocketAddressFromString(p.LocalAddr().String())
			}
		}
	}
	return netsock
}

func (s NetSocket) IsAvailable() bool {
	if s.protocol == NetSocketProtocolType_Tcp {
		return s.conn != nil
	} else if s.protocol == NetSocketProtocolType_Udp {
		return s.packetConn != nil
	}
	return false
}

func (s NetSocket) GetLocalAddress() NetSocketAddress {
	return s.localAddress
}

func (s NetSocket) GetProtocolType() int {
	return s.protocol
}

func (s NetSocket) Close() {
	if s.IsAvailable() {
		if s.protocol == NetSocketProtocolType_Tcp {
			s.conn.Close()
		} else if s.protocol == NetSocketProtocolType_Udp {
			s.packetConn.Close()
		}
	}
}

func (s NetSocket) SetReceivedCallback(callback func(NetSocket, NetSocketReceivedData)) {
	if s.IsAvailable() {
		go s.receiveProc(callback)
	}
}

func (s NetSocket) Send(data NetSocketSendData, address NetSocketAddress) {
	if s.IsAvailable() {
		s.sendProc(data, address, 0)
	}
}

func (s *NetSocket) sendProc(data NetSocketSendData, address NetSocketAddress, bytesTransferred int) {
	length := 0

	if s.protocol == NetSocketProtocolType_Tcp {
		length, _ = s.conn.Write(data.bytes[bytesTransferred:])
		if bytesTransferred > 0 {
			s.connected = true
		} else {
			s.connected = false
		}
	} else if s.protocol == NetSocketProtocolType_Udp {
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

func (s *NetSocket) receiveProc(callback func(NetSocket, NetSocketReceivedData)) {
	buffer := make([]byte, 4096)

	for {
		bytesTransferred := 0
		strAddr := "0.0.0.0:0"

		if s.protocol == NetSocketProtocolType_Tcp {
			bytesTransferred, _ = s.conn.Read(buffer)
			if bytesTransferred > 0 {
				s.connected = true
			} else {
				s.connected = false
			}
			strAddr = s.conn.RemoteAddr().String()

		} else if s.protocol == NetSocketProtocolType_Udp {
			var address net.Addr
			bytesTransferred, address, _ = s.packetConn.ReadFrom(buffer)
			strAddr = address.String()
		}

		remoteAddress := newNetSocketAddressFromString(strAddr)

		if bytesTransferred > 0 {
			s.data.Append(buffer, bytesTransferred)

			for {
				s.result = s.data.Manipulate()

				if s.result == NetSocketDataManipulationResult_Completed {
					callback(*s, NewNetSocketReceivedData(s.data.command, s.data.args, NetSocketReceivedDataResult_Completed, remoteAddress))
					continue
				} else if s.result == NetSocketDataManipulationResult_ParsingError {
					callback(*s, NewNetSocketReceivedData(0x00, make([]interface{}, 0), NetSocketReceivedDataResult_ParsingError, remoteAddress))
					return
				} else if s.result == NetSocketDataManipulationResult_InProgress {
					go checkInterruptedTimeout(s, 15000, callback, remoteAddress)
					break
				} else if s.result == NetSocketDataManipulationResult_NoData {
					break
				}
			}
			continue
		} else {
			callback(*s, NewNetSocketReceivedData(0x00, make([]interface{}, 0), NetSocketReceivedDataResult_Closed, remoteAddress))
			break
		}
	}
}

func checkInterruptedTimeout(s *NetSocket, milliseconds int, callback func(NetSocket, NetSocketReceivedData), address NetSocketAddress) {
	time.Sleep(time.Duration(milliseconds) * time.Millisecond)

	if s.result == NetSocketDataManipulationResult_InProgress {
		callback(*s, NewNetSocketReceivedData(0x00, make([]interface{}, 0), NetSocketReceivedDataResult_Interrupted, address))
	}
}

type NetSocketAddress struct {
	host string
	port int
}

func NewNetSocketAddress(host string, port int) NetSocketAddress {
	return NetSocketAddress{host, port}
}

func newNetSocketAddressFromString(address string) NetSocketAddress {
	ss := strings.Split(address, ":")
	host := strings.Join(ss[:len(ss)-1], ":")
	port, _ := strconv.Atoi(ss[len(ss)-1])
	return NetSocketAddress{host, port}
}

func (d NetSocketAddress) GetHost() string {
	return d.host
}

func (d NetSocketAddress) GetPort() int {
	return d.port
}

func (d NetSocketAddress) String() string {
	return fmt.Sprintf("%s:%d", d.GetHost(), d.GetPort())
}

type NetSocketData struct {
	command  byte
	args     []interface{}
	data     []byte
	datapos  int
	checksum byte
	step     int
	textlen  int
}

func NewNetSocketData() NetSocketData {
	data := NetSocketData{}
	data.command = 0x00
	data.args = make([]interface{}, 0)
	data.data = make([]byte, 0)
	data.datapos = 0
	data.checksum = 0x00
	data.step = NetSocketDataParsingStep_SOH
	data.textlen = 0
	return data
}

func (d NetSocketData) GetArgs() []interface{} {
	return d.args
}

func (d NetSocketData) GetCommand() byte {
	return d.command
}

func (d NetSocketData) getArgLength(datalen int) NetSocketDataArgLength {
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
	return NetSocketDataArgLength{sz, argL}
}

func (d *NetSocketData) Append(buffer []byte, bytesTransferred int) {
	d.data = append(d.data, buffer[:bytesTransferred]...)
}

func (d *NetSocketData) Manipulate() int {
	for {
		datalen := len(d.data) - d.datapos

		switch d.step {
		case NetSocketDataParsingStep_SOH:
			if datalen > 0 {
				if d.data[d.datapos] == 0x01 {
					d.datapos += 1
					d.step = NetSocketDataParsingStep_OTL
					continue
				} else {
					return NetSocketDataManipulationResult_ParsingError
				}
			}

		case NetSocketDataParsingStep_OTL:
			if datalen > 0 {
				switch d.data[d.datapos] {
				case 0x11, 0x12, 0x14:
					a := d.getArgLength(datalen)
					sz := a.GetSize()
					d.textlen = a.GetArgLength()
					if d.textlen >= 0 {
						d.datapos += 1 + sz
						d.step = NetSocketDataParsingStep_STX
						continue
					}
				default:
					return NetSocketDataManipulationResult_ParsingError
				}
			}

		case NetSocketDataParsingStep_STX:
			if datalen > 0 {
				if d.data[d.datapos] == 0x02 {
					d.datapos += 1
					d.step = NetSocketDataParsingStep_ETX
					continue
				} else {
					return NetSocketDataManipulationResult_ParsingError
				}
			}

		case NetSocketDataParsingStep_ETX:
			if datalen > d.textlen {
				if d.data[d.datapos+d.textlen] == 0x03 {
					textfpos := d.datapos
					d.command = d.data[textfpos]
					d.args = make([]interface{}, 0)
					d.datapos += 1

					for d.datapos < d.textlen+textfpos {
						sz := 0
						switch d.data[d.datapos] {
						case 0x31, 0x32, 0x34, 0x38:
							sz = int(d.data[d.datapos] & 0x0F)
							switch sz {
							case 1:
								d.args = append(d.args, int8(d.data[d.datapos+1]))
							case 2:
								d.args = append(d.args, int16(binary.BigEndian.Uint16(d.data[d.datapos+1:])))
							case 4:
								d.args = append(d.args, int32(binary.BigEndian.Uint32(d.data[d.datapos+1:])))
							case 8:
								d.args = append(d.args, int64(binary.BigEndian.Uint64(d.data[d.datapos+1:])))
							}
						case 0x54, 0x58:
							sz = int(d.data[d.datapos] & 0x0F)
							switch sz {
							case 4:
								d.args = append(d.args, math.Float32frombits(binary.BigEndian.Uint32(d.data[d.datapos+1:])))
							case 8:
								d.args = append(d.args, math.Float64frombits(binary.BigEndian.Uint64(d.data[d.datapos+1:])))
							}
						case 0x71:
							sz = 1
							b := true
							if d.data[d.datapos+1] == 0 {
								b = false
							}
							d.args = append(d.args, b)
						case 0x91, 0x92, 0x94:
							a := d.getArgLength(datalen)
							sz = a.GetSize()
							argL := a.GetArgLength()
							d.args = append(d.args, string(d.data[d.datapos+1+sz:d.datapos+1+sz+argL]))
							d.datapos += argL
						case 0xB1, 0xB2, 0xB4:
							a := d.getArgLength(datalen)
							sz = a.GetSize()
							argL := a.GetArgLength()
							ba := []byte{}
							ba = append(ba, d.data[d.datapos+1+sz:d.datapos+1+sz+argL]...)
							d.args = append(d.args, ba)
							d.datapos += argL
						default:
							return NetSocketDataManipulationResult_ParsingError
						}
						d.datapos += 1 + sz
					}

					d.checksum = 0x00
					for i := textfpos; i < textfpos+d.textlen; i++ {
						d.checksum ^= d.data[i]
					}

					d.datapos += 1
					d.step = NetSocketDataParsingStep_CHK
					continue
				} else {
					return NetSocketDataManipulationResult_ParsingError
				}
			}

		case NetSocketDataParsingStep_CHK:
			if datalen > 0 {
				if d.data[d.datapos] == d.checksum {
					d.datapos += 1
					d.step = NetSocketDataParsingStep_EOT
					continue
				} else {
					return NetSocketDataManipulationResult_ParsingError
				}
			}

		case NetSocketDataParsingStep_EOT:
			if datalen > 0 {
				if d.data[d.datapos] == 0x04 {
					d.datapos += 1
					d.data = d.data[d.datapos:]
					d.datapos = 0
					d.checksum = 0
					d.step = NetSocketDataParsingStep_SOH
					d.textlen = 0
					return NetSocketDataManipulationResult_Completed
				} else {
					return NetSocketDataManipulationResult_ParsingError
				}
			}
		}

		if len(d.data) == 0 {
			return NetSocketDataManipulationResult_NoData
		}
		return NetSocketDataManipulationResult_InProgress
	}
}

type NetSocketDataArgLength struct {
	sz   int
	argL int
}

func (a NetSocketDataArgLength) GetSize() int {
	return a.sz
}

func (a NetSocketDataArgLength) GetArgLength() int {
	return a.argL
}

const (
	NetSocketDataManipulationResult_Completed = iota
	NetSocketDataManipulationResult_InProgress
	NetSocketDataManipulationResult_NoData
	NetSocketDataManipulationResult_ParsingError
)

const (
	NetSocketDataParsingStep_SOH = iota
	NetSocketDataParsingStep_OTL
	NetSocketDataParsingStep_STX
	NetSocketDataParsingStep_ETX
	NetSocketDataParsingStep_CHK
	NetSocketDataParsingStep_EOT
)

const (
	NetSocketProtocolType_Tcp = iota
	NetSocketProtocolType_Udp
)

type NetSocketReceivedData struct {
	command byte
	args    []interface{}
	result  int
	address NetSocketAddress
}

func NewNetSocketReceivedData(command byte, args []interface{}, result int, address NetSocketAddress) NetSocketReceivedData {
	return NetSocketReceivedData{command, args, result, address}
}

func (d NetSocketReceivedData) GetArgs() []interface{} {
	return d.args
}

func (d NetSocketReceivedData) GetCommand() byte {
	return d.command
}

func (d NetSocketReceivedData) GetRemoteAddress() NetSocketAddress {
	return d.address
}

func (d NetSocketReceivedData) GetResult() int {
	return d.result
}

const (
	NetSocketReceivedDataResult_Closed = iota
	NetSocketReceivedDataResult_Completed
	NetSocketReceivedDataResult_Interrupted
	NetSocketReceivedDataResult_ParsingError
)

type NetSocketSendData struct {
	command byte
	args    []interface{}
	bytes   []byte
	result  int
}

func (d NetSocketSendData) GetArgs() []interface{} {
	return d.args
}

func (d NetSocketSendData) GetBuildResult() int {
	return d.result
}

func (d NetSocketSendData) GetBytes() []interface{} {
	return d.args
}

func (d NetSocketSendData) GetCommand() byte {
	return d.command
}

func (d NetSocketSendData) GetLength() int {
	return len(d.bytes)
}

func NewNetSocketSendData(command byte, args []interface{}) NetSocketSendData {
	ARG_MAXLEN := 0x7FFFFF - 5
	TXT_MAXLEN := math.MaxInt32 - 10

	senddata := NetSocketSendData{}
	senddata.command = 0x00
	senddata.args = make([]interface{}, 0)
	senddata.bytes = []byte{}
	senddata.result = NetSocketSendDataBuildResult_NoData

	//if command < 0x00 || command > 0xFF {
	//	senddata.result = NetSocketSendDataBuildResult_CommandValueOverflowError
	//	return senddata
	//}

	text := bytes.Buffer{}
	textlen := 0

	text.Write([]byte{command})
	textlen += 1

	for x := 0; x < len(args); x++ {
		arg := args[x]
		argType := reflect.TypeOf(arg).String()

		switch argType {
		case "int", "int8", "int16", "int32", "int64", "uint8", "uint16", "uint32", "uint64":
			i, _ := strconv.ParseInt(fmt.Sprintf("%v", arg), 0, 64)
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

		case "float32", "float64":
			f, _ := strconv.ParseFloat(fmt.Sprintf("%v", arg), 64)
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

		case "bool":
			text.Write([]byte{byte(0x71)})
			textlen += 1
			binary.Write(&text, binary.BigEndian, arg.(bool))
			textlen += 1

		case "string":
			s := []byte(fmt.Sprintf("%v", arg))
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
				senddata.result = NetSocketSendDataBuildResult_StringLengthOverflowError
				return senddata
			}

		case "[]byte", "[]uint8":
			b := arg.([]uint8)
			if len(b) <= ARG_MAXLEN {
				if len(b) <= math.MaxInt8 {
					text.Write([]byte{byte(0xB1)})
					textlen += 1
					binary.Write(&text, binary.BigEndian, int8(len(b)))
					textlen += 1
				} else if len(b) <= math.MaxInt16 {
					text.Write([]byte{byte(0xB2)})
					textlen += 1
					binary.Write(&text, binary.BigEndian, int16(len(b)))
					textlen += 2
				} else {
					text.Write([]byte{byte(0xB4)})
					textlen += 1
					binary.Write(&text, binary.BigEndian, int32(len(b)))
					textlen += 4
				}
				text.Write(b)
				textlen += len(b)
			} else {
				senddata.result = NetSocketSendDataBuildResult_ByteArrayLengthOverflowError
				return senddata
			}

		default:
			senddata.result = NetSocketSendDataBuildResult_DataTypeNotImplementedError
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
		senddata.result = NetSocketSendDataBuildResult_DataTotalLengthOverflowError
		return senddata
	}

	senddata.command = command
	senddata.args = args
	senddata.bytes = data.Bytes()
	senddata.result = NetSocketSendDataBuildResult_Successful
	return senddata
}

const (
	NetSocketSendDataBuildResult_ByteArrayLengthOverflowError = iota
	NetSocketSendDataBuildResult_CommandValueOverflowError
	NetSocketSendDataBuildResult_DataTotalLengthOverflowError
	NetSocketSendDataBuildResult_DataTypeNotImplementedError
	NetSocketSendDataBuildResult_NoData
	NetSocketSendDataBuildResult_StringLengthOverflowError
	NetSocketSendDataBuildResult_Successful
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
	netsock       NetSocket
	remoteAddress NetSocketAddress
}

func NewTcpSocket(c net.Conn) TcpSocket {
	s := TcpSocket{}
	s.netsock = NewNetSocket(c, NetSocketProtocolType_Tcp)
	s.netsock.connected = (c != nil)
	s.remoteAddress = NewNetSocketAddress("0.0.0.0", 0)
	if c != nil {
		ss := strings.Split(c.RemoteAddr().String(), ":")
		host := strings.Join(ss[:len(ss)-1], ":")
		port, _ := strconv.Atoi(ss[len(ss)-1])
		s.remoteAddress = NewNetSocketAddress(host, port)
	}
	return s
}

func (t TcpSocket) IsAvailable() bool {
	return t.netsock.IsAvailable()
}

func (s TcpSocket) IsConnected() bool {
	return s.IsAvailable() && s.netsock.connected
}

func (t TcpSocket) GetLocalAddress() NetSocketAddress {
	return t.netsock.GetLocalAddress()
}

func (t TcpSocket) GetProtocolType() int {
	return t.netsock.GetProtocolType()
}

func (t TcpSocket) GetRemoteAddress() NetSocketAddress {
	return t.remoteAddress
}

func (t TcpSocket) Close() {
	t.netsock.Close()
}

func (t TcpSocket) SetReceivedCallback(callback func(NetSocket, NetSocketReceivedData)) {
	t.netsock.SetReceivedCallback(callback)
}

func (t TcpSocket) Send(data NetSocketSendData) {
	t.netsock.Send(data, NetSocketAddress{})
}

type UdpSocket struct {
	netsock NetSocket
}

func NewUdpSocket(p net.PacketConn) UdpSocket {
	s := UdpSocket{}
	s.netsock = NewNetSocket(p, NetSocketProtocolType_Udp)
	return s
}

func (u UdpSocket) IsAvailable() bool {
	return u.netsock.IsAvailable()
}

func (u UdpSocket) GetLocalAddress() NetSocketAddress {
	return u.netsock.GetLocalAddress()
}

func (u UdpSocket) GetProtocolType() int {
	return u.netsock.GetProtocolType()
}

func (u UdpSocket) Close() {
	u.netsock.Close()
}

func (u UdpSocket) SetReceivedCallback(callback func(NetSocket, NetSocketReceivedData)) {
	u.netsock.SetReceivedCallback(callback)
}

func (u UdpSocket) Send(data NetSocketSendData, address NetSocketAddress) {
	u.netsock.Send(data, address)
}

func TcpConnect(address NetSocketAddress) TcpSocket {
	c, err := net.Dial("tcp", fmt.Sprintf("%s:%d", address.host, address.port))
	if err != nil {
		c = nil
	}
	return NewTcpSocket(c)
}

func TcpListen(address NetSocketAddress) TcpServer {
	l, err := net.Listen("tcp", fmt.Sprintf("%s:%d", address.host, address.port))
	if err != nil {
		l = nil
	}
	return NewTcpServer(l)
}

func UdpCast(address NetSocketAddress) UdpSocket {
	p, err := net.ListenPacket("udp", fmt.Sprintf("%s:%d", address.host, address.port))
	if err != nil {
		p = nil
	}
	return NewUdpSocket(p)
}
