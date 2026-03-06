using CartActivator;

[LogicRunOnMCU(scanInterval = 500)]
public class PortDemo : LadderLogic<CartDefinition>
{
    private int counter = 0;

    public override void Operation(int iteration)
    {
        counter++;
        if (counter < 4) return; // about 2s
        counter = 0;

        // Serial demo (Port 0 / RS485)
        SendModbusReadRequest();

        // CAN demo (Port 4 / CAN-1)
        RunOnMCU.WriteCANMessage(4, new CANMessage
        {
            ID = 0x000, // NMT
            RTR = false,
            Payload = new byte[] { 0x01, 0x01 } // Start Node 1
        });

        RunOnMCU.WriteCANMessage(4, new CANMessage
        {
            ID = 0x601, // SDO Request
            RTR = false,
            Payload = new byte[] { 0x40, 0x41, 0x60, 0x00, 0x00, 0x00, 0x00, 0x00 }
        });

        Console.WriteLine($"[{iteration}] Sent PortDemo messages");
    }

    private void SendModbusReadRequest()
    {
        byte[] request = new byte[8];
        request[0] = 0x01;
        request[1] = 0x03;
        request[2] = 0x00;
        request[3] = 0x00;
        request[4] = 0x00;
        request[5] = 0x0A;

        ushort crc = CalculateCRC16(request, 6);
        request[6] = (byte)(crc & 0xFF);
        request[7] = (byte)(crc >> 8);

        RunOnMCU.WriteStream(request, 0);
    }

    private ushort CalculateCRC16(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x0001) != 0) crc = (ushort)((crc >> 1) ^ 0xA001);
                else crc = (ushort)(crc >> 1);
            }
        }
        return crc;
    }
}
