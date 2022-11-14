module main

go 1.19

require (
	olooko.xyz/comm/netcomm v0.0.0
)

replace (
	olooko.xyz/comm/netcomm v0.0.0 => ./xyz/olooko/comm/netcomm
)
