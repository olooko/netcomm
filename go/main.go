package main

import (
	"fmt"
	"time"

	"olooko.xyz/comm/netcomm"
)

func main() {
	go TcpServerThread()
	go TcpClientThread()
	go UdpSocketThread()

	fmt.Scanln()
}

func TcpServerThread() {
	tcpserver := netcomm.TcpListen(netcomm.NewNetSocketAddress("127.0.0.1", 10010))
	fmt.Println("NetworkComm.TcpServer Started...")

	for tcpserver.IsStarted() {
		tcpsocket := tcpserver.Accept()

		if tcpsocket.IsAvailable() {
			fmt.Println("NetworkComm.TcpSocket Accepted")
			tcpsocket.SetReceivedCallback(NetSocketReceivedCallback)
		} else {
			break
		}
	}
	fmt.Println("NetworkComm.TcpServer Stopped")
}

func TcpClientThread() {
	tcpsocket := netcomm.TcpConnect(netcomm.NewNetSocketAddress("127.0.0.1", 10010))

	if tcpsocket.IsAvailable() {
		fmt.Println("NetworkComm.TcpSocket Started...")
		tcpsocket.SetReceivedCallback(NetSocketReceivedCallback)

		for {
			if tcpsocket.IsConnected() {
				args := []interface{}{-256, true, "Hello", -1.1, []byte{0x41, 0x42, 0x43}}
				data := netcomm.NewNetSocketSendData(byte(0x88), args)
				if data.GetBuildResult() == netcomm.NetSocketSendDataBuildResult_Successful {
					tcpsocket.Send(data)
				}
			} else {
				break
			}
			time.Sleep(5 * time.Second)
		}
	}
}

func UdpSocketThread() {
	udpsocket := netcomm.UdpCast(netcomm.NewNetSocketAddress("127.0.0.1", 10010))

	if udpsocket.IsAvailable() {
		fmt.Println("NetworkComm.UdpSocket Started...")
		udpsocket.SetReceivedCallback(NetSocketReceivedCallback)

		for {
			args := []interface{}{-256, true, "Hello", -1.1, []byte{0x41, 0x42, 0x43}}
			data := netcomm.NewNetSocketSendData(byte(0x88), args)

			if data.GetBuildResult() == netcomm.NetSocketSendDataBuildResult_Successful {
				udpsocket.Send(data, netcomm.NewNetSocketAddress("127.0.0.1", 10010))
			}
			time.Sleep(5 * time.Second)
		}
	}
}

func NetSocketReceivedCallback(socket netcomm.NetSocket, data netcomm.NetSocketReceivedData) {
	if data.GetResult() == netcomm.NetSocketReceivedDataResult_Completed {
		line := fmt.Sprintf("protocol: %d, command: 0x%02X, args: {%s}", socket.GetProtocolType(), data.GetCommand(), data.GetArgs())
		fmt.Println(line)
	} else if data.GetResult() == netcomm.NetSocketReceivedDataResult_Interrupted {
		fmt.Println("Interrupted")
	} else if data.GetResult() == netcomm.NetSocketReceivedDataResult_ParsingError {
		fmt.Println("parsing-error")
	} else if data.GetResult() == netcomm.NetSocketReceivedDataResult_Closed {
		fmt.Println("close")
		socket.Close()
	}
}
