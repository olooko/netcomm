import 'package:comm/xyz/olooko/comm/netcomm.dart';
import 'dart:async';
import 'dart:io';
import 'dart:isolate';
import 'dart:typed_data';

void main(List<String> arguments) 
{
    Isolate.spawn<int>(TcpServerProc, 1);
    Isolate.spawn<int>(TcpClientProc, 2);
    Isolate.spawn<int>(UdpSocketProc, 3);

    var line = stdin.readLineSync();
}

void TcpServerProc(int i) async
{
    TcpServer tcpserver = await TcpListen(NetSocketAddress('127.0.0.1', 10010));
    print('NetworkComm.TcpServer Started...');

    if (tcpserver.started)
        tcpserver.setAcceptCallback(TcpServerAcceptCallback);
}

void TcpClientProc(int i) async
{
    TcpSocket tcpsocket = await TcpConnect(new NetSocketAddress('127.0.0.1', 10010));

    if (tcpsocket.available)
    {
        print('NetworkComm.TcpSocket Started...');
        tcpsocket.setReceivedCallback(NetSocketReceivedCallback);

        Timer.periodic(const Duration(seconds: 5), (timer) {
            if (tcpsocket.connected)
            {
                List<Object> args = [];
                args.add(-256);
                args.add(true);
                args.add('Hello');
                args.add(-1.1);
                args.add(Uint8List.fromList([0x41, 0x42, 0x43]));

                NetSocketSendData data = NetSocketSendData(0x88, args);

                if (data.buildResult == NetSocketSendDataBuildResult.Successful)
                    tcpsocket.send(data);
            }
        });
    }
}

void UdpSocketProc(int i) async
{    
    UdpSocket udpsocket = await UdpCast(NetSocketAddress('127.0.0.1', 10010));

    if (udpsocket.available)
    {
        print('NetworkComm.UdpSocket Started...');
        udpsocket.setReceivedCallback(NetSocketReceivedCallback);

        Timer.periodic(const Duration(seconds: 5), (timer) {
            List<Object> args = [];
            args.add(-256);
            args.add(true);
            args.add('Hello');
            args.add(-1.1);
            args.add(Uint8List.fromList([0x41, 0x42, 0x43]));

            NetSocketSendData data = NetSocketSendData(0x88, args);

            if (data.buildResult == NetSocketSendDataBuildResult.Successful)
                udpsocket.send(data, NetSocketAddress('127.0.0.1', 10010));
        });
    }
}

void TcpServerAcceptCallback(TcpSocket tcpsocket)
{
    if (tcpsocket.available)
    {
        print('NetworkComm.TcpSocket Accepted');
        tcpsocket.setReceivedCallback(NetSocketReceivedCallback);
    }
}

void NetSocketReceivedCallback(NetSocket socket, NetSocketReceivedData data) 
{
    if (data.result == NetSocketReceivedDataResult.Completed) 
    {
        print('protocol: ${socket.protocolType}, command: ${data.command}, args: ${data.args}');
    }
    else if (data.result == NetSocketReceivedDataResult.Interrupted)
    {
        print('Interrupted');
    }
    else if (data.result == NetSocketReceivedDataResult.ParsingError) 
    {
        print('parsing-error');
    }
    else if (data.result == NetSocketReceivedDataResult.Closed) 
    {
        print('close');
        socket.close();
    }
}

