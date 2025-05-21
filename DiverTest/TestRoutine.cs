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

    public enum MotorMode : byte
    {
        SpeedMode = 0x03,
        PositionMode = 0x01
    }

    public enum MotorControlWord: byte
    {
        Stop = 0x05,
        Start = 0x06,
        Start2 = 0x07,
        Start3 = 0x0F
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
        [AsLowerIO] public int read_from_mcu;
        [AsUpperIO] public int write_to_mcu;
    }

    // Logic and MCU is strictly 1:1
    [UseCoralinkerMCU<CoralinkerCL1_0_12p>]
    [LogicRunOnMCU(mcuUri = "serial://name=COM5", scanInterval = 50)]
    public class TestMCURoutine : LadderLogic<TestVehicle>
    {
        int lastIteration = -1;
        int communicationLostTime = 0;
        bool ProtectionFlag = false;

        int motorStageA = (int)MotorBootupStage.Unknown;
        int motorRetryCountA = 0;
        int motorStageB = (int)MotorBootupStage.Unknown;
        int motorRetryCountB = 0;
        const int BootupRetryLimit = 20;
        
        public override void Operation(int iteration)
        {
            Console.WriteLine("ITR = " + iteration.ToString());
            if (iteration <= lastIteration || ProtectionFlag)
            {
                if (communicationLostTime < CommonConstants.CommunicationLostProtectionTime)
                {
                    communicationLostTime += 1;
                }
                else
                {
                    byte[] RPDO1FailSafe = new byte[7] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00 };
                    RunOnMCU.WriteEvent(RPDO1FailSafe, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)MotorID.MotorID3);
                    RunOnMCU.WriteEvent(RPDO1FailSafe, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.RPDO1 + (int)MotorID.MotorID4);
                    ProtectionFlag = true;
                    Console.WriteLine("Error, Protected\n");
                }
                return;
            }
            else
            {
                lastIteration = iteration;
                communicationLostTime = 0;
                ProtectionFlag = false;
            }

            bool isAllMotorBootupOK = true;
            Console.WriteLine("STAGEA = " + motorStageA.ToString());

            switch (motorStageA)
            {
                case (int)MotorBootupStage.Unknown:
                    // 发送 Reset 命令
                    byte[] coMsgReset = new byte[2] { (byte)NMTCommand.ResetNode, (byte)MotorID.MotorID3 };
                    RunOnMCU.WriteEvent(coMsgReset, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.NMT);
                    motorStageA = (int)MotorBootupStage.ResetSent;
                    motorRetryCountA = 0;
                    Console.WriteLine("MotorA Reset Sent");
                    break;
                case (int)MotorBootupStage.ResetSent:
                    // 检查是否收到Bootup
                    byte[] bootupMsg = RunOnMCU.ReadEvent((int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.HEARTBEAT + (int)MotorID.MotorID3);
                    if (bootupMsg != null && bootupMsg.Length == 1 && bootupMsg[0] == (byte)HEARTBEAT.Bootup)
                    {
                        motorStageA = (int)MotorBootupStage.BootupReceived;
                        motorRetryCountA = 0;
                        Console.WriteLine("MotorA Bootup Received");
                    }
                    else
                    {
                        motorRetryCountA++;
                        if (motorRetryCountA > BootupRetryLimit)
                        {
                            // 重发Reset
                            motorStageA = (int)MotorBootupStage.Unknown;
                        }
                    }
                    break;
                case (int)MotorBootupStage.BootupReceived:
                    //isAllMotorBootupOK = false;
                    // 发送Start命令
                    byte[] coMsgStart = new byte[2] { (byte)NMTCommand.StartNode, (byte)MotorID.MotorID3 };
                    RunOnMCU.WriteEvent(coMsgStart, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.NMT);
                    motorStageA = (int)MotorBootupStage.StartSent;
                    Console.WriteLine("Motor Start Sent");
                    break;
                case (int)MotorBootupStage.StartSent:
                    //isAllMotorBootupOK = false;
                    // 检查是否收到Start后的心跳
                    byte[] heartbeatMsg = RunOnMCU.ReadEvent((int)CoralinkerDIVERVehicle.PortIndex.CAN1, (int)CANID.HEARTBEAT + (int)MotorID.MotorID3);
                    if (heartbeatMsg != null && heartbeatMsg.Length > 1 && heartbeatMsg[0] == (byte)HEARTBEAT.Operational)
                    {
                        motorStageA = (int)MotorBootupStage.StartReceived;
                        motorRetryCountA = 0;
                        Console.WriteLine("Motor Start Received");
                    }
                    else
                    {
                        motorRetryCountA++;
                        if (motorRetryCountA > BootupRetryLimit)
                        {
                            // 重发Start
                            motorStageA = (int)MotorBootupStage.BootupReceived;
                        }
                    }
                    break;
                case (int)MotorBootupStage.StartReceived:
                default:
                    // 已启动，无需处理
                    break;
            }

            //if (isAllMotorBootupOK)
            //{
            //    Console.WriteLine("All Motor is started");
            //}

            //if (iteration < 5)
            //{
            //    byte[] coMsgStartMotor = new byte[2] { 0x01, 0x04 };
            //    RunOnMCU.WriteEvent(coMsgStartMotor, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, 0x00);
            //}

            //byte ctlWord = 0x05;
            //byte speed = 0x0;
            //if (iteration <= 10)
            //{
            //    ctlWord = 0x06;
            //}
            //else if (iteration <= 20)
            //{
            //    ctlWord = 0x07;
            //}
            //else if (iteration <= 30)
            //{
            //    ctlWord = 0x0F;
            //}
            //else
            //{
            //    ctlWord = 0x0F;
            //    speed = 0x20;
            //}

            //if (iteration >= 5)
            //{
            //    byte[] coTPDO1 = new byte[7] { 0x03, 0x00, 0x00, speed, 0x00, ctlWord, 0x00 };
            //    RunOnMCU.WriteEvent(coTPDO1, (int)CoralinkerDIVERVehicle.PortIndex.CAN1, 0x204);
            //    Console.WriteLine("CAN SPEED" + (int)speed);
            //    Console.WriteLine("CAN CTLWORD" + (int)ctlWord);
            //}
        }
    }
}
