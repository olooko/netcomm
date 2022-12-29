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
    UdpSocket udpsocket = await UdpCast(NetSocketAddress('127.0.0.1', 10010));

    if (udpsocket.available) 
    {
        print('NetworkComm.UdpSocket Started. ${udpsocket.localAddress}');
        udpsocket.setReceivedCallback(NetSocketReceivedCallback);

        List<Object> args = [];
        args.add(-256);
        args.add(true);
        args.add('Hello');
        args.add(-1.1);
        args.add(Uint8List.fromList([0x41, 0x42, 0x43]));

        NetSocketSendData data = NetSocketSendData(0x88, args);

        if (data.buildResult == NetSocketSendDataBuildResult.Successful) 
        {
            Timer.periodic(const Duration(seconds: 5), (timer) {
                udpsocket.send(data, NetSocketAddress('127.0.0.1', 10010));
            });
        }
    }
}

void TcpServerProc(int i) async 
{
    TcpServer tcpserver = await TcpListen(NetSocketAddress('127.0.0.1', 10010));
    print('NetworkComm.TcpServer Started.');

    if (tcpserver.running)
        tcpserver.setAcceptCallback(TcpServerAcceptCallback);
}

void TcpClientProc(int i) async 
{
    TcpSocket tcpsocket = await TcpConnect(new NetSocketAddress('127.0.0.1', 10010));

    if (tcpsocket.available)
    {
        print('NetworkComm.TcpClient Started. ${tcpsocket.localAddress}');
        tcpsocket.setReceivedCallback(NetSocketReceivedCallback);

        List<Object> args = [];
        args.add(-256);
        args.add(true);
        args.add('Hello');
        args.add(-1.1);
        args.add(Uint8List.fromList([0x41, 0x42, 0x43]));

        NetSocketSendData data = NetSocketSendData(0x88, args);

        if (data.buildResult == NetSocketSendDataBuildResult.Successful)
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
        print('NetworkComm.TcpClient Accepted. ${tcpsocket.remoteAddress}');
        tcpsocket.setReceivedCallback(NetSocketReceivedCallback);
    }
}

void NetSocketReceivedCallback(NetSocket socket, NetSocketReceivedData data) 
{
    if (data.result == NetSocketReceivedDataResult.Completed)
    {
        if (data.command == 0x88) 
        {
            //int a1 = data.args[0] as int;
            //bool a2 = data.args[1] as bool;
            //String a3 = data.args[2] as String;
            //double a4 = data.args[3] as double;  
            var a1 = data.args[0];
            var a2 = data.args[1];
            var a3 = data.args[2];
            var a4 = data.args[3]; 

            String a5 = "";
            Uint8List ba = data.args[4] as Uint8List;
            ba.forEach((b) {
                if (a5 != "") a5 += ",";
                a5 += "0x" + b.toRadixString(16);
            });

            String protocol = "";
            if (socket.protocolType == NetSocketProtocolType.Tcp)
                protocol = "TCP";
            else if (socket.protocolType == NetSocketProtocolType.Udp)
                protocol = "UDP";

            print('${protocol} ${data.remoteAddress} (${a1}, ${a2}, ${a3}, ${a4}, [${a5}])');        
        }
    } 
    else if (data.result == NetSocketReceivedDataResult.Interrupted) {
        print('Interrupted');
    } 
    else if (data.result == NetSocketReceivedDataResult.ParsingError) {
        print('Parsing-Error');
    } 
    else if (data.result == NetSocketReceivedDataResult.Closed) {
        print('Close');
        socket.close();
    }
}

