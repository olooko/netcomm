const net = require('net');
const dgram = require('dgram');

const INT32_MAXVAL = 2147483647;
const FLOAT_MAXVAL = 3.40282347e+38;

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
                    argL = data.readInt16LE(datapos + 1);
                    break;
                case 4:
                    argL = data.readInt32LE(datapos + 1);
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
                                        this.#args.push(this.#data.readInt16LE(this.#datapos + 1));
                                        break;
                                    case 4:
                                        this.#args.push(this.#data.readInt32LE(this.#datapos + 1));
                                        break;					
                                    }
                                } else if ([0x54, 0x58].includes(this.#data[this.#datapos])) {
                                    sz = this.#data[this.#datapos] & 0x0F;
                                    switch (sz) {
                                    case 4:
                                        this.#args.push(this.#data.readFloatLE(this.#datapos + 1));
                                        break;
                                    case 8:
                                        this.#args.push(this.#data.readDoubleLE(this.#datapos + 1));
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

class NetSocketSendData {
    #command;
    #args;
    #bytes;

	constructor (command, args) {
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
					buffer.writeInt16LE(arg);
					text = Buffer.concat([text, buffer]);
				} else if (-2147483648 <= arg && arg <= 2147483647) {
					// 0011 0100
					text = Buffer.concat([text, Buffer.from([0x34])]);
					buffer = Buffer.alloc(4);
					buffer.writeInt32LE(arg);
					text = Buffer.concat([text, buffer]);
				} else {
					// 0011 1000
					text = Buffer.concat([text, Buffer.from([0x38])]);
					buffer = Buffer.alloc(8);
					buffer.writeInt64LE(arg);
					text = Buffer.concat([text, buffer]);					
				}
			} else if (typeof(arg) == 'number' && !Number.isInteger(arg)) {
				if (Math.abs(arg) <= FLOAT_MAXVAL) {
					// 0101 0100
					text = Buffer.concat([text, Buffer.from([0x54])]);
					buffer = Buffer.alloc(4);
					buffer.writeFloatLE(arg);
					text = Buffer.concat([text, buffer]);					
				} else {
					// 0101 1000
					text = Buffer.concat([text, Buffer.from([0x58])]);
					buffer = Buffer.alloc(8);
					buffer.writeDoubleLE(arg);
					text = Buffer.concat([text, buffer]);					
				}
			} else if (typeof(arg) == 'boolean') {
				// 0111 0001
				text = Buffer.concat([text, Buffer.from([0x71])]);
				text = Buffer.concat([text, Buffer.from([arg?0x01:0x00])]);
			} else if (typeof(arg) == 'string') {
				let str = Buffer.from(arg, 'utf8');
				let argL = arg.length;
				if (argL <= INT32_MAXVAL) {
					if (argL <= 0x7F) {
						// 1001 0001
						text = Buffer.concat([text, Buffer.from([0x91])]);
						buffer = Buffer.alloc(1);
						buffer.writeInt8(argL);
						text = Buffer.concat([text, buffer]);						
					} else if (argL <= 0x7FFF) {
						// 1001 0010
						text = Buffer.concat([text, Buffer.from([0x92])]);
						buffer = Buffer.alloc(2);
						buffer.writeInt16LE(argL);
						text = Buffer.concat([text, buffer]);	
					} else if (argL <= 0x7FFFFFFF) {
						// 1001 0100
						text = Buffer.concat([text, Buffer.from([0x94])]);
						buffer = Buffer.alloc(4);
						buffer.writeInt32LE(argL);
						text = Buffer.concat([text, buffer]);							
					}
					text = Buffer.concat([text, str]);
				}
			} else if (typeof(arg) == 'object' && Buffer.isBuffer(arg)) {
				let argL = arg.length;
				if (argL <= INT32_MAXVAL) {
					if (argL <= 0x7F) {
						// 1011 0001
						text = Buffer.concat([text, Buffer.from([0xB1])]);						
						buffer = Buffer.alloc(1);
						buffer.writeInt8(argL);
						text = Buffer.concat([text, buffer]);	
					} else if (argL <= 0x7FFF) {
						// 1011 0010
						text = Buffer.concat([text, Buffer.from([0xB2])]);
						buffer = Buffer.alloc(2);
						buffer.writeInt16LE(argL);
						text = Buffer.concat([text, buffer]);	
					} else if (argL <= 0x7FFFFFFF) {
						// 1011 0100
						text = Buffer.concat([text, Buffer.from([0xB4])]);
						buffer = Buffer.alloc(4);
						buffer.writeInt32LE(argL);
						text = Buffer.concat([text, buffer]);							
					}
					text = Buffer.concat([text, arg]);
				}
			} 
		}
	
		let data = Buffer.alloc(0);
	
		// start of header
		data = Buffer.concat([data, Buffer.from([0x01])]);
	
		let textL = text.length;
		if (textL <= INT32_MAXVAL) {
			if (textL <= 0x7F) {
				// 0001 0001
				data = Buffer.concat([data, Buffer.from([0x11])]);
				buffer = Buffer.alloc(1);
				buffer.writeInt8(textL);
				data = Buffer.concat([data, buffer]);					
			} else if (textL <= 0x7FFF) {
				// 0001 0010
				data = Buffer.concat([data, Buffer.from([0x12])]);
				buffer = Buffer.alloc(2);
				buffer.writeInt16LE(textL);
				data = Buffer.concat([data, buffer]);	
			} else if (textL <= 0x7FFFFFFF) {
				// 0001 0100
				data = Buffer.concat([data, Buffer.from([0x14])]);
				buffer = Buffer.alloc(4);
				buffer.writeInt32LE(textL);
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
		}
		
        this.#bytes = data;
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
    #address;
    #result;

	constructor (s, protocol) {
        const INTRPT_TM = 4000;

        this.#socket = s;
        this.#protocol = protocol;
        this.#data = new NetSocketData();
        this.#address = new NetSocketAddress(s.localAddress, s.localPort);
        this.#result = NetSocketDataManipulationResult.NoData;
    }

    isAvailable = () => {
        return this.#socket != null;
    };

    getLocalAddress = () => {
        return this.#address;
    };
    
    getProtocolType = () => {
        return this.#protocol;
    };        

    close = () => {
        if (this.#protocol == NetSocketProtocolType.Tcp) {
            this.#socket.end();
            this.#socket.destroy();
        } else if (this.#protocol == NetSocketProtocolType.Udp) {
            this.#socket.close();
        }
    };

    #process = (buffer, callback, remoteAddress) => {
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
                }, INTRPT_TM);
                break;
            } else if (this.#result == NetSocketDataManipulationResult.NoData) {
                break;				
            }
        }
    };

    setReceivedCallback = (callback) => {
        if (this.#protocol == NetSocketProtocolType.Tcp) {
            const remoteAddress = new NetSocketAddress(this.#socket.remoteAddress, this.#socket.remotePort);
            this.#socket.on('data', (buffer) => {
                this.#process(buffer, callback, remoteAddress);
            });
            this.#socket.on('error', () => {
            });
        } else if (this.#protocol == NetSocketProtocolType.Udp) {
            this.#socket.on('message', (buffer, rinfo) => {
                const remoteAddress = new NetSocketAddress(rinfo.address.replace('::ffff:', ''), rinfo.port);
                this.#process(buffer, callback, remoteAddress);
            });
            this.#socket.on('error', () => {
            });
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
        this.#address = null;

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
        this.#socket.write(data.getBytes(), 'utf8');
    };
}

class UdpSocket extends NetSocket {
    #socket;

	constructor (s) {
        super(s, NetSocketProtocolType.Udp);

        this.#socket = s;
    }

    send = (data, address) => {
        this.#socket.send(data.getBytes(), address.getPort(), address.getHost());
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


