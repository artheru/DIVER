using System;
using CartActivator;

public static class Node2Constants
{
    public const int CommunicationLostProtectionTime = 10;
    public const int CAN1 = 4; // RS485-1,2,3,4, CAN-1
    public const int BootupRetryLimit = 20;
    // Run motor scale: raw 1,000,000 <=> 2500 rpm  =>  raw = rpm * 400
    public const float RunRawPerRpm = 400.0f;
}

public enum Node2MotorID
{
    MotorNode2Turn = 3,
    MotorNode2Run = 4
}

public enum Node2CANID : int
{
    RPDO1 = 0x200,
    RPDO2 = 0x300,
    TPDO1 = 0x180,
    HEARTBEAT = 0x700,
    NMT = 0x000
}

public enum Node2NMTCommand : byte
{
    StartNode = 0x01,
    StopNode = 0x02,
    EnterPreOp = 0x80,
    ResetNode = 0x81,
    ResetComm = 0x82
}

public enum Node2Heartbeat : byte
{
    Bootup = 0x00,
    Operational = 0x05,
    Stopped = 0x04
}

public enum Node2MotorBootupStage
{
    Unknown = 0,
    ResetSent = 1,
    BootupReceived = 2,
    StartSent = 3,
    StartReceived = 4
}

public static class Node2CanIo
{
    public static void WriteCANPayload(byte[] payload, int port, int canId)
    {
        RunOnMCU.WriteCANMessage(port, new CANMessage
        {
            ID = (ushort)canId,
            RTR = false,
            Payload = payload
        });
    }

    public static byte[] ReadCANPayload(int port, int canId)
    {
        var msg = RunOnMCU.ReadCANMessage(port, canId);
        return msg == null ? null : msg.Payload;
    }
}

public class Node2MotorProtocol
{
    public enum Mode : byte
    {
        FindOrigin = 0x06,
        SpeedMode = 0x03,
        PositionMode = 0x01
    }

    public enum ControlWord : int
    {
        Stop = 0x05,
        Start = 0x06,
        Start2 = 0x07,
        Start3 = 0x0F,
        PositionEnable = 0x103F
    }

    // RPDO1 RunMotor: mode(1) + speed(4) + controlWord(2)
    public static byte[] GenerateRPDO1RunMotor(int controlWord, int speed)
    {
        byte[] d = new byte[7];
        d[0] = (byte)Mode.SpeedMode;
        d[1] = (byte)(speed & 0xFF);
        d[2] = (byte)((speed >> 8) & 0xFF);
        d[3] = (byte)((speed >> 16) & 0xFF);
        d[4] = (byte)((speed >> 24) & 0xFF);
        d[5] = (byte)(controlWord & 0xFF);
        d[6] = (byte)((controlWord >> 8) & 0xFF);
        return d;
    }

    // RPDO1 TurnMotor: mode(1) + position(4) + controlWord(2)
    public static byte[] GenerateRPDO1TurnMotor(int controlWord, int position, int mode)
    {
        byte[] d = new byte[7];
        d[0] = (byte)mode;
        d[1] = (byte)(position & 0xFF);
        d[2] = (byte)((position >> 8) & 0xFF);
        d[3] = (byte)((position >> 16) & 0xFF);
        d[4] = (byte)((position >> 24) & 0xFF);
        d[5] = (byte)(controlWord & 0xFF);
        d[6] = (byte)((controlWord >> 8) & 0xFF);
        return d;
    }

    // RPDO2 TurnMotor: turn speed(4)
    public static byte[] GenerateRPDO2TurnMotor(int turnSpeed)
    {
        byte[] d = new byte[4];
        d[0] = (byte)(turnSpeed & 0xFF);
        d[1] = (byte)((turnSpeed >> 8) & 0xFF);
        d[2] = (byte)((turnSpeed >> 16) & 0xFF);
        d[3] = (byte)((turnSpeed >> 24) & 0xFF);
        return d;
    }

    // Run TPDO1 is typically 8 bytes
    public static int ParseRunActualSpeed(byte[] tpdo1)
    {
        if (tpdo1 == null || tpdo1.Length < 6) return 0;
        return tpdo1[2] | (tpdo1[3] << 8) | (tpdo1[4] << 16) | (tpdo1[5] << 24);
    }

    public static int ParseRunStatusWord(byte[] tpdo1)
    {
        if (tpdo1 == null || tpdo1.Length < 2) return 0;
        return tpdo1[0] | (tpdo1[1] << 8);
    }

    // Turn TPDO1 is typically 7 bytes
    public static int ParseTurnActualPosition(byte[] tpdo1)
    {
        if (tpdo1 == null || tpdo1.Length < 6) return 0;
        return tpdo1[2] | (tpdo1[3] << 8) | (tpdo1[4] << 16) | (tpdo1[5] << 24);
    }

    public static int ParseTurnStatusWord(byte[] tpdo1)
    {
        if (tpdo1 == null || tpdo1.Length < 2) return 0;
        return tpdo1[0] | (tpdo1[1] << 8);
    }
}

public class Node2Motor34Cart : CartDefinition
{
    // UpperIO: controls
    [AsUpperIO] public float run_motor_target_velocity_rear;
    [AsUpperIO] public int turn_motor_target_position_rear;

    // LowerIO: feedback/status
    [AsLowerIO] public float run_motor_actual_velocity_rear;
    [AsLowerIO] public int turn_motor_actual_position_rear;
    [AsLowerIO] public int run_motor_status_word_rear;
    [AsLowerIO] public int turn_motor_status_word_rear;
    [AsLowerIO] public int motor3_boot_stage;
    [AsLowerIO] public int motor4_boot_stage;
    [AsLowerIO] public int node2_ready;
}

[LogicRunOnMCU(scanInterval = 50)]
public class Node2Motor34NewApiDemo : LadderLogic<Node2Motor34Cart>
{
    private bool variableInitialized = false;
    private int lastIteration = 0;
    private int communicationLostTime = 0;
    private bool failProtectionFlag = false;

    // index0 = run motor(M4), index1 = turn motor(M3)
    private int[] motorStage;
    private int[] motorRetryCount;
    private int[] motorID;

    private void Initialize()
    {
        motorStage = new int[2];
        motorRetryCount = new int[2];
        motorID = new int[2];

        motorStage[0] = (int)Node2MotorBootupStage.Unknown; // run
        motorStage[1] = (int)Node2MotorBootupStage.Unknown; // turn
        motorRetryCount[0] = 0;
        motorRetryCount[1] = 0;

        motorID[0] = (int)Node2MotorID.MotorNode2Run;  // M4
        motorID[1] = (int)Node2MotorID.MotorNode2Turn; // M3
    }

    private void FailSafe()
    {
        // Only keep run motor safe stop here, matching prior style.
        byte[] runStop = Node2MotorProtocol.GenerateRPDO1RunMotor(
            (int)Node2MotorProtocol.ControlWord.Stop, 0);
        Node2CanIo.WriteCANPayload(runStop, Node2Constants.CAN1,
            (int)Node2CANID.RPDO1 + motorID[0]);
    }

    private bool MotorBootupHelper(int i)
    {
        switch (motorStage[i])
        {
            case (int)Node2MotorBootupStage.Unknown:
            {
                byte[] reset = new byte[2] { (byte)Node2NMTCommand.ResetNode, (byte)motorID[i] };
                Node2CanIo.WriteCANPayload(reset, Node2Constants.CAN1, (int)Node2CANID.NMT);
                motorStage[i] = (int)Node2MotorBootupStage.ResetSent;
                motorRetryCount[i] = 0;
                Console.WriteLine($"M{motorID[i]} Reset Sent");
                return false;
            }

            case (int)Node2MotorBootupStage.ResetSent:
            {
                byte[] hb = Node2CanIo.ReadCANPayload(Node2Constants.CAN1, (int)Node2CANID.HEARTBEAT + motorID[i]);
                if (hb != null && hb.Length == 1 && hb[0] == (byte)Node2Heartbeat.Bootup)
                {
                    motorStage[i] = (int)Node2MotorBootupStage.BootupReceived;
                    motorRetryCount[i] = 0;
                    Console.WriteLine($"M{motorID[i]} Bootup Received");
                }
                else
                {
                    motorRetryCount[i]++;
                    if (motorRetryCount[i] > Node2Constants.BootupRetryLimit)
                    {
                        motorStage[i] = (int)Node2MotorBootupStage.Unknown;
                        Console.WriteLine($"M{motorID[i]} Bootup Timeout -> Reset");
                    }
                }
                return false;
            }

            case (int)Node2MotorBootupStage.BootupReceived:
            {
                byte[] start = new byte[2] { (byte)Node2NMTCommand.StartNode, (byte)motorID[i] };
                Node2CanIo.WriteCANPayload(start, Node2Constants.CAN1, (int)Node2CANID.NMT);
                motorStage[i] = (int)Node2MotorBootupStage.StartReceived;
                motorRetryCount[i] = 0;
                Console.WriteLine($"M{motorID[i]} Start Sent");
                return false;
            }

            case (int)Node2MotorBootupStage.StartSent:
            {
                byte[] hb = Node2CanIo.ReadCANPayload(Node2Constants.CAN1, (int)Node2CANID.HEARTBEAT + motorID[i]);
                if (hb != null && hb.Length == 1 && hb[0] == (byte)Node2Heartbeat.Operational)
                {
                    motorStage[i] = (int)Node2MotorBootupStage.StartReceived;
                    motorRetryCount[i] = 0;
                    Console.WriteLine($"M{motorID[i]} Start Received");
                }
                else
                {
                    motorRetryCount[i]++;
                    if (motorRetryCount[i] > Node2Constants.BootupRetryLimit)
                    {
                        motorStage[i] = (int)Node2MotorBootupStage.BootupReceived;
                        Console.WriteLine($"M{motorID[i]} Start Timeout -> Retry");
                    }
                }
                return false;
            }

            case (int)Node2MotorBootupStage.StartReceived:
                return true;
            default:
                return false;
        }
    }

    public override void Operation(int iteration)
    {
        if (!variableInitialized)
        {
            Initialize();
            variableInitialized = true;
            Console.WriteLine("Node2(M3/M4) Initialized");
        }

        // keep old-style communication watchdog
        if (iteration <= lastIteration || failProtectionFlag)
        {
            if (communicationLostTime < Node2Constants.CommunicationLostProtectionTime)
            {
                communicationLostTime += 1;
            }
            else
            {
                FailSafe();
                failProtectionFlag = true;
                Console.WriteLine("FailSafe Triggered");
            }
            return;
        }

        lastIteration = iteration;
        communicationLostTime = 0;
        failProtectionFlag = false;

        // 1) Read feedback
        byte[] tpdoRun = Node2CanIo.ReadCANPayload(Node2Constants.CAN1, (int)Node2CANID.TPDO1 + (int)Node2MotorID.MotorNode2Run);
        byte[] tpdoTurn = Node2CanIo.ReadCANPayload(Node2Constants.CAN1, (int)Node2CANID.TPDO1 + (int)Node2MotorID.MotorNode2Turn);

        if (tpdoRun != null)
        {
            int rawRunSpeed = Node2MotorProtocol.ParseRunActualSpeed(tpdoRun);
            cart.run_motor_actual_velocity_rear = rawRunSpeed / Node2Constants.RunRawPerRpm;
            cart.run_motor_status_word_rear = Node2MotorProtocol.ParseRunStatusWord(tpdoRun);
        }
        if (tpdoTurn != null)
        {
            cart.turn_motor_actual_position_rear = Node2MotorProtocol.ParseTurnActualPosition(tpdoTurn);
            cart.turn_motor_status_word_rear = Node2MotorProtocol.ParseTurnStatusWord(tpdoTurn);
        }

        // 2) Bootup state machine
        bool isAllMotorBooted = true;
        for (int i = 0; i < motorStage.Length; i++)
        {
            isAllMotorBooted &= MotorBootupHelper(i);
        }
        cart.motor4_boot_stage = motorStage[0];
        cart.motor3_boot_stage = motorStage[1];
        cart.node2_ready = isAllMotorBooted ? 1 : 0;

        // 3) Command motors (no IO safety/obstacle/emergency logic by request)
        if (isAllMotorBooted)
        {
            // M4 run motor speed control from UpperIO
            int runTargetRaw = (int)(cart.run_motor_target_velocity_rear * Node2Constants.RunRawPerRpm);
            byte[] rpdoRun = Node2MotorProtocol.GenerateRPDO1RunMotor(
                (int)Node2MotorProtocol.ControlWord.Start3,
                runTargetRaw);
            Node2CanIo.WriteCANPayload(rpdoRun, Node2Constants.CAN1,
                (int)Node2CANID.RPDO1 + (int)Node2MotorID.MotorNode2Run);

            // M3 turn motor position control from UpperIO
            byte[] rpdoTurn = Node2MotorProtocol.GenerateRPDO1TurnMotor(
                (int)Node2MotorProtocol.ControlWord.PositionEnable,
                cart.turn_motor_target_position_rear,
                (int)Node2MotorProtocol.Mode.PositionMode);
            Node2CanIo.WriteCANPayload(rpdoTurn, Node2Constants.CAN1,
                (int)Node2CANID.RPDO1 + (int)Node2MotorID.MotorNode2Turn);

            // Turn motor RPDO2 (turning profile/speed), keep fixed like old final version
            byte[] rpdo2Turn = Node2MotorProtocol.GenerateRPDO2TurnMotor(6000000);
            Node2CanIo.WriteCANPayload(rpdo2Turn, Node2Constants.CAN1,
                (int)Node2CANID.RPDO2 + (int)Node2MotorID.MotorNode2Turn);
        }
        else
        {
            byte[] rpdoRunWait = Node2MotorProtocol.GenerateRPDO1RunMotor(
                (int)Node2MotorProtocol.ControlWord.Start, 0);
            Node2CanIo.WriteCANPayload(rpdoRunWait, Node2Constants.CAN1,
                (int)Node2CANID.RPDO1 + (int)Node2MotorID.MotorNode2Run);
        }

        // Key stage summary logs (about every 1 second)
        if (iteration % 20 == 0)
        {
            Console.WriteLine(
                $"Node2 ready={cart.node2_ready} " +
                $"M4(v={cart.run_motor_actual_velocity_rear},sw={cart.run_motor_status_word_rear},target={cart.run_motor_target_velocity_rear}) " +
                $"M3(pos={cart.turn_motor_actual_position_rear},sw={cart.turn_motor_status_word_rear},target={cart.turn_motor_target_position_rear})");
        }
    }
}

public class Node1Motor12Cart : CartDefinition
{
    // UpperIO: controls
    [AsUpperIO] public float run_motor_target_velocity_front;
    [AsUpperIO] public int turn_motor_target_position_front;

    // LowerIO: feedback/status
    [AsLowerIO] public float run_motor_actual_velocity_front;
    [AsLowerIO] public int turn_motor_actual_position_front;
    [AsLowerIO] public int run_motor_status_word_front;
    [AsLowerIO] public int turn_motor_status_word_front;
    [AsLowerIO] public int motor1_boot_stage;
    [AsLowerIO] public int motor2_boot_stage;
    [AsLowerIO] public int node1_ready;
}

[LogicRunOnMCU(scanInterval = 50)]
public class Node1Motor12NewApiDemo : LadderLogic<Node1Motor12Cart>
{
    private bool variableInitialized = false;
    private int lastIteration = 0;
    private int communicationLostTime = 0;
    private bool failProtectionFlag = false;

    // index0 = run motor(M2), index1 = turn motor(M1)
    private int[] motorStage;
    private int[] motorRetryCount;
    private int[] motorID;

    private void Initialize()
    {
        motorStage = new int[2];
        motorRetryCount = new int[2];
        motorID = new int[2];

        motorStage[0] = (int)Node2MotorBootupStage.Unknown; // run
        motorStage[1] = (int)Node2MotorBootupStage.Unknown; // turn
        motorRetryCount[0] = 0;
        motorRetryCount[1] = 0;

        motorID[0] = 2; // M2 run
        motorID[1] = 1; // M1 turn
    }

    private void FailSafe()
    {
        byte[] runStop = Node2MotorProtocol.GenerateRPDO1RunMotor(
            (int)Node2MotorProtocol.ControlWord.Stop, 0);
        Node2CanIo.WriteCANPayload(runStop, Node2Constants.CAN1,
            (int)Node2CANID.RPDO1 + motorID[0]);
    }

    private bool MotorBootupHelper(int i)
    {
        switch (motorStage[i])
        {
            case (int)Node2MotorBootupStage.Unknown:
            {
                byte[] reset = new byte[2] { (byte)Node2NMTCommand.ResetNode, (byte)motorID[i] };
                Node2CanIo.WriteCANPayload(reset, Node2Constants.CAN1, (int)Node2CANID.NMT);
                motorStage[i] = (int)Node2MotorBootupStage.ResetSent;
                motorRetryCount[i] = 0;
                Console.WriteLine($"M{motorID[i]} Reset Sent");
                return false;
            }
            case (int)Node2MotorBootupStage.ResetSent:
            {
                byte[] hb = Node2CanIo.ReadCANPayload(Node2Constants.CAN1, (int)Node2CANID.HEARTBEAT + motorID[i]);
                if (hb != null && hb.Length == 1 && hb[0] == (byte)Node2Heartbeat.Bootup)
                {
                    motorStage[i] = (int)Node2MotorBootupStage.BootupReceived;
                    motorRetryCount[i] = 0;
                    Console.WriteLine($"M{motorID[i]} Bootup Received");
                }
                else
                {
                    motorRetryCount[i]++;
                    if (motorRetryCount[i] > Node2Constants.BootupRetryLimit)
                    {
                        motorStage[i] = (int)Node2MotorBootupStage.Unknown;
                        Console.WriteLine($"M{motorID[i]} Bootup Timeout -> Reset");
                    }
                }
                return false;
            }
            case (int)Node2MotorBootupStage.BootupReceived:
            {
                byte[] start = new byte[2] { (byte)Node2NMTCommand.StartNode, (byte)motorID[i] };
                Node2CanIo.WriteCANPayload(start, Node2Constants.CAN1, (int)Node2CANID.NMT);
                motorStage[i] = (int)Node2MotorBootupStage.StartReceived;
                motorRetryCount[i] = 0;
                Console.WriteLine($"M{motorID[i]} Start Sent");
                return false;
            }
            case (int)Node2MotorBootupStage.StartSent:
            {
                byte[] hb = Node2CanIo.ReadCANPayload(Node2Constants.CAN1, (int)Node2CANID.HEARTBEAT + motorID[i]);
                if (hb != null && hb.Length == 1 && hb[0] == (byte)Node2Heartbeat.Operational)
                {
                    motorStage[i] = (int)Node2MotorBootupStage.StartReceived;
                    motorRetryCount[i] = 0;
                    Console.WriteLine($"M{motorID[i]} Start Received");
                }
                else
                {
                    motorRetryCount[i]++;
                    if (motorRetryCount[i] > Node2Constants.BootupRetryLimit)
                    {
                        motorStage[i] = (int)Node2MotorBootupStage.BootupReceived;
                        Console.WriteLine($"M{motorID[i]} Start Timeout -> Retry");
                    }
                }
                return false;
            }
            case (int)Node2MotorBootupStage.StartReceived:
                return true;
            default:
                return false;
        }
    }

    public override void Operation(int iteration)
    {
        if (!variableInitialized)
        {
            Initialize();
            variableInitialized = true;
            Console.WriteLine("Node1(M1/M2) Initialized");
        }

        if (iteration <= lastIteration || failProtectionFlag)
        {
            if (communicationLostTime < Node2Constants.CommunicationLostProtectionTime)
            {
                communicationLostTime += 1;
            }
            else
            {
                FailSafe();
                failProtectionFlag = true;
                Console.WriteLine("FailSafe Triggered");
            }
            return;
        }

        lastIteration = iteration;
        communicationLostTime = 0;
        failProtectionFlag = false;

        // Feedback
        byte[] tpdoRun = Node2CanIo.ReadCANPayload(Node2Constants.CAN1, (int)Node2CANID.TPDO1 + 2);
        byte[] tpdoTurn = Node2CanIo.ReadCANPayload(Node2Constants.CAN1, (int)Node2CANID.TPDO1 + 1);

        if (tpdoRun != null)
        {
            int rawRunSpeed = Node2MotorProtocol.ParseRunActualSpeed(tpdoRun);
            cart.run_motor_actual_velocity_front = rawRunSpeed / Node2Constants.RunRawPerRpm;
            cart.run_motor_status_word_front = Node2MotorProtocol.ParseRunStatusWord(tpdoRun);
        }
        if (tpdoTurn != null)
        {
            cart.turn_motor_actual_position_front = Node2MotorProtocol.ParseTurnActualPosition(tpdoTurn);
            cart.turn_motor_status_word_front = Node2MotorProtocol.ParseTurnStatusWord(tpdoTurn);
        }

        // Bootup
        bool isAllMotorBooted = true;
        for (int i = 0; i < motorStage.Length; i++)
        {
            isAllMotorBooted &= MotorBootupHelper(i);
        }
        cart.motor2_boot_stage = motorStage[0];
        cart.motor1_boot_stage = motorStage[1];
        cart.node1_ready = isAllMotorBooted ? 1 : 0;

        // Command
        if (isAllMotorBooted)
        {
            int runTargetRaw = (int)(cart.run_motor_target_velocity_front * Node2Constants.RunRawPerRpm);
            byte[] rpdoRun = Node2MotorProtocol.GenerateRPDO1RunMotor(
                (int)Node2MotorProtocol.ControlWord.Start3,
                runTargetRaw);
            Node2CanIo.WriteCANPayload(rpdoRun, Node2Constants.CAN1,
                (int)Node2CANID.RPDO1 + 2);

            byte[] rpdoTurn = Node2MotorProtocol.GenerateRPDO1TurnMotor(
                (int)Node2MotorProtocol.ControlWord.PositionEnable,
                cart.turn_motor_target_position_front,
                (int)Node2MotorProtocol.Mode.PositionMode);
            Node2CanIo.WriteCANPayload(rpdoTurn, Node2Constants.CAN1,
                (int)Node2CANID.RPDO1 + 1);

            byte[] rpdo2Turn = Node2MotorProtocol.GenerateRPDO2TurnMotor(6000000);
            Node2CanIo.WriteCANPayload(rpdo2Turn, Node2Constants.CAN1,
                (int)Node2CANID.RPDO2 + 1);
        }
        else
        {
            byte[] rpdoRunWait = Node2MotorProtocol.GenerateRPDO1RunMotor(
                (int)Node2MotorProtocol.ControlWord.Start, 0);
            Node2CanIo.WriteCANPayload(rpdoRunWait, Node2Constants.CAN1,
                (int)Node2CANID.RPDO1 + 2);
        }

        if (iteration % 20 == 0)
        {
            Console.WriteLine(
                $"Node1 ready={cart.node1_ready} " +
                $"M2(v={cart.run_motor_actual_velocity_front},sw={cart.run_motor_status_word_front},target={cart.run_motor_target_velocity_front}) " +
                $"M1(pos={cart.turn_motor_actual_position_front},sw={cart.turn_motor_status_word_front},target={cart.turn_motor_target_position_front})");
        }
    }
}
