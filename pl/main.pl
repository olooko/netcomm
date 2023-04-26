require "./xyz/olooko/comm/netcomm.pl";

use strict;
use warnings;
use threads;
use IO::Handle;
use Encode;

STDOUT->autoflush(1);

sub TcpServerAcceptCallback() {
    my $tcpsocket = shift;

    if ($tcpsocket->available) {
        printf("TcpClient Accepted. %s\n", $tcpsocket->remoteAddress->toString());
        $tcpsocket->setReceivedCallback(\&CSocketReceivedCallback);
    }
}

sub CSocketReceivedCallback() {
    my $socket = shift;
    my $data = shift;

    if ($data->result == CSocketReceivedDataResult->Completed) {
        if ($data->command == 0x88) {
            my $args = $data->args;
            my $a1 = $args->at(0)->toString();
            my $a2 = $args->at(1)->toString();
            my $a3 = $args->at(2)->toString();
            my $a4 = $args->at(3)->toString();
            my $a5 = $args->at(4)->toString();
            my $protocol = "";
            if ($socket->protocolType == CSocketProtocolType->Tcp) {
                $protocol = "TCP";
            } elsif ($socket->protocolType == CSocketProtocolType->Udp) {
                $protocol = "UDP";
            }

            my $string = decode("UTF-8", $a3);
            $a3 = encode("cp949", $string);

            printf("%s %s (%s, %s, %s, %s, [%s])\n", $protocol, $data->remoteAddress->toString(), $a1, $a2, $a3, $a4, $a5);           
        }
    }
    elsif ($data->result == CSocketReceivedDataResult->Interrupted) {
        print("Interrupted\n");
    }
    elsif ($data->result == CSocketReceivedDataResult->ParsingError) {
        print("Parsing-Error\n");
    }
    elsif ($data->result == CSocketReceivedDataResult->Closed) {
        print("Close\n");
        $socket->close();
    }
}

sub UdpSocketProc() {
    my $address = CSocketAddress->new("127.0.0.1", 10010);
    my $udpsocket = NetworkComm::UdpCast($address);

    if ($udpsocket->available)
    {
        printf("UdpSocket Started. %s\n", $udpsocket->localAddress->toString());
        $udpsocket->setReceivedCallback(\&CSocketReceivedCallback);

        my $args = CSocketDataArgs->new();
        $args->add(CInteger->new(-256));
        $args->add(CBoolean->new(1));
        $args->add(CString->new("Hello"));
        $args->add(CFloat->new(-1.1));

        my @bytearray = (0x41, 0x42, 0x43);
        $args->add(CByteArray->new(pack("C*", @bytearray)));

        my $data = CSocketSendData->new(0x88, $args);

        if ($data->buildResult == CSocketSendDataBuildResult->Successful) {
            while(1) {
                $udpsocket->send($data, $address);
                sleep(5);
            }
        }
    }
}

sub TcpServerProc() {
    my $tcpserver = NetworkComm::TcpListen(CSocketAddress->new("127.0.0.1", 10010));
    print("TcpServer Started.\n");

    if ($tcpserver->running) {
        $tcpserver->setAcceptCallback(\&TcpServerAcceptCallback);
    }
}

sub TcpClientProc() {
    my $tcpsocket = NetworkComm::TcpConnect(CSocketAddress->new("127.0.0.1", 10010));

    if ($tcpsocket->available) {
        printf("TcpClient Started. %s\n", $tcpsocket->localAddress->toString());
        $tcpsocket->setReceivedCallback(\&CSocketReceivedCallback);

        my $args = CSocketDataArgs->new();
        $args->add(CInteger->new(-256));
        $args->add(CBoolean->new(1));
        $args->add(CString->new("Hello"));
        $args->add(CFloat->new(-1.1));

        my @bytearray = (0x41, 0x42, 0x43);
        $args->add(CByteArray->new(pack("C*", @bytearray)));

        my $data = CSocketSendData->new(0x88, $args);

        if ($data->buildResult == CSocketSendDataBuildResult->Successful) {
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