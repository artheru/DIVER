using DiverTest;

namespace CartActivator
{
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
