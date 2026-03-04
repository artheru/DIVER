using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DiverTest.DIVER
{
    public class PField
    {
        public string field;
        public FieldInfo fi;
        public bool isUpper;
        public bool isLower;
        public int typeid, offset; // Reserved for future use
    }
    public class LogicInfo
    {
        public string name;
        public PField[] fields;
        public int iterations;
        public string diver;
        public string map;
    }
    public static class DIVERCommonUtils
    {

        public static void NotifyLowerData(byte[] lowerIOData, object target)
        {
            using var ms = new MemoryStream(lowerIOData);
            using var br = new BinaryReader(ms);
            if (mcu_logics.TryGetValue(mcuUri, out var tup))
            {
                var iteration = br.ReadInt32();
                tup.iterations = iteration;
                //Console.WriteLine($"recv iter {iteration} lowerIO data from {mcuUri}, operation {tup.name}", $"DIVER-{tup.name}");

                for (int cid = 0; cid < tup.fields.Length; cid++)
                {
                    if (tup.fields[cid].isUpper) continue;

                    // Skip upper-only fields on host writeback (we still consume the bytes)
                    byte typeid = br.ReadByte();
                    object value;
                    switch (typeid)
                    {
                        case 0:
                            value = br.ReadBoolean();
                            break;
                        case 1:
                            value = br.ReadByte();
                            break;
                        case 2:
                            value = (sbyte)br.ReadByte();
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
                        case 16:
                            {
                                // ReferenceID: only used for null
                                int rid = br.ReadInt32();
                                value = null;
                                break;
                            }
                        case 12:
                            {
                                // String: [StringHeader=12][len:2][bytes]
                                int slen = br.ReadUInt16();
                                var bytes = br.ReadBytes(slen);
                                value = Encoding.UTF8.GetString(bytes);
                                break;
                            }
                        case 11:
                            {
                                // ArrayHeader: [11][elemTid:1][len:4][payload]
                                byte elemTid = br.ReadByte();
                                int arrLen = br.ReadInt32();
                                Array arr;
                                switch (elemTid)
                                {
                                    case 0:
                                        {
                                            var bytes = br.ReadBytes(arrLen);
                                            var a = new bool[arrLen];
                                            for (int i = 0; i < arrLen; i++) a[i] = bytes[i] != 0;
                                            arr = a;
                                            break;
                                        }
                                    case 1:
                                        arr = br.ReadBytes(arrLen);
                                        break;
                                    case 2:
                                        {
                                            var bytes = br.ReadBytes(arrLen);
                                            var a = new sbyte[arrLen];
                                            for (int i = 0; i < arrLen; i++) a[i] = unchecked((sbyte)bytes[i]);
                                            arr = a;
                                            break;
                                        }
                                    case 3:
                                        {
                                            var a = new char[arrLen];
                                            for (int i = 0; i < arrLen; i++) a[i] = br.ReadChar();
                                            arr = a;
                                            break;
                                        }
                                    case 4:
                                        {
                                            var a = new short[arrLen];
                                            for (int i = 0; i < arrLen; i++) a[i] = br.ReadInt16();
                                            arr = a;
                                            break;
                                        }
                                    case 5:
                                        {
                                            var a = new ushort[arrLen];
                                            for (int i = 0; i < arrLen; i++) a[i] = br.ReadUInt16();
                                            arr = a;
                                            break;
                                        }
                                    case 6:
                                        {
                                            var a = new int[arrLen];
                                            for (int i = 0; i < arrLen; i++) a[i] = br.ReadInt32();
                                            arr = a;
                                            break;
                                        }
                                    case 7:
                                        {
                                            var a = new uint[arrLen];
                                            for (int i = 0; i < arrLen; i++) a[i] = br.ReadUInt32();
                                            arr = a;
                                            break;
                                        }
                                    case 8:
                                        {
                                            var a = new float[arrLen];
                                            for (int i = 0; i < arrLen; i++) a[i] = br.ReadSingle();
                                            arr = a;
                                            break;
                                        }
                                    default:
                                        throw new Exception($"Unsupported array element type {elemTid}");
                                }
                                value = arr;
                                break;
                            }
                        default:
                            throw new Exception($"Unsupported type in lowerIO stream: {typeid}");
                    }

                    if (tup.fields[cid].isUpper) continue;
                    var field = tup.fields[cid].fi;
                    value = CoerceValue(value, field.FieldType);
                    field.SetValue(this, value);
                }

                using var sends = new MemoryStream();
                using var bw = new BinaryWriter(sends);

                for (var cid = 0; cid < tup.fields.Length; cid++)
                {
                    // Skip Lower IO fields, since they are defined as LowNode writeback
                    // Host read in cart definition.
                    if (tup.fields[cid].isLower) continue;

                    var val = tup.fields[cid].fi.GetValue(this);
                    if (val is string s)
                    {
                        var bytes = Encoding.UTF8.GetBytes(s);
                        bw.Write((byte)12); // StringHeader
                        bw.Write((ushort)bytes.Length);
                        bw.Write(bytes);
                    }
                    else if (val is Array arr)
                    {
                        // array of primitives
                        byte elemTid;
                        if (arr is bool[] ab)
                        {
                            elemTid = 0;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(ab.Length);
                            for (int i = 0; i < ab.Length; i++) bw.Write((byte)(ab[i] ? 1 : 0));
                            continue;
                        }
                        if (arr is byte[] a1)
                        {
                            elemTid = 1;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a1.Length); bw.Write(a1);
                            continue;
                        }
                        if (arr is sbyte[] a2)
                        {
                            elemTid = 2;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a2.Length);
                            for (int i = 0; i < a2.Length; i++) bw.Write((byte)a2[i]);
                            continue;
                        }
                        if (arr is char[] ac)
                        {
                            elemTid = 3;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(ac.Length);
                            for (int i = 0; i < ac.Length; i++) bw.Write(ac[i]);
                            continue;
                        }
                        if (arr is short[] a4)
                        {
                            elemTid = 4;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a4.Length);
                            for (int i = 0; i < a4.Length; i++) bw.Write(a4[i]);
                            continue;
                        }
                        if (arr is ushort[] a5)
                        {
                            elemTid = 5;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a5.Length);
                            for (int i = 0; i < a5.Length; i++) bw.Write(a5[i]);
                            continue;
                        }
                        if (arr is int[] a6)
                        {
                            elemTid = 6;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a6.Length);
                            for (int i = 0; i < a6.Length; i++) bw.Write(a6[i]);
                            continue;
                        }
                        if (arr is uint[] a7)
                        {
                            elemTid = 7;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a7.Length);
                            for (int i = 0; i < a7.Length; i++) bw.Write(a7[i]);
                            continue;
                        }
                        if (arr is float[] a8)
                        {
                            elemTid = 8;
                            bw.Write((byte)11); bw.Write(elemTid); bw.Write(a8.Length);
                            for (int i = 0; i < a8.Length; i++) bw.Write(a8[i]);
                            continue;
                        }
                        throw new Exception($"Unsupported array element type for field {tup.fields[cid].field}");
                    }
                    else
                    {
                        // primitive
                        switch (Type.GetTypeCode(val.GetType()))
                        {
                            case TypeCode.Boolean:
                                bw.Write((byte)0); bw.Write((bool)val);
                                break;
                            case TypeCode.Byte:
                                bw.Write((byte)1); bw.Write((byte)val);
                                break;
                            case TypeCode.SByte:
                                bw.Write((byte)2); bw.Write((sbyte)val);
                                break;
                            case TypeCode.Char:
                                bw.Write((byte)3); bw.Write((char)val);
                                break;
                            case TypeCode.Int16:
                                bw.Write((byte)4); bw.Write((short)val);
                                break;
                            case TypeCode.UInt16:
                                bw.Write((byte)5); bw.Write((ushort)val);
                                break;
                            case TypeCode.Int32:
                                bw.Write((byte)6); bw.Write((int)val);
                                break;
                            case TypeCode.UInt32:
                                bw.Write((byte)7); bw.Write((uint)val);
                                break;
                            case TypeCode.Single:
                                bw.Write((byte)8); bw.Write((float)val);
                                break;
                            default:
                                throw new Exception($"Unsupported field primitive type for {tup.fields[cid].field}");
                        }
                    }
                }
                SendUpperData(mcuUri, sends.ToArray());
            }
            else
                Console.WriteLine($"warning: {mcuUri} received lowerIOData but not registered", $"DIVER-{tup.name}");

        }
    }
}
