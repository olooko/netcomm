Imports System.Threading
Imports comm.xyz.olooko.comm.netcomm

Module Program

    Sub Main()
        Dim thread1 As Thread = New Thread(New ThreadStart(AddressOf UdpSocketProc)) With {
            .IsBackground = True
        }
        thread1.Start()

        Thread.Sleep(1000)

        Dim thread2 As Thread = New Thread(New ThreadStart(AddressOf TcpServerProc)) With {
            .IsBackground = True
        }
        thread2.Start()

        Thread.Sleep(1000)

        Dim thread3 As Thread = New Thread(New ThreadStart(AddressOf TcpClientProc)) With {
            .IsBackground = True
        }
        thread3.Start()

        thread1.Join()
        thread2.Join()
        thread3.Join()
    End Sub

    Private Sub UdpSocketProc()
        Dim address As CSocketAddress = New CSocketAddress("127.0.0.1", 10010)
        Dim udpsocket As UdpSocket = NetworkComm.UdpCast(address)

        If udpsocket.Available Then
            Console.WriteLine(String.Format("UdpSocket Started. {0}", udpsocket.LocalAddress))
            udpsocket.SetReceivedCallback(AddressOf CSocketReceivedCallback)
            Dim args As CSocketDataArgs = New CSocketDataArgs()
            args.Add(New CInteger(-256))
            args.Add(New CBoolean(True))
            args.Add(New CString("Hello"))
            args.Add(New CFloat(-1.1))
            args.Add(New CByteArray(New Byte() {&H41, &H42, &H43}))
            Dim data As CSocketSendData = New CSocketSendData(&H88, args)

            If data.BuildResult = CSocketSendDataBuildResult.Successful Then

                While True
                    udpsocket.Send(data, address)
                    Thread.Sleep(5000)
                End While
            End If
        End If
    End Sub

    Private Sub TcpServerProc()
        Dim tcpserver As TcpServer = NetworkComm.TcpListen(New CSocketAddress("127.0.0.1", 10010))
        Console.WriteLine("TcpServer Started.")
        If tcpserver.Running Then tcpserver.SetAcceptCallback(AddressOf TcpServerAcceptCallback)
    End Sub

    Private Sub TcpClientProc()
        Dim tcpsocket As TcpSocket = NetworkComm.TcpConnect(New CSocketAddress("127.0.0.1", 10010))

        If tcpsocket.Available Then
            Console.WriteLine(String.Format("TcpClient Started. {0}", tcpsocket.LocalAddress))
            tcpsocket.SetReceivedCallback(AddressOf CSocketReceivedCallback)
            Dim args As CSocketDataArgs = New CSocketDataArgs()
            args.Add(New CInteger(-256))
            args.Add(New CBoolean(True))
            args.Add(New CString("Hello"))
            args.Add(New CFloat(-1.1))
            args.Add(New CByteArray(New Byte() {&H41, &H42, &H43}))
            Dim data As CSocketSendData = New CSocketSendData(&H88, args)

            If data.BuildResult = CSocketSendDataBuildResult.Successful Then

                While True

                    If tcpsocket.Connected Then
                        tcpsocket.Send(data)
                    Else
                        Exit While
                    End If

                    Thread.Sleep(5000)
                End While
            End If
        End If
    End Sub

    Private Sub TcpServerAcceptCallback(ByVal tcpsocket As TcpSocket)
        If tcpsocket.Available Then
            Console.WriteLine(String.Format("TcpClient Accepted. {0}", tcpsocket.RemoteAddress))
            tcpsocket.SetReceivedCallback(AddressOf CSocketReceivedCallback)
        End If
    End Sub

    Private Sub CSocketReceivedCallback(ByVal socket As CSocket, ByVal data As CSocketReceivedData)
        If data.Result = CSocketReceivedDataResult.Completed Then

            If data.Command = &H88 Then
                Dim a1 = TryCast(data.Args(0), CInteger)
                Dim a2 = TryCast(data.Args(1), CBoolean)
                Dim a3 = TryCast(data.Args(2), CString)
                Dim a4 = TryCast(data.Args(3), CFloat)
                Dim a5 = TryCast(data.Args.At(4), CByteArray)
                Dim protocol As String = String.Empty

                If socket.ProtocolType = CSocketProtocolType.Tcp Then
                    protocol = "TCP"
                ElseIf socket.ProtocolType = CSocketProtocolType.Udp Then
                    protocol = "UDP"
                End If

                Dim output As String = String.Format("{0} {1} ({2}, {3}, {4}, {5}, [{6}])", protocol, data.RemoteAddress, a1, a2, a3, a4, a5)
                Console.WriteLine(output)
            End If
        ElseIf data.Result = CSocketReceivedDataResult.Interrupted Then
            Console.WriteLine("Interrupted")
        ElseIf data.Result = CSocketReceivedDataResult.ParsingError Then
            Console.WriteLine("Parsing-Error")
        ElseIf data.Result = CSocketReceivedDataResult.Closed Then
            Console.WriteLine("Close")
            socket.Close()
        End If
    End Sub
End Module
