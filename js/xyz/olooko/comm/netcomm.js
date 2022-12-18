const net = require('net');
const dgram = require('dgram');

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

    #getArgLength = (data, datalen, datapos) => {
        let sz = data[datapos] & 0x0F;
        let argL = -1;
        if (datalen > sz) {
            switch (sz) {
                case 1:
                    argL = data.readInt8(datapos + 1);
                    break;
                case 2:
                    argL = data.readInt16BE(datapos + 1);
                    break;
                case 4:
                    argL = data.readInt32BE(datapos + 1);
                    break;					
            }
        }
        return { sz: sz, argL: argL };
    };     

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
                    } else {
                        return NetSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case NetSocketDataParsingStep.OTL:
                if (datalen > 0) {
                    if ([0x11, 0x12, 0x14].includes(this.#data[this.#datapos])) {
                        let p = this.#getArgLength(this.#data, datalen, this.#datapos);
                        if (p.argL >= 0) {
                            this.#textL = p.argL;
                            this.#datapos += 1 + p.sz;
                            this.#step = NetSocketDataParsingStep.STX;
                            continue;
                        }
                    } else {
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
                    } else {
                        return NetSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case NetSocketDataParsingStep.ETX:
                if (datalen > this.#textL) {
                    if (this.#data[this.#datapos + this.#textL] == 0x03) {
                        try {
                            this.#args.length = 0;
                            let textfpos = this.#datapos;
                            this.#command = this.#data[textfpos];
                            this.#datapos += 1;
                            while (this.#datapos < this.#textL + textfpos) {
                                let sz = 0;
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
                                } else if ([0x54, 0x58].includes(this.#data[this.#datapos])) {
                                    sz = this.#data[this.#datapos] & 0x0F;
                                    switch (sz) {
                                    case 4:
                                        this.#args.push(this.#data.readFloatBE(this.#datapos + 1));
                                        break;
                                    case 8:
                                        this.#args.push(this.#data.readDoubleBE(this.#datapos + 1));
                                        break;				
                                    }
                                } else if ([0x71].includes(this.#data[this.#datapos])) {
                                    sz = 1
                                    this.#args.push((this.#data[this.#datapos + 1] == 1)? true: false);
                                } else if ([0x91, 0x92, 0x94].includes(this.#data[this.#datapos])) {
                                    let p = this.#getArgLength(this.#data, datalen, this.#datapos);
                                    sz = p.sz;
                                    this.#args.push(this.#data.subarray(this.#datapos + 1 + sz, this.#datapos + 1 + sz + p.argL).toString('utf8'));
                                    this.#datapos += p.argL;
                                } else if ([0xB1, 0xB2, 0xB4].includes(this.#data[this.#datapos])) {
                                    let p = this.#getArgLength(this.#data, datalen, this.#datapos);
                                    sz = p.sz;
                                    this.#args.push(this.#data.subarray(this.#datapos + 1 + sz, this.#datapos + 1 + sz + p.argL));
                                    this.#datapos += p.argL;
                                } else {
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
                        } catch {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
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
                    } else {
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
                    } else {
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

const NetSocketSendDataBuildResult = {
    ByteArrayLengthOverflowError: "bytearray-length-overflow-error", 
    CommandValueOverflowError: "command-value-overflow-error",
	DataTotalLengthOverflowError: "data-totallength-overflow-error",
    DataTypeNotImplementedError: "datatype-not-implemented-error",
    NoData: "no-data",
    StringLengthOverflowError: "string-length-overflow-error", 
    Successful: "successful",
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
					// 0011 0001
					text = Buffer.concat([text, Buffer.from([0x31])]);
					buffer = Buffer.alloc(1);
					buffer.writeInt8(arg);
					text = Buffer.concat([text, buffer]);
				} else if (-32768 <= arg && arg <= 32767) {
					// 0011 0010
					text = Buffer.concat([text, Buffer.from([0x32])]);
					buffer = Buffer.alloc(2);
					buffer.writeInt16BE(arg);
					text = Buffer.concat([text, buffer]);
				} else if (-2147483648 <= arg && arg <= 2147483647) {
					// 0011 0100
					text = Buffer.concat([text, Buffer.from([0x34])]);
					buffer = Buffer.alloc(4);
					buffer.writeInt32BE(arg);
					text = Buffer.concat([text, buffer]);
				} else {
					// 0011 1000
					text = Buffer.concat([text, Buffer.from([0x38])]);
					buffer = Buffer.alloc(8);
					buffer.writeInt64BE(arg);
					text = Buffer.concat([text, buffer]);					
				}
			} else if (typeof(arg) == 'number' && !Number.isInteger(arg)) {
				if (Math.abs(arg) <= 3.40282347e+38) {
					// 0101 0100
					text = Buffer.concat([text, Buffer.from([0x54])]);
					buffer = Buffer.alloc(4);
					buffer.writeFloatBE(arg);
					text = Buffer.concat([text, buffer]);					
				} else {
					// 0101 1000
					text = Buffer.concat([text, Buffer.from([0x58])]);
					buffer = Buffer.alloc(8);
					buffer.writeDoubleBE(arg);
					text = Buffer.concat([text, buffer]);					
				}
			} else if (typeof(arg) == 'boolean') {
				// 0111 0001
				text = Buffer.concat([text, Buffer.from([0x71])]);
				text = Buffer.concat([text, Buffer.from([arg?0x01:0x00])]);
			} else if (typeof(arg) == 'string') {
				let str = Buffer.from(arg, 'utf8');
				let argL = arg.length;
				if (argL <= ARG_MAXLEN) {
					if (argL <= 127) {
						// 1001 0001
						text = Buffer.concat([text, Buffer.from([0x91])]);
						buffer = Buffer.alloc(1);
						buffer.writeInt8(argL);
						text = Buffer.concat([text, buffer]);						
					} else if (argL <= 32767) {
						// 1001 0010
						text = Buffer.concat([text, Buffer.from([0x92])]);
						buffer = Buffer.alloc(2);
						buffer.writeInt16BE(argL);
						text = Buffer.concat([text, buffer]);	
					} else {
						// 1001 0100
						text = Buffer.concat([text, Buffer.from([0x94])]);
						buffer = Buffer.alloc(4);
						buffer.writeInt32BE(argL);
						text = Buffer.concat([text, buffer]);							
					}
					text = Buffer.concat([text, str]);
				} else {
                    this.#result = NetSocketSendDataBuildResult.StringLengthOverflowError;
                    return;
                }
			} else if (typeof(arg) == 'object' && Buffer.isBuffer(arg)) {
				let argL = arg.length;
				if (argL <= ARG_MAXLEN) {
					if (argL <= 127) {
						// 1011 0001
						text = Buffer.concat([text, Buffer.from([0xB1])]);						
						buffer = Buffer.alloc(1);
						buffer.writeInt8(argL);
						text = Buffer.concat([text, buffer]);	
					} else if (argL <= 32767) {
						// 1011 0010
						text = Buffer.concat([text, Buffer.from([0xB2])]);
						buffer = Buffer.alloc(2);
						buffer.writeInt16BE(argL);
						text = Buffer.concat([text, buffer]);	
					} else {
						// 1011 0100
						text = Buffer.concat([text, Buffer.from([0xB4])]);
						buffer = Buffer.alloc(4);
						buffer.writeInt32BE(argL);
						text = Buffer.concat([text, buffer]);							
					}
					text = Buffer.concat([text, arg]);
				} else {
                    this.#result = NetSocketSendDataBuildResult.ByteArrayLengthOverflowError;
                    return;
                }
			} else {
                this.#result = NetSocketSendDataBuildResult.DataTypeNotImplementedError;
                return;                
            }
		}
	
		let data = Buffer.alloc(0);
	
		// start of header
		data = Buffer.concat([data, Buffer.from([0x01])]);
	
		let textL = text.length;
		if (textL <= TXT_MAXLEN) {
			if (textL <= 127) {
				// 0001 0001
				data = Buffer.concat([data, Buffer.from([0x11])]);
				buffer = Buffer.alloc(1);
				buffer.writeInt8(textL);
				data = Buffer.concat([data, buffer]);					
			} else if (textL <= 32767) {
				// 0001 0010
				data = Buffer.concat([data, Buffer.from([0x12])]);
				buffer = Buffer.alloc(2);
				buffer.writeInt16BE(textL);
				data = Buffer.concat([data, buffer]);	
			} else {
				// 0001 0100
				data = Buffer.concat([data, Buffer.from([0x14])]);
				buffer = Buffer.alloc(4);
				buffer.writeInt32BE(textL);
				data = Buffer.concat([data, buffer]);					
			}
			// start of text
			data = Buffer.concat([data, Buffer.from([0x02])]);
			data = Buffer.concat([data, text]);
			// end of text
			data = Buffer.concat([data, Buffer.from([0x03])]); 
			let checksum = 0x00
			for (let i = 0; i < text.length; i++) {
				checksum ^= text[i];
			} 
			// checksum of text 
			data = Buffer.concat([data, Buffer.from([checksum])]);
			// end of transmission
			data = Buffer.concat([data, Buffer.from([0x04])]);
		} else {
            this.#result = NetSocketSendDataBuildResult.DataTotalLengthOverflowError;
            return;
        }
		
        this.#bytes = data;
        this.#result = NetSocketSendDataBuildResult.Successful;
    }

    getArgs = () => {
        return this.#args;
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
    
    getBuildResult = () => {
        return this.#result;
    };    
}

const NetSocketProtocolType = {
	Tcp: "tcp",
	Udp: "udp",
}

const NetSocketReceivedDataResult = {
    Closed: "closed",
    Completed: "completed",
    Interrupted: "interrupted",
    ParsingError: "parsing-error",
}

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

        if (this.isAvailable())
            this.#localAddress = new NetSocketAddress(s.localAddress, s.localPort);
        else
            this.#localAddress = new NetSocketAddress("0.0.0.0", 0);
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
            } else if (this.#protocol == NetSocketProtocolType.Udp) {
                this.#socket.close();
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
            } else if (this.#result == NetSocketDataManipulationResult.ParsingError) {
                callback(this, new NetSocketReceivedData(0x00, [], NetSocketReceivedDataResult.ParsingError, remoteAddress));
                return;
            } else if (this.#result == NetSocketDataManipulationResult.InProgress) {
                let me = this;
                setTimeout(() => {
                    if (this.#result == NetSocketDataManipulationResult.InProgress) {
                        callback(me, new NetSocketReceivedData(0x00, [], NetSocketReceivedDataResult.Interrupted, remoteAddress));
                    }
                }, 15000);
                break;
            } else if (this.#result == NetSocketDataManipulationResult.NoData) {
                break;				
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
            } else if (this.#protocol == NetSocketProtocolType.Udp) {
                this.#socket.on('message', (buffer, rinfo) => {
                    const remoteAddress = new NetSocketAddress(rinfo.address.replace('::ffff:', ''), rinfo.port);
                    this.#receiveProc(buffer, callback, remoteAddress);
                });
                this.#socket.on('error', () => {
                });
            }
        }   
    };       
}

class TcpServer {
    #server;

    constructor (s) {
        this.#server = s;
    }

    isStarted = () => {
        return this.#server != null;
    };

    setAcceptCallback = (callback) => {
        this.#server.on('connection', (socket) => {
            callback(new TcpSocket(socket));
        });
    }

    close = () => {
        try {
            this.#server.close();
            this.#server = null;
        } catch {}       
    }
}

class TcpSocket extends NetSocket {
    #socket;
    #connected;
    #address;

	constructor (s) {
        super(s, NetSocketProtocolType.Tcp);

        this.#socket = s;
        this.#connected = false;
        this.#address = new NetSocketAddress('0.0.0.0', 0);

        if (this.#socket != null) {
            let me = this;
            this.#socket.on('connect', function() {
                me.#connected = true;
            });

            this.#socket.on('close', function() {
                me.#connected = false;
            });

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
    let s = new net.Socket();
    try {
        s.connect(address.getPort(), address.getHost());
    } catch {
        s = null;
    }
    return new TcpSocket(s);
}

function TcpListen(address) {
    let s = net.createServer();
    try {
        s.listen(address.getPort(), address.getHost());
    } catch {
        s = null;
    }
    return new TcpServer(s);
}

function UdpCast(address) {
    let s = dgram.createSocket('udp4');
    try {
        s.bind(address.getPort(), address.getHost());
    } catch {
        s = null;
    }
    return new UdpSocket(s);    
}

module.exports = {
    NetSocketAddress: NetSocketAddress,
    NetSocketDataManipulationResult: NetSocketDataManipulationResult,
    NetSocketDataParsingStep: NetSocketDataParsingStep,
    NetSocketSendData: NetSocketSendData,
    NetSocketSendDataBuildResult: NetSocketSendDataBuildResult,
    NetSocketProtocolType: NetSocketProtocolType,
    NetSocketReceivedDataResult: NetSocketReceivedDataResult,
    NetSocketData: NetSocketData,
    NetSocketReceivedData: NetSocketReceivedData,
    NetSocketSendData: NetSocketSendData,
    NetSocket: NetSocket,
    TcpServer: TcpServer,
    TcpScoket: TcpSocket,
    UdpSocket: UdpSocket,
    TcpConnect: TcpConnect,
    TcpListen: TcpListen,
    UdpCast: UdpCast,
};


