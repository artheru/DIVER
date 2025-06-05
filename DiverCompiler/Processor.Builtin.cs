using System.Collections.Generic;

namespace MCURoutineCompiler;

internal partial class Processor
{
    internal static List<string> BuiltIn =
    [
        "System.Object..ctor()",      //0
        "System.Math.Abs(Decimal)",      //1
        "System.Math.Abs(Double)",      //2
        "System.Math.Abs(Int16)",      //3
        "System.Math.Abs(Int32)",      //4
        "System.Math.Abs(Int64)",      //5
        "System.Math.Abs(SByte)",      //6
        "System.Math.Abs(Single)",      //7
        "System.Math.Acos(Double)",      //8
        "System.Math.Acosh(Double)",      //9
        "System.Math.Asin(Double)",      //10
        "System.Math.Asinh(Double)",      //11
        "System.Math.Atan(Double)",      //12
        "System.Math.Atan2(Double, Double)",      //13
        "System.Math.Atanh(Double)",      //14
        "System.Math.Ceiling(Double)",      //15
        "System.Math.Clamp(Double, Double, Double)",      //16
        "System.Math.Clamp(Int16, Int16, Int16)",      //17
        "System.Math.Clamp(Int32, Int32, Int32)",      //18
        "System.Math.Clamp(Int64, Int64, Int64)",      //19
        "System.Math.Clamp(SByte, SByte, SByte)",      //20
        "System.Math.Clamp(Single, Single, Single)",      //21
        "System.Math.Cos(Double)",      //22
        "System.Math.Cosh(Double)",      //23
        "System.Math.Exp(Double)",      //24
        "System.Math.Floor(Double)",      //25
        "System.Math.Log(Double)",      //26
        "System.Math.Log(Double, Double)",      //27
        "System.Math.Log10(Double)",      //28
        "System.Math.Log2(Double)",      //29
        "System.Math.Max(Double, Double)",      //30
        "System.Math.Max(Int16, Int16)",      //31
        "System.Math.Max(Int32, Int32)",      //32
        "System.Math.Max(Int64, Int64)",      //33
        "System.Math.Max(SByte, SByte)",      //34
        "System.Math.Max(Single, Single)",      //35
        "System.Math.Min(Decimal, Decimal)",      //36
        "System.Math.Min(Double, Double)",      //37
        "System.Math.Min(Int16, Int16)",      //38
        "System.Math.Min(Int32, Int32)",      //39
        "System.Math.Min(Int64, Int64)",      //40
        "System.Math.Min(SByte, SByte)",      //41
        "System.Math.Min(Single, Single)",      //42
        "System.Math.Pow(Double, Double)",      //43
        "System.Math.Round(Double)",      //44
        "System.Math.Sign(Double)",      //45
        "System.Math.Sign(Int16)",      //46
        "System.Math.Sign(Int32)",      //47
        "System.Math.Sign(Int64)",      //48
        "System.Math.Sign(SByte)",      //49
        "System.Math.Sign(Single)",      //50
        "System.Math.Sin(Double)",      //51
        "System.Math.Sinh(Double)",      //52
        "System.Math.Sqrt(Double)",      //53
        "System.Math.Tan(Double)",      //54
        "System.Math.Tanh(Double)",      //55

        "System.String.Format(String, Object)",      //56
        "System.String.Format(String, Object, Object)",      //57
        "System.String.Format(String, Object, Object, Object)",      //58
        "System.String.Format(String, Object[])",      //59
        "System.String.Concat(String, String)",      //60
        "System.String.Concat(String, String, String)",      //61
        "System.String.Concat(String, String, String, String)",      //62
        "System.String.Substring(Int32, Int32)",      //63
        "System.String.get_Length()",      //64

        "CartActivator.RunOnMCU.ReadEvent(Int32, Int32)",      //65
        "CartActivator.RunOnMCU.ReadSnapshot()",      //66
        "CartActivator.RunOnMCU.ReadStream(Int32)",      //67
        "CartActivator.RunOnMCU.WriteEvent(Byte[], Int32, Int32)",      //68
        "CartActivator.RunOnMCU.WriteSnapshot(Byte[])",      //69
        "CartActivator.RunOnMCU.WriteStream(Byte[], Int32)",      //70
         
        "CartActivator.RunOnMCU.GetMicrosFromStart()",      //71
        "CartActivator.RunOnMCU.GetMillisFromStart()",      //72
        "CartActivator.RunOnMCU.GetSecondsFromStart()",      //73

        "System.ValueTuple`2..ctor(T1, T2)",      //74
        "System.ValueTuple`3..ctor(T1, T2, T3)",      //75
        "System.ValueTuple`4..ctor(T1, T2, T3, T4)",      //76

        "System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray(Array, RuntimeFieldHandle)",      //77

        "System.Boolean.ToString()",      //78
        "System.Byte.ToString()",      //79
        "System.Char.ToString()",      //80
        "System.Int16.ToString()",      //81
        "System.Int32.ToString()",      //82
        "System.Single.ToString()",      //83
        "System.UInt16.ToString()",      //84
        "System.UInt32.ToString()",      //85

        "System.Action..ctor(Object, IntPtr)",      //86
        "System.Action.Invoke(T)",      //87
        "System.Action`1..ctor(Object, IntPtr)",      //88
        "System.Action`1.Invoke(T)",      //89
        "System.Action`2..ctor(Object, IntPtr)",      //90
        "System.Action`2.Invoke(T1, T2)",      //91
        "System.Action`3..ctor(Object, IntPtr)",      //92
        "System.Action`3.Invoke(T1, T2, T3)",      //93
        "System.Action`4..ctor(Object, IntPtr)",      //94
        "System.Action`4.Invoke(T1, T2, T3, T4)",      //95
        "System.Action`5..ctor(Object, IntPtr)",      //96
        "System.Action`5.Invoke(T1, T2, T3, T4, T5)",      //97
        
        "System.Func`1..ctor(Object, IntPtr)",      //98
        "System.Func`1.Invoke()",      //99
        "System.Func`2..ctor(Object, IntPtr)",      //100
        "System.Func`2.Invoke(T)",      //101
        "System.Func`3..ctor(Object, IntPtr)",      //102
        "System.Func`3.Invoke(T1, T2)",      //103
        "System.Func`4..ctor(Object, IntPtr)",      //104
        "System.Func`4.Invoke(T1, T2, T3)",      //105
        "System.Func`5..ctor(Object, IntPtr)",      //106
        "System.Func`5.Invoke(T1, T2, T3, T4)",      //107
        "System.Func`6..ctor(Object, IntPtr)",      //108
        "System.Func`6.Invoke(T1, T2, T3, T4, T5)",      //109

        "System.Console.WriteLine(String)",      //110

        // BitConverter
        "System.BitConverter.GetBytes(Boolean)",      //111
        "System.BitConverter.GetBytes(Char)",      //112
        "System.BitConverter.GetBytes(Int16)",      //113
        "System.BitConverter.GetBytes(Int32)",      //114
        "System.BitConverter.GetBytes(Single)",      //115
        "System.BitConverter.GetBytes(UInt16)",      //116
        "System.BitConverter.GetBytes(UInt32)",      //117
        "System.BitConverter.ToBoolean(Byte[], Int32)",      //118
        "System.BitConverter.ToChar(Byte[], Int32)",      //119
        "System.BitConverter.ToInt16(Byte[], Int32)",      //120
        "System.BitConverter.ToInt32(Byte[], Int32)",      //121
        "System.BitConverter.ToSingle(Byte[], Int32)",      //122
        "System.BitConverter.ToUInt16(Byte[], Int32)",      //123
        "System.BitConverter.ToUInt32(Byte[], Int32)",      //124

        // put here.
        "System.String.Join(String, IEnumerable`1)",      //125
        "System.String.Join(String, Object[])",      //126
        "System.Linq.Enumerable.Select(IEnumerable`1, Func`2)",      //127
    ];

}