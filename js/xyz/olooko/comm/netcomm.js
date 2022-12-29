const net = require('net');
const dgram = require('dgram');

class NetSocket {
    #socket;
    #protocol;
    #data;
    #localAddress;
    #result;

	constructor (s, protocol) {
        this.#socket = s;
        this.#protocol = protocol;
        this.#data = new NetSocketData();
        this.#result = NetSocketDataManipulationResult.NoData;
        this.#localAddress = new NetSocketAddress("0.0.0.0", 0);

        if (this.isAvailable()) {
            if (this.#protocol == NetSocketProtocolType.Tcp) {
                this.#localAddress = new NetSocketAddress(s.localAddress, s.localPort)
            } 
            else if (this.#protocol == NetSocketProtocolType.Udp) {
                this.#localAddress = new NetSocketAddress(s.address().address, s.address().port);
            }
        }
    }

    isAvailable = () => {
        return this.#socket != null;
    };

    getLocalAddress = () => {
        return this.#localAddress;
    };
    
    getProtocolType = () => {
        return this.#protocol;
    };        

    close = () => {
        if (this.isAvailable()) {
            if (this.#protocol == NetSocketProtocolType.Tcp) {
                this.#socket.end();
                this.#socket.destroy();
            } 
            else if (this.#protocol == NetSocketProtocolType.Udp) {
                this.#socket.close();
            }
        }
    };

    setReceivedCallback = (callback) => {
        if (this.isAvailable()) {
            if (this.#protocol == NetSocketProtocolType.Tcp) {
                const remoteAddress = new NetSocketAddress(this.#socket.remoteAddress, this.#socket.remotePort);
                this.#socket.on('data', (buffer) => {
                    this.#receiveProc(buffer, callback, remoteAddress);
                });
                this.#socket.on('error', () => {
                });
            } 
            else if (this.#protocol == NetSocketProtocolType.Udp) {
                this.#socket.on('message', (buffer, rinfo) => {
                    const remoteAddress = new NetSocketAddress(rinfo.address, rinfo.port);
                    this.#receiveProc(buffer, callback, remoteAddress);
                });
                this.#socket.on('error', () => {
                });
            }
        }   
    }; 
    
    #receiveProc = (buffer, callback, remoteAddress) => {
        this.#data.append(buffer);

        while (true) {
            this.#result = this.#data.manipulate();
            if (this.#result == NetSocketDataManipulationResult.Completed) {
                callback(this, new NetSocketReceivedData(this.#data.getCommand(), this.#data.getArgs(), NetSocketReceivedDataResult.Completed, remoteAddress));
                continue;
            } 
            else if (this.#result == NetSocketDataManipulationResult.ParsingError) {
                callback(this, new NetSocketReceivedData(0x00, [], NetSocketReceivedDataResult.ParsingError, remoteAddress));
                return;
            } 
            else if (this.#result == NetSocketDataManipulationResult.InProgress) {
                let me = this;
                setTimeout(() => {
                    if (this.#result == NetSocketDataManipulationResult.InProgress) {
                        callback(me, new NetSocketReceivedData(0x00, [], NetSocketReceivedDataResult.Interrupted, remoteAddress));
                    }
                }, 15000);
                break;
            } 
            else if (this.#result == NetSocketDataManipulationResult.NoData) {
                break;				
            }
        }
    };    
}

class NetSocketAddress {
    #host;
    #port;

	constructor(host, port) {
        this.#host = host;
        this.#port = port;
	}

    getHost = () => {
        return this.#host;
    };
    
    getPort = () => {
        return this.#port;
    };    

    toString = () => {
        return `${this.#host}:${this.#port}`;
    };
}

class NetSocketData {
    #command = 0x00;
    #args = [];	
    #data = Buffer.alloc(0);
    #datapos = 0;
    #checksum = 0x00;
    #step = NetSocketDataParsingStep.SOH;
    #textL = 0;	

	constructor () {
    }

    getArgs = () => {
        return this.#args;
    };     

    getCommand = () => {
        return this.#command;
    }; 

    append = (buffer) => {
        this.#data = Buffer.concat([this.#data, buffer]);
    };

    manipulate = () => {
        while (true) {
            let datalen = this.#data.length - this.#datapos;
            switch (this.#step) {
            case NetSocketDataParsingStep.SOH:
                if (datalen > 0) {
                    if (this.#data[this.#datapos] == 0x01) {
                        this.#datapos += 1;
                        this.#step = NetSocketDataParsingStep.OTL;
                        continue;
                    } 
                    else {
                        return NetSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case NetSocketDataParsingStep.OTL:
                if (datalen > 0) {
                    if ([0x11, 0x12, 0x14].includes(this.#data[this.#datapos])) {
                        let a = this.#getArgLength(datalen);
                        if (a.getArgLength() >= 0) {
                            this.#textL = a.getArgLength();
                            this.#datapos += 1 + a.getSize();
                            this.#step = NetSocketDataParsingStep.STX;
                            continue;
                        }
                    } 
                    else {
                        return NetSocketDataManipulationResult.ParsingError;						
                    }
                } 
                break;
            case NetSocketDataParsingStep.STX:
                if (datalen > 0) {
                    if (this.#data[this.#datapos] == 0x02) {
                        this.#datapos += 1;
                        this.#step = NetSocketDataParsingStep.ETX;
                        continue;
                    } 
                    else {
                        return NetSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case NetSocketDataParsingStep.ETX:
                if (datalen > this.#textL) {
                    if (this.#data[this.#datapos + this.#textL] == 0x03) {
                        this.#args.length = 0;
                        let textfpos = this.#datapos;
                        this.#command = this.#data[textfpos];
                        this.#datapos += 1;
                        while (this.#datapos < this.#textL + textfpos) {
                            let sz = 0;
                            let argL = 0;
                            if ([0x31, 0x32, 0x34].includes(this.#data[this.#datapos])) {
                                sz = this.#data[this.#datapos] & 0x0F;
                                switch (sz) {
                                case 1:
                                    this.#args.push(this.#data.readInt8(this.#datapos + 1));
                                    break;
                                case 2:
                                    this.#args.push(this.#data.readInt16BE(this.#datapos + 1));
                                    break;
                                case 4:
                                    this.#args.push(this.#data.readInt32BE(this.#datapos + 1));
                                    break;					
                                }
                            } 
                            else if ([0x54, 0x58].includes(this.#data[this.#datapos])) {
                                sz = this.#data[this.#datapos] & 0x0F;
                                switch (sz) {
                                case 4:
                                    this.#args.push(this.#data.readFloatBE(this.#datapos + 1));
                                    break;
                                case 8:
                                    this.#args.push(this.#data.readDoubleBE(this.#datapos + 1));
                                    break;				
                                }
                            } 
                            else if ([0x71].includes(this.#data[this.#datapos])) {
                                sz = 1
                                this.#args.push((this.#data[this.#datapos + 1] == 1)? true: false);
                            } 
                            else if ([0x91, 0x92, 0x94].includes(this.#data[this.#datapos])) {
                                let a = this.#getArgLength(datalen);
                                sz = a.getSize();
                                argL = a.getArgLength();
                                this.#args.push(this.#data.subarray(this.#datapos + 1 + sz, this.#datapos + 1 + sz + argL).toString('utf8'));
                                this.#datapos += argL;
                            } 
                            else if ([0xB1, 0xB2, 0xB4].includes(this.#data[this.#datapos])) {
                                let a = this.#getArgLength(datalen);
                                sz = a.getSize();
                                argL = a.getArgLength();
                                this.#args.push(this.#data.subarray(this.#datapos + 1 + sz, this.#datapos + 1 + sz + argL));
                                this.#datapos += argL;
                            } 
                            else {
                                return NetSocketDataManipulationResult.ParsingError;
                            }
                            this.#datapos += 1 + sz;
                        }

                        this.#checksum = 0x00;
                        for (let i = textfpos; i < textfpos + this.#textL; i++) {
                            this.#checksum ^= this.#data[i];
                        }  
                        this.#datapos += 1;
                        this.#step = NetSocketDataParsingStep.CHK;
                        continue;
                    }
                    else {
                        return NetSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case NetSocketDataParsingStep.CHK:
                
                if (datalen > 0) {
                    if (this.#data[this.#datapos] == this.#checksum) {
                        this.#datapos += 1;
                        this.#step = NetSocketDataParsingStep.EOT;
                        continue;
                    } 
                    else {
                        return NetSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case NetSocketDataParsingStep.EOT:
                if (datalen > 0) {
                    if (this.#data[this.#datapos] == 0x04) {
                        this.#datapos += 1;
                        this.#data = this.#data.subarray(this.#datapos);

                        this.#datapos = 0;
                        this.#checksum = 0x00;
                        this.#step = NetSocketDataParsingStep.SOH;
                        this.#textL = 0;	

                        return NetSocketDataManipulationResult.Completed;
                    } 
                    else {
                        return NetSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;																	
            }

            if (this.#data.length == 0)
                return NetSocketDataManipulationResult.NoData;

            return NetSocketDataManipulationResult.InProgress;
        };
    }

    #getArgLength = (datalen) => {
        let sz = this.#data[this.#datapos] & 0x0F;
        let argL = -1;
        if (datalen > sz) {
            switch (sz) {
                case 1:
                    argL = this.#data.readInt8(this.#datapos + 1);
                    break;
                case 2:
                    argL = this.#data.readInt16BE(this.#datapos + 1);
                    break;
                case 4:
                    argL = this.#data.readInt32BE(this.#datapos + 1);
                    break;					
            }
        }
        return new NetSocketDataArgLength(sz, argL);
    };         
}

class NetSocketDataArgLength {
    #sz;
    #argL;

    constructor(sz, argL) {
        this.#sz = sz;
        this.#argL = argL;
    }

    getSize = () => {
        return this.#sz;
    };
    
    getArgLength = () => {
        return this.#argL;
    };    
}

const NetSocketDataManipulationResult = {
    Completed: "completed",
    InProgress: "in-progress",
    NoData: "no-data",
    ParsingError: "parsing-error",
}

const NetSocketDataParsingStep = {
	SOH: "soh",
	OTL: "otl",
	STX: "stx",
	ETX: "etx",
	CHK: "chk",
	EOT: "eot",
}

const NetSocketProtocolType = {
	Tcp: "tcp",
	Udp: "udp",
}

class NetSocketReceivedData {
    #command;
    #args;
    #result;
    #remoteAddress;

	constructor (command, args, result, address) {
        this.#command = command;
        this.#args = args;
        this.#result = result;
        this.#remoteAddress = address;
    }

    getArgs = () => {
        return this.#args;
    };     

    getCommand = () => {
        return this.#command;
    };     
    
    getResult = () => {
        return this.#result;
    };      
    
    getRemoteAddress = () => {
        return this.#remoteAddress;
    };         
}

const NetSocketReceivedDataResult = {
    Closed: "closed",
    Completed: "completed",
    Interrupted: "interrupted",
    ParsingError: "parsing-error",
}

class NetSocketSendData {
    #command;
    #args;
    #bytes;
    #result;

	constructor (command, args) {
        const ARG_MAXLEN = 0x7FFFFF - 5;
        const TXT_MAXLEN = 0x7FFFFFFF - 10;

        this.#result = NetSocketSendDataBuildResult.NoData;

        if (command < 0x00 || command > 0xFF) {
            this.#result = NetSocketSendDataBuildResult.CommandValueOverflowError;
            return;
        }
        
        this.#command = command;
        this.#args = args;

		let text = Buffer.alloc(0);
		text = Buffer.concat([text, Buffer.from([command])]);
	
		let buffer;
		for (let i = 0; i < args.length; i++) {
			let arg = args[i];
			if (typeof(arg) == 'number' && Number.isInteger(arg)) {
				if (-128 <= arg && arg <= 127) {
					text = Buffer.concat([text, Buffer.from([0x31])]);
					buffer = Buffer.alloc(1);
					buffer.writeInt8(arg);
					text = Buffer.concat([text, buffer]);
				} 
                else if (-32768 <= arg && arg <= 32767) {
					text = Buffer.concat([text, Buffer.from([0x32])]);
					buffer = Buffer.alloc(2);
					buffer.writeInt16BE(arg);
					text = Buffer.concat([text, buffer]);
				} 
                else if (-2147483648 <= arg && arg <= 2147483647) {
					text = Buffer.concat([text, Buffer.from([0x34])]);
					buffer = Buffer.alloc(4);
					buffer.writeInt32BE(arg);
					text = Buffer.concat([text, buffer]);
				} 
                else {
					text = Buffer.concat([text, Buffer.from([0x38])]);
					buffer = Buffer.alloc(8);
					buffer.writeInt64BE(arg);
					text = Buffer.concat([text, buffer]);					
				}
			} 
            else if (typeof(arg) == 'number' && !Number.isInteger(arg)) {
				if (Math.abs(arg) <= 3.40282347e+38) {
					text = Buffer.concat([text, Buffer.from([0x54])]);
					buffer = Buffer.alloc(4);
					buffer.writeFloatBE(arg);
					text = Buffer.concat([text, buffer]);					
				} 
                else {
					text = Buffer.concat([text, Buffer.from([0x58])]);
					buffer = Buffer.alloc(8);
					buffer.writeDoubleBE(arg);
					text = Buffer.concat([text, buffer]);					
				}
			} 
            else if (typeof(arg) == 'boolean') {
				text = Buffer.concat([text, Buffer.from([0x71])]);
				text = Buffer.concat([text, Buffer.from([arg?0x01:0x00])]);
			} 
            else if (typeof(arg) == 'string') {
				let str = Buffer.from(arg, 'utf8');
				let argL = arg.length;
				if (argL <= ARG_MAXLEN) {
					if (argL <= 127) {
						text = Buffer.concat([text, Buffer.from([0x91])]);
						buffer = Buffer.alloc(1);
						buffer.writeInt8(argL);
						text = Buffer.concat([text, buffer]);						
					} else if (argL <= 32767) {
						text = Buffer.concat([text, Buffer.from([0x92])]);
						buffer = Buffer.alloc(2);
						buffer.writeInt16BE(argL);
						text = Buffer.concat([text, buffer]);	
					} else {
						text = Buffer.concat([text, Buffer.from([0x94])]);
						buffer = Buffer.alloc(4);
						buffer.writeInt32BE(argL);
						text = Buffer.concat([text, buffer]);							
					}
					text = Buffer.concat([text, str]);
				} 
                else {
                    this.#result = NetSocketSendDataBuildResult.StringLengthOverflowError;
                    return;
                }
			} 
            else if (typeof(arg) == 'object' && Buffer.isBuffer(arg)) {
				let argL = arg.length;
				if (argL <= ARG_MAXLEN) {
					if (argL <= 127) {
						text = Buffer.concat([text, Buffer.from([0xB1])]);						
						buffer = Buffer.alloc(1);
						buffer.writeInt8(argL);
						text = Buffer.concat([text, buffer]);	
					} else if (argL <= 32767) {
						text = Buffer.concat([text, Buffer.from([0xB2])]);
						buffer = Buffer.alloc(2);
						buffer.writeInt16BE(argL);
						text = Buffer.concat([text, buffer]);	
					} else {
						text = Buffer.concat([text, Buffer.from([0xB4])]);
						buffer = Buffer.alloc(4);
						buffer.writeInt32BE(argL);
						text = Buffer.concat([text, buffer]);							
					}
					text = Buffer.concat([text, arg]);
				} 
                else {
                    this.#result = NetSocketSendDataBuildResult.ByteArrayLengthOverflowError;
                    return;
                }
			} 
            else {
                this.#result = NetSocketSendDataBuildResult.DataTypeNotImplementedError;
                return;                
            }
		}
	
		let data = Buffer.alloc(0);
	
		data = Buffer.concat([data, Buffer.from([0x01])]);
	
		let textL = text.length;
		if (textL <= TXT_MAXLEN) {
			if (textL <= 127) {
				data = Buffer.concat([data, Buffer.from([0x11])]);
				buffer = Buffer.alloc(1);
				buffer.writeInt8(textL);
				data = Buffer.concat([data, buffer]);					
			} 
            else if (textL <= 32767) {
				data = Buffer.concat([data, Buffer.from([0x12])]);
				buffer = Buffer.alloc(2);
				buffer.writeInt16BE(textL);
				data = Buffer.concat([data, buffer]);	
			} 
            else {
				data = Buffer.concat([data, Buffer.from([0x14])]);
				buffer = Buffer.alloc(4);
				buffer.writeInt32BE(textL);
				data = Buffer.concat([data, buffer]);					
			}
			data = Buffer.concat([data, Buffer.from([0x02])]);
			data = Buffer.concat([data, text]);
			data = Buffer.concat([data, Buffer.from([0x03])]); 
			let checksum = 0x00
			for (let i = 0; i < text.length; i++) {
				checksum ^= text[i];
			} 
			data = Buffer.concat([data, Buffer.from([checksum])]);
			data = Buffer.concat([data, Buffer.from([0x04])]);
		} 
        else {
            this.#result = NetSocketSendDataBuildResult.DataTotalLengthOverflowError;
            return;
        }
		
        this.#bytes = data;
        this.#result = NetSocketSendDataBuildResult.Successful;
    }

    getArgs = () => {
        return this.#args;
    };
    
    getBuildResult = () => {
        return this.#result;
    }; 

    getBytes = () => {
        return this.#bytes;
    };       

    getCommand = () => {
        return this.#command;
    };       

    getLength = () => {
        return this.#bytes.length;
    };   
}

const NetSocketSendDataBuildResult = {
    ByteArrayLengthOverflowError: "bytearray-length-overflow-error", 
    CommandValueOverflowError: "command-value-overflow-error",
	DataTotalLengthOverflowError: "data-totallength-overflow-error",
    DataTypeNotImplementedError: "datatype-not-implemented-error",
    NoData: "no-data",
    StringLengthOverflowError: "string-length-overflow-error", 
    Successful: "successful",
}

class TcpServer {
    #server;

    constructor (s) {
        this.#server = s;
    }

    isRunning = () => {
        return this.#server != null;
    };

    close = () => {
        try {
            this.#server.close();
            this.#server = null;
        } catch {}       
    }

    setAcceptCallback = (callback) => {
        this.#server.on('connection', (socket) => {
            callback(new TcpSocket(socket));
        });
    }
}

class TcpSocket extends NetSocket {
    #socket;
    #connected;
    #address;

	constructor (s) {
        super(s, NetSocketProtocolType.Tcp);

        this.#socket = s;
        this.#connected = (this.#socket != null);
        this.#address = new NetSocketAddress('0.0.0.0', 0);

        if (this.#socket != null) {
            this.#address = new NetSocketAddress(this.#socket.remoteAddress, this.#socket.remotePort);
        }
    }

    isConnected = () => {
        return (this.#socket != null) && this.#connected;
    };

    getRemoteAddress = () => {
        return this.#address;
    };

    send = (data) => {
        if (this.isAvailable()) {
            this.#socket.write(data.getBytes(), 'utf8');
        }
    };
}

class UdpSocket extends NetSocket {
    #socket;

	constructor (s) {
        super(s, NetSocketProtocolType.Udp);

        this.#socket = s;
    }

    send = (data, address) => {
        if (this.isAvailable()) {
            this.#socket.send(data.getBytes(), address.getPort(), address.getHost());
        }
    };     
}

function TcpConnect(address) {
    return new Promise((resolve) => {
        let s = new net.Socket();

        s.on('error', () => {
            resolve(new TcpSocket(null));
        });

        s.on('connect', () => {
            resolve(new TcpSocket(s));
        });   
        
        s.connect(address.getPort(), address.getHost());
    });
};

function TcpListen(address) {
    return new Promise((resolve) => {
        let s = net.createServer();

        s.on('error', () => {
            resolve(new TcpServer(null));
        });

        s.on('listening', () => {
            resolve(new TcpServer(s));
        });   

        s.listen(address.getPort(), address.getHost());
    });
};

function UdpCast(address) {
    return new Promise((resolve) => {
        let s = dgram.createSocket('udp4');

        s.on('error', () => {
            resolve(new UdpSocket(null));
        });

        s.on('listening', () => {
            resolve(new UdpSocket(s));
        });   

        s.bind(address.getPort(), address.getHost());  
    });
};

module.exports = {
    NetSocket: NetSocket,
    NetSocketAddress: NetSocketAddress,
    NetSocketData: NetSocketData,
    NetSocketDataManipulationResult: NetSocketDataManipulationResult,
    NetSocketDataParsingStep: NetSocketDataParsingStep,
    NetSocketProtocolType: NetSocketProtocolType,
    NetSocketReceivedData: NetSocketReceivedData,
    NetSocketReceivedDataResult: NetSocketReceivedDataResult,
    NetSocketSendData: NetSocketSendData,
    NetSocketSendData: NetSocketSendData,
    NetSocketSendDataBuildResult: NetSocketSendDataBuildResult,
    TcpServer: TcpServer,
    TcpScoket: TcpSocket,
    UdpSocket: UdpSocket,
    TcpConnect: TcpConnect,
    TcpListen: TcpListen,
    UdpCast: UdpCast,
};


