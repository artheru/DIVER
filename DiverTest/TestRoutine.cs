using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CartActivator;
using DiverTest.DIVER.CoralinkerAdaption;
using Microsoft.VisualBasic;
using TEST;

namespace DiverTest
{
    // interaction interface.
    public static class CommonConstants
    {
        public const int CommunicationLostProtectionTime = 10;
    }

    public enum MotorID
    {
        MotorID1 = 1,
        MotorID2 = 2,
        MotorNode2Turn = 3,
        MotorNode2Run = 4
    }   

    public enum CANID : int
    {
        RPDO1 = 0x200,
        RPDO2 = 0x300,
        TPDO1 = 0x180,
        HEARTBEAT = 0x700,
        NMT = 0x000
    }

    public enum NMTCommand : byte
    {
        StartNode = 0x01,
        StopNode = 0x02,
        EnterPreOp = 0x80,
        ResetNode = 0x81,
        ResetComm = 0x82
    }

    public enum HEARTBEAT : byte
    {
        Bootup = 0x00,
        Operational = 0x05,
        Stopped = 0x04
    }

    public enum MotorBootupStage
    {
        Unknown = 0,
        ResetSent = 1,
        BootupReceived = 2,
        StartSent = 3,
        StartReceived = 4,
    }

    public class Motor
    {
        public enum Mode : byte
        {
            FindOrigin = 0x06,
            SpeedMode = 0x03,
            PositionMode = 0x01
        }
        public enum ControlWord : byte
        {
            Stop = 0x05,
            Start = 0x06,
            Start2 = 0x07,
            Start3 = 0x0F
        }

        // RPDO1 RunMotor
        // Byte 0 = mode， 60600008 
        // Byte 1 2 3 4 = speed (Little-endian), 60FF0020
        // Byte 5 6 = controlWord (Little-endian), 60400010
        public static byte[] GenerateRPDO1RunMotor(
            int controlWord = (int)ControlWord.Stop,
            int speed = 0
        )
        {
            byte[] RPDO1 = new byte[7];
            RPDO1[0] = (byte)Motor.Mode.SpeedMode;
            RPDO1[1] = (byte)(speed & 0xFF);
            RPDO1[2] = (byte)((speed >> 8) & 0xFF);
            RPDO1[3] = (byte)((speed >> 16) & 0xFF);
            RPDO1[4] = (byte)((speed >> 24) & 0xFF);
            RPDO1[5] = (byte)((int)controlWord & 0xFF);
            RPDO1[6] = (byte)(((int)controlWord >> 8) & 0xFF);
            return RPDO1;
        }

        // TPDO1
        // 60410010, StatusWord
        // 606C0020, ActualVelocity
        // 60400010, ControlWord
        // Parse RPDO1 and return

        //public struct Feedback {
        //    public bool valid;
        //    public int controlWord;
        //    public int speed;
        //    public int statusWord;
        //}
        //public static Feedback ParseTPDO1RunMotor(byte[] TPDO1)
        //{
        //    Feedback feedback = new Feedback();
        //    feedback.valid = false;
        //    if (TPDO1 == null || TPDO1.Length != 8)
        //    {
        //        return feedback;
        //    }

        //    feedback.controlWord = (int)(TPDO1[0] | (TPDO1[1] << 8));
        //    feedback.speed = (int)(TPDO1[2] | (TPDO1[3] << 8) | (TPDO1[4] << 16) | (TPDO1[5] << 24));
        //    feedback.statusWord = (int)(TPDO1[6] | (TPDO1[7] << 8));
        //    feedback.valid = true;
        //    return feedback;
        //}

        public static int ParseTPDO1SpeedRunMotor(byte[] TPDO1)
        {
            if (TPDO1 == null || TPDO1.Length != 8)
            {
                return 0;
            }

            int speed = (int)(TPDO1[2] | (TPDO1[3] << 8) | (TPDO1[4] << 16) | (TPDO1[5] << 24));
            return speed;
        }


    }

    public class TestLinking: Coralinking
    {
        public override void Define()
        {
            Console.WriteLine("Coralinker Definition");
            var node1 = Root.Downlink(typeof(TestMCURoutine));
            var p1= node1.ResolvedPin("battery-12V","input-1"); // denote a pin is forcefully placed.
            var p2 = node1.UnresolvedPin("gnd");
            node1.RequireConnect(p1, p2); // todo vargs
            // .. multi

           
            //var node2 = node1.Downlink(typeof(TestMCURoutineNode2));
            //.. list all connection here.
        }
    }

    [DefineCoralinking<TestLinking>]
    public class TestVehicle : CoralinkerDIVERVehicle
    {
        [AsLowerIO] public int motor_actual_velocity_A;
        [AsLowerIO] public int motor_actual_velocity_B;
        [AsUpperIO] public int motor_target_speed_A;
        [AsUpperIO] public int motor_target_speed_B;
    }

    // Logic and MCU is strictly 1:1
    [UseCoralinkerMCU<CoralinkerCL1_0_12p>]
    [LogicRunOnMCU(mcuUri = "serial://name=COM15", scanInterval = 300)]
    public class TestMCURoutine : LadderLogic<TestVehicle>
    {
        bool variableInitialized = false;

        int lastIteration = 0;
        int communicationLostTime = 0;
        bool failProtectionFlag = false;

        int[] motorStage;
        int[] motorRetryCount;
        int[] motorID;
        int runMotorSpeed;
        int runMotorTargetSpeed;
        int turnMotorPosition;
        int turnMotorTargetPosition;

        bool[] inputs;
        bool[] outputs;

        const int BootupRetryLimit = 20;

        public void Initialize()
        {
            motorStage = new int[2];
            motorStage
                [0] = (int)MotorBootupStage.Unknown;
            motorStage
                [1] = (int)MotorBootupStage.Unknown;

            motorRetryCount = new int[2];
            motorRetryCount
                [0] = 0;
            motorRetryCount
                [1] = 0;

            motorID = new int[2];
            motorID
                [0] = (int)MotorID.MotorNode2Run;
            motorID
                [1] = (int)MotorID.MotorNode2Turn;

            inputs = new bool[16];
            outputs = new bool[16];
        }

        public void FailSafe()
        {
            // Set motor to fail-safe mode (Stop Mode and Speed 0)
            byte[] RPDO1FailSafeRunMotor = Motor.GenerateRPDO1RunMotor();
            RunOnMCU.WriteEvent(RPDO1FailSafeRunMotor, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)motorID[0]);
            //RunOnMCU.WriteEvent(RPDO1FailSafe, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)motorID[1]);
        }

        public bool MotorBootupHelper(int i)
        {
            switch (motorStage[i])
            {
                case (int)MotorBootupStage.Unknown:
                    // 发送 Reset 命令
                    byte[] coMsgReset = new byte[2] { (byte)NMTCommand.ResetNode, (byte)motorID[i] };
                    RunOnMCU.WriteEvent(coMsgReset, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.NMT);
                    motorStage[i] = (int)MotorBootupStage.ResetSent;
                    motorRetryCount[i] = 0;
                    Console.WriteLine("Motor Reset Sent");
                    return false;
                case (int)MotorBootupStage.ResetSent:
                    // 检查是否收到Bootup
                    {
                        byte[] heartbeatMsg = RunOnMCU.ReadEvent(
                            (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.HEARTBEAT + motorID[i]);
                        if (heartbeatMsg != null && heartbeatMsg.Length == 1 && heartbeatMsg[0] == (byte)HEARTBEAT.Bootup)
                        {
                            motorStage[i] = (int)MotorBootupStage.BootupReceived;
                            motorRetryCount[i] = 0;
                            Console.WriteLine("Motor Bootup Received");
                        }
                        else
                        {
                            motorRetryCount[i]++;
                            if (motorRetryCount[i] > BootupRetryLimit)
                            {
                                // 重发Reset
                                motorStage[i] = (int)MotorBootupStage.Unknown;
                            }
                        }
                    }
                    return false;
                case (int)MotorBootupStage.BootupReceived:
                    //isAllMotorBootupOK = false;
                    // 发送Start命令
                    byte[] coMsgStart = new byte[2] { (byte)NMTCommand.StartNode, (byte)motorID[i] };
                    RunOnMCU.WriteEvent(coMsgStart, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.NMT);
                    motorStage[i] = (int)MotorBootupStage.StartReceived;
                    motorRetryCount[i] = 0;
                    Console.WriteLine("Motor Start Sent");
                    return false;
                case (int)MotorBootupStage.StartSent:
                    //isAllMotorBootupOK = false;
                    // 检查是否收到Start后的心跳
                    {
                        byte[] heartbeatMsg = RunOnMCU.ReadEvent(
                            (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.HEARTBEAT + (int)motorID[i]);
                        if (heartbeatMsg != null && heartbeatMsg.Length == 1 && heartbeatMsg[0] == (byte)HEARTBEAT.Operational)
                        {
                            motorStage[i] = (int)MotorBootupStage.StartReceived;
                            motorRetryCount[i] = 0;
                            Console.WriteLine("Motor Start Received");
                        }
                        else
                        {
                            motorRetryCount[i]++;
                            if (motorRetryCount[i] > BootupRetryLimit)
                            {
                                // 重发Start
                                motorStage[i] = (int)MotorBootupStage.BootupReceived;
                            }
                        }
                    }
                    return false;
                case (int)MotorBootupStage.StartReceived:
                    return true;
                default:
                    return false;
            }
        }

        public override void Operation(int iteration)
        {
            // Initialize variables
            if (!variableInitialized)
            {
                Initialize();
                Console.WriteLine("Variable Initialized");
                variableInitialized = true;
            }

            // Check for communication loss
            if (iteration <= lastIteration || failProtectionFlag)
            {
                if (communicationLostTime < CommonConstants.CommunicationLostProtectionTime)
                {
                    communicationLostTime += 1;
                }
                else
                {
                    FailSafe();
                    failProtectionFlag = true;
                    Console.WriteLine("Error, Protected\n");
                }
                return;
            }
            else
            {
                lastIteration = iteration;
                communicationLostTime = 0;
                failProtectionFlag = false;
            }

            byte[] snapshot = RunOnMCU.ReadSnapshot();
            if (snapshot != null && snapshot.Length >= 8)
            {
                // snapshot is Length 8
                // the first byte is input 0-7
                // the second byte is input 8-15
                for (int i = 0; i < 8; i++)
                {
                    inputs[i] = (snapshot[0] & (byte)(1 << i)) != 0;
                    inputs[i + 8] = (snapshot[1] & (byte)(1 << i)) != 0;
                }
            }

            for (int i = 0; i < 16; i++)
            {
                if (inputs[i])
                {
                    Console.WriteLine(i.ToString() + " ON");
                } else
                {
                    Console.WriteLine(i.ToString() + " OFF");
                }
            }

            byte[] TPDO1RunMotor = RunOnMCU.ReadEvent((int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.TPDO1 + (int)motorID[0]);
            byte[] TPDO1TurnMotor = RunOnMCU.ReadEvent((int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.TPDO1 + (int)motorID[1]);
            runMotorSpeed = Motor.ParseTPDO1SpeedRunMotor(TPDO1RunMotor);
            Console.WriteLine("RunMotor Speed = " + runMotorSpeed.ToString());
            //turnMotorPosition

            bool isAllMotorBooted = true;
            for (int i = 0; i < motorStage.Length; i++)
            {
                isAllMotorBooted &= MotorBootupHelper(i);
            }

            if (isAllMotorBooted)
            {
                runMotorTargetSpeed = 100000 * (iteration % 100);
                byte[] RPDO1RunMotor = Motor.GenerateRPDO1RunMotor((int)Motor.ControlWord.Start3, runMotorTargetSpeed);
                RunOnMCU.WriteEvent(
                    RPDO1RunMotor, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)motorID[0]);
                Console.WriteLine("All Motor Booted");
            }
            else
            {
                runMotorTargetSpeed = 0;
                byte[] RPDO1RunMotor = Motor.GenerateRPDO1RunMotor((int)0x06, runMotorTargetSpeed);
                RunOnMCU.WriteEvent(
                    RPDO1RunMotor, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)motorID[0]);
                
                Console.WriteLine("Not All Motor Booted");
            }

           
        }
    }
}
