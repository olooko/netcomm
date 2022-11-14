const { Worker, isMainThread, parentPort } = require('worker_threads');
const { 
	NetSocketAddress, 
	NetSocketSendData, 
	NetSocketReceivedDataResult, 
	TcpConnect,
	TcpListen,
	UdpCast 
} = require('./xyz/olooko/comm/netcomm');


function tcpserver_proc() {
    const tcpserver = TcpListen(new NetSocketAddress('127.0.0.1', 10010));
    if (tcpserver.isStarted()) {
		console.log('NetworkComm.TcpServer Started...');
        tcpserver.setAcceptCallback(tcpserverAcceptCallback);
	}
}

function tcpclient_proc() {
	setTimeout(() => {
		const tcpsocket = TcpConnect(new NetSocketAddress('127.0.0.1', 10010));
		if (tcpsocket.isAvailable()) {
			console.log('NetworkComm.TcpSocket Started...');
			tcpsocket.setReceivedCallback(netsocketReceivedCallback);
			setInterval(() => {
				if (tcpsocket.isConnected()) { 
					data = new NetSocketSendData(0x88, [-256, true, 'Hello', -1.1, Buffer.from([0x41, 0x42, 0x43])]);
					tcpsocket.send(data);
				}
			}, 5000);
		}		
	}, 1000);
}

function udpsocket_proc() {
    const udpsocket = UdpCast(new NetSocketAddress('127.0.0.1', 10010));
    if (udpsocket.isAvailable()) {
        console.log('NetworkComm.UdpSocket Started...');
        udpsocket.setReceivedCallback(netsocketReceivedCallback);
		setInterval(() => {
			data = new NetSocketSendData(0x88, [-256, true, 'Hello', -1.1, Buffer.from([0x41, 0x42, 0x43])]);
            udpsocket.send(data, new NetSocketAddress('127.0.0.1', 10010));
		}, 5000);
	}
}

function tcpserverAcceptCallback(tcpsocket) {
	if (tcpsocket.isAvailable()) {
		console.log('NetworkComm.TcpSocket Accepted');
		tcpsocket.setReceivedCallback(netsocketReceivedCallback);
	}
}

function netsocketReceivedCallback(socket, data) {
	if (data.getResult() == NetSocketReceivedDataResult.Completed) {
		let protocol = socket.getProtocolType();
		let command = data.getCommand();
		let args = data.getArgs();
		console.log(`protocol: ${protocol}, command: ${command}, args: ${JSON.stringify(args)}`);
	} else if (data.getResult() == NetSocketReceivedDataResult.Interrupted) {
		console.log("Interrupted");
	} else if (data.getResult() == NetSocketReceivedDataResult.ParsingError) {
		console.log("parsing-error");
	} else if (data.getResult() == NetSocketReceivedDataResult.Closed) {
		console.log("close");
		socket.close();
	}
}

if (isMainThread) {
	const thread1 = new Worker(__filename);
	thread1.postMessage('tcpserver');

	const thread2 = new Worker(__filename);
	thread2.postMessage('tcpclient');

	const thread3 = new Worker(__filename);
	thread3.postMessage('udpsocket');
} else {
	parentPort.on('message', (value) => {
		switch (value) {
			case 'tcpserver':
				tcpserver_proc();
				break;
			case 'tcpclient':
				tcpclient_proc();
				break;
			case 'udpsocket':
				udpsocket_proc();
				break;					
		}
		parentPort.close();
	})
}




