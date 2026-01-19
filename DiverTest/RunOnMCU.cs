using DiverTest;

namespace CartActivator
{
    /// <summary>
    /// CAN 帧结构（用于 MCU 逻辑代码）
    /// </summary>
    public class CANMessage
    {
        /// <summary>标准帧 ID（0~0x7FF，11 位）</summary>
        public ushort ID;

        /// <summary>远程帧标志：false = 数据帧，true = 远程帧</summary>
        public bool RTR;

        /// <summary>数据负载（0~8 字节），长度即为 DLC</summary>
        public byte[] Payload;

        /// <summary>数据长度码（只读，由 Payload.Length 推导）</summary>
        public int DLC => Payload != null ? Payload.Length : 0;
    }

    public class LogicRunOnMCUAttribute : Attribute
    {
        public string mcuUri = "default";
        public int scanInterval = 1000;
    }

    public class RequireNativeCodeAttribute : Attribute
    {
    }

    public class RunOnMCU
    {
        // if return null: not data, or return payload data excluding CRC
        public static byte[] ReadEvent(int port, int event_id) => default;

        public static void WriteEvent(byte[] payload, int port, int event_id)
        {
        }

        /// <summary>
        /// 读取 CAN 消息（语法糖，封装 ReadEvent）
        /// </summary>
        /// <param name="port">CAN 端口索引</param>
        /// <param name="canId">CAN Standard ID (0~0x7FF)</param>
        /// <returns>CANMessage 或 null（无数据）</returns>
        public static CANMessage ReadCANMessage(int port, int canId)
        {
            var data = ReadEvent(port, canId);
            if (data == null || data.Length < 2)
                return null;

            // 解析 header
            int header = data[0] | (data[1] << 8);
            int dlc = (header >> 12) & 0xF;

            CANMessage msg = new CANMessage();
            msg.ID = (ushort)(header & 0x7FF);
            msg.RTR = (header & (1 << 11)) != 0;

            if (dlc > 0 && data.Length >= 2 + dlc)
            {
                msg.Payload = new byte[dlc];
                for (int i = 0; i < dlc; i++)
                    msg.Payload[i] = data[2 + i];
            }
            return msg;
        }

        /// <summary>
        /// 写入 CAN 消息（语法糖，封装 WriteEvent）
        /// </summary>
        /// <param name="port">CAN 端口索引</param>
        /// <param name="message">CAN 消息</param>
        public static void WriteCANMessage(int port, CANMessage message)
        {
            if (message == null)
                return;

            int dlc = message.Payload != null ? message.Payload.Length : 0;
            if (dlc > 8)
                return; // Payload 超长，不发送

            // 构造 header: bits 0-10 = ID, bit 11 = RTR, bits 12-15 = DLC
            int header = (message.ID & 0x7FF) | (message.RTR ? (1 << 11) : 0) | ((dlc & 0xF) << 12);

            byte[] bytes = new byte[2 + dlc];
            bytes[0] = (byte)(header & 0xFF);
            bytes[1] = (byte)((header >> 8) & 0xFF);

            if (dlc > 0 && message.Payload != null)
            {
                for (int i = 0; i < dlc; i++)
                    bytes[2 + i] = message.Payload[i];
            }

            WriteEvent(bytes, port, message.ID);
        }

        // if return null: not data. 
        public static byte[] ReadStream(int port) => default;

        public static void WriteStream(byte[] payload, int port)
        {
        }

        // always have the same sized data.
        public static byte[] ReadSnapshot() => default;

        public static void WriteSnapshot(byte[] payload)
        {
        }

        public static int GetMicrosFromStart() => default;
        public static int GetMillisFromStart() => default;
        public static int GetSecondsFromStart() => default;

        public static T Iterate<T>(IEnumerable<T> ie) => default;

        // additional run on mcu functions.
    }

    /// 
    public class AsUpperIO : Attribute
    {
    }

    public class AsLowerIO : Attribute
    {
    }

    public abstract class LadderLogic<T> where T : CartDefinition
    {
        public T cart;
        public abstract void Operation(int iteration);
    }

    public abstract class CartDefinition
    {

    }
}
