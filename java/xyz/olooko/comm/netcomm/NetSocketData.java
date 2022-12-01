package xyz.olooko.comm.netcomm;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.Arrays;

class NetSocketDataArgLength {

    private int _sz;
    public int getSize() {
        return _sz;
    }

    private int _argL;
    public int getArgLength() {
        return _argL;
    }

    public NetSocketDataArgLength(int sz, int argL) {
        _sz = sz;
        _argL = argL;
    }
}

public class NetSocketData {

    private ByteBuffer _data;
    private int _datalen;
    private int _datapos;

    private NetSocketDataParsingStep _step;
    private byte _checksum;
    private int _textlen;

    private ArrayList<Object> _args;
    public Object[] getArgs() {
        return _args.toArray();
    }

    private byte _command;
    public byte getCommand() {
        return _command;
    }

    public NetSocketData() {
        _command = 0x00;
        _args = new ArrayList<Object>();

        _data = ByteBuffer.allocate(0);
        _datalen = 0;
        _datapos = 0;
        _checksum = 0x00;
        _step = NetSocketDataParsingStep.SOH;
        _textlen = 0;
    }

    private NetSocketDataArgLength getArgLength(ByteBuffer data, int datalen, int datapos) {
        int sz = (int)(data.get(datapos) & 0x0F);
        int argL = -1;

        if (datalen > sz) {
            switch (sz) {
                case 1: argL = (int)data.get(datapos + 1); break;
                case 2: argL = (int)data.order(ByteOrder.LITTLE_ENDIAN).getShort(datapos + 1); break;
                case 4: argL = (int)data.order(ByteOrder.LITTLE_ENDIAN).getInt(datapos + 1); break;
            }
        }
        return new NetSocketDataArgLength(sz, argL);
    }

    public void Append(byte[] buffer, int bytesTransferred) {

        if (_data.capacity() < _datalen + bytesTransferred) {
            _data = ByteBuffer.allocate(_datalen + bytesTransferred).put(_data);
            _data.position(_datalen);
        }
        _data.put(buffer, 0, bytesTransferred);
        _datalen += bytesTransferred;
    }

    public NetSocketDataManipulationResult Manipulate() {
        while (true) {
            int datalen = _datalen - _datapos;
            
            switch (_step) {
                case SOH:
                    if (datalen > 0) {
                        if (_data.get(_datapos) == 0x01) {
                            _datapos += 1;
                            _step = NetSocketDataParsingStep.OTL;
                            continue;
                        } else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case OTL:
                    if (datalen > 0) {
                        if (Arrays.asList((byte)0x11, (byte)0x12, (byte)0x14).contains(_data.get(_datapos))) {
                            NetSocketDataArgLength a = getArgLength(_data, datalen, _datapos);
                            if (a.getArgLength() >= 0) {
                                _textlen = a.getArgLength();
                                _datapos += 1 + a.getSize();
                                _step = NetSocketDataParsingStep.STX;
                                continue;
                            }
                        } else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case STX:
                    if (datalen > 0) {
                        if (_data.get(_datapos) == 0x02) {
                            _datapos += 1;
                            _step = NetSocketDataParsingStep.ETX;
                            continue;
                        } else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case ETX:
                    if (datalen > _textlen) {
                        if (_data.get(_datapos + _textlen) == 0x03) {
                            try {
                                _args.clear();
                                int textfpos = _datapos;
                                _command = _data.get(textfpos);
                                _datapos += 1;
                                while (_datapos < _textlen + textfpos) {
                                    int sz = 0;
                                    if (Arrays.asList((byte)0x31, (byte)0x32, (byte)0x34, (byte)0x38).contains(_data.get(_datapos))) {
                                        sz = (int)(_data.get(_datapos) & 0x0F);
                                        switch (sz) {
                                            case 1: _args.add((byte)_data.get(_datapos + 1)); break;
                                            case 2: _args.add(_data.order(ByteOrder.LITTLE_ENDIAN).getShort(_datapos + 1)); break;
                                            case 4: _args.add(_data.order(ByteOrder.LITTLE_ENDIAN).getInt(_datapos + 1)); break;
                                            case 8: _args.add(_data.order(ByteOrder.LITTLE_ENDIAN).getLong(_datapos + 1)); break;
                                        }
                                    } else if (Arrays.asList((byte)0x54, (byte)0x58).contains(_data.get(_datapos))) {
                                        sz = (int)(_data.get(_datapos) & 0x0F);
                                        switch (sz) {
                                            case 4: _args.add(_data.order(ByteOrder.LITTLE_ENDIAN).getFloat(_datapos + 1)); break;
                                            case 8: _args.add(_data.order(ByteOrder.LITTLE_ENDIAN).getDouble(_datapos + 1)); break;
                                        }
                                    } else if (Arrays.asList((byte)0x71).contains(_data.get(_datapos))) {
                                        sz = 1;
                                        _args.add((_data.get(_datapos + 1) == 0) ? false : true);
                                    } else if (Arrays.asList((byte)0x91, (byte)0x92, (byte)0x94).contains(_data.get(_datapos))) {
                                        NetSocketDataArgLength a = getArgLength(_data, datalen, _datapos);
                                        _args.add(new String(_data.array(), _datapos + 1 + a.getSize(), a.getArgLength()));
                                        _datapos += a.getArgLength();
                                        sz = a.getSize();
                                    } else if (Arrays.asList((byte)0xB1, (byte)0xB2, (byte)0xB4).contains(_data.get(_datapos))) {
                                        NetSocketDataArgLength a = getArgLength(_data, datalen, _datapos);
                                        byte[] ba = new byte[a.getArgLength()];
                                        System.arraycopy(_data.array(), _datapos + 1 + a.getSize(), ba, 0, a.getArgLength());
                                        _args.add(ba);
                                        _datapos += a.getArgLength();
                                        sz = a.getSize();
                                    } else {
                                        return NetSocketDataManipulationResult.ParsingError;
                                    }
                                    _datapos += 1 + sz;
                                }

                                _checksum = 0x00;
                                for (int i = textfpos; i < textfpos + _textlen; i++)
                                    _checksum ^= _data.get(i);

                                _datapos += 1;
                                _step = NetSocketDataParsingStep.CHK;
                                continue;
                            }
                            catch(Exception e) {
                                return NetSocketDataManipulationResult.ParsingError;
                            }
                        } else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case CHK:
                    if (datalen > 0) {
                        if (_data.get(_datapos) == _checksum) {
                            _datapos += 1;
                            _step = NetSocketDataParsingStep.EOT;
                            continue;
                        } else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case EOT:
                    if (datalen > 0) {
                        if (_data.get(_datapos) == 0x04) {
                            _datapos += 1;
                            _datalen -= _datapos;

                            _data = ByteBuffer.allocate(_datalen).put(_data.array(), _datapos, _datalen);

                            _datapos = 0;
                            _checksum = 0x00;
                            _step = NetSocketDataParsingStep.SOH;
                            _textlen = 0;
 
                            return NetSocketDataManipulationResult.Completed;
                        } else {
                            return NetSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;
            }

            if (_datalen == 0) {
                return NetSocketDataManipulationResult.NoData;
            }

            return NetSocketDataManipulationResult.InProgress;
        }
    }
}

