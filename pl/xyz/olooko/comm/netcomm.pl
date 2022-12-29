my @empty = ();


package NetSocket;
use strict;
use warnings;
use threads;
use IO::Select;
use IO::Socket;

sub new {
    my $class = shift;
    my $self = {
        socket => shift,
        protocol => shift,
        data => NetSocketData->new(),
        result => NetSocketDataManipulationResult->NoData,
        localAddress => NetSocketAddress->new("0,0.0.0", 0),
    };
    bless $self, $class;

    $self->{select} = IO::Select->new();
    $self->{select}->add($self->{socket});

    if ($self->available) {
        my $linfo = $self->{socket}->sockname();
        (my $port, my $host) = sockaddr_in($linfo);
        $self->{localAddress} = NetSocketAddress->new(inet_ntoa($host), $port);       
    }

    $self->{sendProc} = sub {
        my $data = shift;
        my $address = shift;
        my $bytesTransferred = shift;

        my $length = 0;
        my $buffer = substr($data->bytes, $bytesTransferred);

        if ($self->{protocol} == NetSocketProtocolType->Tcp) {
            $length = $self->{socket}->send($buffer, 0);
        }
        elsif ($self->{protocol} == NetSocketProtocolType->Udp) {
            $length = $self->{socket}->send($buffer, 0, pack_sockaddr_in($address->port, inet_aton($address->host)));
        }

        if ($length > 0) {
            $bytesTransferred += $length;
            if ($bytesTransferred < $data->length) {
                $self->{sendProc}($data, $address, $bytesTransferred);
            }
        }
    };

    return $self;
}

sub available {
    my $self = shift;
    return defined($self->{socket});
}

sub localAddress {
    my $self = shift;
    return $self->{localAddress};
}

sub protocolType {
    my $self = shift;    
    return $self->{protocol};
}

sub close() {
    my $self = shift;   
    if ($self->available) {
        $self->{socket}->close();
    }
}

sub setReceivedCallback() {
    my $self = shift;
    my $callback = shift;

    my $receiveProc = sub {
        my $self = shift;
        my $callback = shift;

        my $checkInterruptedTimeout = sub {
            my $self = shift;          
            my $milliseconds = shift;
            my $callback = shift;
            my $address = shift;

            my $starttime = time();
            my $endtime = $starttime;

            while (1) {
                $endtime = time();
                if (($endtime - $starttime) * 1000 > $milliseconds) {
                    if ($self->{result} == NetSocketDataManipulationResult->InProgress) {
                        $callback->($self, NetSocketReceivedData->new(0x00, \@empty, NetSocketReceivedDataResult->Interrupted, $address));
                        last;
                    } 
                } else {
                    next;
                }
            }
        };

        while (1) {
            my @ready = $self->{select}->can_read(0);
            my $ready = @ready;
            if ($ready == 0) {
                next;
            }

            my $rinfo = $self->{socket}->recv(my $buffer, 4096, 0.001);
            (my $port, my $host) = sockaddr_in($rinfo);      

            my $remoteAddress = NetSocketAddress->new(inet_ntoa($host), $port);

            if (length($buffer) > 0) {
                $self->{data}->append($buffer);

                while (1) {
                    $self->{result} = $self->{data}->manipulate();

                    if ($self->{result} == NetSocketDataManipulationResult->Completed) {
                        my @args = @{ $self->{data}->args };
                        $callback->($self, NetSocketReceivedData->new($self->{data}->command, \@args, NetSocketReceivedDataResult->Completed, $remoteAddress));
                        next;
                    }
                    elsif ($self->{result} == NetSocketDataManipulationResult->ParsingError) {
                        $callback->($self, NetSocketReceivedData->new(0x00, \@empty, NetSocketReceivedDataResult->ParsingError, $remoteAddress));
                        return;
                    }
                    elsif ($self->{result} == NetSocketDataManipulationResult->InProgress) {
                        threads->create(\&$checkInterruptedTimeout, $self, 15000, $callback, $remoteAddress);
                        last;
                    }
                    elsif ($self->{result} == NetSocketDataManipulationResult->NoData) {
                        last;
                    }
                }
                next;
            }
            else {
                $callback->($self, NetSocketReceivedData->new(0x00, \@empty, NetSocketReceivedDataResult->Closed, $remoteAddress));
                return;
            }
        }
    };

    if ($self->available) {
        threads->create(\&$receiveProc, $self, $callback);
    }
}

sub _send() {
    my $self = shift;
    my $data = shift;
    my $address = shift;

    if ($self->available) {
        $self->{sendProc}($data, $address, 0);
    }
}


package NetSocketAddress;

sub new() {
    my $class = shift;
    my $self = {
        host => shift,
        port => shift,
    };
    bless $self, $class; 
    return $self;    
}

sub host {
    my $self = shift;
    return $self->{host};
}

sub port {
    my $self = shift;
    return $self->{port};
}

sub toString() {
    my $self = shift;
    return sprintf("%s:%d", $self->{host}, $self->{port});
}


package NetSocketData;
use Encode;

sub new() {
    my $class = shift;
    my $self = {
        command => 0x00,
        args => \@empty,
        data => "",
        datapos => 0,
        checksum => 0x00,
        step => NetSocketDataParsingStep->SOH,
        textlen => 0,
    };
    bless($self, $class); 

    $self->{getArgLength} = sub {
        my $datalen = shift;
        my $argL = -1;
        my $argH = unpack("C", substr($self->{data}, $self->{datapos}, 1));
        my $sz = $argH & 0x0F;
        my $fmt = "";

        if ($sz == 1) { 
            $fmt = "c";
        } elsif ($sz == 2) { 
            $fmt = "s>";
        } elsif ($sz == 4) { 
            $fmt = "l>";
        }
        
        if ($datalen > $sz) {
            $argL = unpack($fmt, substr($self->{data}, $self->{datapos} + 1, $sz));
        }
        return NetSocketDataArgLength->new($sz, $argL);
    };

    return $self;    
}

sub args {
    my $self = shift;
    return $self->{args};
}

sub command {
    my $self = shift;
    return $self->{command};
}

sub append() {
    my $self = shift;
    my $buffer = shift;

    $self->{data} .= $buffer;
}

sub manipulate() {
    my $self = shift;

    while (1) {
        my $datalen = length($self->{data}) - $self->{datapos};

        if ($self->{step} == NetSocketDataParsingStep->SOH) {
            if ($datalen > 0) {
                if (unpack("C", substr($self->{data}, $self->{datapos}, 1)) == 0x01) {
                    $self->{datapos} += 1;
                    $self->{step} = NetSocketDataParsingStep->OTL;
                    next;
                }
                else {
                    return NetSocketDataManipulationResult->ParsingError;
                }
            }
        } 
        elsif ($self->{step} == NetSocketDataParsingStep->OTL) {
            if ($datalen > 0) {
                my $argH = unpack("C", substr($self->{data}, $self->{datapos}, 1));
                my @list_t = (0x11, 0x12, 0x14);

                if ((grep { $_ eq $argH } @list_t) > 0) {
                    my $a = $self->{getArgLength}($datalen);

                    if ($a->argLength >= 0) {
                        $self->{textlen} = $a->argLength;
                        $self->{datapos} += 1 + $a->size;
                        $self->{step} = NetSocketDataParsingStep->STX;
                        next;
                    }
                }
                else {
                    return NetSocketDataManipulationResult->ParsingError;
                }
            }
        } 
        elsif ($self->{step} == NetSocketDataParsingStep->STX) {
            if ($datalen > 0) {
                if (unpack("C", substr($self->{data}, $self->{datapos}, 1)) == 0x02) {
                    $self->{datapos} += 1;
                    $self->{step} = NetSocketDataParsingStep->ETX;
                    next;
                }
                else {
                    return NetSocketDataManipulationResult->ParsingError;
                }
            }
        } 
        elsif ($self->{step} == NetSocketDataParsingStep->ETX) {
            if ($datalen > $self->{textlen}) {
                if (unpack("C", substr($self->{data}, $self->{datapos} + $self->{textlen}, 1)) == 0x03) {
                    my $textfpos = $self->{datapos};
                    my $argCount = 0;

                    $self->{command} = unpack("C", substr($self->{data}, $textfpos, 1));
                    $self->{args} = \@empty;
                    $self->{datapos} += 1;

                    while ($self->{datapos} < $self->{textlen} + $textfpos) {
                        my $fmt = "";
                        my $sz = 0;
                        my $argL = 0;
                        my $argH = unpack("C", substr($self->{data}, $self->{datapos}, 1));
                        
                        my @list_i = (0x31, 0x32, 0x34, 0x38);
                        my @list_f = (0x54, 0x58);
                        my @list_b = (0x71);
                        my @list_s = (0x91, 0x92, 0x94);
                        my @list_ba = (0xB1, 0xB2, 0xB4);

                        my $len = grep { $_ eq $argH } @list_i;

                        if ((grep { $_ eq $argH } @list_i) > 0) {
                            $sz = $argH & 0x0F;
                            if ($sz == 1) { 
                                $fmt = "c";
                            } elsif ($sz == 2) { 
                                $fmt = "s>";
                            } elsif ($sz == 4) { 
                                $fmt = "l>";
                            } elsif ($sz == 8) { 
                                $fmt = "q>";
                            }  
                            $self->{args}[$argCount++] = unpack($fmt, substr($self->{data}, $self->{datapos} + 1, $sz));
                        }
                        elsif ((grep { $_ eq $argH } @list_f) > 0) {
                            $sz = $argH & 0x0F;
                            if ($sz == 4) { 
                                $fmt = "f>";
                            } elsif ($sz == 8) { 
                                $fmt = "d>";
                            }                                    
                            $self->{args}[$argCount++] = unpack($fmt, substr($self->{data}, $self->{datapos} + 1, $sz));
                        }
                        elsif ((grep { $_ eq $argH } @list_b) > 0) {
                            $sz = 1;
                            my $bool = 0;
                            if (unpack("C", substr($self->{data}, $self->{datapos} + 1, 1)) == 1) {
                                $bool = 1;
                            }
                            $self->{args}[$argCount++] = $bool;                           
                        }
                        elsif ((grep { $_ eq $argH } @list_s) > 0) {
                            $a = $self->{getArgLength}($datalen);
                            $sz = $a->size;
                            $argL = $a->argLength;

                            $self->{args}[$argCount++] = Encode::decode("utf8", substr($self->{data}, $self->{datapos} + 1 + $sz, $argL));
                            $self->{datapos} += $argL;
                        }
                        elsif ((grep { $_ eq $argH } @list_ba) > 0) {
                            $a = $self->{getArgLength}($datalen);
                            $sz = $a->size;
                            $argL = $a->argLength;

                            $self->{args}[$argCount++] = substr($self->{data}, $self->{datapos} + 1 + $sz, $argL);
                            $self->{datapos} += $argL;                         
                        }
                        else {
                            return NetSocketDataManipulationResult->ParsingError;
                        }

                        $self->{datapos} += 1 + $sz;
                    }

                    $self->{checksum} = 0x00;

                    my @txtarr = unpack("C*", substr($self->{data}, $textfpos, $self->{textlen}));
                    for (my $i = 0; $i < $self->{textlen}; $i++) {
                        $self->{checksum} ^= $txtarr[$i];
                    }

                    $self->{datapos} += 1;
                    $self->{step} = NetSocketDataParsingStep->CHK;
                    next;
                }
                else {
                    return NetSocketDataManipulationResult->ParsingError;
                }
            }
        } 
        elsif ($self->{step} == NetSocketDataParsingStep->CHK) {
            if ($datalen > 0) {
                if (unpack("C", substr($self->{data}, $self->{datapos}, 1)) == $self->{checksum}) {
                    $self->{datapos} += 1;
                    $self->{step} = NetSocketDataParsingStep->EOT;
                    next;
                }
                else {
                    return NetSocketDataManipulationResult->ParsingError;
                }
            }
        } 
        elsif ($self->{step} == NetSocketDataParsingStep->EOT) {
            if ($datalen > 0) {
                if (unpack("C", substr($self->{data}, $self->{datapos}, 1)) == 0x04) {
                    $self->{datapos} += 1;
                    $self->{data} = substr($self->{data}, $self->{datapos});

                    $self->{datapos} = 0;
                    $self->{checksum} = 0x00;
                    $self->{step} = NetSocketDataParsingStep->SOH;
                    $self->{textlen} = 0;

                    return NetSocketDataManipulationResult->Completed;
                }
                else {
                    return NetSocketDataManipulationResult->ParsingError;
                }
            }
        }

        if (length($self->{data}) == 0) {
            return NetSocketDataManipulationResult->NoData;
        }

        return NetSocketDataManipulationResult->InProgress;
    }
}


package NetSocketDataArgLength;

sub new() {
    my $class = shift;
    my $self = {
        sz => shift,
        argL => shift,
    };
    bless $self, $class; 
    return $self;
}

sub size {
    my $self = shift;
    return $self->{sz};
}

sub argLength {
    my $self = shift;
    return $self->{argL};
}


package NetSocketDataManipulationResult;

use constant {
    Completed => 0,
    InProgress => 1,
    NoData => 2,
    ParsingError => 3,
};


package NetSocketDataParsingStep;

use constant {
    SOH => 0,
    OTL => 1,
    STX => 2,
    ETX => 3,
    CHK => 4,
    EOT => 5,
};


package NetSocketProtocolType;

use constant {
    Tcp => 0,
    Udp => 1,
};


package NetSocketReceivedData;

sub new() {
    my $class = shift;
    my $self = {
        command => shift,
        args => shift,
        result => shift,  
        address => shift,       
    };
    bless $self, $class; 
    return $self;
}

sub args {
    my $self = shift;
    return $self->{args};
}

sub command {
    my $self = shift;
    return $self->{command};
}

sub remoteAddress {
    my $self = shift;
    return $self->{address};
}

sub result {
    my $self = shift;
    return $self->{result};
}


package NetSocketReceivedDataResult;

use constant {
    Closed => 0,
    Completed => 1,
    Interrupted => 2,
    ParsingError => 3,
};


package NetSocketSendDataBuildResult;

use constant {
    ByteArrayLengthOverflowError => 0,
    CommandValueOverflowError => 1,
    DataTotalLengthOverflowError => 2,
    DataTypeNotImplementedError => 3,
    NoData => 4,
    StringLengthOverflowError => 5,
    Successful => 6,
};


package NetSocketSendData;
use Scalar::Util qw(looks_like_number);
use Encode;

sub new() {
    my $class = shift;
    my $self = {
        command => shift,
        args => shift, 
        result => NetSocketSendDataBuildResult->NoData,
        bytes => "",
    };
    bless $self, $class; 

    my $ARG_MAXLEN = 0x7FFFFF - 5;
    my $TXT_MAXLEN = 2147483647 - 10;

    if ($self->{command} < 0x00 || $self->{command} > 0xFF) {
        $self->{result} = NetSocketSendDataBuildResult->CommandValueOverflowError;
        return $self;
    }
    
    my $text = pack("C", $self->{command});

    my @args = @{ $self->{args} };
    my $count = @args;

    for (my $i = 0; $i < $count; $i++) {
        my $arg = $args[$i];
        my $type = ref($arg);

        if ($type eq "") {
            if (looks_like_number($arg) == 1) {
                no warnings;
                if (($arg + 0) == $arg) {
                    if (int($arg) == $arg) {
                        $type = "Integer";
                    } else {
                        $type = "Float";
                    }
                } else {
                    $type = "String";
                }
            } else {
                $type = "String";
            }
        }

        if ($type eq "Integer") {
            if (-128 <= $arg && $arg <= 127) {
                $text .= pack("C", 0x31);
                $text .= pack("c", $arg);
            } 
            elsif (-32768 <= $arg && $arg <= 32767) {
                $text .= pack("C", 0x32);
                $text .= pack("s>", $arg);
            } 
            elsif (-2147483648 <= $arg && $arg <= 2147483647) {
                $text .= pack("C", 0x34);
                $text .= pack("l>", $arg);
            } 
            else {
                $text .= pack("C", 0x38);
                $text .= pack("q>", $arg);
            }
        }     
        elsif ($type eq "Float") {
            if (abs($arg) <= 3.40282347e+38) {
                $text .= pack("C", 0x54);
                $text .= pack("f>", $arg);
            } else {
                $text .= pack("C", 0x58);
                $text .= pack("d>", $arg);
            }
        }
        elsif ($type eq "Boolean") {
            $text .= pack("C", 0x71);
            $text .= pack("c", $arg->value);
        }  
        elsif ($type eq "String") {
            $arg = Encode::encode("utf8", $arg);
            my $argL = length($arg);

            if ($argL <= $ARG_MAXLEN) {
                if ($argL <= 127) {
                    $text .= pack("C", 0x91);
                    $text .= pack("c", $argL);
                } 
                elsif ($argL <= 32767) {
                    $text .= pack("C", 0x92);
                    $text .= pack("s>", $argL);
                } 
                else {
                    $text .= pack("C", 0x94);
                    $text .= pack("l>", $argL);
                }
                $text .= $arg;
            } 
            else {
                $self->{result} = NetSocketSendDataBuildResult->StringLengthOverflowError;
                return $self;
            }
        }
        elsif ($type eq "ByteArray") {
            my $argL = $arg->length;

            if ($argL <= $ARG_MAXLEN) {
                if ($argL <= 127) {
                    $text .= pack("C", 0xB1);
                    $text .= pack("c", $argL);
                } 
                elsif ($argL <= 32767) {
                    $text .= pack("C", 0xB2);
                    $text .= pack("s>", $argL);
                } 
                else {
                    $text .= pack("C", 0xB4);
                    $text .= pack("l>", $argL);
                }
                $text .= $arg->data;
            } 
            else {
                $self->{result} = NetSocketSendDataBuildResult->ByteArrayLengthOverflowError;
                return $self;
            }
        }
        else {
            $self->{result} = NetSocketSendDataBuildResult->DataTypeNotImplementedError;
            return $self;
        }
    }      

    my $data = pack("C", 0x01);

    my $textlen = length($text);

    if ($textlen <= $TXT_MAXLEN) {
        if ($textlen <= 127) {
            $data .= pack("C", 0x11);
            $data .= pack("c", $textlen);
        }
        elsif ($textlen <= 32767) {
            $data .= pack("C", 0x12);
            $data .= pack("s>", $textlen);
        } 
        else {
            $data .= pack("C", 0x14);
            $data .= pack("l>", $textlen);
        }
        
        $data .= pack("C", 0x02);
        $data .= $text;
        $data .= pack("C", 0x03);

        my $checksum = 0x00;
        my @textArray = unpack("C*", $text);

        for (my $j = 0; $j < $textlen; $j++) {
            my $b = $textArray[$j];
            $checksum ^= $b;
        }

        $data .= pack("C", $checksum);
        $data .= pack("C", 0x04);
    } 
    else {
        $self->{result} = NetSocketSendDataBuildResult->DataTotalLengthOverflowError;
        return $self;
    }

    $self->{bytes} = $data;
    $self->{result} = NetSocketSendDataBuildResult->Successful;

    return $self;
}

sub args {
    my $self = shift;
    return $self->{args};
}

sub buildResult {
    my $self = shift;
    return $self->{result};
}

sub bytes {
    my $self = shift;
    return $self->{bytes};
}

sub command {
    my $self = shift;
    return $self->{command};
}

sub length {
    my $self = shift;
    return length($self->{bytes});
}


package TcpServer;
use IO::Socket;

sub new {
    my $class = shift;
    my $self = {
        server => shift,
    };
    bless($self, $class); 
    return $self;   
}

sub running {
    my $self = shift;
    return defined($self->{server});
}

sub close() {
    my $self = shift;
    $self->{server}->close();
    $self->{server} = undef;
}

sub setAcceptCallback() {
    my $self = shift;
    my $callback = shift;

    my $acceptProc = sub {
        my $self = shift;
        my $callback = shift;

        while ($self->running) {
            my $s = undef;
            $s = $self->{server}->accept() or $s = undef;
            $callback->(TcpSocket->new($s));
        }
    };

    threads->create(\&$acceptProc, $self, $callback);
}


package TcpSocket;
use IO::Socket;
use vars qw(@ISA);
@ISA = qw(NetSocket);

sub new {
    my $class = shift;
    my $self = NetSocket->new(shift, NetSocketProtocolType->Tcp);
    bless($self, $class); 
 
    $self->{remoteAddress} = NetSocketAddress->new("0.0.0.0", 0);

    if ($self->available) {
        my $rinfo = getpeername($self->{socket});
        (my $port, my $host) = sockaddr_in($rinfo);
        $self->{remoteAddress} = NetSocketAddress->new(inet_ntoa($host), $port);  
    }
    return $self;
}

sub connected {
    my $self = shift;
    return $self->available && $self->{socket}->connected();
}

sub remoteAddress() {
    my $self = shift;    
    return $self->{remoteAddress};
}

sub send() {
    my $self = shift;
    my $data = shift;
    $self->SUPER::_send($data, undef);
}


package UdpSocket;
use IO::Socket;
use vars qw(@ISA);
@ISA = qw(NetSocket);

sub new() {
    my $class = shift;
    my $self = NetSocket->new(shift, NetSocketProtocolType->Udp);
    bless($self, $class);    
    return $self;
}

sub send() {
    my $self = shift;
    my $data = shift;
    my $address = shift;
    
    $self->SUPER::_send($data, $address);
}


package NetworkComm;
use strict;
use warnings;
use IO::Socket;

sub TcpConnect {
    my $address = shift;
    my $s = IO::Socket->new(Domain => AF_INET, Type => SOCK_STREAM, Proto => "tcp");

    $s->connect(pack_sockaddr_in($address->port, inet_aton($address->host))) or $s = undef;

    return TcpSocket->new($s);
}

sub TcpListen {
    my $address = shift;
    my $s = IO::Socket->new(Domain => AF_INET, Type => SOCK_STREAM, Proto => "tcp");

    $s->bind(pack_sockaddr_in($address->port, inet_aton($address->host))) or $s = undef;

    if (defined($s)) {
        $s->listen() or $s = undef;
    }

    return TcpServer->new($s);
}

sub UdpCast {
    my $address = shift;
    my $s = IO::Socket->new(Domain => AF_INET, Type => SOCK_DGRAM, Proto => "udp");

    $s->bind(pack_sockaddr_in($address->port, inet_aton($address->host))) or $s = undef;
    
    return UdpSocket->new($s);
}

return 1;
