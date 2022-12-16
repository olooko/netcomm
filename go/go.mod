module main

go 1.19

require (
	olooko.xyz/comm/netcomm v0.4.2
)

replace (
	olooko.xyz/comm/netcomm v0.4.2 => ./xyz/olooko/comm
)
