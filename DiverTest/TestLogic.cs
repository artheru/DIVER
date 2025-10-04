using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CartActivator;

namespace DiverTest;

// Interfaces for testing polymorphism
public interface IProcessor
{
    int Process(int input);
    float GetCoefficient();
}

public interface IDataProvider
{
    int[] GetData();
}

// Abstract base with inheritance
public abstract class BaseProcessor : IProcessor
{
    protected int baseValue = 100;
    public abstract int Process(int input);
    public virtual float GetCoefficient() => 1.5f;
    public virtual int Transform(int a, int b) => a + b;
}

public class DerivedProcessor : BaseProcessor, IDataProvider
{
    private int multiplier = 2;
    
    public override int Process(int input) => input * multiplier + baseValue;
    public override float GetCoefficient() => base.GetCoefficient() * 2.0f;
    public override int Transform(int a, int b) => a * b + base.Transform(a, b);
    
    public int[] GetData()
    {
        return new int[] { baseValue, multiplier, baseValue * multiplier };
    }
}

public struct DataPacket
{
    public int id;
    public float value;
    public bool flag;
}

public struct SensorData
{
    public ushort rawValue;
    public float temperature;
    public DerivedProcessor processor;
}

public class TestVehicle : LocalDebugDIVERVehicle
{
    [AsLowerIO] public int read_from_mcu;
    [AsLowerIO] public float sensor_value;
    [AsLowerIO] public DataPacket downlink_packet;
    [AsLowerIO] public SensorData sensor_data;
    [AsLowerIO] public ushort status_code;
    
    [AsUpperIO] public int write_to_mcu;
    [AsUpperIO] public float target_speed;
    [AsUpperIO] public DataPacket uplink_packet;
    [AsUpperIO] public bool[] control_flags;

    public int test_shared_var;
    public float shared_coefficient;
}

[LogicRunOnMCU(scanInterval = 50)]
public class TestLogic : LadderLogic<TestVehicle>
{
    public static (int id, float ff)[] packetBuffer;
    public static bool inited = false;

    // Nested class with complex logic - tests class in class, interfaces, inheritance
    public class DataProcessor
    {
        public static int GlobalCounter = 0;
        public static SensorState GlobalState;

        public float coefficient = 3.14f;
        private SensorState privateState;

        // Complex yield return with lambdas - tests IEnumerator, yield, delegates, closures
        public IEnumerator<SensorState> ProcessStream(int iterations, Func<int, int> transformer)
        {
            if (!inited)
                packetBuffer = new (int id, float ff)[5];
            inited = true;

            (int id, float ff) tempPacket = (iterations, 0.75f);
            var originalTransformer = transformer;

            Action<(int id, float ff)> updateBuffer = (packet) =>
            {
                if (GlobalCounter % 2 == 0)
                    tempPacket = packetBuffer[GlobalCounter % 5] = (packet.id + 1, transformer(packet.id) + packet.ff);
                else
                    transformer = i => originalTransformer(i + 1);
            };

            for (int i = 0; i < iterations * iterations + 5; ++i)
            {
                var temp = GlobalCounter + i + privateState.rawValue;
                GlobalCounter = transformer(transformer(temp) + 2);
                GlobalState.rawValue = (ushort)((int)(GlobalState.rawValue * (GlobalState.proc != null ? GlobalState.proc.GetCoefficient() : 100.0f)) + tempPacket.id);
                GlobalState.rawValue /= 2;

                if (i % 3 != 0)
                {
                    if (GlobalState.proc == null) GlobalState.proc = new DerivedProcessor();
                    yield return GlobalState;
                }
                else
                {
                    GlobalState.rawValue = (ushort)(GlobalState.rawValue % (int)(Math.Abs(packetBuffer[GlobalState.rawValue % 5].ff) * 1000 + 1000));
                    if (i % 2 == 0)
                        yield return privateState = new SensorState()
                        { rawValue = (ushort)(GlobalState.rawValue + GlobalCounter * 2.3), active = (i % 3) * 1.2 < 0.8, proc = new DerivedProcessor() };
                    else
                    {
                        privateState.rawValue += (ushort)tempPacket.ff;
                        updateBuffer((privateState.rawValue, tempPacket.ff));
                    }
                }
            }
        }
    }

    public struct SensorState
    {
        public ushort rawValue;
        public bool active;
        public DerivedProcessor proc;
    }

    private IEnumerator<SensorState> streamIterator;

    // Tests RequireNativeCode attribute
    [RequireNativeCode]
    static byte[] RenderPattern(byte[] buffer)
    {
        int width = 64;
        int height = 32;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double value = Math.Sin(x * 0.1) * Math.Cos(y * 0.1);
                if (value > 0.3)
                    buffer[(y / 8) * width + x] |= (byte)(1 << (y % 8));
            }
        }
        return buffer;
    }

    public override void Operation(int iteration)
    {
        // ============ TEST AsLowerIO/AsUpperIO (Cart read/write) ============
        
        // AsLowerIO: MCU writes, Host reads
        cart.read_from_mcu = (int)(cart.target_speed * 100) + (iteration % 7 + 1) * 100;
        cart.sensor_value = iteration * 1.23f;
        cart.status_code = (ushort)(iteration % 256);
        
        cart.downlink_packet.id = iteration;
        cart.downlink_packet.value = cart.shared_coefficient * iteration;
        cart.downlink_packet.flag = iteration % 2 == 0;

        cart.sensor_data.rawValue = (ushort)(iteration * 3);
        cart.sensor_data.temperature = 25.0f + (iteration % 40) * 0.5f;

        // AsUpperIO: Host writes, MCU reads
        float targetSpeed = cart.target_speed;
        int commandId = cart.write_to_mcu;
        
        if (cart.uplink_packet.flag && cart.uplink_packet.id > 0)
            DataProcessor.GlobalCounter += cart.uplink_packet.id;

        if (cart.control_flags != null && cart.control_flags.Length > 0 && cart.control_flags[0])
            targetSpeed *= 1.5f;

        // ============ TEST Dirty Variables ============
        
        cart.test_shared_var = cart.test_shared_var + iteration % 10;
        cart.shared_coefficient = cart.shared_coefficient * 1.01f + 0.05f;

        // ============ TEST Yield & IEnumerator ============
        
        float zAccum = -3.2f;
        if (streamIterator != null)
        {
            streamIterator.MoveNext();
            if (DataProcessor.GlobalState.active || iteration % 3 == 0)
            {
                DataProcessor.GlobalState.rawValue += 10;
                zAccum = 25.7f;
            }
            zAccum += DataProcessor.GlobalState.rawValue;
        }

        if (DataProcessor.GlobalState.proc == null)
        {
            DataProcessor.GlobalState.proc = new DerivedProcessor();
            var coeffArray = new float[iteration % 5 + 8];
            for (int i = 0; i < coeffArray.Length / 2; ++i)
                coeffArray[i] = (float)Math.Sin(i * 0.5) * 2.1f;

            streamIterator = DataProcessor.GlobalState.proc != null 
                ? new DataProcessor().ProcessStream(iteration, i =>
                {
                    var tmp = (int)(i * coeffArray[Math.Abs(i) % coeffArray.Length] + coeffArray[iteration % coeffArray.Length] * zAccum * i) % 127;
                    return tmp;
                })
                : null;
        }

        for (int i = 0; i < (DataProcessor.GlobalState.rawValue + 1) % 3 + 1; ++i)
            if (streamIterator != null && !streamIterator.MoveNext())
            {
                DataProcessor.GlobalCounter = DataProcessor.GlobalCounter * DataProcessor.GlobalState.rawValue + 7;
                DataProcessor.GlobalState.proc = null;
                break;
            }

        // ============ TEST Tuples ============
        
        (int sum, float avg, bool valid) stats = (iteration * 3, iteration * 1.5f, iteration % 2 == 0);
        var (s, a, v) = stats;

        // Tuple array
        var tupleArray = new (byte code, int value)[3];
        for (int i = 0; i < tupleArray.Length; i++)
            tupleArray[i] = ((byte)(i * 10), iteration + i);

        // ============ TEST Interfaces & Inheritance & Polymorphism ============
        
        IProcessor processor = new DerivedProcessor();
        int processed = processor.Process(iteration);
        float coeff = processor.GetCoefficient();

        IDataProvider dataProvider = (IDataProvider)processor;
        int[] providerData = dataProvider.GetData();

        BaseProcessor baseProc = (BaseProcessor)processor;
        int transformed = baseProc.Transform(iteration, iteration + 1);

        // ============ TEST LINQ ============
        
        var numbers = new int[] { iteration, iteration + 1, iteration + 2, iteration + 3, iteration + 4 };
        
        // Complex LINQ chain
        var result = numbers
            .Where(n => n % 2 == 0)
            .Select(n => n * 2)
            .ToList();
        
        var sumResult = result.Sum();
        var maxResult = result.DefaultIfEmpty(0).Max();
        var minResult = result.DefaultIfEmpty(100).Min();
        
        // LINQ with tuples
        var tupleList = new List<(int x, float y)>
        {
            (iteration, iteration * 0.5f),
            (iteration + 1, (iteration + 1) * 0.5f),
            (iteration + 2, (iteration + 2) * 0.5f)
        };
        
        var filteredTuples = tupleList
            .Where(t => t.x % 2 == 0)
            .Select(t => t.y)
            .ToArray();

        // ============ TEST Complex Logic ============
        
        int Compute(int a, float b, ref int c, out bool success)
        {
            c += a;
            success = b > 0;
            return (int)(a * 2 + b * 2.5f) + c;
        }

        int refParam = iteration;
        bool outParam;
        int computeResult = Compute(iteration, targetSpeed, ref refParam, out outParam);

        // ============ TEST Native Code ============
        
        // RunOnMCU.WriteSnapshot(RenderPattern(new byte[256]));

        // ============ OUTPUT ============
        
        Console.WriteLine(string.Format("iter={0}, counter={1}, state={2}, processed={3}, coeff={4:F2}", 
            iteration, DataProcessor.GlobalCounter, DataProcessor.GlobalState.rawValue, processed, coeff));
        Console.WriteLine(string.Format("LINQ: sum={0}, max={1}, transform={2}, tuple=({3},{4:F1},{5})",
            sumResult, maxResult, transformed, s, a, v));
        Console.WriteLine(string.Format("IO: lowerIO={0}, upperIO={1}, shared={2}, compute={3}",
            cart.read_from_mcu, commandId, cart.test_shared_var, computeResult));
    }
}        