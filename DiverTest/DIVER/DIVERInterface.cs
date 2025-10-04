using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using DiverTest.DIVER.CoralinkerAdaption;
using Newtonsoft.Json;

namespace CartActivator
{
    // todo: currently use this ladderlogic to act interaction with Medulla. 
    // todo: directly integrate into CartActivator.

    // MCU->Medulla is intervally exchanged information.
    public abstract class DIVERVehicle: CartDefinition
    {
        public abstract void SetMCUProgram(string mcuUri, byte[] programAssemblyBytes);
        public abstract void SendUpperData(string mcuUri, byte[] data);
        public virtual void NotifyLog(string mcuUri, string message) { 
        }

        /// ////////////////////////////// INTERFACES ////////////////////////////////////////////

        // whenever a lower io data is uploaded, call this.
        public void NotifyLowerData(string mcuUri, byte[] lowerIOData)
        {
            using var ms = new MemoryStream(lowerIOData);
            using var br = new BinaryReader(ms);
            if (mcu_logics.TryGetValue(mcuUri, out var tup))
            {
                Console.WriteLine($"recv iter {br.ReadInt32()} lowerIO data from {mcuUri}, operation {tup.name}");
                while (ms.Position < lowerIOData.Length)
                {
                    var cid = br.ReadInt16();
                    if (cid < 0 || cid > tup.fields.Length) throw new Exception("invalid Cartfield id!");
                    // if it's upperio skip, otherwise write data.

                    var field = tup.fields[cid];
                    var descriptor = field.descriptor;

                    object value;
                    switch (descriptor.Kind)
                    {
                        case "Primitive":
                            switch (descriptor.PrimitiveTypeId)
                            {
                                case 0:
                                    value = br.ReadBoolean();
                                    break;
                                case 1:
                                    value = br.ReadByte();
                                    break;
                                case 2:
                                    value = br.ReadSByte();
                                    break;
                                case 3:
                                    value = br.ReadChar();
                                    break;
                                case 4:
                                    value = br.ReadInt16();
                                    break;
                                case 5:
                                    value = br.ReadUInt16();
                                    break;
                                case 6:
                                    value = br.ReadInt32();
                                    break;
                                case 7:
                                    value = br.ReadUInt32();
                                    break;
                                case 8:
                                    value = br.ReadSingle();
                                    break;
                                default:
                                    throw new Exception($"Unsupported primitive type ID: {descriptor.PrimitiveTypeId}");
                            }

                            break;

                        case "Array":
                            // Format: length (int32) + elements
                            var length = br.ReadInt32();
                            var elementType = field.fi.FieldType.GetElementType();
                            var array = Array.CreateInstance(elementType, length);

                            for (int i = 0; i < length; i++)
                            {
                                var elementDescriptor = descriptors[descriptor.ElementDescriptorId];
                                var elementValue = DeserializeValue(br, elementDescriptor);
                                array.SetValue(elementValue, i);
                            }

                            value = array;
                            break;

                        case "Struct":
                            // Deserialize struct fields based on descriptor
                            var structValue = Activator.CreateInstance(field.fi.FieldType);
                            foreach (var structField in descriptor.Fields)
                            {
                                var fieldDescriptor = descriptors[structField.DescriptorId];
                                var fieldValue = DeserializeValue(br, fieldDescriptor);
                                var structFieldInfo = field.fi.FieldType.GetField(structField.Name);
                                if (structFieldInfo != null)
                                {
                                    structFieldInfo.SetValue(structValue, fieldValue);
                                }
                            }

                            value = structValue;
                            break;

                        case "String":
                            var strLength = br.ReadInt32();
                            var strBytes = br.ReadBytes(strLength);
                            value = Encoding.UTF8.GetString(strBytes);
                            break;

                        default:
                            throw new Exception($"Unsupported descriptor kind: {descriptor.Kind}");
                    }

                    if (field.isUpper) continue;
                    field.fi.SetValue(this, value);
                }


                // ok to send current data.
                using var sends = new MemoryStream();
                using var bw = new BinaryWriter(sends);
                bw.Write(tup.iterations++);
                for (var cid = 0; cid < tup.fields.Length; cid++)
                {
                    bw.Write((short)cid);
                    var field = tup.fields[cid];
                    var descriptor = field.descriptor;
                    var val = field.fi.GetValue(this);

                    SerializeValue(bw, descriptor, val);
                }

                SendUpperData(mcuUri, sends.ToArray());
            }
            else
                Console.WriteLine($"warning: {mcuUri} received lowerIOData but not registered");
        }

        private void SerializeValue(BinaryWriter bw, DescriptorInfo descriptor, object val)
        {
            switch (descriptor.Kind)
            {
                case "Primitive":
                    bw.Write((byte)descriptor.PrimitiveTypeId);
                    switch (descriptor.PrimitiveTypeId)
                    {
                        case 0:
                            bw.Write((bool)val);
                            break;
                        case 1:
                            bw.Write((byte)val);
                            break;
                        case 2:
                            bw.Write((sbyte)val);
                            break;
                        case 3:
                            bw.Write((char)val);
                            break;
                        case 4:
                            bw.Write((short)val);
                            break;
                        case 5:
                            bw.Write((ushort)val);
                            break;
                        case 6:
                            bw.Write((int)val);
                            break;
                        case 7:
                            bw.Write((uint)val);
                            break;
                        case 8:
                            bw.Write((float)val);
                            break;
                    }

                    break;

                case "Array":
                    if (val == null)
                    {
                        bw.Write(0); // null array length
                        break;
                    }

                    var array = (Array)val;
                    bw.Write(array.Length);
                        for (int i = 0; i < array.Length; i++)
                        {
                            SerializeValue(bw, descriptors[descriptor.ElementDescriptorId], array.GetValue(i));
                        }

                    break;

                case "Struct":
                    var structValue = val;
                    foreach (var structField in descriptor.Fields)
                    {
                        var fieldValue = structValue.GetType().GetField(structField.Name)?.GetValue(structValue);
                        if (fieldValue != null)
                        {
                            SerializeValue(bw, descriptors[structField.DescriptorId], fieldValue);
                        }
                    }

                    break;

                case "String":
                    if (val == null)
                    {
                        bw.Write(0);
                        break;
                    }

                    var str = (string)val;
                    var strBytes = Encoding.UTF8.GetBytes(str);
                    bw.Write(strBytes.Length);
                    bw.Write(strBytes);
                    break;

                default:
                    throw new Exception($"Unsupported descriptor kind for serialization: {descriptor.Kind}");
            }
        }

        private object DeserializeValue(BinaryReader br, DescriptorInfo descriptor)
            {
                switch (descriptor.Kind)
                {
                    case "Primitive":
                        switch (descriptor.PrimitiveTypeId)
                        {
                            case 0:
                                return br.ReadBoolean();
                            case 1:
                                return br.ReadByte();
                            case 2:
                                return br.ReadSByte();
                            case 3:
                                return br.ReadChar();
                            case 4:
                                return br.ReadInt16();
                            case 5:
                                return br.ReadUInt16();
                            case 6:
                                return br.ReadInt32();
                            case 7:
                                return br.ReadUInt32();
                            case 8:
                                return br.ReadSingle();
                            default:
                                throw new Exception($"Unsupported primitive type ID: {descriptor.PrimitiveTypeId}");
                        }

                    case "Array":
                        var arrayLength = br.ReadInt32();
                        // For recursive array deserialization, we need the field type context
                        // This is a simplified version - in practice we'd need more context
                        throw new NotImplementedException("Nested array deserialization not fully implemented");

                    case "Struct":
                        // For struct deserialization, we need the struct type context
                        throw new NotImplementedException("Struct deserialization not fully implemented in helper");

                    case "String":
                        var strLength = br.ReadInt32();
                        var strBytes = br.ReadBytes(strLength);
                        return Encoding.UTF8.GetString(strBytes);

                    default:
                        throw new Exception($"Unsupported descriptor kind: {descriptor.Kind}");
                }
        }

        public virtual void RunDIVER()
        {
            Start(Assembly.GetAssembly(GetType()));
        }

        void Start(Assembly asm)
        {
            var logics = asm.GetTypes()
                .Where(p => p.GetCustomAttribute<LogicRunOnMCUAttribute>() != null).ToArray();
            foreach (var logic in logics)
            {
                var attr = logic.GetCustomAttribute<LogicRunOnMCUAttribute>();
                if (!logic.IsSubclassOfRawGeneric(typeof(LadderLogic<>), out var ll) ||
                    ll.GenericTypeArguments[0] != GetType()) continue;

                Console.WriteLine($"Set logic {logic.Name} to run on MCU-VM @ device {attr.mcuUri}");

                byte[] ReadAllBytes(Stream stream)
                {
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }

                var asmBytes = ReadAllBytes(asm.GetManifestResourceStream($"{logic.Name}.bin"));
                var json = UTF8Encoding.UTF8.GetString(ReadAllBytes(asm.GetManifestResourceStream($"{logic.Name}.bin.json")));
                
                // Console.WriteLine(json);

                var cartInfo = JsonConvert.DeserializeObject<dynamic>(json);
                var fieldsData = cartInfo.fields; 
                var descriptorsData = cartInfo.descriptors;

                descriptors.Clear();
                foreach (var descData in descriptorsData)
                {
                    var descriptor = new DescriptorInfo
                    {
                        Id = (int)descData.Id,
                        Kind = (string)descData.Kind,
                        PrimitiveTypeId = (int)descData.PrimitiveTypeId,
                        ElementDescriptorId = (int)descData.ElementDescriptorId,
                        StructDataOffset = (int)descData.StructDataOffset,
                        ClassId = (int)descData.ClassId,
                        Fields = descData.Fields != null ?
                            ((IEnumerable<dynamic>)descData.Fields).Select(f => new StructFieldInfo
                            {
                                Name = (string)f.Name,
                                Offset = (int)f.Offset,
                                DescriptorId = (int)f.DescriptorId
                            }).ToArray() : new StructFieldInfo[0]
                    };
                    descriptors[descriptor.Id] = descriptor;
                }

                var fields = new PField[fieldsData.Count];
                for (int i = 0; i < fieldsData.Count; i++)
                {
                    var fieldData = fieldsData[i];
                    var descriptor = descriptors[(int)fieldData.DescriptorId];
                    fields[i] = new PField
                    {
                        field = (string)fieldData.FieldName,
                        offset = (int)fieldData.Offset,
                        descriptorId = (int)fieldData.DescriptorId,
                        descriptor = descriptor,
                        typeid = descriptor.Kind == "Primitive" ? descriptor.PrimitiveTypeId : -1
                    };
                }

                if (mcu_logics.ContainsKey(attr.mcuUri))
                    throw new Exception($"Already have logic for {attr.mcuUri}: LadderLogic {logic.Name}");
                mcu_logics[attr.mcuUri] = new LogicInfo() { fields = fields, name = logic.Name };
                foreach (var pField in fields)
                {
                    pField.fi = GetType().GetField(pField.field);
                    if (pField.fi == null)
                        throw new Exception($"field {pField.field} doesn't exist in cart object?");
                    pField.isUpper = pField.fi.IsDefined(typeof(AsUpperIO));
                    // Now we have descriptor information for type checking
                }
                SetMCUProgram(attr.mcuUri, asmBytes);
            }
        }

        /// ///////////////////////////////// DONT CARE /////////////////////////////////////
        class PField
        {
            public string field;
            public FieldInfo fi;
            public bool isUpper;
            public int typeid, offset;
            public int descriptorId;
            public DescriptorInfo descriptor;
        }

        class DescriptorInfo
        {
            public int Id;
            public string Kind; // "Primitive", "Array", "Struct", "String"
            public int PrimitiveTypeId;
            public int ElementDescriptorId;
            public int StructDataOffset;
            public int ClassId;
            public StructFieldInfo[] Fields;
        }

        class StructFieldInfo
        {
            public string Name;
            public int Offset;
            public int DescriptorId;
        }

        class LogicInfo
        {
            public string name;
            public PField[] fields;
            public int iterations;
        }
        private Dictionary<string, LogicInfo> mcu_logics = new();
        private Dictionary<int, DescriptorInfo> descriptors = new();
    }


    public abstract class LocalDebugDIVERVehicle : DIVERVehicle
    {

        public override void SetMCUProgram(string mcu_device_url, byte[] program)
        {
            //todo: replace the following with your implementation.
            MCUTestRunner.DebugSetMCUProgram(program, (bs) =>
            {
                NotifyLowerData("default", bs);
                // debug output all fields of cart object.
                foreach (var field in GetType().GetFields())
                {
                    var value = field.GetValue(this);
                    Console.WriteLine($"{field.Name}: {value}");
                }

            }); // note: apply NotifyLowerData on data received.
        }

        public override void SendUpperData(string mcu_device_url, byte[] data)
        {
            //todo: this is for VM data exchange, contains upperIO/lowerIO modifications.
            MCUTestRunner.DebugSendUpper(data);
        }

        public override void NotifyLog(string mcu_device_url, string message)
        {
            Console.WriteLine($"{mcu_device_url}:{message}");
        }


        unsafe class MCUTestRunner
        {

            public delegate void NotifyLowerDelegate(byte* changedStates, int length);
            [DllImport("MCURuntime.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern void set_lowerio_cb(NotifyLowerDelegate callback);

            private static NotifyLowerDelegate DNotifyStateChanged = StateChanged;

            [DllImport("MCURuntime.dll")]
            static extern void test(byte* bin, int len);
            [DllImport("MCURuntime.dll")]
            static extern void put_upper(byte* bin, int len);

            private static Action<byte[]> lo_notifier;
            private static void StateChanged(byte* changedstates, int length)
            {
                byte[] byteArray = new byte[length];
                Marshal.Copy((IntPtr)changedstates, byteArray, 0, length);
                lo_notifier(byteArray);
            }

            public static void DebugSendUpper(byte[] data)
            {
                fixed (byte* ptr = data)
                {
                    put_upper(ptr, data.Length);
                }
            }
            public static void DebugSetMCUProgram(byte[] program, Action<byte[]> notifier)
            {
                lo_notifier = notifier;
                set_lowerio_cb(DNotifyStateChanged);
                new Thread(() =>
                {
                    var allb = new byte[102400]; //100K runtime
                    Array.Copy(program, allb, program.Length);
                    fixed (byte* ptr = allb)
                    {
                        test(ptr, 102400);
                    }
                })
                { Name = "MCU-debug" }.Start();
            }
        }
    }
}
