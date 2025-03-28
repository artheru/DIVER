using System;

namespace DiverTest
{
    public class BasePack
    {
        private static ushort CalculateCRC16(byte[] data)
        {
            const ushort polynomial = 0xA001; // CRC-16-IBM多项式
            ushort crc = 0xFFFF; // 初始值

            foreach (byte b in data)
            {
                crc ^= b; // 将数据字节与CRC寄存器按位异或

                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= polynomial;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return crc;
        }

        private byte[] _data;

        public void SetData(byte[] data)
        {
            _data = data;
        }

        public byte[] GetPack()
        {
            var pack = new byte[_data.Length + 7];
            pack[0] = 0xBB;
            pack[1] = 0xAA;
            var length = BitConverter.GetBytes(_data.Length);
            pack[2] = length[0];
            pack[3] = length[1];
            for (int i = 0; i < _data.Length; i++)
            {
                pack[4 + i] = _data[i];
            }

            var crcByes = new byte[pack.Length - 5];
            Array.Copy(pack, 2, crcByes, 0, crcByes.Length);
            var crc = BitConverter.GetBytes(CalculateCRC16(crcByes));
            pack[pack.Length - 1] = 0xEE;
            pack[pack.Length - 2] = crc[1];
            pack[pack.Length - 3] = crc[0];
            return pack;
        }
    }

    public class ControlPack : BasePack
    {
        public ControlPack(byte controlCode)
        {
            byte[] controlData = new byte[3] { 0x01, 0x00, controlCode };
            SetData(controlData);
        }
    }

    public class SetConfigPack : BasePack
    {
        // TODO: SetConfigPack is TOO UGLY
        public SetConfigPack(int can1BaudRate, int can2BaudRate, int modbus0BaudRate, int modbus1BaudRate, int modbus2BaudRate,
            int serialBaudRate, int upperSize = 1024, int lowerSize = 1024)
        {
            var upper = BitConverter.GetBytes(upperSize);
            var lower = BitConverter.GetBytes(lowerSize);
            var can = BitConverter.GetBytes(can1BaudRate);
            var can2 = BitConverter.GetBytes(can2BaudRate);
            var modbus0 = BitConverter.GetBytes(modbus0BaudRate);
            var modbus1 = BitConverter.GetBytes(modbus1BaudRate);
            var modbus2 = BitConverter.GetBytes(modbus2BaudRate);
            var serial = BitConverter.GetBytes(serialBaudRate);
            var canBuffer = BitConverter.GetBytes(128);
            var mbBuffer = BitConverter.GetBytes(512);
            var serialBuffer = BitConverter.GetBytes(1024);
            byte[] configData = new byte[]
            {
                0x01, 0x10, 0x02, upper[0], upper[1], upper[2], upper[3], lower[0], lower[1],
                lower[2], lower[3], 0x06, 0x00, 0x00, can[0], can[1], can[2], can[3], canBuffer[0], canBuffer[1], 0x00,
                can2[0], can2[1], can2[2], can2[3], canBuffer[0], canBuffer[1], 0x10, modbus0[0],
                modbus0[1], modbus0[2], modbus0[3], mbBuffer[0], mbBuffer[1], 0x10, modbus1[0],
                modbus1[1], modbus1[2], modbus1[3], mbBuffer[0], mbBuffer[1], 0x10, modbus2[0],
                modbus2[1], modbus2[2], modbus2[3], mbBuffer[0], mbBuffer[1], 0x20, serial[0], serial[1], serial[2],
                serial[3], serialBuffer[0], serialBuffer[1]
            };
            SetData(configData);
        }
    }

    public class ReadConfigPack : BasePack
    {
        public ReadConfigPack()
        {
            var readConfigData = new byte[] { 0x01, 0x10, 0x00 };
            SetData(readConfigData);
        }
    }

    public class DownloadCodePack : BasePack
    {
        public DownloadCodePack(int totalLength, int offset, int currentLength, byte[] codeBytes)
        {
            var codeData = new byte[12 + codeBytes.Length];
            codeData[0] = 0x01;
            codeData[1] = 0x11;
            var totalLengthBytes = BitConverter.GetBytes(totalLength);
            for (int i = 0; i < 4; i++)
            {
                codeData[2 + i] = totalLengthBytes[i];
            }
            var offsetBytes = BitConverter.GetBytes(offset);
            for (int i = 0; i < 4; i++)
            {
                codeData[6 + i] = offsetBytes[i];
            }
            var curLengthBytes = BitConverter.GetBytes(currentLength);
            for (int i = 0; i < 2; i++)
            {
                codeData[10 + i] = curLengthBytes[i];
            }

            for (int i = 0; i < codeBytes.Length; i++)
            {
                codeData[12 + i] = codeBytes[i];
            }
            SetData(codeData);
        }

    }

    public class HeartBeatPack : BasePack
    {
        public HeartBeatPack(int state, int error)
        {
            var errorBytes = BitConverter.GetBytes(error);
            var heartBearData = new byte[] { 0x01, 0xF0, (byte)state, errorBytes[0], errorBytes[1] };
            SetData(heartBearData);
        }
    }

    public class UpperIOPack : BasePack
    {
        public UpperIOPack(byte[] data, int size)
        {
            byte[] UpperData = new byte[6 + size];
            UpperData[0] = 0x01;
            UpperData[1] = 0x20;
            var length = BitConverter.GetBytes(size);
            for (int i = 0; i < 4; i++)
            {
                UpperData[2 + i] = length[i];
            }
            Array.Copy(data, 0, UpperData, 6, size);
            SetData(UpperData);
        }
    }
}
