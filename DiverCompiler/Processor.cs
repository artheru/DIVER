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
    public BaseModuleWeaver bmw;


    public class ResultDLL
    {
        public byte[] bytes;
        public CartFieldInfo[] CartFields;
        public CartDescriptorInfo[] CartDescriptors;
    }

    public enum CartDescriptorKind : byte
    {
        Primitive = 0,
        Array = 1,
        Struct = 2,
        String = 3,
        Reference = 4,
    }

    public class CartFieldInfo
    {
        public string FieldName { get; set; }
        public int Offset { get; set; }
        public byte TypeTag { get; set; }
        public ushort DescriptorId { get; set; }
    }

    public class CartDescriptorInfo
    {
        public ushort Id { get; set; }
        public CartDescriptorKind Kind { get; set; }
        public byte PrimitiveTypeId { get; set; }
        public ushort ElementDescriptorId { get; set; }
        public ushort StructDataOffset { get; set; }
        public CartStructFieldInfo[] Fields { get; set; } = Array.Empty<CartStructFieldInfo>();
        public ushort ClassId { get; set; }
    }

    public class CartStructFieldInfo
    {
        public string Name { get; set; }
        public ushort Offset { get; set; }
        public ushort DescriptorId { get; set; }
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
        internal HashSet<TypeReference> referenced_types = new(new TypeReferenceEqualityComparer());

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
        internal List<(string field, TypeReference tr, byte typeId, int offset, ushort descriptorId)> cart_io_layout = [];
        internal List<CartDescriptorInfo> cart_descriptors = new();
        internal Dictionary<TypeReference, ushort> descriptorLookup = new(new TypeReferenceEqualityComparer());

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

        ushort EnsureDescriptor(TypeReference type)
        {
            if (type == null) return 0;

            // Canonicalize type BEFORE lookup to avoid duplicate descriptor ids for ByRef/Pointer/Enum
            if (type.IsByReference)
                type = ((ByReferenceType)type).ElementType;

            if (type.IsPointer)
                type = ((PointerType)type).ElementType;

            if (type.IsValueType && type.Resolve()?.IsEnum == true)
            {
                var enumType = type.Resolve();
                var valueField = enumType.Fields.FirstOrDefault(f => f.Name == "value__");
                if (valueField != null)
                {
                    type = valueField.FieldType;
                }
                else
                {
                    type = enumType.Module.TypeSystem.Int32;
                }
            }

            if (SI.descriptorLookup.TryGetValue(type, out var existing)) return existing;

            // Reserve id and slot early to stabilize ids and break recursion
            ushort id = (ushort)SI.cart_descriptors.Count;
            SI.descriptorLookup[type] = id;
            SI.cart_descriptors.Add(new CartDescriptorInfo { Id = id }); // placeholder

            var descriptor = new CartDescriptorInfo { Id = id };

            if (tMapDict.TryGetValue(type.Name, out var primitive))
            {
                descriptor.Kind = CartDescriptorKind.Primitive;
                descriptor.PrimitiveTypeId = primitive.typeid;
            }
            else if (type.FullName == "System.String")
            {
                descriptor.Kind = CartDescriptorKind.String;
            }
            else if (type.IsArray)
            {
                var element = ((ArrayType)type).ElementType;
                descriptor.Kind = CartDescriptorKind.Array;
                descriptor.ElementDescriptorId = EnsureDescriptor(element);
            }
            else if (IsStruct(type))
            {
                descriptor.Kind = CartDescriptorKind.Struct;
                var td = type.Resolve();
                if (td == null)
                    throw new WeavingException($"Cannot resolve struct {type.FullName}");

                if (!SI.class_ifield_offset.TryGetValue(type.FullName, out var classInfo))
                {
                    // Build minimal field layout if not present yet
                    classInfo = new shared_info.class_fields
                    {
                        tr = type,
                        baseType = td.BaseType,
                        field_offset = new Dictionary<string, (int offset, TypeReference tr, byte typeid, TypeReference dtr)>(),
                        baseInitialized = true,
                    };
                    int runningOffset = 0;
                    foreach (var fd in td.Fields.Where(f => !f.IsStatic))
                    {
                        var ft = fd.FieldType;
                        var typeId = GetTypeID(ft);
                        classInfo.field_offset[fd.Name] = (runningOffset, ft, typeId, ft);
                        runningOffset += tMapDict.TryGetValue(ft.Name, out var tv) ? tv.size : 4; // rough size for refs
                    }
                    classInfo.size = runningOffset;
                    SI.class_ifield_offset[type.FullName] = classInfo;
                }
                var fields = new List<CartStructFieldInfo>();
                foreach (var fd in td.Fields.Where(f => !f.IsStatic))
                {
                    if (!classInfo.field_offset.TryGetValue(fd.Name, out var meta))
                        continue;
                    var subtype = fd.FieldType;
                    ushort child = EnsureDescriptor(subtype);
                    var offset = (ushort)meta.offset;
                    fields.Add(new CartStructFieldInfo
                    {
                        Name = fd.Name,
                        Offset = offset,
                        DescriptorId = child,
                    });
                }
                descriptor.Fields = fields.ToArray();
                descriptor.StructDataOffset = 0;
                descriptor.ClassId = (ushort)SI.instanceable_classes.IndexOf(type.FullName);
            }
            else if (type.IsGenericInstance)
            {
                // Handle generic types as references
                descriptor.Kind = CartDescriptorKind.Reference;
            }
            else if (type.Name.Contains("`"))
            {
                // Handle generic type definitions
                descriptor.Kind = CartDescriptorKind.Reference;
            }
            else if (type.FullName.StartsWith("System.Collections.Generic"))
            {
                // Handle collection types as references
                descriptor.Kind = CartDescriptorKind.Reference;
            }
            else if (type.FullName.StartsWith("System.Linq"))
            {
                // Handle LINQ types as references
                descriptor.Kind = CartDescriptorKind.Reference;
            }
            else
            {
                // Treat unknown reference types as reference descriptors rather than throwing
                descriptor.Kind = CartDescriptorKind.Reference;
            }

            // Replace placeholder with finalized descriptor
            SI.cart_descriptors[id] = descriptor;
            return id;
        }

    string GetNameNonGeneric(MethodDefinition method)
    {
        return $"{method.DeclaringType.FullName}.{method.Name}({string.Join(", ", method.Parameters.Select(p => p.ParameterType.Name))})";
    }
    byte GetTypeID(TypeReference type)
    {
        string typeName = type.Name;

        // Handle some special cases
        if (typeName == "Void") return 0; // We shouldn't need this for List elements

        // Check if it's a value type or reference type
        if (tMapDict.TryGetValue(typeName, out var typeInfo))
        {
            return typeInfo.typeid;
        }

        // For reference types, return ReferenceID
        return 16; // ReferenceID
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

    // Helper to strip generic parameters from type name for matching against BuiltInMethods
    // E.g., "System.Collections.Generic.List`1<Int32>.Add(Int32)" -> "System.Collections.Generic.List`1.Add(!0)"
    // E.g., "System.ValueTuple`3<Int32, Single, Boolean>..ctor(Int32, Single, Boolean)" -> "System.ValueTuple`3..ctor(T1, T2, T3)"
    // E.g., "System.Linq.Enumerable.Where<Int32>(IEnumerable`1, Func`2)" -> "System.Linq.Enumerable.Where(IEnumerable`1, Func`2)"
    string StripGenericParameters(string methodName)
    {
        // only allow system library to be generic.
        if (!methodName.StartsWith("System.")) 
            return methodName;
        return methodName;

        // First check for generic methods (method-level generics) like Where<Int32>
        // Pattern: TypeName.MethodName<GenericArgs>(params)
        var methodGenericMatch = System.Text.RegularExpressions.Regex.Match(methodName, @"^(.+)\.([^.]+)<(.+?)>\((.*?)\)$");
        if (methodGenericMatch.Success)
        {
            var typeAndNamespace = methodGenericMatch.Groups[1].Value;
            var methodNameOnly = methodGenericMatch.Groups[2].Value;
            var genericArgsJoined = methodGenericMatch.Groups[3].Value; // e.g., "Int32" or "TKey, TValue"
            var parameters = methodGenericMatch.Groups[4].Value;

            // For method-level generics (e.g., DefaultIfEmpty<T>(..., T)), normalize parameters:
            // - If a parameter exactly equals a generic arg name, replace with !index
            // - Strip inner generic type arguments from parameter types (e.g., IEnumerable`1<Int32> -> IEnumerable`1)
            if (!string.IsNullOrEmpty(genericArgsJoined) && !string.IsNullOrEmpty(parameters))
            {
                var genericArgs = genericArgsJoined.Split(new[] { ", " }, StringSplitOptions.None);
                var paramList = parameters.Split(new[] { ", " }, StringSplitOptions.None);
                for (int i = 0; i < paramList.Length; i++)
                {
                    for (int j = 0; j < genericArgs.Length; j++)
                    {
                        if (paramList[i] == genericArgs[j])
                        {
                            paramList[i] = $"!{j}";
                            break;
                        }
                    }
                    // Strip generic arguments from generic type parameters in-place
                    var m = System.Text.RegularExpressions.Regex.Match(paramList[i], @"^(.*?)`(\d+)<.+?>$");
                    if (m.Success)
                    {
                        var typePrefix = m.Groups[1].Value;
                        var gcount = m.Groups[2].Value;
                        paramList[i] = $"{typePrefix}`{gcount}";
                    }
                }
                parameters = string.Join(", ", paramList);
            }

            return $"{typeAndNamespace}.{methodNameOnly}({parameters})";
        }
        
        // For system library generics, strip the <...> part but keep the `N marker
        var match = System.Text.RegularExpressions.Regex.Match(methodName, @"^(.*?)`(\d+)<(.+?)>\.(.+?)\((.*?)\)$");
        if (match.Success)
        {
            // Found a generic type with method and parameters
            var typePrefix = match.Groups[1].Value;
            var genericCount = int.Parse(match.Groups[2].Value);
            var genericArgs = match.Groups[3].Value.Split(new[] { ", " }, StringSplitOptions.None);
            var methodName_only = match.Groups[4].Value;
            var parameters = match.Groups[5].Value;
            
            // Special handling for ValueTuple constructors - use T1, T2, T3 notation
            if (typePrefix.Contains("ValueTuple") && methodName_only == ".ctor")
            {
                if (!string.IsNullOrEmpty(parameters))
                {
                    var paramList = parameters.Split(new[] { ", " }, StringSplitOptions.None);
                    if (paramList.Length == genericCount)
                    {
                        var genericParams = string.Join(", ", Enumerable.Range(1, genericCount).Select(i => $"T{i}"));
                        return $"{typePrefix}`{genericCount}.{methodName_only}({genericParams})";
                    }
                }
            }
            
            // Special handling for Action/Func Invoke methods - use T, T1, T2, T3 notation
            if ((typePrefix.Contains("Action") || typePrefix.Contains("Func")) && methodName_only == "Invoke")
            {
                if (!string.IsNullOrEmpty(parameters))
                {
                    var paramList = parameters.Split(new[] { ", " }, StringSplitOptions.None);
                    // Action has N parameters for Action`N, Func has N-1 parameters for Func`N (last is return type)
                    if (typePrefix.Contains("Action") && paramList.Length == genericCount)
                    {
                        var genericParams = genericCount == 1 ? "T" : string.Join(", ", Enumerable.Range(1, genericCount).Select(i => $"T{i}"));
                        return $"{typePrefix}`{genericCount}.{methodName_only}({genericParams})";
                    }
                    else if (typePrefix.Contains("Func") && paramList.Length == genericCount - 1)
                    {
                        var genericParams = paramList.Length == 1 ? "T" : string.Join(", ", Enumerable.Range(1, paramList.Length).Select(i => $"T{i}"));
                        return $"{typePrefix}`{genericCount}.{methodName_only}({genericParams})";
                    }
                }
                else if (typePrefix.Contains("Func") && genericCount == 1)
                {
                    // Func`1.Invoke() has no parameters (just returns TResult)
                    return $"{typePrefix}`{genericCount}.{methodName_only}()";
                }
            }
            
            // For other generic types (like List<T>), replace matching parameter types with !0, !1, etc.
            if (!string.IsNullOrEmpty(parameters))
            {
                var paramList = parameters.Split(new[] { ", " }, StringSplitOptions.None);
                for (int i = 0; i < paramList.Length; i++)
                {
                    // Check if this parameter matches one of the generic arguments
                    for (int j = 0; j < genericArgs.Length; j++)
                    {
                        if (paramList[i] == genericArgs[j])
                        {
                            paramList[i] = $"!{j}";
                        }
                    }
                }
                parameters = string.Join(", ", paramList);
            }
            
            return $"{typePrefix}`{genericCount}.{methodName_only}({parameters})";
        }
        
        // Simpler case: just strip <...> from type name
        match = System.Text.RegularExpressions.Regex.Match(methodName, @"^(.*?)<.*?>(.*)$");
        if (match.Success)
        {
            return match.Groups[1].Value + match.Groups[2].Value;
        }
        return methodName;
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
    internal class CCoder
    {
        public static int ccid = 0;
        // if a method is:
        //  1. call only builtin-C functions or CC functions 
        //  2. no singleton load a reference.
        //  3. no address loading
        public string CError = "none";

        public List<string> FunPtr = new();
         
        public static string[] CCTyping =
        [
            "u1", "u1", "i1", "i2", "i2", "u2", "i4", "u4", "r4",
            //9~16
            "/","/","/","/","/","/","/","ptr", "/", "/", "/"
        ];
        public static (string sname, string signature, int args)[] Cbuiltins = new[]
        {
            ("System.Object..ctor()", "(void*)()", 0), // we don't pass this.
            ("System.Math.Abs(Decimal)", "(float*)(float)", 1),
            ("System.Math.Abs(Double)", "(float*)(float)", 1),
            ("System.Math.Abs(Int16)", "(int*)(int)", 1),
            ("System.Math.Abs(Int32)", "(int*)(int)", 1),
            ("System.Math.Abs(SByte)", "(int*)(int)", 1),
            ("System.Math.Abs(Single)", "(float*)(float)", 1),
            ("System.Math.Acos(Double)", "(float*)(float)", 1),
            ("System.Math.Acosh(Double)", "(float*)(float)", 1),
            ("System.Math.Asin(Double)", "(float*)(float)", 1),
            ("System.Math.Asinh(Double)", "(float*)(float)", 1),
            ("System.Math.Atan(Double)", "(float*)(float)", 1),
            ("System.Math.Atan2(Double, Double)", "(float*)(float, float)", 2),
            ("System.Math.Atanh(Double)", "(float*)(float)", 1),
            ("System.Math.Ceiling(Double)", "(float*)(float)", 1),
            ("System.Math.Clamp(Double, Double, Double)", "(float*)(float, float, float)", 3),
            ("System.Math.Clamp(Int16, Int16, Int16)", "(int*)(int, int, int)", 3),
            ("System.Math.Clamp(Int32, Int32, Int32)", "(int*)(int, int, int)", 3),
            ("System.Math.Clamp(SByte, SByte, SByte)", "(int*)(int, int, int)", 3),
            ("System.Math.Clamp(Single, Single, Single)", "(float*)(float, float, float)", 3),
            ("System.Math.Cos(Double)", "(float*)(float)", 1),
            ("System.Math.Cosh(Double)", "(float*)(float)", 1),
            ("System.Math.Exp(Double)", "(float*)(float)", 1),
            ("System.Math.Floor(Double)", "(float*)(float)", 1),
            ("System.Math.Log(Double)", "(float*)(float)", 1),
            ("System.Math.Log(Double, Double)", "(float*)(float, float)", 2),
            ("System.Math.Log10(Double)", "(float*)(float)", 1),
            ("System.Math.Log2(Double)", "(float*)(float)", 1),
            ("System.Math.Max(Double, Double)", "(float*)(float, float)", 2),
            ("System.Math.Max(Int16, Int16)", "(int*)(int, int)", 2),
            ("System.Math.Max(Int32, Int32)", "(int*)(int, int)", 2),
            ("System.Math.Max(SByte, SByte)", "(int*)(int, int)", 2),
            ("System.Math.Max(Single, Single)", "(float*)(float, float)", 2),
            ("System.Math.Min(Double, Double)", "(float*)(float, float)", 2),
            ("System.Math.Min(Int16, Int16)", "(int*)(int, int)", 2),
            ("System.Math.Min(Int32, Int32)", "(int*)(int, int)", 2),
            ("System.Math.Min(SByte, SByte)", "(int*)(int, int)", 2),
            ("System.Math.Min(Single, Single)", "(float*)(float, float)", 2),
            ("System.Math.Pow(Double, Double)", "(float*)(float, float)", 2),
            ("System.Math.Round(Double)", "(float*)(float)", 1),
            ("System.Math.Sign(Double)", "(int*)(float)", 1),
            ("System.Math.Sign(Int16)", "(int*)(int)", 1),
            ("System.Math.Sign(Int32)", "(int*)(int)", 1),
            ("System.Math.Sign(SByte)", "(int*)(int)", 1),
            ("System.Math.Sign(Single)", "(int*)(float)", 1),
            ("System.Math.Sin(Double)", "(float*)(float)", 1),
            ("System.Math.Sinh(Double)", "(float*)(float)", 1),
            ("System.Math.Sqrt(Double)", "(float*)(float)", 1),
            ("System.Math.Tan(Double)", "(float*)(float)", 1),
            ("System.Math.Tanh(Double)", "(float*)(float)", 1),
            // LINQ operations for arrays
            ("System.Linq.Enumerable.Where(Byte[], System.Func`2[Byte,Boolean])", "(u1*)(u1*, int)", 2),
            ("System.Linq.Enumerable.Sum(Int32[])", "(int*)(int*)", 1),
            ("System.Linq.Enumerable.Max(Int32[])", "(int*)(int*)", 1),
            ("System.Linq.Enumerable.Min(Int32[])", "(int*)(int*)", 1),
            ("System.Linq.Enumerable.Take(Byte[], Int32)", "(u1*)(u1*, int)", 2),
            // String operations
            ("System.String.Join(System.String, System.String[])", "(char*)(char*, char**)", 2),
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
                // _p.bmw.WriteWarning($"{curI}: st=[{string.Join(",", startingStack.ToArray().Select(p=>p.var_name))}]");
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
        public int clinked_name;

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
            else if (tMapDict.TryGetValue(rettype.Name, out var typing))
            {
                ret.retBytes = [typing.typeid, 0, 0]; 
                ret.ret_name = rettype.Name;
            }
            else if (IsStruct(rettype))
            {
                ret.retBytes = [tMap.aJump.typeid, 0, 0];
                ret.ret_name = rettype.Name;
            }
            else if (rettype.Name == "Object")
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

        // ensure cart descriptors are prepared for cart fields
        if (isRoot)
        {
            foreach (var field in method.DeclaringType.BaseType.Resolve().Fields.Where(f => f.IsPublic && !f.IsStatic))
            {
                if (IsDerivedFrom(field.DeclaringType, "CartActivator.CartDefinition"))
                {
                    EnsureDescriptor(field.FieldType);
                }
            }
        }

        // foreach (var instruction in method.Body.Instructions)
        //     bmw.WriteWarning($"{fname}> s_{stackStates[instruction]} for {instruction}");

        var boffset = 0;
        int i = 0;
        cc.CGenMode();

        foreach (var instruction in method.Body.Instructions) 
        {
            ILoffset2BCoffset[instruction.Offset] = i;

            //bmw.WriteWarning(fname+">"+ instruction.ToString()); 

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
                            if (!SI.methods.TryGetValue(GetGenericResolvedName(rmd, null), out var en)) 
                            {
                                // added processor.
                                bmw.WriteWarning($"{cname}::{rmd} implements {md}, process");
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
                    EnsureDescriptor(ftype);
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
                        SI.class_ifield_offset[cname] = cfields = new() { tr = v.tr };
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

            SI.cart_io_layout = SI.sfield_offset.field_offset
                .Where(fo => IsDerivedFrom(fo.Value.dtr.Resolve(), "CartActivator.CartDefinition"))
                .Select(p =>
                {
                    var descriptorId = EnsureDescriptor(p.Value.tr);
                    return (p.Key, p.Value.tr, p.Value.typeid, p.Value.offset, descriptorId);
                }).ToList();

            bmw.WriteWarning($"abstract method:[{string.Join(",", SI.virtcallMethods.Select(p=>p.Name))}]");
            bmw.WriteWarning($"instanced classes:[{string.Join(", ", SI.instanceable_classes)}]");
            bmw.WriteWarning($"cart IO:[{string.Join(", ", SI.cart_io_layout.Select(p=>$"{p.field}(desc:{p.descriptorId})"))}]");
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
            foreach (var m in all_methods) 
            {
                if (m.ccoder.CError == "none")
                { 
                    bmw.WriteWarning($"======== Generated C Code of '{m.name}' @ {m.registry}: ===========");

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
                      
                    var fcname = CCoder.ccid++;
                    m.ccoder.clinked_name = fcname; 

                    var arg_set = $"int argN=0;\n {
                        string.Join("\n", m.argumentList.Select((p, ai) => $"{CCoder.CCTyping[p.typeID]} arg{ai} = *({CCoder.CCTyping[p.typeID]}*)&args[(argN++)*4];"))}{
                            (m.ccoder.additional_Args.Count == 0 ? "" : $"\n{
                                string.Join("\n", m.ccoder.additional_Args.Select(p => $"{p.argtype} {p.name} = *({p.argtype}*)&args[(argN++)*4];"))}")}";

                    var cCode =
                        $"{retN} cfun{fcname}(u1* args){{\n" +
                        $"//args:\n{arg_set}\n" +
                        $"//stack_vars:\n{string.Join(";\n", m.ccoder.stackVars)};\n " +
                        $"//local_vars:\n{string.Join(";\n", m.variableList.Select((p, ai) => $"{CCoder.CCTyping[p.typeID]} var{ai}"))};\n " +
                        $"{string.Join("", ccodes)}}}\n\n";
                    bmw.WriteWarning($"Full CCode of {m.name}:\n{cCode}");
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
            }

            byte[] code_chunk = [..code_table, ..codes.SelectMany(p => p.meta.Concat(p.code).ToArray())];

            byte[][] iclass = SI.instanceable_classes.Select((p,id) =>
            {
                var classInfo = SI.class_ifield_offset[p];
                EnsureInheritanceLayout(classInfo.tr);
                return classInfo.field_offset.SelectMany(k =>
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
                }).ToArray();
            }).ToArray();

            List<byte> iclass_layout = [];
            int ic_offset = 0;
            for (int j=0; j<SI.instanceable_classes.Count; ++j)
            {
                var p = SI.class_ifield_offset[SI.instanceable_classes[j]];
                iclass_layout.AddRange(BitConverter.GetBytes((ushort)p.size));
                iclass_layout.Add((byte)p.field_offset.Count);
                iclass_layout.AddRange(BitConverter.GetBytes(ic_offset));


                bmw.WriteWarning(
                    $"class {SI.instanceable_classes[j]} sz {p.size}(n={p.field_offset.Count}), offset_{ic_offset}");
                ic_offset += iclass[j].Length;
            }
             
            var staticFieldTypesInOrder = SI.sfield_offset.field_offset
                .ToArray()
                .OrderBy(p => p.Value.offset)
                .Select(p => p.Value.tr)
                .ToArray();

            // Ensure all static field types have descriptors
            foreach (var t in staticFieldTypesInOrder)
                EnsureDescriptor(t);

            // Map descriptor Id -> array index in cart_descriptors, then emit indices for statics
            var idToIndex = SI.cart_descriptors
                .Select((d, idx) => (d.Id, idx))
                .ToDictionary(p => p.Id, p => (ushort)p.idx);

            var orderedStaticDescriptorIndices = staticFieldTypesInOrder
                .Select(t => idToIndex[SI.descriptorLookup[t]])
                .ToArray();

            bmw.WriteWarning($"statics descriptor indices: [{string.Join(",", orderedStaticDescriptorIndices)}], descCount={SI.cart_descriptors.Count}");

            byte[] cart_desc_blob = SI.cart_descriptors.SelectMany(desc =>
            {
                List<byte> data = [];
                data.AddRange(BitConverter.GetBytes((ushort)desc.Id));
                data.Add((byte)desc.Kind);
                data.Add(desc.PrimitiveTypeId);
                data.AddRange(BitConverter.GetBytes(desc.ElementDescriptorId));
                data.AddRange(BitConverter.GetBytes(desc.StructDataOffset));
                data.AddRange(BitConverter.GetBytes(desc.ClassId));
                data.Add((byte)desc.Fields.Length);
                foreach (var field in desc.Fields)
                {
                    data.AddRange(BitConverter.GetBytes(field.Offset));
                    data.AddRange(BitConverter.GetBytes(field.DescriptorId));
                }
                return data;
            }).ToArray();

            byte[] cart_fields_blob = SI.cart_io_layout.SelectMany(p =>
                BitConverter.GetBytes((short)p.offset)
                    .Concat(new[]{p.typeId})
                    .Concat(BitConverter.GetBytes(p.descriptorId))).ToArray();

            byte[] program_desc =
            [ 
                ..BitConverter.GetBytes((ushort)SI.cart_io_layout.Count),
                ..cart_fields_blob,
                ..BitConverter.GetBytes((ushort)SI.cart_descriptors.Count),
                ..cart_desc_blob,
                ..BitConverter.GetBytes((ushort)SI.instanceable_classes.Count),
                ..iclass_layout, 
                ..iclass.SelectMany(p=>p) 
            ];     
              
            byte[] statics_descriptor =
            [  
                ..BitConverter.GetBytes((ushort)orderedStaticDescriptorIndices.Length),
                ..orderedStaticDescriptorIndices.SelectMany(id => BitConverter.GetBytes(id))
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

            ret.dll = new ResultDLL()
            {
                bytes = dll,
                CartFields = SI.cart_io_layout.Select(p => new CartFieldInfo
                {
                    FieldName = p.field,
                    Offset = p.offset,
                    TypeTag = p.typeId,
                    DescriptorId = p.descriptorId,
                }).ToArray(),
                CartDescriptors = SI.cart_descriptors.ToArray(),
            }; 
             
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
                return [0x79];
            case Code.Newobj:
            { 
                // if 0x7A, first according to class_id, get layout, then generate a heap object
                var mref = (MethodReference)instruction.Operand;
                var bc = mref.DeclaringType.Resolve();
                if (IsDerivedFrom(bc, "CartActivator.CartDefinition"))
                    throw new WeavingException("Must not new CartActivator.CartDefinition object!");

                // Use builtin class instantiation via BuildInClasses.
                if (mref.Name == ".ctor" && mref.Parameters.Count == 0)
                {
                    var decType = mref.DeclaringType;
                    var fullTypeName = decType.Resolve().FullName;
                    SI.referenced_types.Add(decType);
                    if (BuildInClasses.Contains(fullTypeName))
                    {
                        // Use builtin method id for ctor in BuiltInMethods
                        var ctorSig = $"{fullTypeName}..ctor()";
                        var id = BuiltInMethods.IndexOf(ctorSig);
                        if (id >= 0)
                        {
                            // Emit a builtin call directly (A7)
                            return [0xA7, (byte)(id & 0xff), (byte)(id >> 8)];
                        }
                    }
                }

                cc.Error("not allowed to create heap object");
                var methodCallBytes = HandleMethodCall(mref);
                if (methodCallBytes == null)
                    return null;
                    
                byte[] ret = [0x7A, 0, 0, ..methodCallBytes];
                SI.linking_actions.Add(() =>
                {
                    var id = SI.instanceable_classes.IndexOf(mref.DeclaringType.FullName);
                    if (id == -1)
                    {
                        // just reference.
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
                    SI.referenced_types.Add(tr);

                    var iscart = IsDerivedFrom(fd.DeclaringType, "CartActivator.CartDefinition");  
                    byte type = (byte)((instanced ? 0 : 1) | (iscart ? 2 : 0));

                    byte[] ret = [code, type, 0, 0, 0, 0]; // code|type|offset|(classid,io_id)
                    SI.linking_actions.Add(() =>
                    {
                        // instanced offset relative to pointer address; static offset relative to class_static_field_start.
                        var offset = (instanced&&!iscart? SI.class_ifield_offset[cname]: SI.sfield_offset).field_offset[fr.Resolve().Name].offset;
                        ret[2] = (byte)(offset & 0xff);
                        ret[3] = (byte)(offset >> 8);
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
                        }

                        if (iscart)
                        {
                            var entry = SI.cart_io_layout.First(p => p.field == fd.Name);
                            ret[4] = (byte)(entry.descriptorId & 0xff);
                            ret[5] = (byte)(entry.descriptorId >> 8);
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
            {
                var result = HandleMethodCall((MethodReference)instruction.Operand); //A6:custom call, A7:builtin call.
                if (result == null)
                    throw new WeavingException($"Failed to handle method call at {instruction}");
                return result;
            }
            case Code.Callvirt:
            {
                cc.Error("no reference to virtual object");
                // limited polymophism support.
                var methodRef = (MethodReference)instruction.Operand;
                var mdr = methodRef.Resolve();

                if (mdr.IsAbstract)
                {
                    bmw.WriteWarning($"Abstract method call {methodRef.Name}, add to virt call list.");

                    var id = SI.virtcallMethods.IndexOf(mdr);
                    if (id == -1)
                    {
                        id = SI.virtcallMethods.Count;
                        SI.virtcallMethods.Add(mdr);
                    }

                    return [0xA0, (byte)(id & 0xff), (byte)(id >> 8)];
                } 
                else
                {
                    var callBytes = HandleMethodCall((MethodReference)instruction.Operand);
                    if (callBytes == null)
                        throw new WeavingException($"Failed to handle virtual method call at {instruction}");
                    return [0xA2, ..callBytes];
                }
            }
            case Code.Ldftn:
            {
                cc.Error();
                // if use 0xA1 to load address type, 3rd param decide what actual addr-val to be loaded. A1 should convert index into address to mem0
                var ldftnBytes = HandleMethodCall((MethodReference)instruction.Operand);
                if (ldftnBytes == null)
                    throw new WeavingException($"Failed to handle ldftn at {instruction}");
                return [0xA1, tMap.aAddress.typeid, ..ldftnBytes];
            }

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
                SI.referenced_types.Add(t);
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
            
            if (BuiltInMethods.Contains(sname))
            { 
                // check if it's ..ctor?
                if (sname.Contains(".ctor"))
                {
                    SI.RT_types[GetGenericResolvedName(methodRef.Resolve(), methodRef)] =
                        (methodRef.DeclaringType, methodRef);
                } 
                var id = BuiltInMethods.IndexOf(sname);
                if (id == -1)
                {
                    bmw.WriteError($"Runtime library doesn't support ctor `{sname}`!");
                    return null;
                }

                // Handle special builtin methods
                if (sname == "System.Object..ctor()")
                {
                    cc.Append(_ => "", 1);
                }
                else if (sname.StartsWith("System.Action") || sname.StartsWith("System.Func"))
                {
                    // Delegates and other system generics don't need special handling
                    cc.Append(_ => "", 1);
                }
                else
                {
                    // Other builtins may not be C-transpilable
                    cc.Error($"C library doesn't support newobj `{sname}`");
                }
                
                return [0xA7, (byte)(id&0xff), (byte)(id>>8)];
            } 

            if (CCoder.Cbuiltins.Any(p => p.sname == sname))
            {
                var (_, _, args) = CCoder.Cbuiltins.First(p => p.sname == sname);
                var cid = cc.FunPtr.IndexOf(sname);
                if (cid == -1)
                {
                    cid = cc.FunPtr.Count;
                    cc.FunPtr.Add(sname);
                }
                // todo function call..
                cc.Append(me => $"fun_{cid}({string.Join(",", me)}", args);
            }
            
            // Check if it's a system library method that's not supported
            if (sname.StartsWith("System.")) 
            {
                bmw.WriteError($"Runtime library doesn't support `{sname}`!");
                return null;
            }

            var en = new Processor(this) { bmw = bmw }.Process(methodDefinition, methodRef);
            if (en == null || en.ccoder.CError != "none") 
                cc.Error("calling method not C tranpilable.");
            if (en == null) return null;

            var ird = en.registry;
            return [0xA6, (byte)(ird & 0xff), (byte)(ird >> 8)];
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
        if (baseType != null && baseType!=methodDefinition.DeclaringType)
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
        return method1.Overrides.Any(p => p.Resolve() == method2);
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
                cc.AnalyzeFrom(previous);

                // bmw.WriteWarning($"ins={instruction}");
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
            throw new WeavingException($"Method {method.FullName} requires native code but cannot transpile!");
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

        // placeholder for descriptor size calculation

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

        int GetDescriptorSize(ushort descriptorId) => 0;
}