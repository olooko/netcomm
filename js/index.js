const { Worker, isMainThread, parentPort } = require('worker_threads');
const { 
	CBoolean,
	CByteArray,
	CFloat,
	CInteger,
	CString,
	CSocketAddress, 
	CSocketDataArgs,
	CSocketSendData, 
	CSocketReceivedDataResult, 
	CSocketSendDataBuildResult,
	TcpConnect,
	TcpListen,
	UdpCast, 
	CSocketProtocolType
} = require('./xyz/olooko/comm/netcomm');

async function UdpSocketProc() {
	const address = new CSocketAddress('127.0.0.1', 10010);
    const udpsocket = await UdpCast(address);
    if (udpsocket.isAvailable()) {
        console.log(`NetworkComm.UdpSocket Started. ${udpsocket.getLocalAddress()}`);
        udpsocket.setReceivedCallback(CSocketReceivedCallback);
		args = new CSocketDataArgs();
		args.add(new CInteger(-256));
		args.add(new CBoolean(true));
		args.add(new CString('Hello'));
		args.add(new CFloat(-1.1));
		args.add(new CByteArray(Buffer.from([0x41, 0x42, 0x43])));
		data = new CSocketSendData(0x88, args);
		if (data.getBuildResult() == CSocketSendDataBuildResult.Successful) {
			setInterval(() => {
				udpsocket.send(data, address);
			}, 5000);
		}
	}
}

async function TcpServerProc() {
    const tcpserver = await TcpListen(new CSocketAddress('127.0.0.1', 10010));
    if (tcpserver.isRunning()) {
		console.log('TcpServer Started.');
        tcpserver.setAcceptCallback(TcpServerAcceptCallback);
	}
}

async function TcpClientProc() {
	const tcpsocket = await TcpConnect(new CSocketAddress('127.0.0.1', 10010));
	if (tcpsocket.isAvailable()) {
		console.log(`TcpClient Started. ${tcpsocket.getLocalAddress()}`);
		tcpsocket.setReceivedCallback(CSocketReceivedCallback);
		args = new CSocketDataArgs();
		args.add(new CInteger(-256));
		args.add(new CBoolean(true));
		args.add(new CString('Hello'));
		args.add(new CFloat(-1.1));
		args.add(new CByteArray(Buffer.from([0x41, 0x42, 0x43])));
		data = new CSocketSendData(0x88, args);
		if (data.getBuildResult() == CSocketSendDataBuildResult.Successful) {
			setInterval(() => {
				if (tcpsocket.isConnected()) { 
					tcpsocket.send(data);
				}
			}, 5000);
		}
	}
}

function TcpServerAcceptCallback(tcpsocket) {
	if (tcpsocket.isAvailable()) {
		console.log(`TcpClient Accepted. ${tcpsocket.getRemoteAddress()}`);
		tcpsocket.setReceivedCallback(CSocketReceivedCallback);
	}
}

function CSocketReceivedCallback(socket, data) {
	if (data.getResult() == CSocketReceivedDataResult.Completed) {
		if (data.getCommand() == 0x88) {
			args = data.getArgs();
			let a1 = args.at(0);
            let a2 = args.at(1);
            let a3 = args.at(2);
            let a4 = args.at(3);   
			let a5 = args.at(4);

			let protocol = "";
			if (socket.getProtocolType() == CSocketProtocolType.Tcp) {
				protocol = "TCP";
			} else if (socket.getProtocolType() == CSocketProtocolType.Udp) {
				protocol = "UDP";
			}

			console.log(`${protocol} ${data.getRemoteAddress()} (${a1}, ${a2}, ${a3}, ${a4}, [${a5}])`);
		}
	} 
	else if (data.getResult() == CSocketReceivedDataResult.Interrupted) {
		console.log("Interrupted");
	} 
	else if (data.getResult() == CSocketReceivedDataResult.ParsingError) {
		console.log("Parsing-Error");
	} 
	else if (data.getResult() == CSocketReceivedDataResult.Closed) {
		console.log("Close");
		socket.close();
	}
}

if (isMainThread) {
	const thread1 = new Worker(__filename);
	thread1.postMessage('udpsocket');

	setTimeout(() => {
		const thread2 = new Worker(__filename);
		thread2.postMessage('tcpserver');
	}, 1000);

	setTimeout(() => {
		const thread3 = new Worker(__filename);
		thread3.postMessage('tcpclient');
	}, 2000);
} else {
	parentPort.on('message', (value) => {
		switch (value) {
			case 'udpsocket':
				UdpSocketProc();
				break;				
			case 'tcpserver':
				TcpServerProc();
				break;
			case 'tcpclient':
				TcpClientProc();
				break;
		}
		parentPort.close();
	})
}




