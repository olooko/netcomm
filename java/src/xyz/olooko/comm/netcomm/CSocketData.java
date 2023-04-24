package xyz.olooko.comm.netcomm;

import java.nio.ByteBuffer;
import java.util.Arrays;

public class CSocketData
{
    private byte _command;
    private CSocketDataArgs _args;
    private ByteBuffer _data;
    private int _datalen;
    private int _datapos;
    private CSocketDataParsingStep _step;
    private byte _checksum;
    private int _textlen;

    public CSocketDataArgs getArgs()
    {
        return _args;
    }
    
    public int getCommand() 
    {
        return _command & 0xFF;
    }

    public CSocketData()
    {
        _command = 0x00;
        _args = new CSocketDataArgs();
        _data = ByteBuffer.allocate(0);
        _datalen = 0;
        _datapos = 0;
        _checksum = 0x00;
        _step = CSocketDataParsingStep.SOH;
        _textlen = 0;
    }

    public void append(byte[] buffer, int bytesTransferred) 
    {
        if (_data.capacity() < _datalen + bytesTransferred) 
        {
            _data = ByteBuffer.allocate(_datalen + bytesTransferred).put(_data);
            _data.position(_datalen);
        }

        _data.put(buffer, 0, bytesTransferred);
        _datalen += bytesTransferred;
    }

    public CSocketDataManipulationResult manipulate()
    {
        while (true) 
        {
            int datalen = _datalen - _datapos;
            
            switch (_step) 
            {
                case SOH:
                    if (datalen > 0) 
                    {
                        if (_data.get(_datapos) == 0x01) 
                        {
                            _datapos += 1;
                            _step = CSocketDataParsingStep.OTL;
                            continue;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case OTL:
                    if (datalen > 0) 
                    {
                        if (Arrays.asList((byte)0x11, (byte)0x12, (byte)0x14).contains(_data.get(_datapos))) 
                        {
                            CSocketDataArgLength a = getArgLength(datalen);

                            if (a.getArgL() >= 0) 
                            {
                                _textlen = a.getArgL();
                                _datapos += 1 + a.getSize();
                                _step = CSocketDataParsingStep.STX;
                                continue;
                            }
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case STX:
                    if (datalen > 0) 
                    {
                        if (_data.get(_datapos) == 0x02) 
                        {
                            _datapos += 1;
                            _step = CSocketDataParsingStep.ETX;
                            continue;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case ETX:
                    if (datalen > _textlen) 
                    {
                        if (_data.get(_datapos + _textlen) == 0x03) 
                        {
                            int textfpos = _datapos;
                            
                            _command = _data.get(textfpos);
                            _args.clear();
                            _datapos += 1;

                            while (_datapos < _textlen + textfpos) 
                            {
                                int sz = 0;
                                int argL = 0;

                                if (Arrays.asList((byte)0x31, (byte)0x32, (byte)0x34, (byte)0x38).contains(_data.get(_datapos))) 
                                {
                                    sz = (int)(_data.get(_datapos) & 0x0F);
                                    long i = 0;
                                    switch (sz) 
                                    {
                                        case 1: i = (long)_data.get(_datapos + 1); break;
                                        case 2: i = (long)_data.getShort(_datapos + 1); break;
                                        case 4: i = (long)_data.getInt(_datapos + 1); break;
                                        case 8: i = (long)_data.getLong(_datapos + 1); break;
                                    }
                                    _args.add(new CInteger(i));
                                } 
                                else if (Arrays.asList((byte)0x54, (byte)0x58).contains(_data.get(_datapos))) 
                                {
                                    sz = (int)(_data.get(_datapos) & 0x0F);
                                    double f = 0.0;
                                    switch (sz) 
                                    {
                                        case 4: f = (double)_data.getFloat(_datapos + 1); break;
                                        case 8: f = _data.getDouble(_datapos + 1); break;
                                    }
                                    _args.add(new CFloat(f));
                                } 
                                else if (Arrays.asList((byte)0x71).contains(_data.get(_datapos))) 
                                {
                                    sz = 1;
                                    _args.add(new CBoolean((_data.get(_datapos + 1) == 0) ? false : true));
                                } 
                                else if (Arrays.asList((byte)0x91, (byte)0x92, (byte)0x94).contains(_data.get(_datapos)))
                                {
                                    CSocketDataArgLength a = getArgLength(datalen);
                                    sz = a.getSize();
                                    argL = a.getArgL();

                                    _args.add(new CString(new String(_data.array(), _datapos + 1 + sz, argL)));
                                    _datapos += argL;                                   
                                } 
                                else if (Arrays.asList((byte)0xB1, (byte)0xB2, (byte)0xB4).contains(_data.get(_datapos))) 
                                {
                                    CSocketDataArgLength a = getArgLength(datalen);
                                    sz = a.getSize();
                                    argL = a.getArgL();

                                    byte[] ba = new byte[argL];
                                    System.arraycopy(_data.array(), _datapos + 1 + sz, ba, 0, argL);

                                    _args.add(new CByteArray(ba));
                                    _datapos += argL;
                                    
                                } else {
                                    return CSocketDataManipulationResult.ParsingError;
                                }
                                _datapos += 1 + sz;
                            }

                            _checksum = 0x00;
                            for (int i = textfpos; i < textfpos + _textlen; i++)
                                _checksum ^= _data.get(i);

                            _datapos += 1;
                            _step = CSocketDataParsingStep.CHK;
                            continue;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case CHK:
                    if (datalen > 0) 
                    {
                        if (_data.get(_datapos) == _checksum) 
                        {
                            _datapos += 1;
                            _step = CSocketDataParsingStep.EOT;
                            continue;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;

                case EOT:
                    if (datalen > 0) 
                    {
                        if (_data.get(_datapos) == 0x04) 
                        {
                            _datapos += 1;
                            _datalen -= _datapos;

                            _data = ByteBuffer.allocate(_datalen).put(_data.array(), _datapos, _datalen);

                            _datapos = 0;
                            _checksum = 0x00;
                            _step = CSocketDataParsingStep.SOH;
                            _textlen = 0;
 
                            return CSocketDataManipulationResult.Completed;
                        } 
                        else {
                            return CSocketDataManipulationResult.ParsingError;
                        }
                    }
                    break;
            }

            if (_datalen == 0) {
                return CSocketDataManipulationResult.NoData;
            }

            return CSocketDataManipulationResult.InProgress;
        }
    }

    private CSocketDataArgLength getArgLength(int datalen)
    {
        int sz = (int)(_data.get(_datapos) & 0x0F);
        int argL = -1;

        if (datalen > sz) 
        {
            switch (sz) 
            {
                case 1: argL = (int)_data.get(_datapos + 1); break;
                case 2: argL = (int)_data.getShort(_datapos + 1); break;
                case 4: argL = (int)_data.getInt(_datapos + 1); break;
            }
        }
        return new CSocketDataArgLength(sz, argL);
    }    
}

