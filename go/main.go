package main

import (
	"fmt"
	"time"

	"olooko.xyz/comm/netcomm"
)

func main() {

	go UdpSocketProc()

	time.Sleep(1 * time.Second)

	go TcpServerProc()

	time.Sleep(1 * time.Second)

	go TcpClientProc()

	fmt.Scanln()
}

func UdpSocketProc() {
	udpsocket := netcomm.UdpCast(netcomm.NewNetSocketAddress("127.0.0.1", 10010))

	if udpsocket.IsAvailable() {
		output := fmt.Sprintf("NetworkComm.UdpSocket Started. %s", udpsocket.GetLocalAddress().String())
		fmt.Println(output)
		udpsocket.SetReceivedCallback(NetSocketReceivedCallback)

		args := []interface{}{-256, true, "Hello", -1.1, []byte{0x41, 0x42, 0x43}}
		data := netcomm.NewNetSocketSendData(byte(0x88), args)

		if data.GetBuildResult() == netcomm.NetSocketSendDataBuildResult_Successful {
			for {
				udpsocket.Send(data, netcomm.NewNetSocketAddress("127.0.0.1", 10010))
				time.Sleep(5 * time.Second)
			}
		}
	}
}

func TcpServerProc() {
	tcpserver := netcomm.TcpListen(netcomm.NewNetSocketAddress("127.0.0.1", 10010))
	fmt.Println("NetworkComm.TcpServer Started.")

	if tcpserver.IsRunning() {
		tcpserver.SetAcceptCallback(TcpServerAcceptCallback)
	}
}

func TcpClientProc() {
	tcpsocket := netcomm.TcpConnect(netcomm.NewNetSocketAddress("127.0.0.1", 10010))

	if tcpsocket.IsAvailable() {
		output := fmt.Sprintf("NetworkComm.TcpClient Started. %s", tcpsocket.GetLocalAddress().String())
		fmt.Println(output)
		tcpsocket.SetReceivedCallback(NetSocketReceivedCallback)

		args := []interface{}{-256, true, "Hello", -1.1, []byte{0x41, 0x42, 0x43}}
		data := netcomm.NewNetSocketSendData(byte(0x88), args)

		if data.GetBuildResult() == netcomm.NetSocketSendDataBuildResult_Successful {
			for {
				if tcpsocket.IsConnected() {
					tcpsocket.Send(data)
				} else {
					break
				}
				time.Sleep(5 * time.Second)
			}
		}
	}
}

func TcpServerAcceptCallback(tcpsocket netcomm.TcpSocket) {
	if tcpsocket.IsAvailable() {
		output := fmt.Sprintf("NetworkComm.TcpClient Accepted. %s", tcpsocket.GetRemoteAddress().String())
		fmt.Println(output)
		tcpsocket.SetReceivedCallback(NetSocketReceivedCallback)
	}
}

func NetSocketReceivedCallback(socket netcomm.NetSocket, data netcomm.NetSocketReceivedData) {
	if data.GetResult() == netcomm.NetSocketReceivedDataResult_Completed {
		if data.GetCommand() == 0x88 {
			//a1, _ := strconv.ParseInt(fmt.Sprintf("%v", data.GetArgs()[0]), 0, 64)
			//a2 := data.GetArgs()[1].(bool)
			//a3 := data.GetArgs()[2].(string)
			//a4, _ := strconv.ParseFloat(fmt.Sprintf("%v", data.GetArgs()[3]), 64)
			a1 := data.GetArgs()[0]
			a2 := data.GetArgs()[1]
			a3 := data.GetArgs()[2]
			a4 := data.GetArgs()[3]

			a5 := ""
			ba := data.GetArgs()[4].([]byte)
			for i := 0; i < len(ba); i++ {
				if a5 != "" {
					a5 += ","
				}
				a5 += fmt.Sprintf("0x%02X", ba[i])
			}

			protocol := ""
			if socket.GetProtocolType() == netcomm.NetSocketProtocolType_Tcp {
				protocol = "TCP"
			} else if socket.GetProtocolType() == netcomm.NetSocketProtocolType_Udp {
				protocol = "UDP"
			}

			output := fmt.Sprintf("%s %s (%d, %t, %s, %f, [%s])",
				protocol, data.GetRemoteAddress().String(), a1, a2, a3, a4, a5)
			fmt.Println(output)
		}
	} else if data.GetResult() == netcomm.NetSocketReceivedDataResult_Interrupted {
		fmt.Println("Interrupted")
	} else if data.GetResult() == netcomm.NetSocketReceivedDataResult_ParsingError {
		fmt.Println("Parsing-Error")
	} else if data.GetResult() == netcomm.NetSocketReceivedDataResult_Closed {
		fmt.Println("Close")
		socket.Close()
	}
}
