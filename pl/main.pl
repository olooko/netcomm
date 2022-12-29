require "./xyz/olooko/comm/netcomm.pl";

use strict;
use warnings;
use threads;
use IO::Handle;


STDOUT->autoflush(1);


sub TcpServerAcceptCallback() {
    my $tcpsocket = shift;

    if ($tcpsocket->available) {
        printf("NetworkComm.TcpClient Accepted. %s\n", $tcpsocket->remoteAddress->toString());
        $tcpsocket->setReceivedCallback(\&NetSocketReceivedCallback);
    }
}

sub NetSocketReceivedCallback() {
    my $socket = shift;
    my $data = shift;

    if ($data->result == NetSocketReceivedDataResult->Completed) {
        if ($data->command == 0x88) {
            my @args = @{ $data->args };
            my $a1 = $args[0];
            my $a2 = $args[1];
            my $a3 = $args[2];
            my $a4 = $args[3];
            my $a5 = "";
            my @ba = unpack("C*", $args[4]);
            for (my $i = 0; $i < length($args[4]); $i++) {
                if ($a5 ne "") {
                    $a5 .= ",";
                }
                $a5 .= sprintf("0x%02X", $ba[$i]);
            }
            my $protocol = "";
            if ($socket->protocolType == NetSocketProtocolType->Tcp) {
                $protocol = "TCP";
            } elsif ($socket->protocolType == NetSocketProtocolType->Udp) {
                $protocol = "UDP";
            }
            printf("%s %s (%d, %d, %s, %f, [%s])\n", $protocol, $data->remoteAddress->toString(), $a1, $a2, $a3, $a4, $a5);           
        }
    }
    elsif ($data->result == NetSocketReceivedDataResult->Interrupted) {
        print("Interrupted\n");
    }
    elsif ($data->result == NetSocketReceivedDataResult->ParsingError) {
        print("Parsing-Error\n");
    }
    elsif ($data->result == NetSocketReceivedDataResult->Closed) {
        print("Close\n");
        $socket->close();
    }
}

sub UdpSocketProc() {
    my $udpsocket = NetworkComm::UdpCast(NetSocketAddress->new("127.0.0.1", 10010));

    if ($udpsocket->available)
    {
        printf("NetworkComm.UdpSocket Started. %s\n", $udpsocket->localAddress->toString());
        $udpsocket->setReceivedCallback(\&NetSocketReceivedCallback);

        my @bytearray = (0x41, 0x42, 0x43);
        my @args = (-256, 1, "Hello", -1.1, pack("C*", @bytearray));
        my $data = NetSocketSendData->new(0x88, \@args);

        if ($data->buildResult == NetSocketSendDataBuildResult->Successful) {
            while(1) {
                $udpsocket->send($data, NetSocketAddress->new("127.0.0.1", 10010));
                sleep(5);
            }
        }
    }
}

sub TcpServerProc() {
    my $tcpserver = NetworkComm::TcpListen(NetSocketAddress->new("127.0.0.1", 10010));
    print("NetworkComm.TcpServer Started.\n");

    if ($tcpserver->running) {
        $tcpserver->setAcceptCallback(\&TcpServerAcceptCallback);
    }
}

sub TcpClientProc() {
    my $tcpsocket = NetworkComm::TcpConnect(NetSocketAddress->new("127.0.0.1", 10010));

    if ($tcpsocket->available) {
        printf("NetworkComm.TcpClient Started. %s\n", $tcpsocket->localAddress->toString());
        $tcpsocket->setReceivedCallback(\&NetSocketReceivedCallback);

        my @bytearray = (0x41, 0x42, 0x43);
        my @args = (-256, 1, "Hello", -1.1, pack("C*", @bytearray));
        my $data = NetSocketSendData->new(0x88, \@args);

        if ($data->buildResult == NetSocketSendDataBuildResult->Successful) {
            while(1) {
                if ($tcpsocket->connected) {
                    $tcpsocket->send($data);
                } else {
                    last;
                }
                sleep(5);              
            }
        }
    }
}

my $thread1 = threads->create(\&UdpSocketProc);

sleep(1);

my $thread2 = threads->create(\&TcpServerProc);

sleep(1);

my $thread3 = threads->create(\&TcpClientProc);

$thread1->join();
$thread2->join();
$thread3->join();