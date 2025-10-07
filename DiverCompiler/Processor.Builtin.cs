using System.Collections.Generic;

namespace MCURoutineCompiler;

internal partial class Processor
{
    internal static List<(string name, byte[] bc)> BuiltInDirects =
    [
        ("System.Environment.get_CurrentManagedThreadId()", [0x15, 6, 0x01, 0x00, 0x00, 0x00]),
    ];

    internal static List<(string name, ushort ctor_clsid)> BuiltInMethods =
    [
        ("System.Object..ctor()", 0),      //0
        ("System.Math.Abs(Decimal)", 0),      //1
        ("System.Math.Abs(Double)", 0),      //2
        ("System.Math.Abs(Int16)", 0),      //3
        ("System.Math.Abs(Int32)", 0),      //4
        ("System.Math.Abs(Int64)", 0),      //5
        ("System.Math.Abs(SByte)", 0),      //6
        ("System.Math.Abs(Single)", 0),      //7
        ("System.Math.Acos(Double)", 0),      //8
        ("System.Math.Acosh(Double)", 0),      //9
        ("System.Math.Asin(Double)", 0),      //10
        ("System.Math.Asinh(Double)", 0),      //11
        ("System.Math.Atan(Double)", 0),      //12
        ("System.Math.Atan2(Double, Double)", 0),      //13
        ("System.Math.Atanh(Double)", 0),      //14
        ("System.Math.Ceiling(Double)", 0),      //15
        ("System.Math.Clamp(Double, Double, Double)", 0),      //16
        ("System.Math.Clamp(Int16, Int16, Int16)", 0),      //17
        ("System.Math.Clamp(Int32, Int32, Int32)", 0),      //18
        ("System.Math.Clamp(Int64, Int64, Int64)", 0),      //19
        ("System.Math.Clamp(SByte, SByte, SByte)", 0),      //20
        ("System.Math.Clamp(Single, Single, Single)", 0),      //21
        ("System.Math.Cos(Double)", 0),      //22
        ("System.Math.Cosh(Double)", 0),      //23
        ("System.Math.Exp(Double)", 0),      //24
        ("System.Math.Floor(Double)", 0),      //25
        ("System.Math.Log(Double)", 0),      //26
        ("System.Math.Log(Double, Double)", 0),      //27
        ("System.Math.Log10(Double)", 0),      //28
        ("System.Math.Log2(Double)", 0),      //29
        ("System.Math.Max(Double, Double)", 0),      //30
        ("System.Math.Max(Int16, Int16)", 0),      //31
        ("System.Math.Max(Int32, Int32)", 0),      //32
        ("System.Math.Max(Int64, Int64)", 0),      //33
        ("System.Math.Max(SByte, SByte)", 0),      //34
        ("System.Math.Max(Single, Single)", 0),      //35
        ("System.Math.Min(Decimal, Decimal)", 0),      //36
        ("System.Math.Min(Double, Double)", 0),      //37
        ("System.Math.Min(Int16, Int16)", 0),      //38
        ("System.Math.Min(Int32, Int32)", 0),      //39
        ("System.Math.Min(Int64, Int64)", 0),      //40
        ("System.Math.Min(SByte, SByte)", 0),      //41
        ("System.Math.Min(Single, Single)", 0),      //42
        ("System.Math.Pow(Double, Double)", 0),      //43
        ("System.Math.Round(Double)", 0),      //44
        ("System.Math.Sign(Double)", 0),      //45
        ("System.Math.Sign(Int16)", 0),      //46
        ("System.Math.Sign(Int32)", 0),      //47
        ("System.Math.Sign(Int64)", 0),      //48
        ("System.Math.Sign(SByte)", 0),      //49
        ("System.Math.Sign(Single)", 0),      //50
        ("System.Math.Sin(Double)", 0),      //51
        ("System.Math.Sinh(Double)", 0),      //52
        ("System.Math.Sqrt(Double)", 0),      //53
        ("System.Math.Tan(Double)", 0),      //54
        ("System.Math.Tanh(Double)", 0),      //55

        ("System.String.Format(String, Object)", 0),      //56
        ("System.String.Format(String, Object, Object)", 0),      //57
        ("System.String.Format(String, Object, Object, Object)", 0),      //58
        ("System.String.Format(String, Object[])", 0),      //59
        ("System.String.Concat(String, String)", 0),      //60
        ("System.String.Concat(String, String, String)", 0),      //61
        ("System.String.Concat(String, String, String, String)", 0),      //62
        ("System.String.Substring(Int32, Int32)", 0),      //63
        ("System.String.get_Length()", 0),      //64

        ("CartActivator.RunOnMCU.ReadEvent(Int32, Int32)", 0),      //65
        ("CartActivator.RunOnMCU.ReadSnapshot()", 0),      //66
        ("CartActivator.RunOnMCU.ReadStream(Int32)", 0),      //67
        ("CartActivator.RunOnMCU.WriteEvent(Byte[], Int32, Int32)", 0),      //68
        ("CartActivator.RunOnMCU.WriteSnapshot(Byte[])", 0),      //69
        ("CartActivator.RunOnMCU.WriteStream(Byte[], Int32)", 0),      //70
         
        ("CartActivator.RunOnMCU.GetMicrosFromStart()", 0),      //71
        ("CartActivator.RunOnMCU.GetMillisFromStart()", 0),      //72
        ("CartActivator.RunOnMCU.GetSecondsFromStart()", 0),      //73

        ("System.ValueTuple`2..ctor(T1, T2)", 0),      //74
        ("System.ValueTuple`3..ctor(T1, T2, T3)", 0),      //75
        ("System.ValueTuple`4..ctor(T1, T2, T3, T4)", 0),      //76

        ("System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray(Array, RuntimeFieldHandle)", 0),      //77

        ("System.Boolean.ToString()", 0),      //78
        ("System.Byte.ToString()", 0),      //79
        ("System.Char.ToString()", 0),      //80
        ("System.Int16.ToString()", 0),      //81
        ("System.Int32.ToString()", 0),      //82
        ("System.Single.ToString()", 0),      //83
        ("System.UInt16.ToString()", 0),      //84
        ("System.UInt32.ToString()", 0),      //85

        ("System.Action..ctor(Object, IntPtr)", 0xf000),      //86
        ("System.Action.Invoke(T)", 0),      //87
        ("System.Action`1..ctor(Object, IntPtr)", 0xf001),      //88
        ("System.Action`1.Invoke(T)", 0),      //89
        ("System.Action`2..ctor(Object, IntPtr)", 0xf004),      //90
        ("System.Action`2.Invoke(T1, T2)", 0),      //91
        ("System.Action`3..ctor(Object, IntPtr)", 0xf005),      //92
        ("System.Action`3.Invoke(T1, T2, T3)", 0),      //93
        ("System.Action`4..ctor(Object, IntPtr)", 0xf006),      //94
        ("System.Action`4.Invoke(T1, T2, T3, T4)", 0),      //95
        ("System.Action`5..ctor(Object, IntPtr)", 0xf007),      //96
        ("System.Action`5.Invoke(T1, T2, T3, T4, T5)", 0),      //97
        
        ("System.Func`1..ctor(Object, IntPtr)", 0xf002),      //98
        ("System.Func`1.Invoke()", 0),      //99
        ("System.Func`2..ctor(Object, IntPtr)", 0xf003),      //100
        ("System.Func`2.Invoke(T)", 0),      //101
        ("System.Func`3..ctor(Object, IntPtr)", 0xf008),      //102
        ("System.Func`3.Invoke(T1, T2)", 0),      //103
        ("System.Func`4..ctor(Object, IntPtr)", 0xf009),      //104
        ("System.Func`4.Invoke(T1, T2, T3)", 0),      //105
        ("System.Func`5..ctor(Object, IntPtr)", 0xf00a),      //106
        ("System.Func`5.Invoke(T1, T2, T3, T4)", 0),      //107
        ("System.Func`6..ctor(Object, IntPtr)", 0xf00b),      //108
        ("System.Func`6.Invoke(T1, T2, T3, T4, T5)", 0),      //109

        ("System.Console.WriteLine(String)", 0),      //110

        // BitConverter
        ("System.BitConverter.GetBytes(Boolean)", 0),      //111
        ("System.BitConverter.GetBytes(Char)", 0),      //112
        ("System.BitConverter.GetBytes(Int16)", 0),      //113
        ("System.BitConverter.GetBytes(Int32)", 0),      //114
        ("System.BitConverter.GetBytes(Single)", 0),      //115
        ("System.BitConverter.GetBytes(UInt16)", 0),      //116
        ("System.BitConverter.GetBytes(UInt32)", 0),      //117
        ("System.BitConverter.ToBoolean(Byte[], Int32)", 0),      //118
        ("System.BitConverter.ToChar(Byte[], Int32)", 0),      //119
        ("System.BitConverter.ToInt16(Byte[], Int32)", 0),      //120
        ("System.BitConverter.ToInt32(Byte[], Int32)", 0),      //121
        ("System.BitConverter.ToSingle(Byte[], Int32)", 0),      //122
        ("System.BitConverter.ToUInt16(Byte[], Int32)", 0),      //123
        ("System.BitConverter.ToUInt32(Byte[], Int32)", 0),      //124

        // put here.
        ("System.String.Join(String, IEnumerable`1)", 0),      //125
        ("System.String.Join(String, Object[])", 0),      //126
        ("System.Linq.Enumerable.Select(IEnumerable`1, Func`2)", 0),      //127

        // List<T> support
        ("System.Collections.Generic.List`1..ctor()", 0xF00C),      //128
        ("System.Collections.Generic.List`1.Add(T)", 0),      //129
        ("System.Collections.Generic.List`1.get_Count()", 0),  //130
        ("System.Collections.Generic.List`1.get_Item(Int32)", 0), //131
        ("System.Collections.Generic.List`1.set_Item(Int32, T)", 0), //132
        ("System.Collections.Generic.List`1.RemoveAt(Int32)", 0), //133
        ("System.Collections.Generic.List`1.Clear()", 0), //134
        ("System.Collections.Generic.List`1.Contains(T)", 0), //135
        ("System.Collections.Generic.List`1.IndexOf(T)", 0), //136
        ("System.Collections.Generic.List`1.InsertRange(Int32, IEnumerable`1)", 0), //137
        ("System.Linq.Enumerable.ToList(IEnumerable`1)", 0), //138
        ("System.Linq.Enumerable.Where(IEnumerable`1, Func`2)", 0),      //139
        ("System.Linq.Enumerable.Sum(IEnumerable`1)", 0),                //140
        ("System.Linq.Enumerable.Max(IEnumerable`1)", 0),                //141
        ("System.Linq.Enumerable.Min(IEnumerable`1)", 0),                //142
        ("System.Linq.Enumerable.DefaultIfEmpty(IEnumerable`1, TSource)", 0), //143
        ("System.Linq.Enumerable.ToArray(IEnumerable`1)", 0), //144
        // Queue<T>
        ("System.Collections.Generic.Queue`1..ctor()", 0xF00D), //145
        ("System.Collections.Generic.Queue`1.Enqueue(T)", 0), //146
        ("System.Collections.Generic.Queue`1.Dequeue()", 0), //147
        ("System.Collections.Generic.Queue`1.Peek()", 0), //148
        ("System.Collections.Generic.Queue`1.get_Count()", 0), //149
        // Stack<T>
        ("System.Collections.Generic.Stack`1..ctor()", 0xF00E), //150
        ("System.Collections.Generic.Stack`1.Push(T)", 0), //151
        ("System.Collections.Generic.Stack`1.Pop()", 0), //152
        ("System.Collections.Generic.Stack`1.Peek()", 0), //153
        ("System.Collections.Generic.Stack`1.get_Count()", 0), //154
        // Dictionary<TKey,TValue>
        ("System.Collections.Generic.Dictionary`2..ctor()", 0xF00F), //155
        ("System.Collections.Generic.Dictionary`2.Add(TKey, TValue)", 0), //156
        ("System.Collections.Generic.Dictionary`2.get_Item(TKey)", 0), //157
        ("System.Collections.Generic.Dictionary`2.set_Item(TKey, TValue)", 0), //158
        ("System.Collections.Generic.Dictionary`2.Remove(TKey)", 0), //159
        ("System.Collections.Generic.Dictionary`2.ContainsKey(TKey)", 0), //160
        ("System.Collections.Generic.Dictionary`2.get_Count()", 0), //161
        // HashSet<T>
        ("System.Collections.Generic.HashSet`1..ctor()", 0xF010), //162
        ("System.Collections.Generic.HashSet`1.Add(T)", 0), //163
        ("System.Collections.Generic.HashSet`1.Remove(T)", 0), //164
        ("System.Collections.Generic.HashSet`1.Contains(T)", 0), //165
        ("System.Collections.Generic.HashSet`1.get_Count()", 0), //166

        // string interpolation
        ("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler..ctor(Int32, Int32)", 0),      //167
        ("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendLiteral(String)", 0),      //168
        ("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(String)", 0),      //169
        ("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(T)", 0),      //170
        ("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.AppendFormatted(T, String)", 0),      //171
        ("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler.ToStringAndClear()", 0),      //172
    ];

}
