package xyz.olooko.comm.netcomm;

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;

class NetSocketDataStream extends ByteArrayOutputStream {

    public byte[] getBuffer() {
        return super.buf;
    }

    public int getCount() {
        return super.count;
    }
}

public class NetSocketSendData {
 
    private Object[] _args;
    public Object[] getArgs() {
        return _args;
    }

    private byte[] _bytes;
    public byte[] getBytes() {
        return _bytes;
    }

    private byte _command;
    public byte getCommand() {
        return _command;
    }

    public int getLength() {
        return _bytes.length;
    }

    private NetSocketSendDataBuildResult _result;
    public NetSocketSendDataBuildResult getBuildResult() {
        return _result;
    }

    public NetSocketSendData(byte command, Object[] args) {       
        _result = NetSocketSendDataBuildResult.NoData;
        _command = command;
        _args = args;

        NetSocketDataStream textds = new NetSocketDataStream();

        textds.write(new byte[] { command }, 0, 1);

        for (int n = 0; n < args.length; n++) {
            Object arg = args[n];
            switch (arg.getClass().getSimpleName().toLowerCase()) {
                case "byte":
                case "short":
                case "int":
                case "long":
                case "integer":
                    long i = Long.valueOf(String.valueOf(arg));
                    if (-128 <= i && i <= 127) {
                        // 0011 0001
                        textds.write(new byte[] { (byte)0x31 }, 0, 1);
                        textds.write(ByteBuffer.allocate(1).put((byte)arg).array(), 0, 1);
                    } else if (-32768 <= i && i <= 32767) {
                        // 0011 0010
                        textds.write(new byte[] { (byte)0x32 }, 0, 1);
                        textds.write(ByteBuffer.allocate(2).order(ByteOrder.LITTLE_ENDIAN).putShort((short)i).array(), 0, 2);
                    } else if (-2147483648 <= i && i <= 2147483647) {
                        // 0011 0100
                        textds.write(new byte[] { (byte)0x34 }, 0, 1);
                        textds.write(ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt((int)i).array(), 0, 4);
                    } else {
                        // 0011 1000
                        textds.write(new byte[] { (byte)0x38 }, 0, 1);
                        textds.write(ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN).putLong((long)i).array(), 0, 8);
                    }
                    break;

                case "float":
                case "double":
                    double f = Double.valueOf(String.valueOf(arg));
                    if (Math.abs(f) <= Float.MAX_VALUE) {
                        // 0101 0100
                        textds.write(new byte[] { (byte)0x54 }, 0, 1);
                        textds.write(ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putFloat((float)f).array(), 0, 4);
                    } else {
                        // 0101 1000
                        textds.write(new byte[] { (byte)0x58 }, 0, 1);
                        textds.write(ByteBuffer.allocate(8).order(ByteOrder.LITTLE_ENDIAN).putDouble(f).array(), 0, 8);
                    }
                    break;
                
                case "boolean":
                    textds.write(new byte[] { (byte)0x71 }, 0, 1);
                    textds.write(ByteBuffer.allocate(1).put((byte)(Boolean.valueOf(String.valueOf(arg))?1:0)).array(), 0, 1);
                    break;

                case "string":
                    byte[] s = ((String)arg).getBytes();
                    if (s.length <= Integer.MAX_VALUE) {
                        if (s.length <= 0x7F) {
                            // 1001 0001
                            textds.write(new byte[] { (byte)0x91 }, 0, 1);
                            textds.write(ByteBuffer.allocate(1).put((byte)s.length).array(), 0, 1);
                        } else if (s.length <= 0x7FFF) {
                            // 1001 0010
                            textds.write(new byte[] { (byte)0x92 }, 0, 1);
                            textds.write(ByteBuffer.allocate(2).order(ByteOrder.LITTLE_ENDIAN).putShort((short)s.length).array(), 0, 2);
                        } else if (s.length <= 0x7FFFFFFF) {
                            // 1001 0100
                            textds.write(new byte[] { (byte)0x94 }, 0, 1);
                            textds.write(ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt((int)s.length).array(), 0, 4);
                        }
                        textds.write(s, 0, s.length);
                    } else {
                        _result = NetSocketSendDataBuildResult.StringOverflowError;
                        return;
                    }
                    break;

                case "byte[]":
                    byte[] b = (byte[])arg;
                    if (b.length <= Integer.MAX_VALUE) {
                        if (b.length <= 0x7F) {
                            // 1011 0001
                            textds.write(new byte[] { (byte)0xB1 }, 0, 1);
                            textds.write(ByteBuffer.allocate(1).put((byte)b.length).array(), 0, 1);
                        } else if (b.length <= 0x7FFF) {
                            // 1011 0010
                            textds.write(new byte[] { (byte)0xB2 }, 0, 1);
                            textds.write(ByteBuffer.allocate(2).order(ByteOrder.LITTLE_ENDIAN).putShort((short)b.length).array(), 0, 2);
                        } else if (b.length <= 0x7FFFFFFF) {
                            // 1011 0100
                            textds.write(new byte[] { (byte)0xB4 }, 0, 1);
                            textds.write(ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt((int)b.length).array(), 0, 4);
                        }
                        textds.write(b, 0, b.length);
                    } else {
                        _result = NetSocketSendDataBuildResult.ByteArrayOverflowError;
                        return;
                    }
                    break;

                default:
                    _result = NetSocketSendDataBuildResult.TypeNotImplementedError;
                    return;
            }
        }

        int textlen = textds.getCount();

        int otl = 0;
        if (textlen <= 0x7F) otl = 2;
        else if (textlen <= 0x7FFF) otl = 3;
        else if (textlen <= 0x7FFFFFFF) otl = 5;

        //SOH(1)+OTL(v)+STX(1)+TXT(v)+ETX(1)+CHK(1)+EOT(1)
        byte[] data = new byte[1 + otl + 1 + textlen + 1 + 1 + 1];
        int datapos = 0;

        if (textlen <= Integer.MAX_VALUE) {

            // start of header
            data[datapos] = 0x01; datapos += 1;

            if (textlen <= 0x7F) {
                // 0001 0001
                data[datapos] = 0x11; datapos += 1;
                System.arraycopy(ByteBuffer.allocate(1).put((byte)textlen).array(), 0, data, datapos, 1); datapos += 1;
            } else if (textlen <= 0x7FFF) {
                // 0001 0010
                data[datapos] = 0x12; datapos += 1;
                System.arraycopy(ByteBuffer.allocate(2).order(ByteOrder.LITTLE_ENDIAN).putShort((short)textlen).array(), 0, data, datapos, 2); datapos += 2;
            } else if (textlen <= 0x7FFFFFFF) {
                // 0001 0100
                data[datapos] = 0x14; datapos += 1;
                System.arraycopy(ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt((int)textlen).array(), 0, data, datapos, 4); datapos += 4;
            }

            // start of text
            data[datapos] = 0x02; datapos += 1;

            // text
            byte[] text = textds.getBuffer();

            System.arraycopy(text, 0, data, datapos, textlen); datapos += textlen;

            // end of text
            data[datapos] = 0x03; datapos += 1;

            // checksum of text
            byte checksum = 0x00;
            for (int i = 0; i < textlen; i++) checksum ^= text[i];

            data[datapos] = checksum; datapos += 1;

            // end of transmission
            data[datapos] = 0x04; datapos += 1;
        } else {
            _result = NetSocketSendDataBuildResult.TextOverflowError;
            return;
        }
        
        _bytes = data;
        _result = NetSocketSendDataBuildResult.Successful;
    }
}
