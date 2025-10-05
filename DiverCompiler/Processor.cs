using Fody;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Xml.Linq;
using System.Diagnostics;
using System.IO;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using System.Security.Cryptography;
using static System.Net.Mime.MediaTypeNames;

namespace MCURoutineCompiler;

internal partial class Processor
{
    private const bool debugAnalyze = true;

    public BaseModuleWeaver bmw;


    public class ResultDLL
    {
        public byte[] bytes;
        public (string FieldName, int offset, byte typeid)[] IOs;
        public byte[] diver_src;
        public byte[] diver_map;
    }

    string GetNameNonGeneric(MethodReference methodRef)
    {
        return $"{methodRef.DeclaringType.FullName}.{methodRef.Name}({string.Join(", ", methodRef.Parameters.Select(p => p.ParameterType.Name))})";
    }

    public class StackInitVals
    {
        public int offset, typeID, instantiateClsID;
    }
    public class MethodEntry
    {
        public string name;
        public bool main;
        public int registry;
        public List<StackInitVals> variableList, argumentList;
        public List<(byte[] bytes, int offset)> buffer = [];

        public ResultDLL dll;
        public MethodDefinition md;
        public string ret_name;
        public byte[] retBytes;

        public CCoder ccoder;
    }

    // shared vars throughout each processing.

    internal class shared_info
    {
        internal MethodDefinition EntryMethod;

        internal Dictionary<string, MethodEntry> methods = new();

        internal Dictionary<string, (TypeReference tr, FieldReference fr)> referenced_typefield = []; //name::fieldname

        internal class class_fields
        {
            public TypeReference tr;
            public TypeReference baseType;
            public Dictionary<string, (int offset, TypeReference tr, byte typeid, TypeReference dtr)> field_offset = new(); // field name
            public int size = 0;
            public bool baseInitialized;
        }

        internal Dictionary<string, class_fields> class_ifield_offset = new(); //key:typereference.fullname
        internal class_fields sfield_offset = new();
        internal List<string> instanceable_classes = [];
        internal List<string> cart_io_list = [];
        internal List<Action> linking_actions = [];

        internal Dictionary<string, (TypeReference tr, MethodReference mr)> RT_types = [];

        internal List<MethodDefinition> virtcallMethods = [];
    }

    internal shared_info SI = new();

    private bool isRoot = false;
    public Processor()
    {
        isRoot = true;
    }

    public Processor(Processor p)
    {
        SI = p.SI;
    } 

    static bool IsDerivedFrom(TypeDefinition type, string baseTypeFullName)
    {
        if (type == null)
            return false;

        if (type.FullName == baseTypeFullName)
            return true;

        return IsDerivedFrom(type.BaseType?.Resolve(), baseTypeFullName);
    }

    string GetNameNonGeneric(MethodDefinition method)
    {
        return $"{method.DeclaringType.FullName}.{method.Name}({string.Join(", ", method.Parameters.Select(p => p.ParameterType.Name))})";
    }
    string GetGenericResolvedName(MethodDefinition method, MethodReference methodRef)
    {
        if (method == null)
        {
            bmw?.WriteWarning($"GetGenericResolvedName: method is null! methodRef={methodRef?.FullName}");
            return methodRef?.FullName ?? "Unknown";
        }
        
        if (methodRef == null) return GetNameNonGeneric(method);

        // Get the declaring type name, including generic arguments if any
        string declaringTypeName = method.DeclaringType.FullName;
        if (methodRef.DeclaringType is GenericInstanceType genericDeclaringType)
        {
            declaringTypeName = $"{method.DeclaringType.Namespace}.{method.DeclaringType.Name}";
            declaringTypeName += $"<{string.Join(", ", genericDeclaringType.GenericArguments.Select(arg => arg.Name))}>";
        }

        // Get the method name, including generic arguments if any
        string methodName = method.Name;
        if (methodRef is GenericInstanceMethod genericMethod)
        {
            methodName += $"<{string.Join(", ", genericMethod.GenericArguments.Select(arg => arg.Name))}>";
        }

        // Resolve parameter types
        var parameterNames = new List<string>();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            var param = method.Parameters[i];
            var paramType = param.ParameterType;

            // If the parameter type is generic, get the actual type from methodRef
            if (paramType.IsGenericParameter)
            {
                var genericParam = paramType as GenericParameter;
                
                // Check if it's a type-level generic parameter (e.g., from List<T>)
                if (genericParam.Owner is TypeReference)
                {
                    var dtype = methodRef.DeclaringType as GenericInstanceType;
                    if (dtype != null && genericParam.Position < dtype.GenericArguments.Count)
                    {
                        paramType = dtype.GenericArguments[genericParam.Position];
                    }
                }
                // Check if it's a method-level generic parameter (e.g., from Where<T>)
                else if (genericParam.Owner is MethodReference && methodRef is GenericInstanceMethod gim)
                {
                    if (genericParam.Position < gim.GenericArguments.Count)
                    {
                        paramType = gim.GenericArguments[genericParam.Position];
                    }
                }
            }

            parameterNames.Add(paramType.Name);
        }

        return $"{declaringTypeName}.{methodName}({string.Join(", ", parameterNames)})";
    }

    public struct TypeInfo
    {
        public byte typeid { get; }
        public int size { get; }

        public TypeInfo(byte typeid, int size)
        {
            this.typeid = typeid;
            this.size = size;
        }
    }

    static class tMap
    {
        // value-type:
        public static TypeInfo vBoolean => new(0, 1); //todo: maybe put to 10... it always causes trouble.
        public static TypeInfo vByte => new(1, 1);
        public static TypeInfo vSByte => new(2, 1);
        public static TypeInfo vChar => new(3, 2);
        public static TypeInfo vInt16 => new(4, 2);
        public static TypeInfo vUInt16 => new(5, 2);
        public static TypeInfo vInt32 => new(6, 4);
        public static TypeInfo vUInt32 => new(7, 4);
        public static TypeInfo vSingle => new(8, 4);

        // public static TypeInfo vInt64 => new(9, 8);
        // public static TypeInfo vUInt64 => new(10, 8);

        public static TypeInfo aJump => new(17, 4); // used when the value is actually a struct, jump to a dedicated stack zone
        public static TypeInfo pObject => new(18, 5); // boxed val, typeid+actual val.
        // addr w.r.t mem0  
        public static TypeInfo aAddress => new(15, 4);
        public static TypeInfo aReference => new(16, 2);

        // only on heap, size refers to header size.
        public static TypeInfo hArrayHeader => new(11, 5);      // |typeid:1B|len:4B||..payload:{len*sizeof(typeid)}B| 
        public static TypeInfo hString => new(12, 2);           // |len:2B||..payload:{len}B|
        public static TypeInfo hObjectHeader => new(13, 2);     // |class_id:2B| >heap:... payload
        public static TypeInfo hMethodPointer => new(14, 4);    // |pointer:4B|

    }

    static Dictionary<string, (byte typeid, int size)> tMapDict = new() // size: exclude typeid byte.
    {
        // value types:
        { "Boolean", (0, 1) },
        { "Byte", (1, 1) },
        { "SByte", (2, 1) },
        { "Char", (3, 2) },
        { "Int16", (4, 2) },
        { "UInt16", (5, 2) },
        { "Int32", (6, 4) },
        { "UInt32", (7, 4) },
        { "Single", (8, 4) },

        // transform 64bit valuetype to 32bit.
        { "Int64", (6, 4) },
        { "UInt64", (7, 4) }, 
        { "Double", (8, 4) },

        // only on heap, size refers to header size.
        // { "*ArrayHeader", (11, 5) },      // |typeid:1B|len:4B||..payload:{len*sizeof(typeid)}B| 
        // { "*String", (12, 4) },           // |len:2B||..payload:{len}B|
        // { "*ObjectHeader", (13, 4) },     // |class_id:2B|..payload|  if clsid==-1, it's a dummy class.
        // { "*MethodPointer", (14, 4) },    // |type 1B|method_id:2B|pad 1B|

        // addr w.r.t mem0
        { "&Address",(15, 4)}, // 
        { "&Reference", (16, 4)},

        // upperIO/lowerIO.
    };

    public List<StackInitVals> args = [], vars= [];
    private Dictionary<int, int> ILoffset2BCoffset = new();
    private List<(byte[] bytes, int offset)> myBuffer;

    private class TypeReferenceEqualityComparer : IEqualityComparer<TypeReference>
    {
        public bool Equals(TypeReference x, TypeReference y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return x.FullName == y.FullName;
        }

        public int GetHashCode(TypeReference obj)
        {
            return obj?.FullName?.GetHashCode() ?? 0;
        }
    }

    public bool IsStruct(TypeReference tr)
    {
        var ret = tr.IsValueType;
        // if (ret) bmw.WriteWarning($"{tr} is struct");
        return ret;
    }

    // Memory chunk for value: |typeid 1B|payload {n}B| 

    // use methodRef because there could be generic parameters.


    // if a method is:
    //  1. call only builtin-C functions or CC functions 
    //  2. no singleton load a reference.
    //  3. no address loading
    internal class CCoder
    {
        public static int ccid = 0;

        public int myid = 0;
        public CCoder()
        {
            myid = ccid;
            ccid += 1;
        }
        public string CError = "none";
        
        public static string[] CCTyping =
        [
            "u1", "u1", "i1", "i2", "i2", "u2", "i4", "u4", "r4",
            //9~16
            "/","/","/","/","/","/","/","ptr", "/", "/", "/"
        ];
        public static (string sname, string signature, int args, string cc_ret)[] Cbuiltins = new[]
        {
            ("System.Object..ctor()", "_ctor", 0, "i1"), // we don't pass this. (arbitrary, not used)
            ("System.Math.Abs(Decimal)", "abs", 1, "i1"), // Not supported, placeholder
            ("System.Math.Abs(Double)", "abs", 1, "r4"),
            ("System.Math.Abs(Int16)", "abs", 1, "i2"),
            ("System.Math.Abs(Int32)", "abs", 1, "i4"),
            ("System.Math.Abs(SByte)", "abs", 1, "i1"),
            ("System.Math.Abs(Single)", "abs", 1, "r4"),
            ("System.Math.Acos(Double)", "acos", 1, "r4"),
            ("System.Math.Acosh(Double)", "acosh", 1, "r4"),
            ("System.Math.Asin(Double)", "asin", 1, "r4"),
            ("System.Math.Asinh(Double)", "asinh", 1, "r4"),
            ("System.Math.Atan(Double)", "atan", 1, "r4"),
            ("System.Math.Atan2(Double, Double)", "atan2", 2, "r4"),
            ("System.Math.Atanh(Double)", "atanh", 1, "r4"),
            ("System.Math.Ceiling(Double)", "ceiling", 1, "r4"),
            ("System.Math.Clamp(Double, Double, Double)", "clamp", 3, "r4"),
            ("System.Math.Clamp(Int16, Int16, Int16)", "clamp", 3, "i2"),
            ("System.Math.Clamp(Int32, Int32, Int32)", "clamp", 3, "i4"),
            ("System.Math.Clamp(SByte, SByte, SByte)", "clamp", 3, "i1"),
            ("System.Math.Clamp(Single, Single, Single)", "clamp", 3, "r4"),
            ("System.Math.Cos(Double)", "cos", 1, "r4"),
            ("System.Math.Cosh(Double)", "cosh", 1, "r4"),
            ("System.Math.Exp(Double)", "exp", 1, "r4"),
            ("System.Math.Floor(Double)", "floor", 1, "r4"),
            ("System.Math.Log(Double)", "log", 1, "r4"),
            ("System.Math.Log(Double, Double)", "log", 2, "r4"),
            ("System.Math.Log10(Double)", "log10", 1, "r4"),
            ("System.Math.Log2(Double)", "log2", 1, "r4"),
            ("System.Math.Max(Double, Double)", "max", 2, "r4"),
            ("System.Math.Max(Int16, Int16)", "max", 2, "i2"),
            ("System.Math.Max(Int32, Int32)", "max", 2, "i4"),
            ("System.Math.Max(SByte, SByte)", "max", 2, "i1"),
            ("System.Math.Max(Single, Single)", "max", 2, "r4"),
            ("System.Math.Min(Double, Double)", "min", 2, "r4"),
            ("System.Math.Min(Int16, Int16)", "min", 2, "i2"),
            ("System.Math.Min(Int32, Int32)", "min", 2, "i4"),
            ("System.Math.Min(SByte, SByte)", "min", 2, "i1"),
            ("System.Math.Min(Single, Single)", "min", 2, "r4"),
            ("System.Math.Pow(Double, Double)", "pow", 2, "r4"),
            ("System.Math.Round(Double)", "round", 1, "r4"),
            ("System.Math.Sign(Double)", "sign", 1, "i4"),
            ("System.Math.Sign(Int16)", "sign", 1, "i4"),
            ("System.Math.Sign(Int32)", "sign", 1, "i4"),
            ("System.Math.Sign(SByte)", "sign", 1, "i4"),
            ("System.Math.Sign(Single)", "sign", 1, "i4"),
            ("System.Math.Sin(Double)", "sin", 1, "r4"),
            ("System.Math.Sinh(Double)", "sinh", 1, "r4"),
            ("System.Math.Sqrt(Double)", "sqrt", 1, "r4"),
            ("System.Math.Tan(Double)", "tan", 1, "r4"),
            ("System.Math.Tanh(Double)", "tanh", 1, "r4"),
        };

        public Instruction curI;

        public void Error(string errorMsg="")
        {
            if (!error)
                CError = $"{curI} Not support because:{errorMsg}";
            error = true;
        } 

        public HashSet<string> stackVars = new();

        public bool error = false;
        public bool analyzeStack = true; 
        public List<Func<string>> ccode = new();

        public Dictionary<Instruction, Func<string>> codes = new();
        private Dictionary<Instruction, List<(string var_name, string type)>> stackFinStates = new();

        public void AnalyzeFrom(Instruction prev)
        {
            if (error) 
                return;
            if (prev == null)
            {
                startingStack = new List<(string var_name, string type)>();
            }
            else
            { 
                if (!stackFinStates.ContainsKey(prev))
                {
                    _p?.bmw?.WriteWarning($"AnalyzeFrom: prev instruction not in stackFinStates! prev={prev?.OpCode}, offset={prev?.Offset:X4}");
                    // Fallback to empty stack
                    startingStack = new List<(string var_name, string type)>();
                    return;
                }
                startingStack = new(stackFinStates[prev].ToArray());
                //check stack:
                var st = startingStack.ToArray();
                for (int i = 0; i < st.Length; ++i)
                {
                    var parts = st[i].var_name.Split('_');
                    if (parts.Length > 1)
                    {
                        int snd = int.Parse(parts[1]);
                        if (snd != i)
                            throw new WeavingException($"chk??? {st[i].var_name} is on {startingStack.Count}?, all=[{string.Join(",",st.Select(p=>p.var_name))}]");
                    }
                }
            }
        } 

        private List<(string var_name, string type)> startingStack;

        // type: 1:logbits of array argument. 2:statics region.
        public HashSet<(string name, string argtype)> additional_Args = new();

        public string AddArg(string name, int kind)
        {
            string[] kinds = ["??", "arr_stride_lb", "static"];
            string[] types = ["??", "int", "int"];
            var n = $"{name}_{kinds[kind]}"; 
            additional_Args.Add((n, types[kind]));
            return n;
        }

        public void AddTmp(string decl)
        {
            stackVars.Add(decl);
        }

        public void Append(Func<string[], string> g, int pop = 0, string push_type=null, int push_repeat=0)
        {
            if (error) return; 
             
            string[] stack = []; 

            if (analyzeStack)
            {
                if (debugAnalyze)
                    _p.bmw.WriteWarning($"{curI}: st=[{string.Join(",", startingStack.ToArray().Select(p=>p.var_name))}]");
                var type = push_type;
                if (push_type == "_stack0") 
                    type = startingStack.Last().type;

                int sd = _p.stackDepth[curI];
                if (startingStack.Count != sd) 
                    throw new WeavingException($"WTF? {curI} required s_{sd}, get {startingStack.Count}");

                if (pop > 0)
                {
                    if (pop > startingStack.Count)
                        throw new WeavingException($"??? {curI} poped > stack? {pop}>{startingStack.Count}");
                    if (pop > sd)
                        throw new WeavingException($"??? {curI} poped > sd? {pop}>{sd}");

                    List<string> poppedStackVars = new(); 
                    for (int i = 0; i < pop; ++i)
                    { 
                        var (vn, _) = startingStack.Last();
                        startingStack.RemoveRange(startingStack.Count - 1,1);
                        // _p.bmw.WriteWarning($"popped {vn}");
                        var parts = vn.Split('_');
                        if (parts.Length > 1) 
                        {
                            int snd = int.Parse(parts[1]);
                            if (snd != startingStack.Count)
                                throw new WeavingException($"??? {vn} is on {startingStack.Count}?, ");
                        }
                        poppedStackVars.Add(vn);
                    } 
                      
                    poppedStackVars.Reverse();
                    stack = poppedStackVars.ToArray();  
                }

                var cLabel = $"IL_{curI.Offset:x4}"; 
                var myI = curI;
                if (push_type == null)
                    codes[myI]=()=>$"{cLabel}: {g(stack)}; //{myI}: s_{sd}, pop{pop}, push0\n";
                else
                {  
                    if (type == ".") 
                    {
                        startingStack.Add((g(stack), type));  
                        codes[myI] = ()=>$"{cLabel}: //{myI}: s_{sd}, pop{pop}, push 1ref\n";
                    }
                    else if (type != "/")
                    {
                        var pushStackVar = $"stack_{startingStack.Count}_{type}";
                        stackVars.Add($"{type} {pushStackVar};"); 
                        // _p.bmw.WriteWarning($"stackvars.add {pushStackVar} @ {startingStack.Count}[{string.Join(",",startingStack.ToArray().Select(p=>p.var_name))}], len={stackVars.Count}");
                        startingStack.Add((pushStackVar, type));

                        for (int i = 0; i < push_repeat; ++i) 
                        {
                            pushStackVar = $"stack_{startingStack.Count}_{type}";
                            stackVars.Add($"{type} {pushStackVar};");
                            startingStack.Add((pushStackVar, type));
                        }

                        codes[myI] = ()=>$"{cLabel}: {pushStackVar}=({g(stack)}); //{myI}: s_{sd}, pop{pop}, push{1+push_repeat}\n";
                    }
                    else Error("invalid type for C, only primitives allowed.");
                }

                stackFinStates[myI] = startingStack;
                // _p.bmw.WriteWarning($"after {curI} processed={evaluationStack.Count}");
                //evaluationStack = null;//reset.
            }
            else
            {
                ccode.Add(codes[curI]);
            }
        }

        public void NopTrick()
        {
            if (!analyzeStack)
            {
                var cLabel = $"IL_{curI.Offset:x4}:\r\n";
                ccode.Add(() => cLabel);
            }
        }

        public Processor _p;

        public void CGenMode()
        {
            analyzeStack = false;
        }
    }

    private CCoder cc = new();
     
    public MethodEntry Process(MethodDefinition method, MethodReference methodRef=null, int scanInterval=1000)
    {
        var stringInterpolationHandler = new StringInterpolationHandler(bmw.ModuleDefinition, bmw);
        stringInterpolationHandler.ProcessMethod(method);

        if (isRoot) 
        {
            SI.EntryMethod = method;
        } 
        var fname = GetGenericResolvedName(method, methodRef);
        if (SI.methods.TryGetValue(fname, out var ret))
            return ret; 

        rettype = methodRef?.ReturnType ?? method.ReturnType;
        if (rettype.IsGenericParameter) 
            rettype = ((GenericInstanceType)methodRef.DeclaringType).GenericArguments[
                ((GenericParameter)rettype).Position];
         
        // bmw.WriteWarning($"method name={methodRef?.FullName ?? method.FullName}");
        // bmw.WriteWarning($"retv={rettype.Name} / {rettype.FullName}"); 
        SI.methods[fname] = ret = new MethodEntry()
        {
            name = fname, registry = SI.methods.Count,
            main = isRoot, md = method, ccoder = cc
        };
        cc._p = this;

        // process return value.
        {  
            if (rettype.Name == "Void")
            {
                ret.ret_name = "void";
                ret.retBytes = [0xff, 0, 0];
            }
            // else if (IsStruct(rettype))
            // {
            //     ret.retBytes = [tMap.aJump.typeid, 0, 0]; //method always use newobj for a struct on the heap.
            // }
            else if (tMapDict.TryGetValue(rettype.Name, out var typing))
            {
                ret.retBytes = [typing.typeid, 0, 0]; 
                ret.ret_name = rettype.Name;
            } else if (rettype.Name == "Object")
            {
                ret.retBytes = [tMap.pObject.typeid, 0, 0]; //boxed object.
            }
            else 
            {
                SI.linking_actions.Add(() =>
                {
                    var clsid = SI.instanceable_classes.IndexOf(rettype.FullName);
                    // if (clsid == -1)
                    //     throw new WeavingException(
                    //         $"{methodRef?.FullName ?? method.FullName} retval {rettype.FullName} not instanceable?");
                    ret.retBytes = [tMap.aReference.typeid, (byte)(clsid & 0xff), (byte)(clsid >> 8)];
                });
                ret.ret_name = rettype.Name;
            }
        }
         
        if (method.Body == null) 
            throw new WeavingException($"Bad method:{fname}, has no body?");

        string methodinitInfo = "";

        var psize = 0;

        if (!method.IsStatic)
        {
            //instanced method, add this arg.
            args.Add(new StackInitVals{offset = 0, typeID = tMap.aReference.typeid, instantiateClsID = -1});
            psize += 5;
            methodinitInfo += $"This({args.Last().typeID}@{args.Last().offset}),";
        } 
         
        foreach (var pref in method.Parameters)
        {  
            var paramType = pref.ParameterType;
            if (paramType.IsGenericParameter)
            {
                var genericParam = paramType as GenericParameter;
                var dtype = methodRef.DeclaringType as GenericInstanceType;
                paramType = dtype.GenericArguments[genericParam.Position]; 
            }

            var tname = paramType.Name;
            if (tMapDict.TryGetValue(tname, out var typing))
            {
                args.Add(new StackInitVals(){offset = psize, typeID = typing.typeid, instantiateClsID = -1});  
                psize += 1+typing.size;
            }
            else if (paramType.Resolve().IsEnum)
            {
                args.Add(new StackInitVals() { offset = psize, typeID = tMapDict["Int32"].typeid, instantiateClsID = -1 });
                psize += 1 + tMapDict["Int32"].size;
            }
            else if (IsStruct(paramType))
            {
                var tt = new StackInitVals() { offset = psize, typeID = tMap.aJump.typeid, instantiateClsID = -1 };
                args.Add(tt);
                SI.linking_actions.Add(() =>
                {
                    tt.instantiateClsID = SI.instanceable_classes.IndexOf(paramType.FullName);
                    if (tt.instantiateClsID == -1) throw new WeavingException($"Parameter struct {paramType} not instanceable?");
                });
                psize += 5;
            }
            else if (paramType.IsByReference) 
            { 
                // address.
                args.Add(new StackInitVals() { offset = psize, typeID = tMap.aAddress.typeid, instantiateClsID = -1 });
                psize += 6;
            }
            else
            {
                args.Add(new StackInitVals() { offset = psize, typeID = tMap.aReference.typeid, instantiateClsID = -1 });
                psize += 5;
            }
            methodinitInfo += $"Arg({tname}:{args.Last().typeID}@{args.Last().offset}),";
        }
        ret.argumentList = args;

        var vsize = 0;
        foreach (var vd in method.Body.Variables)
        {
            var paramType = vd.VariableType;
            if (paramType.IsGenericParameter)
            {
                var genericParam = paramType as GenericParameter;
                var dtype = methodRef.DeclaringType as GenericInstanceType; 
                paramType = dtype.GenericArguments[genericParam.Position];
            }

            var tname = paramType.Name; 
            if (tMapDict.TryGetValue(tname, out var typing))
            {
                vars.Add(new StackInitVals() { offset = vsize, typeID = typing.typeid, instantiateClsID = -1 });
                vsize += 1+typing.size;
            }
            else if (IsStruct(paramType))
            {
                var tt = new StackInitVals() { offset = vsize, typeID = tMap.aJump.typeid, instantiateClsID = -1 };
                vars.Add(tt); 
                SI.linking_actions.Add(() =>
                {
                    tt.instantiateClsID = SI.instanceable_classes.IndexOf(paramType.FullName);
                    if (tt.instantiateClsID == -1) throw new WeavingException($"Variable struct {paramType} not instanceable?");
                });
                vsize += 5;
            }
            else 
            {
                vars.Add(new StackInitVals() { offset = vsize, typeID = tMap.aReference.typeid, instantiateClsID = -1 });
                vsize +=5;
            }
            methodinitInfo += $"Var({tname}:{vars.Last().typeID}@{vars.Last().offset}),"; 
        } 

        ret.variableList = vars;

        myBuffer = ret.buffer;

        bmw.WriteWarning($"** Process method {fname} into #{SI.methods.Count}=> {methodinitInfo}.");


        AnalyzeMethod(method);

        // foreach (var instruction in method.Body.Instructions)
        //     bmw.WriteWarning($"{fname}> s_{stackStates[instruction]} for {instruction}");

        var boffset = 0;
        int i = 0;
        cc.CGenMode();

        foreach (var instruction in method.Body.Instructions) 
        {
            ILoffset2BCoffset[instruction.Offset] = i;

            // bmw.WriteWarning(fname+">"+ instruction.ToString()); 

            // Generate bytecode based on IL instructions
            if (ConvertToBytecode(instruction, methodRef) is { } ilbytes)
            {
                ret.buffer.Add((ilbytes, boffset));
                boffset += ilbytes.Length;
            }
            else
            {
                var ins = instruction; 
                SequencePoint sequencePoint;
                do
                {
                    sequencePoint = method.DebugInformation.GetSequencePoint(ins);
                    ins = ins.Previous;
                } while ((sequencePoint == null || sequencePoint.StartLine == 0xfeefee) && ins != null);

                bmw.WriteError(
                    sequencePoint != null
                        ? $"File: {sequencePoint.Document.Url}, Line: {sequencePoint.StartLine}, Not supported instruction: {instruction.OpCode}"
                        : $"Method {fname}, not supported");
                return null;
            }

            ++i;
        }

           
        foreach (var pp in postProcessor) 
            pp(); 
         
        if (isRoot)  
        {
            // make sure this object is there.
            var ct = method.DeclaringType;   
            var fr = ct.BaseType.Resolve().Fields.First(p => p.Name == "cart");
            var zname = $"{ct.FullName}::{fr.Name}";
            if (!SI.referenced_typefield.ContainsKey(zname))
                SI.referenced_typefield[zname] = (ct.BaseType, fr);
            //bmw.WriteWarning("cart object not used, no data communication to upper controller."); this is not correct.

        re_link:
            
            //todo: flawwed virtual call table.
            Dictionary<MethodDefinition, Dictionary<string, int>> virtCallDefs = new();
            var current_referenced = SI.referenced_typefield.ToArray();
            bool virt_expand_occurred = false;
            foreach (var md in SI.virtcallMethods)
            {
                var dict = virtCallDefs[md] = new();
                foreach (var tup in current_referenced)
                {
                    var cname = tup.Key;
                    var td = tup.Value.tr.Resolve();
                    var (b, rmd) = ImplementsMethod(td, md);
                    if (b) 
                    {
                        if (rmd != null)
                        {
                            if (rmd.IsAbstract) // abstract method has no body.
                                continue;
                            if (!SI.methods.TryGetValue(GetGenericResolvedName(rmd, null), out var en)) 
                            {
                                // added processor.
                                bmw.WriteWarning($"{cname}::{rmd} actually implements {md}, process");
                                en = new Processor(this) { bmw = bmw }.Process(rmd);
                                if (en == null) throw new WeavingException("HALT before linking..."); 
                                goto re_link;
                            }
                             
                            dict[tup.Value.tr.FullName] = en.registry;
                            virt_expand_occurred = true;
                        }
                        else   
                        {
                            throw new WeavingException($"{td} implements {md} but no rmd?");
                        } 
                    } 
                }
                 
                // Defer throwing until after we know whether any virt expansion occurred at all
                if (dict.Count == 0 && !virt_expand_occurred) throw new WeavingException($"{md} is not implemented in any using type?");
            }

            var order = new List<string>();

            var allTypes = SI.RT_types.Select(p => p.Value.tr.Resolve());
            foreach (var tup in SI.RT_types)
            {
                var tr = tup.Value.tr;
                if (allTypes.Contains(tr.Resolve().BaseType))
                    throw new WeavingException(
                        $"Polymorphism is not supported, {tr.FullName} is derived from {tr.Resolve().BaseType}, not allowed");
                var td = tup.Value.tr.Resolve(); 
                for (var jj = 0; jj < td.Fields.Count; jj++)
                {
                    var ft = tup.Value.mr.Parameters[jj].ParameterType; // ftype.
                    var dt = tup.Value.mr.DeclaringType;
                    var key = $"{tr.FullName}::{td.Fields[jj].Name}";
                    SI.referenced_typefield[key] = (tr, new FieldReference(td.Fields[jj].Name, ft, dt));
                    order.Add(key);
                }
            } 

            order.AddRange(SI.referenced_typefield.Keys);
            HashSet<string> parsed = new(); 

            bmw.WriteWarning(">>> Allocate class/struct fields");
            foreach (var cls_fld_name in order)
            {
                if (parsed.Contains(cls_fld_name)) continue;
                parsed.Add(cls_fld_name);

                var v = SI.referenced_typefield[cls_fld_name];
                var fd = v.fr.Resolve();

                var ftype = v.fr.FieldType;   
                if (v.fr.FieldType is GenericParameter gp)
                    ftype = ((GenericInstanceType)v.tr).GenericArguments[gp.Position];

                var mysz = 5;
                var typeid = tMap.aReference.typeid;
                if (ftype.IsByReference)
                { 
                    typeid = tMap.aAddress.typeid; 
                }
                else if (ftype.Name == "Object")
                {
                    typeid = tMap.pObject.typeid;
                    mysz = 6; //1B typeid+4B max data-width. 
                } 
                else if (tMapDict.TryGetValue(ftype.Name, out var typing))
                {
                    mysz = typing.size + 1;
                    typeid = typing.typeid; 
                }
                else if (ftype.IsDefinition && ftype.Resolve().IsEnum)
                {
                    // Treat enums as integers
                    mysz = tMapDict["Int32"].size + 1;
                    typeid = tMapDict["Int32"].typeid;
                }
                else if (IsStruct(ftype))
                {
                    // heap object struct is still a heap object.
                }
                // treat CartActivator.CartDefinition's all field as static.

                var iscart = IsDerivedFrom(fd.DeclaringType, "CartActivator.CartDefinition");
                if (iscart)
                {
                    // Allow primitive, array of primitive, or string for Cart IO fields
                    bool isAllowed = false;
                    if (ftype.IsPrimitive)
                    {
                        isAllowed = true;
                    }
                    else if (ftype.IsArray && ftype is ArrayType arrType && arrType.ElementType.IsPrimitive)
                    {
                        isAllowed = true;
                    }
                    else if (ftype.FullName == "System.String")
                    {
                        isAllowed = true;
                    }

                    if (!isAllowed)
                        throw new WeavingException($"Cart IO must be primitive, array of primitive, or string! problem field:{fd.FullName}, actual type: {ftype.FullName}");
                }
                if (fd.IsStatic || iscart)
                {
                    bmw.WriteWarning($"{cls_fld_name}> static allocate {v.fr.Name}({ftype.Name}) @ {SI.sfield_offset.size}");
                    if (!SI.sfield_offset.field_offset.ContainsKey(fd.Name))
                    {
                        SI.sfield_offset.field_offset[fd.Name] = (SI.sfield_offset.size, ftype, typeid, fd.DeclaringType);
                        SI.sfield_offset.size += mysz;
                    }
                }
                else
                {
                    bmw.WriteWarning($"{cls_fld_name}> instanced allocate: {v.fr.Name}({ftype.Name})");

                    var cname = v.tr.FullName;
                    if (cname.StartsWith("CartActivator.LadderLogic"))
                        cname = SI.EntryMethod.DeclaringType.FullName;
                    EnsureInheritanceLayout(v.tr);
                    if (!SI.class_ifield_offset.TryGetValue(cname, out var cfields))
                        SI.class_ifield_offset[cname] = cfields = new();
                    cfields.field_offset[fd.Name] = (cfields.size, ftype, typeid, v.tr);
                    cfields.size += mysz;
                    cfields.tr = v.tr;
                }
            }

            bmw.WriteWarning($"======== LINKING ========");
            bmw.WriteWarning($"runtime library internal classes:[{string.Join(",", SI.RT_types.Select(p=>p.Key))}]");
            foreach (var tup in SI.RT_types)
            {
                var tr = tup.Value.tr;
                var td = tup.Value.tr.Resolve(); 
                var ctor = tup.Value.mr;  

                //todo: relate parameters with fields.... 
                for (var pi = td.Fields.Count; pi < ctor.Parameters.Count; pi++)
                {
                    var pp = ctor.Parameters[pi];
                    // fields first, then un-used params next
                    var ffd = td.Fields.FirstOrDefault(p => p.Name.ToLower() == pp.Name.ToLower());
                    if (ffd != null) continue;
                    var fd = new FieldDefinition(pp.Name + $"_p_{pi}", FieldAttributes.InitOnly, pp.ParameterType);
                    fd.DeclaringType = td;

                    var paramType = pp.ParameterType;
                    if (paramType.IsGenericParameter)
                    {
                        var genericParam = paramType as GenericParameter;
                        var dtype = ctor.DeclaringType as GenericInstanceType;
                        paramType = dtype.GenericArguments[genericParam.Position];
                    }

                    bmw.WriteWarning($"{td.Name}: append ctor param {pi}-th:`{pp.Name}`({paramType})");
                    var mysz = 5;
                    var typeid = tMap.aReference.typeid;
                    if (tMapDict.TryGetValue(paramType.Name, out var typing))
                    { 
                        mysz = typing.size + 1;
                        typeid = typing.typeid;
                    }

                    if (!SI.class_ifield_offset.TryGetValue(tr.FullName, out var cfields))
                        SI.class_ifield_offset[tr.FullName] = cfields = new();
                    cfields.field_offset[fd.Name] = (cfields.size, paramType, typeid, tr);
                    cfields.size += mysz;
                    cfields.tr = tr;
                }
            } 

            SI.instanceable_classes = SI.class_ifield_offset.Keys.ToList(); 

            var bcs = SI.class_ifield_offset.Values.Select(p => p.tr.Resolve()).ToList();
            foreach (var derivedType in bcs)
            {
                EnsureInheritanceLayout(derivedType);
            }


            SI.cart_io_list = SI.sfield_offset.field_offset
                .Where(fo => IsDerivedFrom(fo.Value.dtr.Resolve(), "CartActivator.CartDefinition")).Select(p=>p.Key).ToList();

            bmw.WriteWarning($"abstract method:[{string.Join(",", SI.virtcallMethods.Select(p=>p.Name))}]");
            bmw.WriteWarning($"instanced classes:[{string.Join(", ", SI.instanceable_classes)}]");
            bmw.WriteWarning($"cart IO:[{string.Join(", ", SI.cart_io_list)}]");
            // bmw.WriteWarning(
            //     $"instanced fields:[{string.Join(",", class_ifield_offset.Select(p => p.Key.FullName + ":" + p.Value.sz))}]");
            // bmw.WriteWarning(
            //     $"static fields:[{string.Join(",", sfield_offset.Select(p => p.Key.FullName + ":" + p.Value.sz))}]");


            // ========== PERFORM LINKING....
            foreach (var la in SI.linking_actions)
                la();
            ////////


            var all_methods = SI.methods.Values.ToArray();

            var allCCodes = """
                            // DIVER C Code Generation
                            // This file contains C code generated from .NET methods for MCU compilation
                            // To compile this code, ensure you have an ARM embedded toolchain installed
                            // Visit: https://developer.arm.com/downloads/-/arm-gnu-toolchain-downloads

                            #include <math.h>  // Math functions support

                            #define i1 char
                            #define u1 unsigned char
                            #define i2 short
                            #define u2 unsigned short
                            #define i4 int
                            #define u4 unsigned int
                            #define r4 float
                            #define ptr void*

                            // function begins
                            
                            """;
            // todo: add forward declarations.
            foreach (var m in all_methods)
            {
                if (m.ccoder.CError == "none")
                {
                    var retN = "void*";
                    if (m.ret_name == "Void" || m.md.IsConstructor)
                    {
                        retN = "void";
                    }
                    else if (m.retBytes[0] < 10)
                    {
                        retN = CCoder.CCTyping[m.retBytes[0]];
                    }
                    var fcname = m.ccoder.myid;
                    allCCodes += $"{retN} cfun{fcname}(u1* args);\n";
                }
            }

            allCCodes += "\n";

            foreach (var m in all_methods) 
            {
                if (m.ccoder.CError == "none")
                { 
                    bmw.WriteWarning($"Generate C Code for '{m.name}' @ {m.registry}.");

                    var ccodes = m.ccoder.ccode.Select(p => p()).ToArray();

                    var retN = "void*";
                    if (m.ret_name == "Void" || m.md.IsConstructor) 
                    {
                        retN = "void";
                    }
                    else if (m.retBytes[0] < 10) 
                    {
                        retN = CCoder.CCTyping[m.retBytes[0]]; 
                    }
                      
                    var fcname = m.ccoder.myid;

                    var arg_set = $"int argN=0;\n{
                        string.Join("\n", m.argumentList.Select((p, ai) => $"{CCoder.CCTyping[p.typeID]} arg{ai} = *({CCoder.CCTyping[p.typeID]}*)&args[(argN++)*4];"))}{
                            (m.ccoder.additional_Args.Count == 0 ? "" : $"\n{
                                string.Join("\n", m.ccoder.additional_Args.Select(p => $"{p.argtype} {p.name} = *({p.argtype}*)&args[(argN++)*4];"))}")}";

                    var cCode =
                        $"// {m.name}\n" +
                        $"{retN} cfun{fcname}(u1* args){{\n" +
                        $"//args:\n{arg_set}\n" +
                        $"//stack_vars:\n{string.Join(";\n", m.ccoder.stackVars)};\n " +
                        $"//local_vars:\n{string.Join(";\n", m.variableList.Select((p, ai) => $"{CCoder.CCTyping[p.typeID]} var{ai}"))};\n" +
                        $"//code:\n{string.Join("", ccodes)}}}\n\n";
                    //bmw.WriteWarning($"Full CCode of {m.name}:\n{cCode}");
                    allCCodes += cCode;
                } 
                else
                {
                    bmw.WriteWarning($"CCode error of {m.name}: {m.ccoder.CError}");
                }
            }
             
            Directory.CreateDirectory("native");
            File.WriteAllText($"native\\code.c", allCCodes);


            (bool main, byte[]meta, byte[]code)[] codes = all_methods.Select(mi => (
                mi.main,
                mi.retBytes.Concat(BitConverter.GetBytes((ushort)mi.argumentList.Count))
                    .Concat(mi.argumentList.SelectMany(v => new[]{(byte)v.typeID}.Concat(BitConverter.GetBytes((short)v.instantiateClsID)).ToArray()))
                    .Concat(BitConverter.GetBytes((ushort)mi.variableList.Count))
                    .Concat(mi.variableList.SelectMany(a => new[]{(byte)a.typeID}.Concat(BitConverter.GetBytes((short)a.instantiateClsID)).ToArray()))
                    .Concat(BitConverter.GetBytes(mi.md.Body.MaxStackSize))
                    .ToArray(),
                mi.buffer.SelectMany(bs => bs.bytes).ToArray())).ToArray();
            List<byte> code_table = [(byte)all_methods.Length, (byte)(all_methods.Length >> 8),];
            
            int c_offset = 0;
            int entry_offset = 0;
            List<int> methodMetaOffsets = new();
            List<int> methodCodeOffsets = new();
            for (var j = 0; j < codes.Length; j++)
            {
                var (main, meta, code) = codes[j];
                if (main) entry_offset = j;
                var a = c_offset;
                code_table.AddRange(BitConverter.GetBytes(c_offset));
                c_offset += meta.Length;
                var b = c_offset;
                code_table.AddRange(BitConverter.GetBytes(c_offset));
                c_offset += code.Length;

                bmw.WriteWarning($"method:{all_methods[j].name}, meta/code_offset={a}/{b}");
                methodMetaOffsets.Add(a);
                methodCodeOffsets.Add(b);
            }

            byte[] code_chunk = [..code_table, ..codes.SelectMany(p => p.meta.Concat(p.code).ToArray())];

            byte[][] iclass = SI.instanceable_classes.Select((p,id) =>
                SI.class_ifield_offset[p].field_offset.SelectMany(k =>
                {
                    var aux = -1; 
                    if (IsStruct(k.Value.tr) && k.Value.typeid== tMap.aReference.typeid)
                        aux = SI.instanceable_classes.IndexOf(k.Value.tr.FullName);

                    bmw.WriteWarning($"clsid_{id} fld > {p}.{k.Key}: @{k.Value.offset}, type={k.Value.tr}({k.Value.typeid}), aux={aux}");

                    return new[]
                    {
                        k.Value.typeid,
                        (byte)(k.Value.offset & 0xff), (byte)(k.Value.offset >> 8),
                        (byte)(aux &0xff), (byte)(aux>>8)
                    };
                }).ToArray()).ToArray();

            List<byte> iclass_layout = [];
            int ic_offset = 0;
            for (int j=0; j<SI.instanceable_classes.Count; ++j)
            {
                var p = SI.class_ifield_offset[SI.instanceable_classes[j]];
                iclass_layout.AddRange(BitConverter.GetBytes((ushort)p.size));
                iclass_layout.Add((byte)p.field_offset.Count);
                iclass_layout.AddRange(BitConverter.GetBytes(ic_offset));


                bmw.WriteWarning(
                    $"class_{j} {SI.instanceable_classes[j]} sz {p.size}(n={p.field_offset.Count}), meta offset:{ic_offset}");
                ic_offset += iclass[j].Length;
            }
             
            byte[] program_desc =
            [ 
                ..BitConverter.GetBytes((ushort)SI.cart_io_list.Count),
                ..SI.cart_io_list.SelectMany(p => BitConverter.GetBytes(SI.sfield_offset.field_offset[p].offset)), 
                ..BitConverter.GetBytes((ushort)SI.instanceable_classes.Count),
                ..iclass_layout, 
                ..iclass.SelectMany(p=>p) 
            ];     
              
            byte[] statics_descriptor =
            [  
                ..BitConverter.GetBytes((ushort)SI.sfield_offset.field_offset.Count),
                ..SI.sfield_offset.field_offset.ToArray().OrderBy(p => p.Value).SelectMany(p =>
                {
                    var aux = -1; 
                    if (IsStruct(p.Value.tr) && p.Value.typeid== tMap.aReference.typeid) 
                        aux=SI.instanceable_classes.IndexOf(p.Value.tr.FullName);
                    return new byte[] { p.Value.typeid, (byte)(aux &0xff), (byte)(aux>>8)};
                })
            ]; 



            // virtual method calls:
            byte[][] virt_method_calls = SI.virtcallMethods.Select(p => 
                new byte[]{(byte)virtCallDefs[p].Count, (byte)p.Parameters.Count}
                .Concat(virtCallDefs[p].SelectMany(k =>
                    BitConverter.GetBytes((short)SI.instanceable_classes.IndexOf(k.Key)) 
                        .Concat(BitConverter.GetBytes((short)k.Value)))).ToArray()).ToArray();
            List<byte> virt_table=BitConverter.GetBytes((short)virt_method_calls.Length).ToList();
            bmw.WriteWarning($"virtual methods={SI.virtcallMethods.Count}"); 
            int vc_offset = 0;
            for (int j = 0; j < virt_method_calls.Length; j++)
            {
                virt_table.AddRange(BitConverter.GetBytes((short)vc_offset));
                bmw.WriteWarning($"fuck debugger:{string.Join(" ", virt_method_calls[j].Select(p=>$"{p:X2}"))}");
                vc_offset += virt_method_calls[j].Length;
            }
            byte[] virts = [..virt_table, ..virt_method_calls.SelectMany(p => p)];
              


            var this_clsID = SI.instanceable_classes.IndexOf(method.DeclaringType.FullName);
            // if (this_clsID == -1)
            // {  
            //     throw new WeavingException("Pure function is not allowed, must make interaction with 'cart' object!");
            // }

            bmw.WriteWarning($"entry point, this cls_id={this_clsID}");

            byte[] dll = [
                ..BitConverter.GetBytes(scanInterval), //operation interval. 
                ..BitConverter.GetBytes(entry_offset),
                ..BitConverter.GetBytes(program_desc.Length),
                ..BitConverter.GetBytes(code_chunk.Length),
                ..BitConverter.GetBytes(virts.Length), 
                ..BitConverter.GetBytes(statics_descriptor.Length),
                ..BitConverter.GetBytes(this_clsID), // this.
                ..program_desc, ..code_chunk, ..virts, ..statics_descriptor];

            // Build .diver source and map
            try
            {
                int headerLen = 7 * 4;
                int methodsN = all_methods.Length;
                int methodDetailBase = headerLen + program_desc.Length + 2 + methodsN * 8;

                var diver = new StringBuilder();
                var map = new StringBuilder();
                map.Append('[');
                bool first = true;

                var sourceCache = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

                int globalLine = 1;
                for (int j = 0; j < all_methods.Length; j++)
                {
                    var m = all_methods[j];
                    diver.AppendLine($"=== Method `{m.name}` ===");

                    var ilList = m.md.Body.Instructions;
                    int instCount = Math.Min(ilList.Count, m.buffer.Count);
                    for (int k = 0; k < instCount; k++)
                    {
                        var il = ilList[k];
                        string csLine = "";
                        try
                        {
                            var ins = il;
                            SequencePoint sp;
                            do
                            {
                                sp = m.md.DebugInformation.GetSequencePoint(ins);
                                ins = ins?.Previous;
                            } while ((sp == null || sp.StartLine == 0xfeefee) && ins != null);

                            if (sp != null && !string.IsNullOrEmpty(sp.Document?.Url) && File.Exists(sp.Document.Url))
                            {
                                if (!sourceCache.TryGetValue(sp.Document.Url, out var lines))
                                {
                                    lines = File.ReadAllLines(sp.Document.Url);
                                    sourceCache[sp.Document.Url] = lines;
                                }
                                if (sp.StartLine >= 1 && sp.StartLine <= lines.Length)
                                {
                                    csLine = lines[sp.StartLine - 1].Trim();
                                }
                            }
                        }
                        catch { }

                        int abs = methodDetailBase + methodCodeOffsets[j] + m.buffer[k].offset;
                        string ilText = il.ToString();
                        diver.AppendLine($"{globalLine}:    {ilText}{(csLine.Length > 0 ? "  // " + csLine : "")}");

                        if (!first) map.Append(',');
                        first = false;

                        string escName = m.name.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        map.Append("{\"a\":").Append(abs)
                           .Append(",\"m\":").Append(j)
                           .Append(",\"l\":").Append(globalLine)
                           .Append(",\"n\":\"").Append(escName).Append("\"}");

                        globalLine++;
                    }
                }

                map.Append(']');
                ret.dll = new ResultDLL()
                {
                    bytes = dll,
                    IOs = SI.cart_io_list.Select(p => (p, SI.sfield_offset.field_offset[p].offset, SI.sfield_offset.field_offset[p].typeid)).ToArray(),
                    diver_src = Encoding.UTF8.GetBytes(diver.ToString()),
                    diver_map = Encoding.UTF8.GetBytes(map.ToString())
                };
            }
            catch
            {
                ret.dll = new ResultDLL()
                {
                    bytes = dll,
                    IOs = SI.cart_io_list.Select(p => (p, SI.sfield_offset.field_offset[p].offset, SI.sfield_offset.field_offset[p].typeid)).ToArray(),
                    diver_src = Encoding.UTF8.GetBytes(string.Empty),
                    diver_map = Encoding.UTF8.GetBytes("[]")
                };
            }
             
            // foreach (var m in all_methods)
            // {
            //     bmw.WriteWarning($"======== Generated Bytecode of '{m.name}' @ {m.registry}: ===========");
            //     for (var j = 0; j < m.md.Body.Instructions.Count; j++)
            //     {
            //         var instruction = m.md.Body.Instructions[j];
            //         bmw.WriteWarning($"{instruction} [{string.Join(" ", m.buffer[j].bytes.Select(p => $"{p:X2}"))}]");
            //     }
            // }
        }
         
        
        return ret;
    }

    private List<Action> postProcessor = [];
    private TypeReference rettype;

    private byte[] ConvertToBytecode(Instruction instruction, MethodReference p_methodRef)
    {
        cc.curI = instruction;
        switch (instruction.OpCode.Code)
        {
            case Code.Nop:
                cc.NopTrick();
                return [0x00];

            // Load Arguments:
            // load: we only need offset because typeid is there.
            case Code.Ldarg_0:
                cc.Append(_ => "arg0", 0, CCoder.CCTyping[args[0].typeID]);
                return [0x02, ..BitConverter.GetBytes((short)args[0].offset)]; // this is correct
            case Code.Ldarg_1:
                cc.Append(_ => "arg1", 0, CCoder.CCTyping[args[1].typeID]);
                return [0x02, ..BitConverter.GetBytes((short)args[1].offset)];
            case Code.Ldarg_2:
                cc.Append(_ => "arg2", 0, CCoder.CCTyping[args[2].typeID]);
                return [0x02, ..BitConverter.GetBytes((short)args[2].offset)];
            case Code.Ldarg_3:
                cc.Append(_ => "arg3", 0, CCoder.CCTyping[args[3].typeID]);
                return [0x02, ..BitConverter.GetBytes((short)args[3].offset)];
            case Code.Ldarg:
            case Code.Ldarg_S:
            {
                var id = ((ParameterDefinition)instruction.Operand).Sequence;
                cc.Append(_ => $"arg{id}", 0, CCoder.CCTyping[args[id].typeID]);
                return [0x02, ..BitConverter.GetBytes((short)args[id].offset)];
            }
            // load address:
            case Code.Ldarga: 
            case Code.Ldarga_S:
            {
                cc.Error("not allowed to load address as arg");
                var id = ((ParameterDefinition)instruction.Operand).Sequence;
                return [0x03, ..BitConverter.GetBytes((short)args[id].offset)]; // put argument's address (reference to vm's mem0) to stack. 
            }

            // Store Arguments:
            case Code.Starg:
            case Code.Starg_S:
            {
                // local copy is modified:
                var id = ((ParameterDefinition)instruction.Operand).Sequence;
                cc.Append(me => $"arg{id}={me[0]}", 1);
                return [0x04, ..BitConverter.GetBytes((short)args[id].offset)];
            }

            // Load variables: 
            // same to load, we only need offset, (typeid is at offset).
            case Code.Ldloc_0:
                cc.Append(_ => "var0", 0, CCoder.CCTyping[vars[0].typeID]);
                return [0x06, ..BitConverter.GetBytes((short)vars[0].offset)];
            case Code.Ldloc_1:
                cc.Append(_ => "var1", 0, CCoder.CCTyping[vars[1].typeID]);
                return [0x06, ..BitConverter.GetBytes((short)vars[1].offset)];
            case Code.Ldloc_2:
                cc.Append(_ => "var2", 0, CCoder.CCTyping[vars[2].typeID]);
                return [0x06, ..BitConverter.GetBytes((short)vars[2].offset)];
            case Code.Ldloc_3:
                cc.Append(_ => "var3", 0, CCoder.CCTyping[vars[3].typeID]);
                return [0x06, ..BitConverter.GetBytes((short)vars[3].offset)]; 
            case Code.Ldloc:
            case Code.Ldloc_S:
            { 
                var id = ((VariableDefinition)instruction.Operand).Index;
                cc.Append(_ => $"var{id}", 0, CCoder.CCTyping[vars[id].typeID]);
                return [0x06, ..BitConverter.GetBytes((short)vars[id].offset)]; 
            }

            case Code.Ldloca:
            case Code.Ldloca_S:
            {
                cc.Error("local var address not allowed to load");
                var id = ((VariableDefinition)instruction.Operand).Index;
                return [0x0B, ..BitConverter.GetBytes((short)vars[id].offset)]; // put variable's address (reference to vm's mem0) to stack. 
            }

            case Code.Stloc_0:
                cc.Append(me => $"var0={me[0]}", 1);
                return [0x0A, (byte)vars[0].typeID, ..BitConverter.GetBytes((short)vars[0].offset)];
            case Code.Stloc_1:
                cc.Append(me => $"var1={me[0]}", 1);
                return [0x0A, (byte)vars[1].typeID, ..BitConverter.GetBytes((short)vars[1].offset)];
            case Code.Stloc_2:
                cc.Append(me => $"var2={me[0]}", 1);
                return [0x0A, (byte)vars[2].typeID, ..BitConverter.GetBytes((short)vars[2].offset)];
            case Code.Stloc_3:
                cc.Append(me => $"var3={me[0]}", 1);
                return [0x0A, (byte)vars[3].typeID, ..BitConverter.GetBytes((short)vars[3].offset)];
            case Code.Stloc:
            case Code.Stloc_S:
            {
                var id = ((VariableDefinition)instruction.Operand).Index;
                cc.Append(me => $"var{id}={me[0]}", 1); 
                return [0x0A, (byte)vars[id].typeID, ..BitConverter.GetBytes((short)vars[id].offset)];
            }

            // 0x15 directly load a value: |0x15|typeid|payload|
            case Code.Ldc_I4_M1:
                cc.Append(_=>"-1",0,"i4");
                return [0x15, 6, 0xff, 0xff, 0xff, 0xff];
            case Code.Ldc_I4_0:
                cc.Append(_ => "0", 0, "i4");
                return [0x15, 6, 0x00, 0x00, 0x00, 0x00];
            case Code.Ldc_I4_1:
                cc.Append(_ => "1", 0, "i4");
                return [0x15, 6, 0x01, 0x00, 0x00, 0x00];
            case Code.Ldc_I4_2:
                cc.Append(_ => "2", 0, "i4");
                return [0x15, 6, 0x02, 0x00, 0x00, 0x00];
            case Code.Ldc_I4_3:
                cc.Append(_ => "3", 0, "i4");
                return [0x15, 6, 0x03, 0x00, 0x00, 0x00];
            case Code.Ldc_I4_4:
                cc.Append(_ => "4", 0, "i4");
                return [0x15, 6, 0x04, 0x00, 0x00, 0x00];
            case Code.Ldc_I4_5:
                cc.Append(_ => "5", 0, "i4");
                return [0x15, 6, 0x05, 0x00, 0x00, 0x00];
            case Code.Ldc_I4_6:
                cc.Append(_ => "6", 0, "i4");
                return [0x15, 6, 0x06, 0x00, 0x00, 0x00];
            case Code.Ldc_I4_7:
                cc.Append(_ => "7", 0, "i4");
                return [0x15, 6, 0x07, 0x00, 0x00, 0x00];
            case Code.Ldc_I4_8:
                cc.Append(_ => "8", 0, "i4");
                return [0x15, 6, 0x08, 0x00, 0x00, 0x00];

            case Code.Ldc_I4_S:
                sbyte int8Value = (sbyte)instruction.Operand;
                cc.Append(_ => $"{int8Value}", 0, "i4");
                byte[] int32Bytes = BitConverter.GetBytes((int)int8Value);
                return [0x15, 6, ..int32Bytes];
            case Code.Ldc_I4: 
                int intValue = (int)instruction.Operand;
                cc.Append(_ => $"{intValue}", 0, "i4"); 
                byte[] intBytes = BitConverter.GetBytes(intValue);
                return [0x15, 6, ..intBytes];
            case Code.Ldc_R4:
            {
                float floatValue = (float)instruction.Operand;
                cc.Append(_ => $"{floatValue:0.0}f", 0, "r4");
                byte[] floatBytes = BitConverter.GetBytes(floatValue);
                return [0x15, 8, ..floatBytes];
            }
            case Code.Ldc_R8:
            {
                // just use single. 
                double doubleValue = (double)instruction.Operand;
                cc.Append(_ => $"{doubleValue:0.0}f", 0, "r4");
                byte[] floatBytes = BitConverter.GetBytes((float)doubleValue);
                return [0x15, 8, .. floatBytes];
            }
            case Code.Ldnull:
            {
                cc.Append(_ => $"NULL", 0, "i4");
                return [0x15, tMap.aReference.typeid];
            }

            case Code.Ldstr:
                cc.Error("string operation not allowed");
                // 0x16: load a object reference, also init a new type on heap.
                var strbytes = Encoding.UTF8.GetBytes((string)instruction.Operand);
                return [0x16, tMap.hString.typeid, (byte)(strbytes.Length&0xff), (byte)(strbytes.Length>>8), ..strbytes];

            case Code.Dup:
                var duppler = "";
                cc.Append(me => duppler = me[0], 1, "_stack0", 1);
                return [0x23]; 
            case Code.Pop:
                cc.Append(me => "", 1); //popped
                return [0x24]; 
            // case Code.Jmp:
            //     // todo.
            //     cc.Error("jump?");
            //     return [0x25, ..HandleMethodCall((MethodReference)instruction.Operand)];
            case Code.Ret:
                if (rettype.Name == "Void")
                    cc.Append(_ => "return");
                else 
                    cc.Append(me => $"return {me[0]}", 1);
                return [0x26];
            
            case Code.Br: 
            case Code.Br_S:
                byte[] gen_bcodes(byte id)
                {
                    byte[] ret = [id, 0, 0];  
                    postProcessor.Add(() =>
                    {
                        var operand_offset = ILoffset2BCoffset[((Instruction)instruction.Operand).Offset];

                        // offset relative to starting_pointer.
                        var offset = myBuffer[operand_offset].offset;
                        ret[1] = (byte)(offset & 0xff);
                        ret[2] = (byte)(offset >> 8);
                    });
                    return ret;
                }

                string genGoto()
                {
                    return $"goto IL_{((Instruction)instruction.Operand).Offset:x4}";
                }
                cc.Append(me => $"{genGoto()}");
                return gen_bcodes(0x27);

            case Code.Brfalse:
            case Code.Brfalse_S:
                cc.Append(me => $"if (!({me[0]})) {genGoto()}",1);  
                return gen_bcodes(0x28);

            case Code.Brtrue:
            case Code.Brtrue_S:
                cc.Append(me => $"if ({me[0]}) {genGoto()}",1); 
                return gen_bcodes(0x29);

            case Code.Beq: 
            case Code.Beq_S:
                cc.Append(me => $"if (({me[0]})==({me[1]})) {genGoto()}",2); 
                return gen_bcodes(0x2A);

            case Code.Bge:
            case Code.Bge_S:
                cc.Append(me => $"if (({me[0]})>=({me[1]})) {genGoto()}", 2); 
                return gen_bcodes(0x2B);

            case Code.Bgt:
            case Code.Bgt_S:
                cc.Append(me => $"if (({me[0]})>({me[1]})) {genGoto()}", 2); 
                return gen_bcodes(0x2C);

            case Code.Ble:
            case Code.Ble_S:
                cc.Append(me => $"if (({me[0]})<=({me[1]})) {genGoto()}", 2);
                return gen_bcodes(0x2D);

            case Code.Blt:
            case Code.Blt_S:
                cc.Append(me => $"if (({me[0]})<({me[1]})) {genGoto()}", 2);
                return gen_bcodes(0x2E);

            case Code.Bne_Un:
            case Code.Bne_Un_S:
                cc.Append(me => $"if ((unsigned char)({me[0]})!=(unsigned char)({me[1]})) {genGoto()}", 2);
                return gen_bcodes(0x2F);

            case Code.Bge_Un:
            case Code.Bge_Un_S:
                cc.Append(me => $"if ((unsigned char)({me[0]})>=(unsigned char)({me[1]})) {genGoto()}", 2);
                return gen_bcodes(0x30);

            case Code.Bgt_Un:
            case Code.Bgt_Un_S:
                cc.Append(me => $"if ((unsigned char)({me[0]})>(unsigned char)({me[1]})) {genGoto()}", 2); 
                return gen_bcodes(0x31);

            case Code.Ble_Un:
            case Code.Ble_Un_S:
                cc.Append(me => $"if ((unsigned char)({me[0]})<=(unsigned char)({me[1]})) {genGoto()}", 2); 
                return gen_bcodes(0x32);

            case Code.Blt_Un:
            case Code.Blt_Un_S:
                cc.Append(me => $"if ((unsigned char)({me[0]})<(unsigned char)({me[1]})) {genGoto()}", 2); 
                return gen_bcodes(0x33);

            case Code.Ldind_I1:
                cc.Append(me => $"*(char*)({me[0]})", 1, "i1");
                return [0x41, tMap.vSByte.typeid];
            case Code.Ldind_U1:
                cc.Append(me => $"*(unsigned char*)({me[0]})", 1, "u1");
                return [0x41, tMap.vByte.typeid];
            case Code.Ldind_I2:
                cc.Append(me => $"*(short*)({me[0]})", 1, "i2");
                return [0x41, tMap.vInt16.typeid];
            case Code.Ldind_U2: 
                cc.Append(me => $"*(unsigned short*)({me[0]})", 1, "u2");
                return [0x41, tMap.vUInt16.typeid];
            case Code.Ldind_I4:
            case Code.Ldind_I:
                cc.Append(me => $"*(int*)({me[0]})", 1, "i4");
                return [0x41, tMap.vInt32.typeid];
            case Code.Ldind_U4:
                cc.Append(me => $"*(unsigned int*)({me[0]})", 1, "u4");
                return [0x41, tMap.vUInt32.typeid];
            case Code.Ldind_R4: 
            case Code.Ldind_R8:
                cc.Append(me => $"*(float*)({me[0]})", 1,"r4");
                return [0x41, tMap.vSingle.typeid]; // every double is single.
            case Code.Ldind_Ref:
                cc.Error("object must be flatten");
                return [0x41, tMap.aReference.typeid];

            case Code.Stind_Ref:
                cc.Error("not allowed to operate heap object");
                return [0x4C, tMap.aReference.typeid];
            case Code.Stind_I1:
                cc.Append(me => $"*(char*)({me[0]})=({me[1]})",2);
                return [0x4C, tMap.vSByte.typeid];
            case Code.Stind_I2:
                cc.Append(me => $"*(short*)({me[0]})=({me[1]})", 2);
                return [0x4C, tMap.vInt16.typeid];
            case Code.Stind_I:
            case Code.Stind_I4:
                cc.Append(me => $"*(int*)({me[0]})=({me[1]})", 2);
                return [0x4C, tMap.vInt32.typeid];
            case Code.Stind_R4:  
            case Code.Stind_R8: 
                cc.Append(me => $"*(float*)({me[0]})=({me[1]})", 2); 
                return [0x4C, tMap.vSingle.typeid];
                 
            case Code.Add:
                cc.Append(me => $"({me[0]})+({me[1]})", 2, "_stack0");
                return [0x4D, 0x60];
            case Code.Sub:
                cc.Append(me => $"({me[0]})-({me[1]})", 2, "_stack0");
                return [0x4D, 0x61];
            case Code.Mul:
                cc.Append(me => $"({me[0]})*({me[1]})", 2, "_stack0");
                return [0x4D, 0x62];
            case Code.Div:
                cc.Append(me => $"({me[0]})/({me[1]})", 2, "_stack0");
                return [0x4D, 0x63];
            case Code.Div_Un:
                cc.Append(me => $"({me[0]})/({me[1]})", 2, "_stack0");
                return [0x4D, 0x64];
            case Code.Rem:
                cc.Append(me => $"({me[0]})%({me[1]})", 2, "_stack0");
                return [0x4D, 0x65]; 
            case Code.Rem_Un:
                cc.Append(me => $"({me[0]})%({me[1]})", 2, "_stack0");
                return [0x4D, 0x66];
            case Code.And:
                cc.Append(me => $"({me[0]})&({me[1]})", 2, "_stack0");
                return [0x4D, 0x67];
            case Code.Or:
                cc.Append(me => $"({me[0]})|({me[1]})", 2, "_stack0");
                return [0x4D, 0x68];
            case Code.Xor:
                cc.Append(me => $"({me[0]})^({me[1]})", 2, "_stack0");
                return [0x4D, 0x69];
            case Code.Shl:
                cc.Append(me => $"({me[0]})<<({me[1]})", 2, "_stack0");
                return [0x4D, 0x6A];
            case Code.Shr:
                cc.Append(me => $"({me[0]})>>({me[1]})", 2, "_stack0");
                return [0x4D, 0x6B];
            case Code.Shr_Un:
                cc.Append(me => $"({me[0]})>>({me[1]})", 2, "_stack0");
                return [0x4D, 0x6C];
            
            
            case Code.Neg:
                cc.Append(me => $"!({me[0]})", 1, "_stack0");
                return [0x6D];
            case Code.Not:
                cc.Append(me => $"!({me[0]})", 1, "_stack0");
                return [0x6E];

            case Code.Conv_I1:
                cc.Append(me => me[0], 1, "i1");
                return [0x70];
            case Code.Conv_U1:
                cc.Append(me => me[0], 1, "u1");
                return [0x71];
            case Code.Conv_I2:
                cc.Append(me => me[0], 1, "i2");
                return [0x72];
            case Code.Conv_U2:
                cc.Append(me => me[0], 1, "u2");
                return [0x73]; 
            case Code.Conv_I:
            case Code.Conv_I4:
                cc.Append(me => me[0], 1, "i4");
                return [0x74];
            case Code.Conv_U: 
            case Code.Conv_U4:
                cc.Append(me => me[0], 1, "u4");
                return [0x75];
            case Code.Conv_R4:
            case Code.Conv_R8:
                cc.Append(me => me[0], 1, "r4");
                return [0x76];
            case Code.Conv_R_Un:
                cc.Append(me => me[0], 1, "r4"); //??
                return [0x77];

            case Code.Initobj:
                cc.Error("not allowed to create heap object");
                // Use dedicated opcode for initobj to avoid colliding with castclass
                return [0x78];
            case Code.Newobj:
            { 
                cc.Error("not allowed to create heap object");
                // if 0x7A, first according to class_id, get layout, then generate a heap object
                var mref = (MethodReference)instruction.Operand;
                var bc = mref.DeclaringType.Resolve();
                if (IsDerivedFrom(bc, "CartActivator.CartDefinition"))
                    throw new WeavingException("Must not new CartActivator.CartDefinition object!");

                byte[] ret = [0x7A, 0, 0, ..HandleMethodCall(mref)]; //automatically handles builtin class creation.
                // detect builtin ctor and set class id if provided
                var ctorName = GetNameNonGeneric(mref.Resolve());
                var bmIndex = BuiltInMethods.FindIndex(p => p.name == ctorName);
                var is_builtin = false;
                if (bmIndex >= 0)
                {
                    var clsid = BuiltInMethods[bmIndex].ctor_clsid;
                    if (clsid != 0)
                    {
                        is_builtin = true;
                        ret[1] = (byte)(clsid & 0xff);
                        ret[2] = (byte)(clsid >> 8);
                    }
                }
                SI.linking_actions.Add(() =>
                {
                    if (is_builtin) return;
                    var id = SI.instanceable_classes.IndexOf(mref.DeclaringType.FullName);
                    if (id == -1)
                    {
                        // just reference???
                        ret[1] = ret[2] = 0xff;
                    }
                    else
                    {
                        ret[1] = (byte)(id & 0xff);
                        ret[2] = (byte)(id >> 8);
                    }
                });
                return ret;
            }
                 
            case Code.Unbox:
            case Code.Box:
                cc.Error("box/unbox not allowed");
                return [0];

            // case Code.Throw:
            //     cc.Error(instruction);
            //     return null;

            // instanced: 
            case Code.Ldfld:
                byte[] C_FLDop(byte[] code, bool store)
                {
                    var type = code[1];
                    int is_static = type & 1;
                    int is_cart_io = type & 2;
                    var tname = ((FieldReference)instruction.Operand).FieldType.Name;
                    if (!tMapDict.TryGetValue(tname, out var typing))
                        cc.Error($"type {tname} not supported");
                    var ft = CCoder.CCTyping[typing.typeid];
                    if (is_cart_io>0)
                        cc.Error("C operate cart.io not supported");
                    else if (is_static> 0)
                    {
                        cc.AddArg("strgn", 2);
                        if (!store)
                        {
                            // load
                            if (ft == "r4")
                            {
                                cc.AddTmp($"float tmpF;");
                                cc.Append(me => $"*(int*)&tmpF=*(int*)&(strgn[{BitConverter.ToInt32(code, 2)}+1]),tmpF", 0, ft);
                            }
                            else
                            {
                                cc.Append(me => $"*({ft}*)(&strgn[{BitConverter.ToInt32(code, 2)}+1])", 0, ft);
                            }
                        }
                        else
                        {
                            // store
                            if (ft == "r4")
                            {
                                cc.Append(me => $"*(int*)&(strgn[{BitConverter.ToInt32(code, 2)}+1])=*(int*)&{me[0]}", 1);
                            }
                            else
                            {
                                cc.Append(me => $"*({ft}*)&(strgn[{BitConverter.ToInt32(code, 2)}+1])={me[0]}", 1);
                            }
                        }
                    }
                    else
                    {
                        if (!store)
                        {
                            // load
                            if (ft == "r4")
                            {
                                cc.AddTmp($"float tmpF;");
                                cc.Append(me => $"*(int*)&tmpF=*(int*)&({me[0]}[{BitConverter.ToInt32(code, 2)}+1]),tmpF", 1, ft);
                            }
                            else
                            {
                                cc.Append(me => $"*({ft}*)(&{me[0]}[{BitConverter.ToInt32(code, 2)}+1])", 1, ft);
                            }
                        }
                        else
                        {
                            // store
                            if (ft == "r4")
                            {
                                cc.Append(me => $"*(int*)&({me[0]}[{BitConverter.ToInt32(code, 2)}+1])=*(int*)&{me[1]}", 2);
                            }
                            else
                            {
                                cc.Append(me => $"*({ft}*)&({me[0]}[{BitConverter.ToInt32(code, 2)}+1])={me[1]}", 2);
                            }
                        }
                    }

                    return code;
                }

                byte[] g_fcode(byte code, bool instanced = true)
                {
                    var fr = (FieldReference)instruction.Operand;
                     
                    // Resolve the generic type if necessary 
                    TypeReference tr = fr.DeclaringType;
                    if (p_methodRef!=null && p_methodRef.DeclaringType.Resolve() == fr.DeclaringType.Resolve())
                        tr = p_methodRef.DeclaringType;
                    if (tr is GenericInstanceType genericInstanceType)
                        tr = ResolveGenericType(genericInstanceType);
                     
                    var td = tr.Resolve() ?? throw new WeavingException($"type {tr.Name} cannot be resolved");
                    var fd = fr.Resolve();
                     
                    // CartActivator.CartDefinition. any field , is treated as static field.

                    var cname = tr.FullName;
                    // only allow CartActivator.LadderLogic to be polymophism.
                    if (cname.StartsWith("CartActivator.LadderLogic"))
                        cname = SI.EntryMethod.DeclaringType.FullName;

                    SI.referenced_typefield[$"{cname}::{fr.Name}"] = (tr, fr);

                    var iscart = IsDerivedFrom(fd.DeclaringType, "CartActivator.CartDefinition");  
                    byte type = (byte)((instanced ? 0 : 1) | (iscart ? 2 : 0));

                    byte[] ret = [code, type, 0, 0, 0, 0]; // code|type|offset|(classid,io_id)
                    SI.linking_actions.Add(() =>
                    {
                        // instanced offset relative to pointer address; static offset relative to class_static_field_start.
                        var offset = (instanced && !iscart ? SI.class_ifield_offset[cname] : SI.sfield_offset).field_offset[fr.Resolve().Name].offset;
                        ret[2] = (byte)(offset & 0xff);
                        ret[3] = (byte)(offset >> 8);
                        // bmw.WriteWarning($"FLD encode: op={(int)code:X2} decl={cname} field={fr.Resolve().FullName} inst={instanced} iscart={iscart} off={offset}");
                    });
                    SI.linking_actions.Add(() =>
                    {
                        if (instanced && !iscart) 
                        {
                            var id = SI.instanceable_classes.IndexOf(cname);
                            if (id == -1)
                                throw new WeavingException(
                                    $"WTF? an instanced class '{td.FullName}' doesn't appear in instancable_classes?");

                            ret[4] = (byte)(id & 0xff);
                            ret[5] = (byte)(id >> 8);
                            // bmw.WriteWarning($"FLD encode: classid {id} for {cname}");
                        }

                        if (iscart)
                        {
                            var id = SI.cart_io_list.IndexOf(fd.Name);
                            if (id == -1)
                                throw new WeavingException(
                                    $"WTF? a cart io '{fd.FullName}' doesn't in cart_io_ls?");

                            ret[4] = (byte)(id & 0xff);
                            ret[5] = (byte)(id >> 8);
                        }
                    });
                    return ret; 
                }

                return C_FLDop(g_fcode(0x7B), false); 
            case Code.Ldflda:
                cc.Error("load address not supported(currently not allowed to use struct)");
                return g_fcode(0x7C); 
            case Code.Stfld:
            {
                return C_FLDop(g_fcode(0x7D), true);
            }

            // statics:
            case Code.Ldsfld:
                return C_FLDop(g_fcode(0x7B, false), false); 
            case Code.Ldsflda: 
                cc.Error("address not supported");
                return g_fcode(0x7C, false);
            case Code.Stsfld:
                return C_FLDop(g_fcode(0x7D, false), true);

            case Code.Newarr:
            {
                cc.Error("heap operation not allowed");
                var fr = (TypeReference)instruction.Operand;
                if (fr.Name == "Object")
                    return [0x16, tMap.hArrayHeader.typeid, tMap.pObject.typeid];
                if (tMapDict.TryGetValue(fr.Name, out var typing)) 
                    return [0x16, tMap.hArrayHeader.typeid, typing.typeid];

                if (IsStruct(fr))
                {
                    byte[] ret = [0x16, tMap.hArrayHeader.typeid, tMap.aReference.typeid, 0, 0];
                    var cname = fr.FullName;
                    SI.linking_actions.Add(() =>
                    {
                        var id = SI.instanceable_classes.IndexOf(cname);
                        if (id == -1) 
                            throw new WeavingException(
                                $"WTF? an instanced class '{fr.FullName}' doesn't appear in instancable_classes?");
                        ret[3] = (byte)(id & 0xff);
                        ret[4] = (byte)(id >> 8);
                    }); 
                    return ret; 
                }

                return [0x16, tMap.hArrayHeader.typeid, tMap.aReference.typeid, 0xff, 0xff];
            }
            case Code.Ldlen:
                cc.Append(me => $"*(((i4*){me[0]})-1)", 1, "i4"); // arrayheader store len.
                return [0x8E];
            case Code.Ldelema:
                cc.Append(me => $"( ((i1*){me[0]})+({me[1]}<<{cc.AddArg(me[0], 1)}) )", 2, "ptr");
                return [0x8F];
            case Code.Ldelem_I1:
                cc.Append(me => $"((i1*)({me[0]}))[{me[1]}]", 2, "i1");
                return [0x90, tMap.vSByte.typeid];
            case Code.Ldelem_U1:
                cc.Append(me => $"((u1*)({me[0]}))[{me[1]}]", 2, "u1");
                return [0x90, tMap.vByte.typeid];
            case Code.Ldelem_I2:
                cc.Append(me => $"((i2*)({me[0]}))[{me[1]}]", 2, "i2");
                return [0x90, tMap.vInt16.typeid];
            case Code.Ldelem_U2:
                cc.Append(me => $"((u2*)({me[0]}))[{me[1]}]", 2, "u2");
                return [0x90, tMap.vUInt16.typeid];
            case Code.Ldelem_I4:
            case Code.Ldelem_I:
                cc.Append(me => $"((i4*)({me[0]}))[{me[1]}]", 2, "i4");
                return [0x90, tMap.vInt32.typeid];
            case Code.Ldelem_U4:
                cc.Append(me => $"((u4*)({me[0]}))[{me[1]}]", 2, "u4");
                return [0x90, tMap.vUInt32.typeid];
            case Code.Ldelem_R4: 
            case Code.Ldelem_R8:
                cc.AddTmp("float tmpF");
                cc.Append(me => $"*(i4*)&tmpF=*(i4*)&( ((u4*)({me[0]}))[{me[1]}] )", 2, "r4"); // use indirect load of float.
                return [0x90, tMap.vSingle.typeid];
            case Code.Ldelem_Ref:
                cc.Error("reference not allowed");
                return [0x90, tMap.aReference.typeid];
                 
            case Code.Stelem_Any:
            {
                cc.Error("any?"); 
                // used to store struct.
                var tr = (TypeReference)instruction.Operand;
                var cname = tr.FullName; 
                byte[] ret = [0x91, tMap.aJump.typeid];
                //     , 0, 0];
                // SI.linking_actions.Add(() =>
                // {
                //     var id = SI.instanceable_classes.IndexOf(cname);
                //     if (id == -1)
                //         throw new WeavingException(
                //             $"WTF? struct '{cname}' doesn't appear in instancable_classes?");
                //     ret[2] = (byte)(id & 0xff);
                //     ret[3] = (byte)(id >> 8);
                // });
                return ret;
            }
            case Code.Stelem_I1:
                cc.Append(me => $"((i1*)({me[0]}))[{me[1]}]=(i1)({me[2]})", 3);
                return [0x91, tMap.vSByte.typeid];
            case Code.Stelem_I2:
                cc.Append(me => $"((i2*)({me[0]}))[{me[1]}]=(i2)({me[2]})", 3);
                return [0x91, tMap.vInt16.typeid];
            case Code.Stelem_I:
            case Code.Stelem_I4:
                cc.Append(me => $"((i4*)({me[0]}))[{me[1]}]=(i4)({me[2]})", 3);
                return [0x91, tMap.vInt32.typeid];
            case Code.Stelem_R4:
            case Code.Stelem_R8:
                cc.Append(me => $"((i4*)({me[0]}))[{me[1]}]=*(i4*)&({me[2]})", 3);
                return [0x91, tMap.vSingle.typeid]; 
            case Code.Stelem_Ref:
                cc.Error("reference not allowed");
                return [0x91, tMap.aReference.typeid];
               
                  

            case Code.Call:
                return HandleMethodCall((MethodReference)instruction.Operand); //A6:custom call, A7:builtin call.
            case Code.Callvirt:
            {
                cc.Error("no reference to virtual object");
                // limited polymophism support.
                var methodRef = (MethodReference)instruction.Operand;
                var mdr = methodRef.Resolve();

                if (mdr.IsAbstract)
                {
                    var id = SI.virtcallMethods.IndexOf(mdr);
                    if (id == -1)
                    {
                        id = SI.virtcallMethods.Count;
                        SI.virtcallMethods.Add(mdr);
                        bmw.WriteWarning($"Abstract method call {methodRef.Name}, add to virt call list => id={id}");
                    }

                    return [0xA0, (byte)(id & 0xff), (byte)(id >> 8)];
                } 
                else
                {
                    return [0xA2, .. HandleMethodCall((MethodReference)instruction.Operand)];
                }
            }
            case Code.Ldftn: 
                cc.Error();
                // if use 0xA1 to load address type, 3rd param decide what actual addr-val to be loaded. A1 should convert index into address to mem0
                return [0xA1, tMap.aAddress.typeid, ..HandleMethodCall((MethodReference)instruction.Operand)];

            case Code.Calli:
            {
                // should we do arguments?
                return [0xA8];
            }

            case Code.Ceq:
                cc.Append(me => $"({me[0]})==({me[1]})", 2, "i4");
                return [0xE2];
            case Code.Cgt:
                cc.Append(me => $"({me[0]})>({me[1]})", 2, "i4");
                return [0xE3]; 
            case Code.Cgt_Un:
                cc.Append(me => $"({me[0]})>({me[1]})", 2, "i4");
                return [0xE4];
            case Code.Clt:
                cc.Append(me => $"({me[0]})<({me[1]})", 2, "i4");
                return [0xE5];
            case Code.Clt_Un:
                cc.Append(me => $"({me[0]})<({me[1]})", 2, "i4");
                return [0xE6];

            case Code.Ldtoken:
            { 
                cc.Error();
                var operand = instruction.Operand; 
                if (operand is FieldReference fieldRef)
                {
                    // bmw.WriteWarning($"Ldtoken Instruction: Field = {fieldRef.FullName}");
                    var fieldDef = fieldRef.Resolve();
                    if (fieldDef != null)
                    {
                        var fieldInitialValue = fieldDef.InitialValue;
                        if (fieldInitialValue != null)
                        {
                            // bmw.WriteWarning($"Field Initial Value: {BitConverter.ToString(fieldInitialValue)}");
                            var len = fieldDef.InitialValue.Length;

                            // use 0xA1 to load address type. 0xA1 0x0f, 0x11: just a chunk of data.
                            byte[] ret = [0xA1, tMap.aAddress.typeid, 0x11, (byte)(len & 0xff), (byte)(len << 8), .. fieldDef.InitialValue];
                            return ret;
                        }
                        else
                        {
                            bmw.WriteWarning("Field has no initial value.");
                        }
                    } 
                    else
                    {
                        bmw.WriteWarning("Unable to resolve FieldDefinition.");
                    }
                }
                else if (operand is TypeReference typeRef)
                {
                    bmw.WriteWarning($"Ldtoken Instruction: Type = {typeRef.FullName}");
                }
                else if (operand is MethodReference methodRef)
                {
                    bmw.WriteWarning($"Ldtoken Instruction: Method = {methodRef.FullName}");
                }
                else
                {
                    bmw.WriteWarning($"Ldtoken Instruction: Unknown operand type."); 
                }

                return null; 
            } 
            case Code.Switch:
            {
                var targets = (Instruction[])instruction.Operand;
                var switchBytes = new byte[1 + 2 + targets.Length * 2]; // 1 byte for opcode, 2 bytes for count, 2 bytes for each target offset
                switchBytes[0] = 0x50; // Let's assume 0x50 is the opcode for the switch in our bytecode
                BitConverter.GetBytes((short)targets.Length).CopyTo(switchBytes, 1); // Add the number of targets
                cc.Append(me => $"switch({me[0]}){{{string.Join("\n",targets.Select((ins,i)=>$"case {i}: IL_{ins.Offset:x4};"))}}}", 1);
                postProcessor.Add(() =>
                {
                    for (int i = 0; i < targets.Length; i++)
                    {
                        var operandOffset = ILoffset2BCoffset[targets[i].Offset];
                        var offset = myBuffer[operandOffset].offset;
                        BitConverter.GetBytes((short)offset).CopyTo(switchBytes, 3 + i * 2);
                    }
                });

                return switchBytes;
            } 
            case Code.Castclass:
            {
                // Validate cast statically when possible; emit runtime check otherwise
                var t = (TypeReference)instruction.Operand;
                var resolvedTarget = t.Resolve();
                if (resolvedTarget == null)
                {
                    cc.Error("cast target not resolvable");
                }
                // No stack shape change at IL level; at runtime we just verify compatibility
                // Emit dedicated VM opcode 0x79; runtime will validate and leave stack unchanged
                return [0x79];
            }
            default:
                return null; // Unsupported instruction
        }
    }

    private byte[] HandleMethodCall(MethodReference methodRef)
    {
        // Example for handling method calls, can be customized further
        // just 6F.
        var methodDefinition = methodRef.Resolve();

        if (methodDefinition != null)
        {
            //var sname = GetGenericResolvedName(methodDefinition, methodRef); //why bother?
            var sname = GetNameNonGeneric(methodDefinition);
            
            if (BuiltInMethods.Any(p => p.name == sname))
            { 
                // check if it's ..ctor?
                if (sname.Contains(".ctor"))
                {
                    SI.RT_types[GetGenericResolvedName(methodRef.Resolve(), methodRef)] =
                        (methodRef.DeclaringType, methodRef);
                } 
                var id = BuiltInMethods.FindIndex(p => p.name == sname);
                if (id == -1)
                {
                    bmw.WriteError($"Runtime library doesn't support ctor `{sname}`!");
                    return null;
                }

                // Builtin methods: special-case constructors to consume only ctor args (no 'this')
                if (sname.Contains(".ctor"))
                {
                    // Consume ctor parameters so C-side stack stays balanced; runtime handles object via builtin_arg0
                    cc.Append(_ => "", methodDefinition.Parameters.Count + (methodDefinition.HasThis ? 1 : 0));
                }
                else
                {
                    // Handle C builtin methods (math, etc.)
                    var iscbuiltin = CCoder.Cbuiltins.Any(p => p.sname == sname);
                    if (iscbuiltin)
                    {
                        var (_, signature, args, rettype) = CCoder.Cbuiltins.First(p => p.sname == sname);
                        cc.Append(me => $"{signature}({string.Join(",", me)})", args, rettype);
                    }
                    else
                    {
                        // Non-ctor builtins that aren't C-transpilable are still allowed (handled in VM)
                        cc.Append(_ => "", methodDefinition.Parameters.Count + (methodDefinition.HasThis ? 1 : 0));
                    }
                }
                
                return [0xA7, (byte)(id&0xff), (byte)(id>>8)];
            } 

            
            // Check if it's a system library method that's not supported
            if (sname.StartsWith("System.")) 
            {
                bmw.WriteError($"Runtime library doesn't support `{sname}`!");
                return null;
            }

            var en = new Processor(this) { bmw = bmw }.Process(methodDefinition, methodRef);
            if (en == null || en.ccoder.CError != "none") 
                cc.Error("calling method not C transpilable.");
            if (en == null) return null;

            // Emit C-call glue for transpiled methods so cfun's can call each other
            int args_count = methodDefinition.Parameters.Count + (methodDefinition.HasThis ? 1 : 0);
            string rettype_str = null;
            if (!(methodDefinition.ReturnType.FullName == "System.Void" || methodDefinition.IsConstructor))
            {
                if (tMapDict.TryGetValue(methodDefinition.ReturnType.Name, out var typing) && typing.typeid < 10)
                {
                    rettype_str = CCoder.CCTyping[typing.typeid];
                }
                else
                {
                    cc.Error("invalid return type for C, only primitives allowed.");
                }
            }
            cc.Append(me => $"cfun{en.ccoder.myid}({string.Join(",", me)})", args_count, rettype_str);

            var ird = en.registry;
            return [0xA6, (byte)(ird & 0xff), (byte)(ird >> 8)];
        }

        // Fallback: try to match by reference name for built-ins (e.g., String.Format)
        var srefname = GetNameNonGeneric(methodRef);
        var bid = BuiltInMethods.FindIndex(p => p.name == srefname);
        if (bid >= 0)
        {
            // consume args (+ this if present)
            cc.Append(_ => "", methodRef.Parameters.Count + (methodRef.HasThis ? 1 : 0));
            return [0xA7, (byte)(bid & 0xff), (byte)(bid >> 8)];
        }

        bmw.WriteWarning($"Cannot resolve method reference: {methodRef.FullName}");
        return null;
    }

    TypeReference ResolveGenericType(GenericInstanceType genericInstanceType)
    {
        var resolvedType = genericInstanceType.ElementType.Resolve();
        if (resolvedType.HasGenericParameters)
        {
            var instance = new GenericInstanceType(resolvedType);
            foreach (var arg in genericInstanceType.GenericArguments)
            {
                instance.GenericArguments.Add(arg);
            }
            return instance;
        }
        return genericInstanceType;
    }


    private (bool, MethodDefinition) ImplementsMethod(TypeDefinition type, MethodDefinition methodDefinition)
    {
        // Check if the type itself implements the method
        var matchingMethod = type.Methods.FirstOrDefault(m => MethodsAreEquivalent(m, methodDefinition));
        if (matchingMethod != null)
        {
            return (true, matchingMethod); 
        } 

        // Check if any of the base types implement the method (for interfaces)
        foreach (var @interface in type.Interfaces)
        {
            var resolvedInterface = @interface.InterfaceType.Resolve();
            if (resolvedInterface != null)
            {
                var interfaceResult = ImplementsMethod(resolvedInterface, methodDefinition);
                if (interfaceResult.Item1)
                {
                    return interfaceResult;
                }
            }
        }

        // Check base types (for class inheritance)
        var baseType = type.BaseType?.Resolve();
        if (baseType != null)
        {
            var baseResult = ImplementsMethod(baseType, methodDefinition);
            if (baseResult.Item1)
            {
                return baseResult;
            }
        }

        return (false, null);
    } 

    private bool MethodsAreEquivalent(MethodDefinition method1, MethodDefinition method2)
    {
        if (method1 == null || method2 == null) return false;

        // Prefer explicit override metadata
        if (method1.HasOverrides && method1.Overrides.Any(p => p.Resolve() == method2))
            return true;

        // Fallback: signature-based match for virtual/override across inheritance
        if (!string.Equals(method1.Name, method2.Name, StringComparison.Ordinal))
            return false;

        if (method1.Parameters.Count != method2.Parameters.Count)
            return false;

        for (int i = 0; i < method1.Parameters.Count; i++)
        {
            var p1 = method1.Parameters[i].ParameterType;
            var p2 = method2.Parameters[i].ParameterType;
            if (!string.Equals(p1.FullName, p2.FullName, StringComparison.Ordinal))
                return false;
        }

        if (!string.Equals(method1.ReturnType.FullName, method2.ReturnType.FullName, StringComparison.Ordinal))
            return false;

        return true;
    }

    Dictionary<Instruction, int> stackDepth = new Dictionary<Instruction, int>();

    public void AnalyzeMethod(MethodDefinition method)
    {
        if (!method.HasBody)
            throw new ArgumentException("Method must have a body.");
          
        var body = method.Body;
        var instructions = body.Instructions;
          
        // Initialize stack state tracking
        var workQueue = new Queue<(Instruction instruction, int stackDepth, Instruction previous)>();
        workQueue.Enqueue((instructions[0], 0, null));
         
        while (workQueue.Count > 0)
        {
            var (instruction, stackDepth, previous) = workQueue.Dequeue();

            if (this.stackDepth.ContainsKey(instruction))
            { 
                if (this.stackDepth[instruction] != stackDepth)
                {
                    throw new InvalidOperationException($"Inconsistent stack state at instruction {instruction}. Expected: {this.stackDepth[instruction]}, Found: {stackDepth}");
                }
            }
            else 
            {
                // Process next instruction(s)
                var nextInstruction = instruction.Next;
                if (instruction.OpCode.Code==Code.Nop) 
                     
                    if (nextInstruction != null && !this.stackDepth.ContainsKey(nextInstruction))
                    {
                        workQueue.Enqueue((nextInstruction, stackDepth, previous)); 
                        continue; // just skip nop.
                    }

                this.stackDepth[instruction] = stackDepth;
                
                // if (debugAnalyze)
                    // bmw.WriteWarning($"ins={instruction}");

                cc.AnalyzeFrom(previous); 
                
                ConvertToBytecode(instruction, method); 

                stackDepth = UpdateStackDepth(instruction, stackDepth);

                  
                // Handle branch instructions
                if (instruction.OpCode == OpCodes.Switch)
                {
                    // Get the array of target instructions for the switch
                    var targets = (Instruction[])instruction.Operand;

                    // Enqueue each target instruction
                    foreach (var target in targets)
                    {
                        if (!this.stackDepth.ContainsKey(target)) 
                        {
                            workQueue.Enqueue((target, stackDepth, instruction)); 
                        }
                    }

                    // Also continue to the next instruction after the switch, if any
                    if (nextInstruction != null && !this.stackDepth.ContainsKey(nextInstruction))
                    {
                        workQueue.Enqueue((nextInstruction, stackDepth, instruction));
                    }
                }else if (instruction.OpCode.FlowControl == FlowControl.Branch) 
                {
                    var target = (Instruction)instruction.Operand;
                    if (!this.stackDepth.ContainsKey(target))
                    {
                        workQueue.Enqueue((target, stackDepth, instruction));
                    }
                }
                else if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
                {
                    var target = (Instruction)instruction.Operand;
                    if (!this.stackDepth.ContainsKey(target))
                    {
                        workQueue.Enqueue((target, stackDepth, instruction));
                    }

                    // For conditional branches, continue to the next instruction as well
                    if (nextInstruction != null && !this.stackDepth.ContainsKey(nextInstruction))
                    {
                        workQueue.Enqueue((nextInstruction, stackDepth, instruction));
                    }
                }
                else
                {
                    // For all other instructions, continue to the next instruction
                    if (nextInstruction != null && !this.stackDepth.ContainsKey(nextInstruction))
                    {
                        workQueue.Enqueue((nextInstruction, stackDepth, instruction));
                    }
                }
            }
        }
         
        if (method.CustomAttributes.Any(p => p.AttributeType.Name == "RequireNativeCodeAttribute") && cc.error)
            throw new WeavingException($"Method {method.FullName} requires native code but cannot transpile!, error={cc.CError}");
    }

    private int UpdateStackDepth(Instruction instruction, int stackDepth)
    {
        var opCode = instruction.OpCode;

        // Calculate the stack pop count
        stackDepth -= GetPopCount(opCode, instruction.Operand);

        // Calculate the stack push count
        stackDepth += GetPushCount(opCode, instruction.Operand);

        if (stackDepth < 0)
        {
            throw new InvalidOperationException($"Stack underflow at instruction {instruction}");
        }

        return stackDepth;
    }

    private int GetPopCount(OpCode opCode, object operand)
    {
        switch (opCode.StackBehaviourPop)
        {
            case StackBehaviour.Pop0:
                return 0;

            case StackBehaviour.Pop1:
            case StackBehaviour.Popi:
            case StackBehaviour.Popref:
                return 1;

            case StackBehaviour.Pop1_pop1:
            case StackBehaviour.Popi_pop1:
            case StackBehaviour.Popi_popi:
            case StackBehaviour.Popi_popi8:
            case StackBehaviour.Popi_popr4:
            case StackBehaviour.Popi_popr8:
            case StackBehaviour.Popref_pop1:
            case StackBehaviour.Popref_popi:
                return 2;

            case StackBehaviour.Popref_popi_popref:
            case StackBehaviour.Popref_popi_popi:
            case StackBehaviour.Popref_popi_popi8:
            case StackBehaviour.Popref_popi_popr4:
            case StackBehaviour.Popref_popi_popr8:
                return 3;

            case StackBehaviour.Varpop:
                if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt)
                {
                    // Pop parameters for method call, plus 'this' for instance methods
                    var method = (MethodReference)operand;
                    return method.Parameters.Count + (method.HasThis ? 1 : 0);
                }
                else if (opCode == OpCodes.Newobj)
                {
                    var ctor = (MethodReference)operand; 
                    return ctor.Parameters.Count;
                } 
                else if (opCode == OpCodes.Ret)
                {
                    return rettype.FullName == "System.Void" ? 0 : 1;
                }
                throw new NotImplementedException($"Unhandled Varpop case for opcode {opCode}");

            default:
                throw new NotImplementedException($"Unhandled StackBehaviourPop case: {opCode.StackBehaviourPop}");
        }
    }

    private int GetPushCount(OpCode opCode, object operand)
    {
        switch (opCode.StackBehaviourPush)
        {
            case StackBehaviour.Push0:
                return 0;

            case StackBehaviour.Push1:
            case StackBehaviour.Pushi:
            case StackBehaviour.Pushi8:
            case StackBehaviour.Pushr4:
            case StackBehaviour.Pushr8:
            case StackBehaviour.Pushref:
                return 1;

            case StackBehaviour.Push1_push1:
                return 2;

            case StackBehaviour.Varpush:
                if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt)
                {
                    var method = (MethodReference)operand;
                    return method.ReturnType.FullName == "System.Void" ? 0 : 1;
                }
                throw new NotImplementedException($"Unhandled Varpush case for opcode {opCode}");

            default:
                throw new NotImplementedException($"Unhandled StackBehaviourPush case: {opCode.StackBehaviourPush}");
        }
    }

    void EnsureInheritanceLayout(TypeReference type)
    {
        var td = type.Resolve();
        if (td == null) return;
        if (!SI.class_ifield_offset.TryGetValue(type.FullName, out var info)) return;
        if (info.baseInitialized) return;
        info.baseInitialized = true;
        if (td.BaseType == null) return;
        var baseType = td.BaseType.Resolve();
        if (baseType == null || baseType.FullName == "System.Object") return;
        EnsureInheritanceLayout(td.BaseType);
        if (SI.class_ifield_offset.TryGetValue(td.BaseType.FullName, out var baseInfo))
        {
            info.baseType = td.BaseType;
            foreach (var kv in baseInfo.field_offset)
            {
                if (!info.field_offset.ContainsKey(kv.Key))
                {
                    info.field_offset[kv.Key] = kv.Value;
                }
            }
            info.size = Math.Max(info.size, baseInfo.size);
        }
    }
}