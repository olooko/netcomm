import 'package:comm/xyz/olooko/comm/netcomm.dart';
import 'dart:async';
import 'dart:io';
import 'dart:isolate';
import 'dart:typed_data';

void main(List<String> arguments) 
{
    Isolate.spawn<int>(UdpSocketProc, 1);

    sleep(const Duration(seconds:1));

    Isolate.spawn<int>(TcpServerProc, 2);

    sleep(const Duration(seconds:1));

    Isolate.spawn<int>(TcpClientProc, 3);

    var line = stdin.readLineSync();
}

void UdpSocketProc(int i) async 
{    
    CSocketAddress address = CSocketAddress('127.0.0.1', 10010);
    UdpSocket udpsocket = await NetworkComm.UdpCast(address);

    if (udpsocket.available) 
    {
        print('UdpSocket Started. ${udpsocket.localAddress}');
        udpsocket.setReceivedCallback(CSocketReceivedCallback);

        CSocketDataArgs args = CSocketDataArgs();
        args.add(CInteger(-256));
        args.add(CBoolean(true));
        args.add(CString('Hello'));
        args.add(CFloat(-1.1));
        args.add(CByteArray(Uint8List.fromList([0x41, 0x42, 0x43])));

        CSocketSendData data = CSocketSendData(0x88, args);

        if (data.buildResult == CSocketSendDataBuildResult.Successful) 
        {
            Timer.periodic(const Duration(seconds: 5), (timer) {
                udpsocket.send(data, address);
            });
        }
    }
}

void TcpServerProc(int i) async 
{
    TcpServer tcpserver = await NetworkComm.TcpListen(CSocketAddress('127.0.0.1', 10010));
    print('TcpServer Started.');

    if (tcpserver.running)
        tcpserver.setAcceptCallback(TcpServerAcceptCallback);
}

void TcpClientProc(int i) async 
{
    TcpSocket tcpsocket = await NetworkComm.TcpConnect(new CSocketAddress('127.0.0.1', 10010));

    if (tcpsocket.available)
    {
        print('TcpClient Started. ${tcpsocket.localAddress}');
        tcpsocket.setReceivedCallback(CSocketReceivedCallback);

        CSocketDataArgs args = CSocketDataArgs();
        args.add(CInteger(-256));
        args.add(CBoolean(true));
        args.add(CString('Hello'));
        args.add(CFloat(-1.1));
        args.add(CByteArray(Uint8List.fromList([0x41, 0x42, 0x43])));

        CSocketSendData data = CSocketSendData(0x88, args);

        if (data.buildResult == CSocketSendDataBuildResult.Successful)
        {
            Timer.periodic(const Duration(seconds: 5), (timer) {
                if (tcpsocket.connected) 
                    tcpsocket.send(data);
                else
                    timer.cancel();
            });
        }
    }
}

void TcpServerAcceptCallback(TcpSocket tcpsocket) 
{
    if (tcpsocket.available) 
    {
        print('TcpClient Accepted. ${tcpsocket.remoteAddress}');
        tcpsocket.setReceivedCallback(CSocketReceivedCallback);
    }
}

void CSocketReceivedCallback(CSocket socket, CSocketReceivedData data) 
{
    if (data.result == CSocketReceivedDataResult.Completed)
    {
        if (data.command == 0x88) 
        {
            var a1 = data.args[0];
            var a2 = data.args[1];
            var a3 = data.args[2];
            var a4 = data.args[3]; 
            var a5 = data.args.at(4);

            String protocol = "";
            if (socket.protocolType == CSocketProtocolType.Tcp)
                protocol = "TCP";
            else if (socket.protocolType == CSocketProtocolType.Udp)
                protocol = "UDP";

            print('${protocol} ${data.remoteAddress} (${a1}, ${a2}, ${a3}, ${a4}, [${a5}])');        
        }
    } 
    else if (data.result == CSocketReceivedDataResult.Interrupted) {
        print('Interrupted');
    } 
    else if (data.result == CSocketReceivedDataResult.ParsingError) {
        print('Parsing-Error');
    } 
    else if (data.result == CSocketReceivedDataResult.Closed) {
        print('Close');
        socket.close();
    }
}

