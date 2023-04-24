package xyz.olooko.comm.netcomm;

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;

class CSocketDataStream extends ByteArrayOutputStream
{
    public byte[] getBuffer() 
    {
        return super.buf;
    }

    public int getCount() 
    {
        return super.count;
    }
}

public class CSocketSendData
{
    private static final int ARG_MAXLEN = 0x7FFFFF - 5;
    private static final int TXT_MAXLEN = Integer.MAX_VALUE - 10;

    private byte _command;
    private CSocketDataArgs _args;
    private byte[] _bytes;
    private CSocketSendDataBuildResult _result;

    public CSocketDataArgs getArgs()
    {
        return _args;
    }
    
    public CSocketSendDataBuildResult getBuildResult()
    {
        return _result;
    }

    public byte[] getBytes() 
    {
        return _bytes;
    }
    
    public int getCommand() 
    {
        return _command & 0xFF;
    }

    public int getLength() 
    {
        return _bytes.length;
    }

    public CSocketSendData(int command, CSocketDataArgs args)
    {  
        _result = CSocketSendDataBuildResult.NoData;

        if (command < 0x00 || command > 0xFF) {
            _result = CSocketSendDataBuildResult.CommandValueOverflowError;
            return;
        }
        
        _command = (byte)(command & 0xFF);
        _args = args;

        CSocketDataStream textds = new CSocketDataStream();

        textds.write(new byte[] { _command }, 0, 1);

        for (int n = 0; n < args.getLength(); n++) 
        {
            IDataType arg = args.at(n);

            switch (arg.getDataType()) {
                case CInteger -> {
                    long i = ((CInteger) arg).getValue();
                    if (Byte.MIN_VALUE <= i && i <= Byte.MAX_VALUE) {
                        textds.write(new byte[]{(byte) 0x31}, 0, 1);
                        textds.write(ByteBuffer.allocate(1).put((byte) i).array(), 0, 1);
                    } else if (Short.MIN_VALUE <= i && i <= Short.MAX_VALUE) {
                        textds.write(new byte[]{(byte) 0x32}, 0, 1);
                        textds.write(ByteBuffer.allocate(2).putShort((short) i).array(), 0, 2);
                    } else if (Integer.MIN_VALUE <= i && i <= Integer.MAX_VALUE) {
                        textds.write(new byte[]{(byte) 0x34}, 0, 1);
                        textds.write(ByteBuffer.allocate(4).putInt((int) i).array(), 0, 4);
                    } else {
                        textds.write(new byte[]{(byte) 0x38}, 0, 1);
                        textds.write(ByteBuffer.allocate(8).putLong((long) i).array(), 0, 8);
                    }
                }
                case CFloat -> {
                    double f = ((CFloat) arg).getValue();
                    if (Math.abs(f) <= Float.MAX_VALUE) {
                        textds.write(new byte[]{(byte) 0x54}, 0, 1);
                        textds.write(ByteBuffer.allocate(4).putFloat((float) f).array(), 0, 4);
                    } else {
                        textds.write(new byte[]{(byte) 0x58}, 0, 1);
                        textds.write(ByteBuffer.allocate(8).putDouble(f).array(), 0, 8);
                    }
                }
                case CBoolean -> {
                    textds.write(new byte[]{(byte) 0x71}, 0, 1);
                    textds.write(ByteBuffer.allocate(1).put((byte) (((CBoolean) arg).getValue() ? 1 : 0)).array(), 0, 1);
                }
                case CString -> {
                    byte[] s = ((CString) arg).getValue().getBytes();
                    if (s.length <= ARG_MAXLEN) {
                        if (s.length <= Byte.MAX_VALUE) {
                            textds.write(new byte[]{(byte) 0x91}, 0, 1);
                            textds.write(ByteBuffer.allocate(1).put((byte) s.length).array(), 0, 1);
                        } else if (s.length <= Short.MAX_VALUE) {
                            textds.write(new byte[]{(byte) 0x92}, 0, 1);
                            textds.write(ByteBuffer.allocate(2).putShort((short) s.length).array(), 0, 2);
                        } else {
                            textds.write(new byte[]{(byte) 0x94}, 0, 1);
                            textds.write(ByteBuffer.allocate(4).putInt((int) s.length).array(), 0, 4);
                        }

                        textds.write(s, 0, s.length);
                    } else {
                        _result = CSocketSendDataBuildResult.StringLengthOverflowError;
                        return;
                    }
                }
                case CByteArray -> {
                    byte[] ba = ((CByteArray) arg).getValue();
                    if (ba.length <= ARG_MAXLEN) {
                        if (ba.length <= Byte.MAX_VALUE) {
                            textds.write(new byte[]{(byte) 0xB1}, 0, 1);
                            textds.write(ByteBuffer.allocate(1).put((byte) ba.length).array(), 0, 1);
                        } else if (ba.length <= Short.MAX_VALUE) {
                            textds.write(new byte[]{(byte) 0xB2}, 0, 1);
                            textds.write(ByteBuffer.allocate(2).putShort((short) ba.length).array(), 0, 2);
                        } else {
                            textds.write(new byte[]{(byte) 0xB4}, 0, 1);
                            textds.write(ByteBuffer.allocate(4).putInt((int) ba.length).array(), 0, 4);
                        }
                        textds.write(ba, 0, ba.length);
                    } else {
                        _result = CSocketSendDataBuildResult.ByteArrayLengthOverflowError;
                        return;
                    }
                }
                default -> {
                    _result = CSocketSendDataBuildResult.DataTypeNotImplementedError;
                    return;
                }
            }
        }

        int textlen = textds.getCount();

        int otl = 0;
        if (textlen <= Byte.MAX_VALUE) otl = 2;
        else if (textlen <= Short.MAX_VALUE) otl = 3;
        else if (textlen <= Integer.MAX_VALUE) otl = 5;

        //SOH(1)+OTL(v)+STX(1)+TXT(v)+ETX(1)+CHK(1)+EOT(1)
        byte[] data = new byte[1 + otl + 1 + textlen + 1 + 1 + 1];
        int datapos = 0;

        if (textlen <= TXT_MAXLEN) 
        {
            data[datapos] = 0x01; 
            datapos += 1;

            if (textlen <= Byte.MAX_VALUE) 
            {
                data[datapos] = 0x11; 
                datapos += 1;

                System.arraycopy(ByteBuffer.allocate(1).put((byte)textlen).array(), 0, data, datapos, 1); 
                datapos += 1;
            } 
            else if (textlen <= Short.MAX_VALUE) 
            {
                data[datapos] = 0x12; 
                datapos += 1;

                System.arraycopy(ByteBuffer.allocate(2).putShort((short)textlen).array(), 0, data, datapos, 2); 
                datapos += 2;
            } 
            else 
            {
                data[datapos] = 0x14; 
                datapos += 1;

                System.arraycopy(ByteBuffer.allocate(4).putInt((int)textlen).array(), 0, data, datapos, 4); 
                datapos += 4;
            }

            data[datapos] = 0x02; 
            datapos += 1;

            byte[] text = textds.getBuffer();

            System.arraycopy(text, 0, data, datapos, textlen); 
            datapos += textlen;

            data[datapos] = 0x03; 
            datapos += 1;

            byte checksum = 0x00;

            for (int i = 0; i < textlen; i++) 
                checksum ^= text[i];

            data[datapos] = checksum; 
            datapos += 1;

            data[datapos] = 0x04; 
            datapos += 1;
        } 
        else 
        {
            _result = CSocketSendDataBuildResult.DataTotalLengthOverflowError;
            return;
        }
        
        _bytes = data;
        _result = CSocketSendDataBuildResult.Successful;
    }
}
