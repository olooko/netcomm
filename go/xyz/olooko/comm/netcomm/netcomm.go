package netcomm

import (
	"bytes"
	"encoding/binary"
	"errors"
	"fmt"
	"math"
	"net"
	"reflect"
	"strconv"
	"strings"
	"time"
)

const (
	INT_MAXVAL   = 2147483647
	FLOAT_MAXVAL = 3.40282347e+38
	BUF_SZ       = 4096
	INTRPT_TM    = 4000
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
	NetSocketDataManipulationResult_Completed = iota
	NetSocketDataManipulationResult_InProgress
	NetSocketDataManipulationResult_NoData
	NetSocketDataManipulationResult_ParsingError
)

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

func (d NetSocketData) getArgLength(data []byte, datalen int, datapos int) (size int, argLength int) {
	sz := int(data[datapos] & 0x0F)
	argL := -1

	if datalen > sz {
		switch sz {
		case 1:
			argL = int(data[datapos+1])
		case 2:
			argL = int(binary.LittleEndian.Uint16(data[datapos+1:]))
		case 4:
			argL = int(binary.LittleEndian.Uint32(data[datapos+1:]))
		}
	}
	return sz, argL
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
					sz := 0
					sz, d.textlen = d.getArgLength(d.data, datalen, d.datapos)
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
								d.args = append(d.args, int16(binary.LittleEndian.Uint16(d.data[d.datapos+1:])))
							case 4:
								d.args = append(d.args, int32(binary.LittleEndian.Uint32(d.data[d.datapos+1:])))
							case 8:
								d.args = append(d.args, int64(binary.LittleEndian.Uint64(d.data[d.datapos+1:])))
							}
						case 0x54, 0x58:
							sz = int(d.data[d.datapos] & 0x0F)
							switch sz {
							case 4:
								d.args = append(d.args, math.Float32frombits(binary.LittleEndian.Uint32(d.data[d.datapos+1:])))
							case 8:
								d.args = append(d.args, math.Float64frombits(binary.LittleEndian.Uint64(d.data[d.datapos+1:])))
							}
						case 0x71:
							sz = 1
							b := true
							if d.data[d.datapos+1] == 0 {
								b = false
							}
							d.args = append(d.args, b)
						case 0x91, 0x92, 0x94:
							argL := 0
							sz, argL = d.getArgLength(d.data, datalen, d.datapos)
							d.args = append(d.args, string(d.data[d.datapos+1+sz:d.datapos+1+sz+argL]))
							d.datapos += argL
						case 0xB1, 0xB2, 0xB4:
							argL := 0
							sz, argL = d.getArgLength(d.data, datalen, d.datapos)
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

const (
	NetSocketProtocolType_Tcp = iota
	NetSocketProtocolType_Udp
)

const (
	NetSocketReceivedDataResult_Closed = iota
	NetSocketReceivedDataResult_Completed
	NetSocketReceivedDataResult_Interrupted
	NetSocketReceivedDataResult_ParsingError
)

type NetSocketAddress struct {
	host string
	port int
}

func NewNetSocketAddress(host string, port int) NetSocketAddress {
	return NetSocketAddress{host, port}
}

func (d NetSocketAddress) GetHost() string {
	return d.host
}

func (d NetSocketAddress) GetPort() int {
	return d.port
}

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

type NetSocketSendData struct {
	command byte
	args    []interface{}
	bytes   []byte
}

func (d NetSocketSendData) GetArgs() []interface{} {
	return d.args
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

func NewNetSocketSendData(command byte, args []interface{}) (NetSocketSendData, error) {
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
			if -128 <= i && i <= 127 {
				// 0011 0001
				text.Write([]byte{byte(0x31)})
				textlen += 1
				binary.Write(&text, binary.LittleEndian, int8(i))
				textlen += 1
			} else if -32768 <= i && i <= 32767 {
				// 0011 0010
				text.Write([]byte{byte(0x32)})
				textlen += 1
				binary.Write(&text, binary.LittleEndian, int16(i))
				textlen += 2
			} else if -2147483648 <= i && i <= 2147483647 {
				// 0011 0100
				text.Write([]byte{byte(0x34)})
				textlen += 1
				binary.Write(&text, binary.LittleEndian, int32(i))
				textlen += 4
			} else {
				// 0011 1000
				text.Write([]byte{byte(0x38)})
				textlen += 1
				binary.Write(&text, binary.LittleEndian, int64(i))
				textlen += 8
			}

		case "float32", "float64":
			f, _ := strconv.ParseFloat(fmt.Sprintf("%v", arg), 64)
			if math.Abs(f) <= FLOAT_MAXVAL {
				// 0101 0100
				text.Write([]byte{byte(0x54)})
				textlen += 1
				binary.Write(&text, binary.LittleEndian, float32(f))
				textlen += 4
			} else {
				// 0101 1000
				text.Write([]byte{byte(0x58)})
				textlen += 1
				binary.Write(&text, binary.LittleEndian, f)
				textlen += 8
			}

		case "bool":
			text.Write([]byte{byte(0x71)})
			textlen += 1
			binary.Write(&text, binary.LittleEndian, arg.(bool))
			textlen += 1

		case "string":
			s := []byte(fmt.Sprintf("%v", arg))
			if len(s) <= INT_MAXVAL {
				if len(s) <= 0x7F {
					// 1001 0001
					text.Write([]byte{byte(0x91)})
					textlen += 1
					binary.Write(&text, binary.LittleEndian, int8(len(s)))
					textlen += 1
				} else if len(s) <= 0x7FFF {
					// 1001 0010
					text.Write([]byte{byte(0x92)})
					textlen += 1
					binary.Write(&text, binary.LittleEndian, int16(len(s)))
					textlen += 2
				} else if len(s) <= 0x7FFFFFFF {
					// 1001 0100
					text.Write([]byte{byte(0x94)})
					textlen += 1
					binary.Write(&text, binary.LittleEndian, int32(len(s)))
					textlen += 4
				}
				text.Write(s)
				textlen += len(s)
			} else {
				return NetSocketSendData{0x00, make([]interface{}, 0), []byte{}}, errors.New("string is too large")
			}

		case "[]byte", "[]uint8":
			b := arg.([]uint8)
			if len(b) <= INT_MAXVAL {
				if len(b) <= 0x7F {
					// 1011 0001
					text.Write([]byte{byte(0xB1)})
					textlen += 1
					binary.Write(&text, binary.LittleEndian, int8(len(b)))
					textlen += 1
				} else if len(b) <= 0x7FFF {
					// 1011 0010
					text.Write([]byte{byte(0xB2)})
					textlen += 1
					binary.Write(&text, binary.LittleEndian, int16(len(b)))
					textlen += 2
				} else if len(b) <= 0x7FFFFFFF {
					// 1011 0100
					text.Write([]byte{byte(0xB4)})
					textlen += 1
					binary.Write(&text, binary.LittleEndian, int32(len(b)))
					textlen += 4
				}
				text.Write(b)
				textlen += len(b)
			} else {
				return NetSocketSendData{0x00, make([]interface{}, 0), []byte{}}, errors.New("[]byte is too large")
			}

		default:
			errmsg := fmt.Sprintf("type %s is not implemented", argType)
			return NetSocketSendData{0x00, make([]interface{}, 0), []byte{}}, errors.New(errmsg)
		}
	}

	textbytes := text.Bytes()

	data := bytes.Buffer{}
	datapos := 0

	if textlen <= INT_MAXVAL {

		// start of header
		data.Write([]byte{byte(0x01)})
		datapos += 1

		if textlen <= 0x7F {
			// 0001 0001
			data.Write([]byte{byte(0x11)})
			datapos += 1
			binary.Write(&data, binary.LittleEndian, int8(textlen))
			datapos += 1
		} else if textlen <= 0x7FFF {
			// 0001 0010
			data.Write([]byte{byte(0x12)})
			datapos += 1
			binary.Write(&data, binary.LittleEndian, int16(textlen))
			datapos += 2
		} else if textlen <= 0x7FFFFFFF {
			// 0001 0100
			data.Write([]byte{byte(0x14)})
			datapos += 1
			binary.Write(&data, binary.LittleEndian, int32(textlen))
			datapos += 4
		}

		// start of text
		data.Write([]byte{byte(0x02)})
		datapos += 1

		data.Write(textbytes)
		datapos += textlen

		// end of text
		data.Write([]byte{byte(0x03)})
		datapos += 1

		// checksum of text
		checksum := byte(0x00)
		for i := 0; i < textlen; i++ {
			checksum ^= textbytes[i]
		}

		data.Write([]byte{byte(checksum)})
		datapos += 1

		// end of transmission
		data.Write([]byte{byte(0x04)})
		datapos += 1
	} else {
		return NetSocketSendData{0x00, make([]interface{}, 0), []byte{}}, errors.New("text is too large")
	}
	return NetSocketSendData{command, args, data.Bytes()}, nil
}

type NetSocket struct {
	conn         net.Conn
	packetConn   net.PacketConn
	buffer       []byte
	data         NetSocketData
	protocol     int
	localAddress NetSocketAddress
	connected    bool
	result       int
}

func NewNetSocket(c net.Conn, p net.PacketConn, protocol int) NetSocket {
	netsock := NetSocket{}
	netsock.buffer = make([]byte, BUF_SZ)
	netsock.data = NewNetSocketData()
	netsock.protocol = protocol

	if protocol == NetSocketProtocolType_Tcp {
		netsock.conn = c
		ss := strings.Split(c.LocalAddr().String(), ":")
		host := strings.Join(ss[:len(ss)-1], ":")
		port, _ := strconv.Atoi(ss[len(ss)-1])
		netsock.localAddress = NewNetSocketAddress(host, port)
	} else if protocol == NetSocketProtocolType_Udp {
		netsock.packetConn = p
		ss := strings.Split(p.LocalAddr().String(), ":")
		host := strings.Join(ss[:len(ss)-1], ":")
		port, _ := strconv.Atoi(ss[len(ss)-1])
		netsock.localAddress = NewNetSocketAddress(host, port)
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
	if s.protocol == NetSocketProtocolType_Tcp {
		s.conn.Close()
	} else if s.protocol == NetSocketProtocolType_Udp {
		s.packetConn.Close()
	}
}

func (s NetSocket) Send(data NetSocketSendData, address NetSocketAddress) {
	s.sendProc(data, address, 0)
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
		if bytesTransferred < len(data.bytes) {
			s.sendProc(data, address, bytesTransferred)
		}
	}
}

func (s NetSocket) SetReceivedCallback(callback func(NetSocket, NetSocketReceivedData)) {
	go s.receiveProc(callback)
}

func (s *NetSocket) receiveProc(callback func(NetSocket, NetSocketReceivedData)) {
	for {
		bytesTransferred := 0
		var remoteAddress NetSocketAddress

		if s.protocol == NetSocketProtocolType_Tcp {
			bytesTransferred, _ = s.conn.Read(s.buffer)
			if bytesTransferred > 0 {
				s.connected = true
			} else {
				s.connected = false
			}

			ss := strings.Split(s.conn.RemoteAddr().String(), ":")
			host := strings.Join(ss[:len(ss)-1], ":")
			port, _ := strconv.Atoi(ss[len(ss)-1])
			remoteAddress = NewNetSocketAddress(host, port)

		} else if s.protocol == NetSocketProtocolType_Udp {
			var address net.Addr
			bytesTransferred, address, _ = s.packetConn.ReadFrom(s.buffer)

			ss := strings.Split(address.String(), ":")
			host := strings.Join(ss[:len(ss)-1], ":")
			port, _ := strconv.Atoi(ss[len(ss)-1])
			remoteAddress = NewNetSocketAddress(host, port)
		}

		if bytesTransferred > 0 {
			s.data.Append(s.buffer, bytesTransferred)

			for {
				s.result = s.data.Manipulate()

				if s.result == NetSocketDataManipulationResult_Completed {
					callback(*s, NewNetSocketReceivedData(s.data.command, s.data.args, NetSocketReceivedDataResult_Completed, remoteAddress))
					continue
				} else if s.result == NetSocketDataManipulationResult_ParsingError {
					callback(*s, NewNetSocketReceivedData(0x00, make([]interface{}, 0), NetSocketReceivedDataResult_ParsingError, remoteAddress))
					return
				} else if s.result == NetSocketDataManipulationResult_InProgress {
					go s.checkInterruptedTimeout(INTRPT_TM, callback, remoteAddress)
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

func (s NetSocket) checkInterruptedTimeout(milliseconds int, callback func(NetSocket, NetSocketReceivedData), address NetSocketAddress) {
	time.Sleep(time.Duration(milliseconds) * time.Millisecond)

	if s.result == NetSocketDataManipulationResult_InProgress {
		callback(s, NewNetSocketReceivedData(0x00, make([]interface{}, 0), NetSocketReceivedDataResult_Interrupted, address))
	}
}

type TcpServer struct {
	server net.Listener
}

func NewTcpServer(l net.Listener) TcpServer {
	return TcpServer{l}
}

func (t TcpServer) IsStarted() bool {
	return t.server != nil
}

func (t TcpServer) Accept() TcpSocket {
	c, err := t.server.Accept()
	if err != nil {
		c = nil
	}
	return NewTcpSocket(c)
}

func (t *TcpServer) Close() {
	t.server.Close()
	t.server = nil
}

type TcpSocket struct {
	netsock       NetSocket
	remoteAddress NetSocketAddress
}

func NewTcpSocket(c net.Conn) TcpSocket {
	s := TcpSocket{}
	s.netsock = NewNetSocket(c, nil, NetSocketProtocolType_Tcp)
	s.netsock.connected = (c != nil)
	ss := strings.Split(c.RemoteAddr().String(), ":")
	host := strings.Join(ss[:len(ss)-1], ":")
	port, _ := strconv.Atoi(ss[len(ss)-1])
	s.remoteAddress = NewNetSocketAddress(host, port)

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
	s.netsock = NewNetSocket(nil, p, NetSocketProtocolType_Udp)
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
