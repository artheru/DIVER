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
        MotorID3 = 3,
        MotorID4 = 4
    }   

    public enum CANID : int
    {
        RPDO1 = 0x200,
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

        // RPDO1
        // Byte 0 = mode， 60600008 
        // Byte 1 2 3 4 = speed (Little-endian), 60FF0020
        // Byte 5 6 = controlWord (Little-endian), 60400010
        public static byte[] GenerateRPDO1(
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

        public struct Feedback {
            public bool valid;
            public int controlWord;
            public int speed;
            public int statusWord;
        }
        public static Feedback ParseTPDO1(byte[] TPDO1)
        {
            Feedback feedback = new Feedback();
            feedback.valid = false;
            if (TPDO1 == null || TPDO1.Length != 8)
            {
                return feedback;
            }

            feedback.controlWord = (int)(TPDO1[0] | (TPDO1[1] << 8));
            feedback.speed = (int)(TPDO1[2] | (TPDO1[3] << 8) | (TPDO1[4] << 16) | (TPDO1[5] << 24));
            feedback.statusWord = (int)(TPDO1[6] | (TPDO1[7] << 8));
            return feedback;
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
    [LogicRunOnMCU(mcuUri = "serial://name=COM5", scanInterval = 50)]
    public class TestMCURoutine : LadderLogic<TestVehicle>
    {
        bool variableInitialized = false;

        int lastIteration = 0;
        int communicationLostTime = 0;
        bool failProtectionFlag = false;

        int[] motorStage;
        int[] motorRetryCount;
        int[] motorID;
        Motor.Feedback[] motorFeedback;
        int[] motorTargetSpeed;

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
                [0] = (int)MotorID.MotorID3;
            motorID
                [1] = (int)MotorID.MotorID4;

            motorFeedback = new Motor.Feedback[2];
            motorFeedback[0].valid = false;
            motorFeedback[1].valid = false;

            motorTargetSpeed = new int[2];
            motorTargetSpeed[0] = 0;
            motorTargetSpeed[1] = 0;
        }

        public void FailSafe()
        {
            // Set motor to fail-safe mode (Stop Mode and Speed 0)
            byte[] RPDO1FailSafe = Motor.GenerateRPDO1();
            RunOnMCU.WriteEvent(RPDO1FailSafe, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)motorID[0]);
            RunOnMCU.WriteEvent(RPDO1FailSafe, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)motorID[1]);
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

            bool isAllMotorBooted = true;
            for (int i = 0; i < motorStage.Length; i++)
            {
                isAllMotorBooted &= MotorBootupHelper(i);

                byte[] TPDO1 = RunOnMCU.ReadEvent((int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.TPDO1 + (int)motorID[i]);
                Motor.Feedback fb = Motor.ParseTPDO1(TPDO1);
                if (fb.valid)
                {
                    motorFeedback[0] = fb;
                    Console.WriteLine("Actual Speed = " + fb.speed);
                }
            }

            if (isAllMotorBooted)
            {
                motorTargetSpeed[0] = 0;
                motorTargetSpeed[1] = 100000 * (iteration % 100) ;
                for (int i = 0; i < motorStage.Length; i++)
                {
                    byte[] TPDO1 = RunOnMCU.ReadEvent((int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.TPDO1 + (int)motorID[i]);
                    Motor.Feedback fb = Motor.ParseTPDO1(TPDO1);
                    if (fb.valid)
                    {
                        motorFeedback[0] = fb;
                    }

                    byte[] RPDO1 = Motor.GenerateRPDO1((int)Motor.ControlWord.Start3, motorTargetSpeed[i]);
                    RunOnMCU.WriteEvent(
                        RPDO1, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)motorID[i]);
                }
            }
            else
            {
                Console.WriteLine("Still Waiting");
                motorTargetSpeed[0] = 0;
                motorTargetSpeed[1] = 0;
                // Set motor to wait mode
                byte[] RPDO1Wait = Motor.GenerateRPDO1((int)Motor.ControlWord.Start);
                RunOnMCU.WriteEvent(RPDO1Wait, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)motorID[0]);
                RunOnMCU.WriteEvent(RPDO1Wait, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)motorID[1]);
            }

        }
    }
}
