const { Worker, isMainThread, parentPort } = require('worker_threads');
const { 
	NetSocketAddress, 
	NetSocketSendData, 
	NetSocketReceivedDataResult, 
	NetSocketSendDataBuildResult,
	TcpConnect,
	TcpListen,
	UdpCast, 
	NetSocketProtocolType
} = require('./xyz/olooko/comm/netcomm');

async function UdpSocketProc() {
    const udpsocket = await UdpCast(new NetSocketAddress('127.0.0.1', 10010));
    if (udpsocket.isAvailable()) {
        console.log(`NetworkComm.UdpSocket Started. ${udpsocket.getLocalAddress()}`);
        udpsocket.setReceivedCallback(NetSocketReceivedCallback);
		data = new NetSocketSendData(0x88, [-256, true, 'Hello', -1.1, Buffer.from([0x41, 0x42, 0x43])]);
		if (data.getBuildResult() == NetSocketSendDataBuildResult.Successful) {
			setInterval(() => {
				udpsocket.send(data, new NetSocketAddress('127.0.0.1', 10010));
			}, 5000);
		}
	}
}

async function TcpServerProc() {
    const tcpserver = await TcpListen(new NetSocketAddress('127.0.0.1', 10010));
    if (tcpserver.isRunning()) {
		console.log('NetworkComm.TcpServer Started.');
        tcpserver.setAcceptCallback(TcpServerAcceptCallback);
	}
}

async function TcpClientProc() {
	const tcpsocket = await TcpConnect(new NetSocketAddress('127.0.0.1', 10010));
	if (tcpsocket.isAvailable()) {
		console.log(`NetworkComm.TcpClient Started. ${tcpsocket.getLocalAddress()}`);
		tcpsocket.setReceivedCallback(NetSocketReceivedCallback);
		data = new NetSocketSendData(0x88, [-256, true, 'Hello', -1.1, Buffer.from([0x41, 0x42, 0x43])]);
		if (data.getBuildResult() == NetSocketSendDataBuildResult.Successful) {
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
		console.log(`NetworkComm.TcpClient Accepted. ${tcpsocket.getRemoteAddress()}`);
		tcpsocket.setReceivedCallback(NetSocketReceivedCallback);
	}
}

function NetSocketReceivedCallback(socket, data) {
	if (data.getResult() == NetSocketReceivedDataResult.Completed) {
		if (data.getCommand() == 0x88) {
			let a1 = data.getArgs()[0];
            let a2 = data.getArgs()[1];
            let a3 = data.getArgs()[2];
            let a4 = data.getArgs()[3];   
			
			let a5 = "";
			let ba = data.getArgs()[4];
			for (let i = 0; i < ba.length; i++) {
				if (a5 != "") a5 += ",";
                a5 += "0x" + ba[i].toString(16);
			} 

			let protocol = "";
			if (socket.getProtocolType() == NetSocketProtocolType.Tcp) {
				protocol = "TCP";
			} else if (socket.getProtocolType() == NetSocketProtocolType.Udp) {
				protocol = "UDP";
			}

			console.log(`${protocol} ${data.getRemoteAddress()} (${a1}, ${a2}, ${a3}, ${a4}, [${a5}])`);
		}
	} 
	else if (data.getResult() == NetSocketReceivedDataResult.Interrupted) {
		console.log("Interrupted");
	} 
	else if (data.getResult() == NetSocketReceivedDataResult.ParsingError) {
		console.log("Parsing-Error");
	} 
	else if (data.getResult() == NetSocketReceivedDataResult.Closed) {
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




