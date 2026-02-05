using CartActivator;

/// <summary>
/// 示例: 端口通信演示
/// 演示串口和CAN通信、端口数据抓包、协议解析
/// 
/// 端口配置:
/// - Port 0: RS485 串口 (Modbus RTU)
/// - Port 4: CAN 总线 (CANOpen)
/// 
/// 功能: 周期性发送各种示例报文
/// - Modbus RTU: 读保持寄存器请求、模拟响应(长报文)
/// - CANOpen: SDO 读取 (0x6041, 0x1017)、NMT 命令、Heartbeat
/// </summary>
[LogicRunOnMCU(scanInterval = 500)]
public class PortDemo : LadderLogic<CartDefinition>
{
    private int counter = 0;
    
    public override void Operation(int iteration)
    {
        counter++;
        
        // 每 2 秒发送一轮示例报文
        if (counter >= 4)
        {
            counter = 0;
            
            // ========== Modbus RTU 示例 (Port 0, RS485) ==========
            SendModbusReadRequest();
            SendModbusResponse();  // 模拟长报文响应
            
            // ========== CANOpen 示例 (Port 4, CAN) ==========
            SendCANopenSDOReadStatusword();   // 读 0x6041 (CiA 402)
            SendCANopenSDOReadHeartbeatTime(); // 读 0x1017 (CiA 301)
            SendCANopenNMT();
            SendCANopenHeartbeat();
            
            Console.WriteLine($"[{iteration}] Sent sample messages");
        }
    }
    
    // ============================================
    // Modbus RTU 示例报文
    // ============================================
    
    /// <summary>
    /// Modbus 功能码 0x03: 读保持寄存器 (请求)
    /// 从站1, 起始地址 0x0000, 读取 10 个寄存器
    /// </summary>
    private void SendModbusReadRequest()
    {
        byte[] request = new byte[8];
        request[0] = 0x01;  // 从站地址
        request[1] = 0x03;  // 功能码: 读保持寄存器
        request[2] = 0x00;  // 起始地址高字节
        request[3] = 0x00;  // 起始地址低字节
        request[4] = 0x00;  // 寄存器数量高字节
        request[5] = 0x0A;  // 寄存器数量低字节 (10个)
        
        ushort crc = CalculateCRC16(request, 6);
        request[6] = (byte)(crc & 0xFF);
        request[7] = (byte)(crc >> 8);
        
        RunOnMCU.WriteStream(request, 0);  // Port 0
    }
    
    /// <summary>
    /// Modbus 功能码 0x03: 读保持寄存器 (模拟响应)
    /// 从站1 响应, 返回 10 个寄存器数据 (20 字节)
    /// 用于演示长报文折叠功能
    /// </summary>
    private void SendModbusResponse()
    {
        // 响应格式: 从站地址(1) + 功能码(1) + 字节数(1) + 数据(20) + CRC(2) = 25 字节
        byte[] response = new byte[25];
        response[0] = 0x01;  // 从站地址
        response[1] = 0x03;  // 功能码
        response[2] = 0x14;  // 字节数 (20 = 10个寄存器 * 2字节)
        
        // 模拟 10 个寄存器数据
        response[3] = 0x00; response[4] = 0x01;   // 寄存器0 = 1
        response[5] = 0x00; response[6] = 0x02;   // 寄存器1 = 2
        response[7] = 0x00; response[8] = 0x03;   // 寄存器2 = 3
        response[9] = 0x00; response[10] = 0x04;  // 寄存器3 = 4
        response[11] = 0x00; response[12] = 0x05; // 寄存器4 = 5
        response[13] = 0x01; response[14] = 0x00; // 寄存器5 = 256
        response[15] = 0x02; response[16] = 0x00; // 寄存器6 = 512
        response[17] = 0x03; response[18] = 0x00; // 寄存器7 = 768
        response[19] = 0x04; response[20] = 0x00; // 寄存器8 = 1024
        response[21] = 0x05; response[22] = 0x00; // 寄存器9 = 1280
        
        ushort crc = CalculateCRC16(response, 23);
        response[23] = (byte)(crc & 0xFF);
        response[24] = (byte)(crc >> 8);
        
        RunOnMCU.WriteStream(response, 0);  // Port 0
    }
    
    // ============================================
    // CANOpen 示例报文
    // ============================================
    
    /// <summary>
    /// CANOpen SDO Upload Request
    /// 读取节点1的 Statusword (0x6041) - CiA 402 驱动器对象
    /// COB-ID = 0x601
    /// </summary>
    private void SendCANopenSDOReadStatusword()
    {
        // SDO Upload Request: 命令字 0x40, Index 0x6041, SubIndex 0x00
        RunOnMCU.WriteCANMessage(4, new CANMessage
        {
            ID = 0x601,
            RTR = false,
            Payload = new byte[] { 0x40, 0x41, 0x60, 0x00, 0x00, 0x00, 0x00, 0x00 }
        });
    }
    
    /// <summary>
    /// CANOpen SDO Upload Request
    /// 读取节点1的 Producer Heartbeat Time (0x1017) - CiA 301 通信对象
    /// COB-ID = 0x601
    /// </summary>
    private void SendCANopenSDOReadHeartbeatTime()
    {
        // SDO Upload Request: 命令字 0x40, Index 0x1017, SubIndex 0x00
        RunOnMCU.WriteCANMessage(4, new CANMessage
        {
            ID = 0x601,
            RTR = false,
            Payload = new byte[] { 0x40, 0x17, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00 }
        });
    }
    
    /// <summary>
    /// CANOpen NMT 命令
    /// 启动节点1 (Start Remote Node)
    /// COB-ID = 0x000
    /// </summary>
    private void SendCANopenNMT()
    {
        // NMT: 0x01 = Start Remote Node, 0x01 = 目标节点 ID
        RunOnMCU.WriteCANMessage(4, new CANMessage
        {
            ID = 0x000,
            RTR = false,
            Payload = new byte[] { 0x01, 0x01 }
        });
    }
    
    /// <summary>
    /// CANOpen Heartbeat (心跳)
    /// 模拟节点1的心跳消息
    /// COB-ID = 0x701
    /// </summary>
    private void SendCANopenHeartbeat()
    {
        // Heartbeat: 0x05 = Operational 状态
        RunOnMCU.WriteCANMessage(4, new CANMessage
        {
            ID = 0x701,
            RTR = false,
            Payload = new byte[] { 0x05 }
        });
    }
    
    // ============================================
    // 工具函数
    // ============================================
    
    private ushort CalculateCRC16(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0)
                    crc = (ushort)((crc >> 1) ^ 0xA001);
                else
                    crc = (ushort)(crc >> 1);
            }
        }
        return crc;
    }
}
