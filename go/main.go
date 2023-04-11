package main

import (
	"fmt"
	"time"

	nc "olooko.xyz/comm/netcomm"
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
	address := nc.NewCSocketAddress("127.0.0.1", 10010)
	udpsocket := nc.UdpCast(address)

	if udpsocket.IsAvailable() {
		output := fmt.Sprintf("UdpSocket Started. %s", udpsocket.GetLocalAddress().String())
		fmt.Println(output)
		udpsocket.SetReceivedCallback(CSocketReceivedCallback)

		args := nc.NewCSocketDataArgs()

		args.Add(nc.NewCInteger(-256))
		args.Add(nc.NewCBoolean(true))
		args.Add(nc.NewCString("Hello"))
		args.Add(nc.NewCFloat(-1.1))
		args.Add(nc.NewCByteArray([]byte{0x41, 0x42, 0x43}))

		data := nc.NewCSocketSendData(byte(0x88), args)

		if data.GetBuildResult() == nc.CSocketSendDataBuildResult_Successful {
			for {
				udpsocket.Send(data, address)
				time.Sleep(5 * time.Second)
			}
		}
	}
}

func TcpServerProc() {
	tcpserver := nc.TcpListen(nc.NewCSocketAddress("127.0.0.1", 10010))
	fmt.Println("TcpServer Started.")

	if tcpserver.IsRunning() {
		tcpserver.SetAcceptCallback(TcpServerAcceptCallback)
	}
}

func TcpClientProc() {
	tcpsocket := nc.TcpConnect(nc.NewCSocketAddress("127.0.0.1", 10010))

	if tcpsocket.IsAvailable() {
		output := fmt.Sprintf("TcpClient Started. %s", tcpsocket.GetLocalAddress().String())
		fmt.Println(output)
		tcpsocket.SetReceivedCallback(CSocketReceivedCallback)

		args := nc.NewCSocketDataArgs()

		args.Add(nc.NewCInteger(-256))
		args.Add(nc.NewCBoolean(true))
		args.Add(nc.NewCString("Hello"))
		args.Add(nc.NewCFloat(-1.1))
		args.Add(nc.NewCByteArray([]byte{0x41, 0x42, 0x43}))

		data := nc.NewCSocketSendData(byte(0x88), args)

		if data.GetBuildResult() == nc.CSocketSendDataBuildResult_Successful {
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

func TcpServerAcceptCallback(tcpsocket nc.TcpSocket) {
	if tcpsocket.IsAvailable() {
		output := fmt.Sprintf("TcpClient Accepted. %s", tcpsocket.GetRemoteAddress().String())
		fmt.Println(output)
		tcpsocket.SetReceivedCallback(CSocketReceivedCallback)
	}
}

func CSocketReceivedCallback(socket nc.CSocket, data nc.CSocketReceivedData) {
	if data.GetResult() == nc.CSocketReceivedDataResult_Completed {
		if data.GetCommand() == 0x88 {
			args := data.GetArgs()
			a1 := args.At(0)
			a2 := args.At(1)
			a3 := args.At(2)
			a4 := args.At(3)
			a5 := args.At(4)

			protocol := ""
			if socket.GetProtocolType() == nc.CSocketProtocolType_Tcp {
				protocol = "TCP"
			} else if socket.GetProtocolType() == nc.CSocketProtocolType_Udp {
				protocol = "UDP"
			}

			output := fmt.Sprintf("%s %s (%s, %s, %s, %s, [%s])",
				protocol, data.GetRemoteAddress().String(), a1, a2, a3, a4, a5)

			fmt.Println(output)
		}
	} else if data.GetResult() == nc.CSocketReceivedDataResult_Interrupted {
		fmt.Println("Interrupted")
	} else if data.GetResult() == nc.CSocketReceivedDataResult_ParsingError {
		fmt.Println("Parsing-Error")
	} else if data.GetResult() == nc.CSocketReceivedDataResult_Closed {
		fmt.Println("Close")
		socket.Close()
	}
}
