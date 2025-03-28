using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;


namespace DiverTest
{
    public delegate void FunctionReceiveBytes(byte[] bytes);

    public class SerialAdaptor
    {
        private byte[] _receiveBytes;
        private SerialPort _port;
        private int _state = 0;
        private List<byte> _receiveList = new List<byte>();
        private int _length = 0;
        private FunctionReceiveBytes _onReceivedLowerIO;
        private FunctionReceiveBytes _onReceivedLogs;

        public bool isOpen;

        public SerialAdaptor(
            string name,
            int baudRate,
            FunctionReceiveBytes onReceivedLowerIO,
            FunctionReceiveBytes onReceivedLogs
        )
        {
            isOpen = false;

            _port = new SerialPort();
            _port.PortName = name;
            _port.BaudRate = baudRate;
            _port.Parity = Parity.None;
            _port.DataBits = 8;
            _port.StopBits = StopBits.One;
            _port.Handshake = Handshake.None;
            try {
                _port.Open();
            }
            catch (Exception exception) {
                return;
            }
            isOpen = true;

            _onReceivedLowerIO = onReceivedLowerIO;
            _onReceivedLogs = onReceivedLogs;

            new Thread(() =>
            {
                while (true)
                {
                    Thread.Sleep(10);
                    try
                    {
                        int bytesToRead = _port.BytesToRead;
                        if (bytesToRead > 0)
                        {
                            var readBuffer = new byte[bytesToRead];
                            //var start = DateTime.Now;
                            _port.Read(readBuffer, 0, bytesToRead);
                            //var time1 = DateTime.Now - start;
                            ProcessBuffer(readBuffer);
                            //var time2 = DateTime.Now - start;
                            //Hedingben.ToastText($"total time:{time2.TotalMilliseconds},read time:{time1},read count:{bytesToRead}", "timeDebug");
                        }
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine(exception);
                    }
                }
            }).Start();
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                int bytesToRead = _port.BytesToRead;
                if (bytesToRead > 0)
                {
                    var readBuffer = new byte[bytesToRead];
                    //var start = DateTime.Now;
                    _port.Read(readBuffer, 0, bytesToRead);
                    //var time1 = DateTime.Now - start;
                    ProcessBuffer(readBuffer);
                    //var time2 = DateTime.Now - start;
                    //Hedingben.ToastText($"total time:{time2.TotalMilliseconds},read time:{time1},read count:{bytesToRead}", "timeDebug");
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }
        }

        private void ProcessBuffer(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                var newByte = buffer[i];
                switch (_state)
                {
                    case 0:
                        _receiveList.Clear();
                        if (newByte == 0xBB)
                        {
                            _receiveList.Add(newByte);
                            _state = 1;
                        }
                        break;
                    case 1:
                        if (newByte == 0xAA)
                        {
                            _receiveList.Add(newByte);
                            _state = 2;
                        }
                        else
                        {
                            _state = 0;
                        }
                        break;
                    case 2:
                        _receiveList.Add(newByte);
                        if (_receiveList.Count >= 4)
                        {
                            _length = BitConverter.ToUInt16(_receiveList.ToArray(), 2);
                        }

                        if (_receiveList.Count == _length + 7)
                        {
                            _state = 0;
                            if (_receiveList[_receiveList.Count - 1] == 0xEE)
                            {
                                _receiveBytes = _receiveList.ToArray();
                                Console.WriteLine($"SerialAdaptor: Receive from mcu: {BitConverter.ToString(_receiveBytes)}");
                                if (_receiveBytes[5] == 0xA0)
                                {
                                    var upperIODataSize = BitConverter.ToUInt32(_receiveBytes, 6);
                                    var upperIOData = new byte[upperIODataSize];
                                    Array.Copy(_receiveBytes, 14, upperIOData, 0, upperIODataSize);
                                    _onReceivedLowerIO(upperIOData);
                                    var logDataSize = BitConverter.ToUInt32(_receiveBytes, 10);
                                    if (logDataSize > 0)
                                    {
                                        byte[] logData = new byte[logDataSize];
                                        Array.Copy(_receiveBytes, 14 + upperIODataSize, logData, 0, logData.Length);
                                        _onReceivedLogs(logData);
                                    }
                                }
                            }
                            else
                            {
                                _state = 0;
                                Console.WriteLine($"SerialAdaptor: ERROR, mcu packet error, should end with 0xEE , actual {_receiveList[_receiveList.Count - 1]}");
                            }
                        }
                        break;
                    default: break;
                }
            }
        }

        public void SendMessage(byte[] data)
        {
            Console.WriteLine($"SerialAdaptor: Send packet to mcu :{BitConverter.ToString(data)}");
            try
            {
                _port.Write(data, 0, data.Length);
            }
            catch (Exception exception)
            {
                return;
            }
        }

        public byte[] GetMessage()
        {
            return _receiveBytes;
        }
    }
}
