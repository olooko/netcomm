const net = require('net');
const dgram = require('dgram');

class IDataType {
    #value;
    constructor(value) {
        this.#value = value;
    }

    getValue() {
        return this.#value;
    }

    toString() {
        return this.#value.toString();
    }
}

class CBoolean extends IDataType {
    constructor(value) {
        super(value);
    }
}

class CByteArray extends IDataType {
    #value;
    constructor(value) {
        super(value);
        this.#value = value;
    }
    toString() {
        let s = "";
        let ba = this.#value;
        for (let i = 0; i < ba.length; i++) {
            if (s != "") s += ",";
            s+= "0x" + ba[i].toString(16);
        }  
        return s;       
    }
}

class CFloat extends IDataType {
    constructor(value) {
        super(value);
    }
}

class CInteger extends IDataType {
    constructor(value) {
        super(value);
    }
}

class CString extends IDataType {
    constructor(value) {
        super(value);
    }
}

class CSocket {
    #socket;
    #protocol;
    #data;
    #localAddress;
    #result;

	constructor(s, protocol) {
        this.#socket = s;
        this.#protocol = protocol;
        this.#data = new CSocketData();
        this.#result = CSocketDataManipulationResult.NoData;
        this.#localAddress = new CSocketAddress("0.0.0.0", 0);

        if (this.constructor === CSocket) {
            throw new Error("Create an instance of an abstract class.");
        }

        if (this.isAvailable()) {
            if (this.#protocol == CSocketProtocolType.Tcp) {
                this.#localAddress = new CSocketAddress(s.localAddress, s.localPort)
            } 
            else if (this.#protocol == CSocketProtocolType.Udp) {
                this.#localAddress = new CSocketAddress(s.address().address, s.address().port);
            }
        }
    }

    isAvailable() {
        return this.#socket != null;
    }

    getLocalAddress() {
        return this.#localAddress;
    }
    
    getProtocolType() {
        return this.#protocol;
    }       

    close() {
        if (this.isAvailable()) {
            if (this.#protocol == CSocketProtocolType.Tcp) {
                this.#socket.end();
                this.#socket.destroy();
            } 
            else if (this.#protocol == CSocketProtocolType.Udp) {
                this.#socket.close();
            }
        }
    }

    setReceivedCallback(callback) {
        if (this.isAvailable()) {
            if (this.#protocol == CSocketProtocolType.Tcp) {
                const remoteAddress = new CSocketAddress(this.#socket.remoteAddress, this.#socket.remotePort);
                this.#socket.on('data', (buffer) => {
                    this.#receiveProc(buffer, callback, remoteAddress);
                });
                this.#socket.on('error', () => {
                });
            } 
            else if (this.#protocol == CSocketProtocolType.Udp) {
                this.#socket.on('message', (buffer, rinfo) => {
                    const remoteAddress = new CSocketAddress(rinfo.address, rinfo.port);
                    this.#receiveProc(buffer, callback, remoteAddress);
                });
                this.#socket.on('error', () => {
                });
            }
        }   
    }
    
    #receiveProc(buffer, callback, remoteAddress) {
        this.#data.append(buffer);

        while (true) {
            this.#result = this.#data.manipulate();
            if (this.#result == CSocketDataManipulationResult.Completed) {
                callback(this, new CSocketReceivedData(this.#data.getCommand(), this.#data.getArgs(), CSocketReceivedDataResult.Completed, remoteAddress));
                continue;
            } 
            else if (this.#result == CSocketDataManipulationResult.ParsingError) {
                callback(this, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.ParsingError, remoteAddress));
                return;
            } 
            else if (this.#result == CSocketDataManipulationResult.InProgress) {
                let me = this;
                setTimeout(() => {
                    if (this.#result == CSocketDataManipulationResult.InProgress) {
                        callback(me, new CSocketReceivedData(0x00, new CSocketDataArgs(), CSocketReceivedDataResult.Interrupted, remoteAddress));
                    }
                }, 15000);
                break;
            } 
            else if (this.#result == CSocketDataManipulationResult.NoData) {
                break;				
            }
        }
    }
}

class CSocketAddress {
    #host;
    #port;

	constructor(host, port) {
        this.#host = host;
        this.#port = port;
	}

    getHost() {
        return this.#host;
    }
    
    getPort() {
        return this.#port;
    }  

    toString() {
        return `${this.#host}:${this.#port}`;
    }
}

class CSocketData {
    #command = 0x00;
    #args = new CSocketDataArgs();	
    #data = Buffer.alloc(0);
    #datapos = 0;
    #checksum = 0x00;
    #step = CSocketDataParsingStep.SOH;
    #textL = 0;	

	constructor() {
    }

    getArgs() {
        return this.#args;
    }  

    getCommand() {
        return this.#command;
    }

    append(buffer) {
        this.#data = Buffer.concat([this.#data, buffer]);
    }

    manipulate() {
        while (true) {
            let datalen = this.#data.length - this.#datapos;
            switch (this.#step) {
            case CSocketDataParsingStep.SOH:
                if (datalen > 0) {
                    if (this.#data[this.#datapos] == 0x01) {
                        this.#datapos += 1;
                        this.#step = CSocketDataParsingStep.OTL;
                        continue;
                    } 
                    else {
                        return CSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case CSocketDataParsingStep.OTL:
                if (datalen > 0) {
                    if ([0x11, 0x12, 0x14].includes(this.#data[this.#datapos])) {
                        let a = this.#getArgLength(datalen);
                        if (a.getArgLength() >= 0) {
                            this.#textL = a.getArgLength();
                            this.#datapos += 1 + a.getSize();
                            this.#step = CSocketDataParsingStep.STX;
                            continue;
                        }
                    } 
                    else {
                        return CSocketDataManipulationResult.ParsingError;						
                    }
                } 
                break;
            case CSocketDataParsingStep.STX:
                if (datalen > 0) {
                    if (this.#data[this.#datapos] == 0x02) {
                        this.#datapos += 1;
                        this.#step = CSocketDataParsingStep.ETX;
                        continue;
                    } 
                    else {
                        return CSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case CSocketDataParsingStep.ETX:
                if (datalen > this.#textL) {
                    if (this.#data[this.#datapos + this.#textL] == 0x03) {
                        this.#args.clear();
                        let textfpos = this.#datapos;
                        this.#command = this.#data[textfpos];
                        this.#datapos += 1;
                        while (this.#datapos < this.#textL + textfpos) {
                            let sz = 0;
                            let argL = 0;
                            if ([0x31, 0x32, 0x34].includes(this.#data[this.#datapos])) {
                                sz = this.#data[this.#datapos] & 0x0F;
                                let i = 0;
                                switch (sz) {
                                case 1:
                                    i = this.#data.readInt8(this.#datapos + 1);
                                    break;
                                case 2:
                                    i = this.#data.readInt16BE(this.#datapos + 1);
                                    break;
                                case 4:
                                    i = this.#data.readInt32BE(this.#datapos + 1);
                                    break;					
                                }
                                this.#args.add(new CInteger(i));
                            } 
                            else if ([0x54, 0x58].includes(this.#data[this.#datapos])) {
                                sz = this.#data[this.#datapos] & 0x0F;
                                let f = 0.0;
                                switch (sz) {
                                case 4:
                                    f = this.#data.readFloatBE(this.#datapos + 1);
                                    break;
                                case 8:
                                    f = this.#data.readDoubleBE(this.#datapos + 1);
                                    break;				
                                }
                                this.#args.add(new CFloat(f));
                            } 
                            else if ([0x71].includes(this.#data[this.#datapos])) {
                                sz = 1
                                this.#args.add(new CBoolean((this.#data[this.#datapos + 1] == 1)? true: false));
                            } 
                            else if ([0x91, 0x92, 0x94].includes(this.#data[this.#datapos])) {
                                let a = this.#getArgLength(datalen);
                                sz = a.getSize();
                                argL = a.getArgLength();
                                let s = this.#data.subarray(this.#datapos + 1 + sz, this.#datapos + 1 + sz + argL).toString('utf8');
                                this.#args.add(new CString(s));
                                this.#datapos += argL;
                            } 
                            else if ([0xB1, 0xB2, 0xB4].includes(this.#data[this.#datapos])) {
                                let a = this.#getArgLength(datalen);
                                sz = a.getSize();
                                argL = a.getArgLength();
                                let ba = this.#data.subarray(this.#datapos + 1 + sz, this.#datapos + 1 + sz + argL)
                                this.#args.add(new CByteArray(ba));
                                this.#datapos += argL;
                            } 
                            else {
                                return CSocketDataManipulationResult.ParsingError;
                            }
                            this.#datapos += 1 + sz;
                        }

                        this.#checksum = 0x00;
                        for (let i = textfpos; i < textfpos + this.#textL; i++) {
                            this.#checksum ^= this.#data[i];
                        }  
                        this.#datapos += 1;
                        this.#step = CSocketDataParsingStep.CHK;
                        continue;
                    }
                    else {
                        return CSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case CSocketDataParsingStep.CHK:
                
                if (datalen > 0) {
                    if (this.#data[this.#datapos] == this.#checksum) {
                        this.#datapos += 1;
                        this.#step = CSocketDataParsingStep.EOT;
                        continue;
                    } 
                    else {
                        return CSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;
            case CSocketDataParsingStep.EOT:
                if (datalen > 0) {
                    if (this.#data[this.#datapos] == 0x04) {
                        this.#datapos += 1;
                        this.#data = this.#data.subarray(this.#datapos);

                        this.#datapos = 0;
                        this.#checksum = 0x00;
                        this.#step = CSocketDataParsingStep.SOH;
                        this.#textL = 0;	

                        return CSocketDataManipulationResult.Completed;
                    } 
                    else {
                        return CSocketDataManipulationResult.ParsingError;
                    }
                } 
                break;																	
            }

            if (this.#data.length == 0)
                return CSocketDataManipulationResult.NoData;

            return CSocketDataManipulationResult.InProgress;
        };
    }

    #getArgLength(datalen) {
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
        return new CSocketDataArgLength(sz, argL);
    }      
}

class CSocketDataArgLength {
    #sz;
    #argL;

    constructor(sz, argL) {
        this.#sz = sz;
        this.#argL = argL;
    }

    getSize() {
        return this.#sz;
    }

    getArgLength() {
        return this.#argL;
    }
}

class CSocketDataArgs {
    #list = [];

    constructor() {
    }

    getLength() {
        return this.#list.length;
    }

    add(arg) {
        this.#list.push(arg);
    } 

    at(index) {
        return this.#list[index];
    }

    clear() {
        this.#list.length = 0;
    }  
}

const CSocketDataManipulationResult = {
    Completed: "completed",
    InProgress: "in-progress",
    NoData: "no-data",
    ParsingError: "parsing-error",
}

const CSocketDataParsingStep = {
	SOH: "soh",
	OTL: "otl",
	STX: "stx",
	ETX: "etx",
	CHK: "chk",
	EOT: "eot",
}

const CSocketProtocolType = {
	Tcp: "tcp",
	Udp: "udp",
}

class CSocketReceivedData {
    #command;
    #args;
    #result;
    #remoteAddress;

	constructor(command, args, result, address) {
        this.#command = command;
        this.#args = args;
        this.#result = result;
        this.#remoteAddress = address;
    }

    getArgs() {
        return this.#args;
    }

    getCommand() {
        return this.#command;
    }
    
    getResult() {
        return this.#result;
    }     
    
    getRemoteAddress() {
        return this.#remoteAddress;
    }       
}

const CSocketReceivedDataResult = {
    Closed: "closed",
    Completed: "completed",
    Interrupted: "interrupted",
    ParsingError: "parsing-error",
}

class CSocketSendData {
    #command;
    #args;
    #bytes;
    #result;

	constructor(command, args) {
        const ARG_MAXLEN = 0x7FFFFF - 5;
        const TXT_MAXLEN = 0x7FFFFFFF - 10;

        this.#result = CSocketSendDataBuildResult.NoData;

        if (command < 0x00 || command > 0xFF) {
            this.#result = CSocketSendDataBuildResult.CommandValueOverflowError;
            return;
        }
        
        this.#command = command;
        this.#args = args;

		let text = Buffer.alloc(0);
		text = Buffer.concat([text, Buffer.from([command])]);
	
		let buffer;
		for (let x = 0; x < args.getLength(); x++) {
			let arg = args.at(x);
			if (arg.constructor.name == 'CInteger') {
                let i = arg.getValue();
				if (-128 <= i && i <= 127) {
					text = Buffer.concat([text, Buffer.from([0x31])]);
					buffer = Buffer.alloc(1);
					buffer.writeInt8(i);
					text = Buffer.concat([text, buffer]);
				} 
                else if (-32768 <= i && i <= 32767) {
					text = Buffer.concat([text, Buffer.from([0x32])]);
					buffer = Buffer.alloc(2);
					buffer.writeInt16BE(i);
					text = Buffer.concat([text, buffer]);
				} 
                else if (-2147483648 <= i && i <= 2147483647) {
					text = Buffer.concat([text, Buffer.from([0x34])]);
					buffer = Buffer.alloc(4);
					buffer.writeInt32BE(i);
					text = Buffer.concat([text, buffer]);
				} 
                else {
					text = Buffer.concat([text, Buffer.from([0x38])]);
					buffer = Buffer.alloc(8);
					buffer.writeInt64BE(i);
					text = Buffer.concat([text, buffer]);					
				}
			} 
            else if (arg.constructor.name == 'CFloat') {
                let f = arg.getValue(); 
				if (Math.abs(f) <= 3.40282347e+38) {
					text = Buffer.concat([text, Buffer.from([0x54])]);
					buffer = Buffer.alloc(4);
					buffer.writeFloatBE(f);
					text = Buffer.concat([text, buffer]);					
				} 
                else {
					text = Buffer.concat([text, Buffer.from([0x58])]);
					buffer = Buffer.alloc(8);
					buffer.writeDoubleBE(f);
					text = Buffer.concat([text, buffer]);					
				}
			} 
            else if (arg.constructor.name == 'CBoolean') {
                let b = arg.getValue();
				text = Buffer.concat([text, Buffer.from([0x71])]);
				text = Buffer.concat([text, Buffer.from([b?0x01:0x00])]);
			} 
            else if (arg.constructor.name == 'CString') {
				let s = Buffer.from(arg.getValue(), 'utf8');
				let argL = s.length;
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
					text = Buffer.concat([text, s]);
				} 
                else {
                    this.#result = CSocketSendDataBuildResult.StringLengthOverflowError;
                    return;
                }
			} 
            else if (arg.constructor.name == 'CByteArray') {
                let ba = arg.getValue();
				let argL = ba.length;
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
					text = Buffer.concat([text, ba]);
				} 
                else {
                    this.#result = CSocketSendDataBuildResult.ByteArrayLengthOverflowError;
                    return;
                }
			} 
            else {
                this.#result = CSocketSendDataBuildResult.DataTypeNotImplementedError;
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
            this.#result = CSocketSendDataBuildResult.DataTotalLengthOverflowError;
            return;
        }
		
        this.#bytes = data;
        this.#result = CSocketSendDataBuildResult.Successful;
    }

    getArgs() {
        return this.#args;
    }
    
    getBuildResult() {
        return this.#result;
    }

    getBytes() {
        return this.#bytes;
    }

    getCommand() {
        return this.#command;
    }       

    getLength() {
        return this.#bytes.length;
    }
}

const CSocketSendDataBuildResult = {
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

    constructor(s) {
        this.#server = s;
    }

    isRunning() {
        return this.#server != null;
    }

    close() {
        try {
            this.#server.close();
            this.#server = null;
        } catch {}       
    }

    setAcceptCallback(callback) {
        this.#server.on('connection', (socket) => {
            callback(new TcpSocket(socket));
        });
    }
}

class TcpSocket extends CSocket {
    #socket;
    #connected;
    #address;

	constructor(s) {
        super(s, CSocketProtocolType.Tcp);

        this.#socket = s;
        this.#connected = (this.#socket != null);
        this.#address = new CSocketAddress('0.0.0.0', 0);

        if (this.#socket != null) {
            this.#address = new CSocketAddress(this.#socket.remoteAddress, this.#socket.remotePort);
        }
    }

    isConnected() {
        return (this.#socket != null) && this.#connected;
    }

    getRemoteAddress() {
        return this.#address;
    }

    send(data) {
        if (this.isAvailable()) {
            this.#socket.write(data.getBytes(), 'utf8');
        }
    }
}

class UdpSocket extends CSocket {
    #socket;

	constructor(s) {
        super(s, CSocketProtocolType.Udp);
        this.#socket = s;
    }

    send(data, address) {
        if (this.isAvailable()) {
            this.#socket.send(data.getBytes(), address.getPort(), address.getHost());
        }
    }    
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
    CBoolean: CBoolean,
    CByteArray: CByteArray,
    CFloat: CFloat,
    CInteger: CInteger,
    CString: CString,
    CSocket: CSocket,
    CSocketAddress: CSocketAddress,
    CSocketData: CSocketData,
    CSocketDataArgs: CSocketDataArgs,
    CSocketDataManipulationResult: CSocketDataManipulationResult,
    CSocketDataParsingStep: CSocketDataParsingStep,
    CSocketProtocolType: CSocketProtocolType,
    CSocketReceivedData: CSocketReceivedData,
    CSocketReceivedDataResult: CSocketReceivedDataResult,
    CSocketSendData: CSocketSendData,
    CSocketSendData: CSocketSendData,
    CSocketSendDataBuildResult: CSocketSendDataBuildResult,
    TcpServer: TcpServer,
    TcpScoket: TcpSocket,
    UdpSocket: UdpSocket,
    TcpConnect: TcpConnect,
    TcpListen: TcpListen,
    UdpCast: UdpCast,
};


