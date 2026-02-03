using System;
using CartActivator;

namespace CoralinkerTestCar
{
    #region CANOpen Protocol Constants

    /// <summary>
    /// CANOpen CAN ID offsets for different message types
    /// </summary>
    public static class CANOpenID
    {
        public const int NMT = 0x000;
        public const int SYNC = 0x080;
        public const int EMCY = 0x080;       // + NodeID
        public const int TPDO1 = 0x180;      // + NodeID
        public const int RPDO1 = 0x200;      // + NodeID
        public const int TPDO2 = 0x280;      // + NodeID
        public const int RPDO2 = 0x300;      // + NodeID
        public const int TPDO3 = 0x380;      // + NodeID
        public const int RPDO3 = 0x400;      // + NodeID
        public const int TPDO4 = 0x480;      // + NodeID
        public const int RPDO4 = 0x500;      // + NodeID
        public const int SDO_TX = 0x580;     // + NodeID
        public const int SDO_RX = 0x600;     // + NodeID
        public const int HEARTBEAT = 0x700;  // + NodeID
    }

    /// <summary>
    /// CANOpen NMT commands
    /// </summary>
    public enum NMTCommand : byte
    {
        StartNode = 0x01,
        StopNode = 0x02,
        EnterPreOperational = 0x80,
        ResetNode = 0x81,
        ResetCommunication = 0x82
    }

    /// <summary>
    /// CANOpen heartbeat states
    /// </summary>
    public enum HeartbeatState : byte
    {
        Bootup = 0x00,
        Stopped = 0x04,
        Operational = 0x05,
        PreOperational = 0x7F
    }

    /// <summary>
    /// CiA 402 motor operation modes
    /// </summary>
    public enum MotorMode : byte
    {
        PositionMode = 0x01,
        VelocityMode = 0x03,
        HomingMode = 0x06
    }

    /// <summary>
    /// CiA 402 controlword bit definitions
    /// </summary>
    public static class ControlWord
    {
        public const int SwitchOn = 0x0001;
        public const int EnableVoltage = 0x0002;
        public const int QuickStop = 0x0004;
        public const int EnableOperation = 0x0008;
        public const int NewSetpoint = 0x0010;        // Position mode
        public const int HomingStart = 0x0010;        // Homing mode
        public const int ChangeSetImmediately = 0x0020;
        public const int AbsoluteRelative = 0x0040;

        // Common control word values
        public const int Shutdown = EnableVoltage | QuickStop;                    // 0x06
        public const int SwitchOnEnable = SwitchOn | EnableVoltage | QuickStop;   // 0x07
        public const int EnableOp = SwitchOn | EnableVoltage | QuickStop | EnableOperation; // 0x0F
    }

    /// <summary>
    /// CiA 402 statusword bit definitions
    /// </summary>
    public static class StatusWord
    {
        public const int ReadyToSwitchOn = 0x0001;
        public const int SwitchedOn = 0x0002;
        public const int OperationEnabled = 0x0004;
        public const int Fault = 0x0008;
        public const int VoltageEnabled = 0x0010;
        public const int QuickStop = 0x0020;
        public const int SwitchOnDisabled = 0x0040;
        public const int Warning = 0x0080;
        public const int TargetReached = 0x0400;
        public const int HomingAttained = 0x1000;      // Bit 12 in homing mode
        public const int HomingError = 0x2000;         // Bit 13 in homing mode
    }

    #endregion

    #region Motor Boot State Machine

    /// <summary>
    /// Motor boot state machine stages
    /// </summary>
    public enum MotorBootStage
    {
        Unknown = 0,
        ResetSent = 1,
        BootupReceived = 2,
        StartSent = 3,
        Operational = 4,
        Error = 99
    }

    #endregion

    #region CANOpen Motor Class

    /// <summary>
    /// CANOpen motor with state management.
    /// Handles NMT boot sequence and PDO operations.
    /// Supports both velocity and position modes.
    /// </summary>
    public class CANOpenMotor
    {
        // Configuration (set once at init)
        public string Name;
        public int NodeId;
        public int CanPort;
        public int PdoCycle;
        public int BootupRetryLimit;
        public int MotorType; // 0 = Speed, 1 = Position

        // State
        public int BootStage;
        public int RetryCount;
        public int PdoCounter;

        // Feedback values
        public int StatusWord;
        public int ActualSpeed;
        public int ActualPosition;
        public int ActualMode;

        // Target values
        public int TargetSpeed;
        public int TargetPosition;
        public int TargetControlWord;
        public int ProfileVelocity;

        // Homing state (for position motors)
        public bool HomingStarted;

        public CANOpenMotor()
        {
            BootupRetryLimit = 20;
            PdoCycle = 1;
            TargetControlWord = ControlWord.EnableOp;
            ProfileVelocity = 100000;
        }

        #region Properties

        public bool IsOperational()
        {
            return BootStage == (int)MotorBootStage.Operational;
        }

        public bool HasFault()
        {
            return (StatusWord & CoralinkerTestCar.StatusWord.Fault) != 0;
        }

        public bool IsHomingComplete()
        {
            return (StatusWord & CoralinkerTestCar.StatusWord.HomingAttained) != 0;
        }

        public bool HasHomingError()
        {
            return (StatusWord & CoralinkerTestCar.StatusWord.HomingError) != 0;
        }

        #endregion

        #region NMT Boot Sequence

        /// <summary>
        /// Process motor bootup sequence. Returns true when operational.
        /// </summary>
        public bool ProcessBootup()
        {
            switch (BootStage)
            {
                case (int)MotorBootStage.Unknown:
                    SendNMTCommand((byte)NMTCommand.ResetNode);
                    BootStage = (int)MotorBootStage.ResetSent;
                    RetryCount = 0;
                    Console.WriteLine("[" + Name + "] Reset sent");
                    return false;

                case (int)MotorBootStage.ResetSent:
                    {
                        byte[] heartbeat = ReadHeartbeat();
                        if (heartbeat != null && heartbeat.Length == 1 && heartbeat[0] == (byte)HeartbeatState.Bootup)
                        {
                            BootStage = (int)MotorBootStage.BootupReceived;
                            RetryCount = 0;
                            Console.WriteLine("[" + Name + "] Bootup received");
                        }
                        else
                        {
                            RetryCount++;
                            if (RetryCount > BootupRetryLimit)
                            {
                                BootStage = (int)MotorBootStage.Unknown; // Retry reset
                            }
                        }
                    }
                    return false;

                case (int)MotorBootStage.BootupReceived:
                    SendNMTCommand((byte)NMTCommand.StartNode);
                    BootStage = (int)MotorBootStage.StartSent;
                    RetryCount = 0;
                    Console.WriteLine("[" + Name + "] Start sent");
                    return false;

                case (int)MotorBootStage.StartSent:
                    {
                        byte[] heartbeat = ReadHeartbeat();
                        if (heartbeat != null && heartbeat.Length == 1 && heartbeat[0] == (byte)HeartbeatState.Operational)
                        {
                            BootStage = (int)MotorBootStage.Operational;
                            RetryCount = 0;
                            Console.WriteLine("[" + Name + "] Operational");
                        }
                        else
                        {
                            RetryCount++;
                            if (RetryCount > BootupRetryLimit)
                            {
                                BootStage = (int)MotorBootStage.BootupReceived; // Retry start
                            }
                        }
                    }
                    return false;

                case (int)MotorBootStage.Operational:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Reset motor to initial state
        /// </summary>
        public void Reset()
        {
            BootStage = (int)MotorBootStage.Unknown;
            RetryCount = 0;
            StatusWord = 0;
            HomingStarted = false;
        }

        #endregion

        #region CAN Communication

        void SendNMTCommand(byte cmd)
        {
            byte[] data = new byte[2];
            data[0] = cmd;
            data[1] = (byte)NodeId;
            RunOnMCU.WriteEvent(data, CanPort, CANOpenID.NMT);
        }

        byte[] ReadHeartbeat()
        {
            return RunOnMCU.ReadEvent(CanPort, CANOpenID.HEARTBEAT + NodeId);
        }

        void WriteRPDO1(byte[] data)
        {
            RunOnMCU.WriteEvent(data, CanPort, CANOpenID.RPDO1 + NodeId);
        }

        void WriteRPDO2(byte[] data)
        {
            RunOnMCU.WriteEvent(data, CanPort, CANOpenID.RPDO2 + NodeId);
        }

        byte[] ReadTPDO1()
        {
            return RunOnMCU.ReadEvent(CanPort, CANOpenID.TPDO1 + NodeId);
        }

        /// <summary>
        /// Check if PDO should be sent this cycle based on configured cycle time
        /// </summary>
        bool ShouldSendPDO()
        {
            if (PdoCycle <= 0)
                return true;

            PdoCounter++;
            if (PdoCounter >= PdoCycle)
            {
                PdoCounter = 0;
                return true;
            }
            return false;
        }

        #endregion

        #region Feedback Processing

        /// <summary>
        /// Process motor feedback (read TPDO)
        /// </summary>
        public void ProcessFeedback()
        {
            byte[] tpdo = ReadTPDO1();
            if (tpdo == null)
                return;

            if (MotorType == 0)
            {
                // Speed motor: [StatusWord:2][ActualVelocity:4][ControlWord:2] = 8 bytes
                if (tpdo.Length >= 8)
                {
                    StatusWord = tpdo[0] | (tpdo[1] << 8);
                    ActualSpeed = tpdo[2] | (tpdo[3] << 8) | (tpdo[4] << 16) | (tpdo[5] << 24);
                }
            }
            else
            {
                // Position motor: [StatusWord:2][ActualPosition:4][Mode:1] = 7 bytes
                if (tpdo.Length >= 7)
                {
                    StatusWord = tpdo[0] | (tpdo[1] << 8);
                    ActualPosition = tpdo[2] | (tpdo[3] << 8) | (tpdo[4] << 16) | (tpdo[5] << 24);
                    ActualMode = tpdo[6];
                }
            }
        }

        #endregion

        #region Command Sending

        /// <summary>
        /// Send motor command (write RPDO)
        /// </summary>
        public void SendCommand()
        {
            if (!ShouldSendPDO())
                return;

            if (MotorType == 0)
            {
                SendSpeedCommand();
            }
            else
            {
                SendPositionCommand();
            }
        }

        void SendSpeedCommand()
        {
            // RPDO1 format: [Mode:1][TargetVelocity:4][ControlWord:2] = 7 bytes
            byte[] rpdo = new byte[7];
            rpdo[0] = (byte)MotorMode.VelocityMode;
            rpdo[1] = (byte)(TargetSpeed & 0xFF);
            rpdo[2] = (byte)((TargetSpeed >> 8) & 0xFF);
            rpdo[3] = (byte)((TargetSpeed >> 16) & 0xFF);
            rpdo[4] = (byte)((TargetSpeed >> 24) & 0xFF);
            rpdo[5] = (byte)(TargetControlWord & 0xFF);
            rpdo[6] = (byte)((TargetControlWord >> 8) & 0xFF);
            WriteRPDO1(rpdo);
        }

        void SendPositionCommand()
        {
            // Determine mode and control word
            byte mode = (byte)MotorMode.PositionMode;
            int ctrlWord = TargetControlWord;

            // If homing not complete, do homing first
            if (!IsHomingComplete() && !HasHomingError())
            {
                mode = (byte)MotorMode.HomingMode;
                if (HomingStarted)
                {
                    ctrlWord = ControlWord.EnableOp | ControlWord.HomingStart;
                }
                else
                {
                    ctrlWord = ControlWord.EnableOp;
                }
                HomingStarted = true;
            }
            else
            {
                // Position mode with new setpoint
                ctrlWord = TargetControlWord | ControlWord.NewSetpoint | ControlWord.ChangeSetImmediately;
            }

            // RPDO1 format: [Mode:1][TargetPosition:4][ControlWord:2] = 7 bytes
            byte[] rpdo1 = new byte[7];
            rpdo1[0] = mode;
            rpdo1[1] = (byte)(TargetPosition & 0xFF);
            rpdo1[2] = (byte)((TargetPosition >> 8) & 0xFF);
            rpdo1[3] = (byte)((TargetPosition >> 16) & 0xFF);
            rpdo1[4] = (byte)((TargetPosition >> 24) & 0xFF);
            rpdo1[5] = (byte)(ctrlWord & 0xFF);
            rpdo1[6] = (byte)((ctrlWord >> 8) & 0xFF);
            WriteRPDO1(rpdo1);

            // RPDO2 format: [ProfileVelocity:4] = 4 bytes
            byte[] rpdo2 = new byte[4];
            rpdo2[0] = (byte)(ProfileVelocity & 0xFF);
            rpdo2[1] = (byte)((ProfileVelocity >> 8) & 0xFF);
            rpdo2[2] = (byte)((ProfileVelocity >> 16) & 0xFF);
            rpdo2[3] = (byte)((ProfileVelocity >> 24) & 0xFF);
            WriteRPDO2(rpdo2);
        }

        #endregion

        #region Emergency Stop

        /// <summary>
        /// Emergency stop - set motor to safe state
        /// </summary>
        public void EmergencyStop()
        {
            TargetSpeed = 0;
            TargetControlWord = ControlWord.Shutdown;

            // Send immediately
            byte[] rpdo = new byte[7];
            if (MotorType == 0)
            {
                rpdo[0] = (byte)MotorMode.VelocityMode;
                rpdo[1] = 0;
                rpdo[2] = 0;
                rpdo[3] = 0;
                rpdo[4] = 0;
            }
            else
            {
                rpdo[0] = (byte)MotorMode.PositionMode;
                rpdo[1] = (byte)(ActualPosition & 0xFF);
                rpdo[2] = (byte)((ActualPosition >> 8) & 0xFF);
                rpdo[3] = (byte)((ActualPosition >> 16) & 0xFF);
                rpdo[4] = (byte)((ActualPosition >> 24) & 0xFF);
            }
            rpdo[5] = (byte)(ControlWord.Shutdown & 0xFF);
            rpdo[6] = (byte)((ControlWord.Shutdown >> 8) & 0xFF);
            WriteRPDO1(rpdo);
        }

        #endregion
    }

    #endregion

    #region Vehicle State Machine

    /// <summary>
    /// Vehicle operation states
    /// </summary>
    public enum VehicleState
    {
        Initializing = 0,
        MotorBooting = 1,
        Ready = 2,
        Running = 3,
        EmergencyStop = 4,
        Fault = 5
    }

    /// <summary>
    /// Error codes
    /// </summary>
    public enum ErrorCode
    {
        None = 0,
        MotorBootFailed = 1,
        MotorFault = 2,
        EmergencyStop = 3,
        EdgeTouchTriggered = 4,
        CommunicationLost = 5
    }

    #endregion

    #region Test Vehicle Definition

    /// <summary>
    /// Test vehicle IO definition.
    /// Defines variables exchanged between host and MCU.
    /// </summary>
    public class TestVehicle : CartDefinition
    {
        // ========== Remote Control Inputs (Host → MCU) ==========
        /// <summary>Target linear velocity from joystick (-1000 to 1000)</summary>
        [AsUpperIO] public int remote_linear_velocity;

        /// <summary>Target angular velocity from joystick (-1000 to 1000)</summary>
        [AsUpperIO] public int remote_angular_velocity;

        // ========== Control Signals (Host → MCU) ==========
        /// <summary>Start signal from host (rising edge triggers start)</summary>
        [AsUpperIO] public int start_signal;

        /// <summary>Stop signal from host (rising edge triggers stop)</summary>
        [AsUpperIO] public int stop_signal;

        /// <summary>Reset signal from host (rising edge triggers reset)</summary>
        [AsUpperIO] public int reset_signal;

        // ========== Safety Status (MCU → Host) ==========
        /// <summary>Emergency stop button status (1 = pressed/active)</summary>
        [AsLowerIO] public int emergency_stop_status;

        /// <summary>Edge touch sensor status (1 = triggered)</summary>
        [AsLowerIO] public int edge_touch_status;

        // ========== Motor Status (MCU → Host) ==========
        /// <summary>Left motor actual speed</summary>
        [AsLowerIO] public int left_motor_speed;

        /// <summary>Right motor actual speed</summary>
        [AsLowerIO] public int right_motor_speed;

        /// <summary>Left motor status word</summary>
        [AsLowerIO] public int left_motor_status;

        /// <summary>Right motor status word</summary>
        [AsLowerIO] public int right_motor_status;

        // ========== System Status (MCU → Host) ==========
        /// <summary>Vehicle operation state</summary>
        [AsLowerIO] public int vehicle_state;

        /// <summary>Error code (0 = no error)</summary>
        [AsLowerIO] public int error_code;
    }

    #endregion

    #region Test MCU Logic

    /// <summary>
    /// MCU logic for test vehicle.
    /// Handles motor control, safety monitoring, and remote control processing.
    /// </summary>
    [LogicRunOnMCU(mcuUri = "serial://vid=1A86&pid=55D3&serial=TEST001&id=testcar", scanInterval = 50)]
    public class TestMCULogic : LadderLogic<TestVehicle>
    {
        // ========== Motor Configuration ==========
        // These can be adjusted for different motor setups
        const int LEFT_MOTOR_NODE_ID = 1;
        const int RIGHT_MOTOR_NODE_ID = 2;
        const int CAN_PORT = 5;  // CAN1 on Coralinker
        const int PDO_CYCLE = 1; // Send PDO every scan cycle

        // ========== Speed Limits ==========
        const int MAX_LINEAR_SPEED = 500000;   // Max speed units
        const int MAX_ANGULAR_SPEED = 300000;  // Max turn rate

        // ========== Safety Parameters ==========
        const int COMMUNICATION_TIMEOUT = 10;  // Scans before comm lost protection

        // ========== Motor Count ==========
        const int MOTOR_COUNT = 2;
        const int LEFT_MOTOR_IDX = 0;
        const int RIGHT_MOTOR_IDX = 1;

        // ========== State Variables ==========
        bool _initialized;
        int _lastIteration;
        int _commLostCounter;
        int _state;
        int _errorCode;

        // Previous signal states for edge detection
        int _lastStartSignal;
        int _lastStopSignal;
        int _lastResetSignal;

        // ========== Motors (using array instead of List) ==========
        CANOpenMotor[] _motors;

        // ========== IO State ==========
        bool[] _digitalInputs;

        void Initialize()
        {
            // Create motor array
            _motors = new CANOpenMotor[MOTOR_COUNT];

            // Left motor (speed mode)
            _motors[LEFT_MOTOR_IDX] = new CANOpenMotor();
            _motors[LEFT_MOTOR_IDX].Name = "LeftMotor";
            _motors[LEFT_MOTOR_IDX].NodeId = LEFT_MOTOR_NODE_ID;
            _motors[LEFT_MOTOR_IDX].CanPort = CAN_PORT;
            _motors[LEFT_MOTOR_IDX].PdoCycle = PDO_CYCLE;
            _motors[LEFT_MOTOR_IDX].BootupRetryLimit = 20;
            _motors[LEFT_MOTOR_IDX].MotorType = 0; // Speed motor

            // Right motor (speed mode)
            _motors[RIGHT_MOTOR_IDX] = new CANOpenMotor();
            _motors[RIGHT_MOTOR_IDX].Name = "RightMotor";
            _motors[RIGHT_MOTOR_IDX].NodeId = RIGHT_MOTOR_NODE_ID;
            _motors[RIGHT_MOTOR_IDX].CanPort = CAN_PORT;
            _motors[RIGHT_MOTOR_IDX].PdoCycle = PDO_CYCLE;
            _motors[RIGHT_MOTOR_IDX].BootupRetryLimit = 20;
            _motors[RIGHT_MOTOR_IDX].MotorType = 0; // Speed motor

            // Initialize digital inputs array
            _digitalInputs = new bool[16];

            _state = (int)VehicleState.MotorBooting;
            _lastIteration = -1;

            Console.WriteLine("TestCar: Initialized");
        }

        void ReadDigitalInputs()
        {
            byte[] snapshot = RunOnMCU.ReadSnapshot();
            if (snapshot != null && snapshot.Length >= 8)
            {
                for (int i = 0; i < 8; i++)
                {
                    _digitalInputs[i] = (snapshot[0] & (byte)(1 << i)) != 0;
                    _digitalInputs[i + 8] = (snapshot[1] & (byte)(1 << i)) != 0;
                }
            }
        }

        void UpdateSafetyStatus()
        {
            // Configure these based on actual wiring:
            // Emergency stop: Input A.3 (index 3), NC (active low)
            // Edge touch: Input B.3 (index 11), NO (active high)

            bool emgRaw = !_digitalInputs[3];  // NC contact, active when open
            bool edgeRaw = _digitalInputs[11]; // NO contact, active when closed

            cart.emergency_stop_status = emgRaw ? 1 : 0;
            cart.edge_touch_status = edgeRaw ? 1 : 0;

            // Check for safety triggers
            if (emgRaw && _state == (int)VehicleState.Running)
            {
                _state = (int)VehicleState.EmergencyStop;
                _errorCode = (int)ErrorCode.EmergencyStop;
                Console.WriteLine("TestCar: Emergency Stop Triggered!");
            }

            if (edgeRaw && _state == (int)VehicleState.Running)
            {
                _state = (int)VehicleState.EmergencyStop;
                _errorCode = (int)ErrorCode.EdgeTouchTriggered;
                Console.WriteLine("TestCar: Edge Touch Triggered!");
            }
        }

        void ProcessControlSignals()
        {
            // Detect rising edges
            bool startEdge = (cart.start_signal == 1 && _lastStartSignal == 0);
            bool stopEdge = (cart.stop_signal == 1 && _lastStopSignal == 0);
            bool resetEdge = (cart.reset_signal == 1 && _lastResetSignal == 0);

            _lastStartSignal = cart.start_signal;
            _lastStopSignal = cart.stop_signal;
            _lastResetSignal = cart.reset_signal;

            // Handle state transitions
            if (_state == (int)VehicleState.Ready)
            {
                if (startEdge)
                {
                    _state = (int)VehicleState.Running;
                    Console.WriteLine("TestCar: Started");
                }
            }
            else if (_state == (int)VehicleState.Running)
            {
                if (stopEdge)
                {
                    _state = (int)VehicleState.Ready;
                    EmergencyStopAllMotors();
                    Console.WriteLine("TestCar: Stopped");
                }
            }
            else if (_state == (int)VehicleState.EmergencyStop || _state == (int)VehicleState.Fault)
            {
                if (resetEdge)
                {
                    ResetAllMotors();
                    _state = (int)VehicleState.MotorBooting;
                    _errorCode = (int)ErrorCode.None;
                    Console.WriteLine("TestCar: Reset");
                }
            }
        }

        void CalculateMotorSpeeds(out int leftSpeed, out int rightSpeed)
        {
            // Differential drive kinematics
            // Linear velocity = (left + right) / 2
            // Angular velocity = (right - left) / wheelbase

            int linear = cart.remote_linear_velocity;
            int angular = cart.remote_angular_velocity;

            // Clamp inputs
            if (linear > 1000) linear = 1000;
            if (linear < -1000) linear = -1000;
            if (angular > 1000) angular = 1000;
            if (angular < -1000) angular = -1000;

            // Scale to motor speed units
            int linearScaled = (linear * MAX_LINEAR_SPEED) / 1000;
            int angularScaled = (angular * MAX_ANGULAR_SPEED) / 1000;

            // Calculate wheel speeds
            leftSpeed = linearScaled - angularScaled;
            rightSpeed = linearScaled + angularScaled;
        }

        void UpdateMotorFeedback()
        {
            cart.left_motor_speed = _motors[LEFT_MOTOR_IDX].ActualSpeed;
            cart.right_motor_speed = _motors[RIGHT_MOTOR_IDX].ActualSpeed;
            cart.left_motor_status = _motors[LEFT_MOTOR_IDX].StatusWord;
            cart.right_motor_status = _motors[RIGHT_MOTOR_IDX].StatusWord;
        }

        #region Motor Array Operations

        bool ProcessAllMotorBootup()
        {
            bool allOperational = true;
            for (int i = 0; i < MOTOR_COUNT; i++)
            {
                if (!_motors[i].ProcessBootup())
                {
                    allOperational = false;
                }
            }
            return allOperational;
        }

        void ProcessAllMotorFeedback()
        {
            for (int i = 0; i < MOTOR_COUNT; i++)
            {
                _motors[i].ProcessFeedback();
            }
        }

        void SendAllMotorCommands()
        {
            for (int i = 0; i < MOTOR_COUNT; i++)
            {
                _motors[i].SendCommand();
            }
        }

        void EmergencyStopAllMotors()
        {
            for (int i = 0; i < MOTOR_COUNT; i++)
            {
                _motors[i].EmergencyStop();
            }
        }

        void ResetAllMotors()
        {
            for (int i = 0; i < MOTOR_COUNT; i++)
            {
                _motors[i].Reset();
            }
        }

        bool HasAnyMotorFault()
        {
            for (int i = 0; i < MOTOR_COUNT; i++)
            {
                if (_motors[i].HasFault())
                    return true;
            }
            return false;
        }

        #endregion

        public override void Operation(int iteration)
        {
            // Initialize on first run
            if (!_initialized)
            {
                Initialize();
                _initialized = true;
            }

            // Communication loss protection
            if (iteration <= _lastIteration)
            {
                _commLostCounter++;
                if (_commLostCounter > COMMUNICATION_TIMEOUT)
                {
                    _state = (int)VehicleState.Fault;
                    _errorCode = (int)ErrorCode.CommunicationLost;
                    EmergencyStopAllMotors();
                    Console.WriteLine("TestCar: Communication Lost!");
                }
                return;
            }
            _lastIteration = iteration;
            _commLostCounter = 0;

            // Read inputs
            ReadDigitalInputs();

            // Update safety status
            UpdateSafetyStatus();

            // Process control signals
            ProcessControlSignals();

            // Process motor feedback
            ProcessAllMotorFeedback();

            // State machine
            if (_state == (int)VehicleState.MotorBooting)
            {
                if (ProcessAllMotorBootup())
                {
                    _state = (int)VehicleState.Ready;
                    Console.WriteLine("TestCar: All motors operational, ready");
                }
            }
            else if (_state == (int)VehicleState.Ready)
            {
                // Motors enabled but not moving
                _motors[LEFT_MOTOR_IDX].TargetSpeed = 0;
                _motors[RIGHT_MOTOR_IDX].TargetSpeed = 0;
                _motors[LEFT_MOTOR_IDX].TargetControlWord = ControlWord.EnableOp;
                _motors[RIGHT_MOTOR_IDX].TargetControlWord = ControlWord.EnableOp;
                SendAllMotorCommands();
            }
            else if (_state == (int)VehicleState.Running)
            {
                // Calculate and apply motor speeds
                int leftSpeed;
                int rightSpeed;
                CalculateMotorSpeeds(out leftSpeed, out rightSpeed);

                _motors[LEFT_MOTOR_IDX].TargetSpeed = leftSpeed;
                _motors[RIGHT_MOTOR_IDX].TargetSpeed = rightSpeed;
                _motors[LEFT_MOTOR_IDX].TargetControlWord = ControlWord.EnableOp;
                _motors[RIGHT_MOTOR_IDX].TargetControlWord = ControlWord.EnableOp;
                SendAllMotorCommands();
            }
            else if (_state == (int)VehicleState.EmergencyStop || _state == (int)VehicleState.Fault)
            {
                // Keep motors stopped
                EmergencyStopAllMotors();
            }

            // Check for motor faults
            if (HasAnyMotorFault() && _state != (int)VehicleState.Fault)
            {
                _state = (int)VehicleState.Fault;
                _errorCode = (int)ErrorCode.MotorFault;
                Console.WriteLine("TestCar: Motor fault detected!");
            }

            // Update status to host
            cart.vehicle_state = _state;
            cart.error_code = _errorCode;
            UpdateMotorFeedback();
        }
    }

    #endregion
}
