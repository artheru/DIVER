using Fody;
using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;
using MCURoutineCompiler;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CartActivator;

public class ModuleWeaver : BaseModuleWeaver 
{  
    public override void Execute()
    {
        const string ver = "v0.36e";

        WriteWarning($"RunOnMCU Processor {ver}"); 

        // Load additional built-in methods
        var extraMethodsPath = "extra_methods.txt";
        if (File.Exists(extraMethodsPath))
        {
            var extraMethods = File.ReadAllLines(extraMethodsPath);
            WriteWarning($"Loading {extraMethods.Length} additional built-in methods");
            Processor.BuiltInMethods.AddRange(extraMethods); 
        }
        else
        {
            WriteWarning("No extra_methods.txt found, proceeding with default built-ins only");
        }

        // Debugger.Launch();    
   
        ModuleDefinition module = ModuleDefinition;
        
        // Process RunOnMCU types  
        foreach (var type in module.Types) 
        {  
            if (type.CustomAttributes.Any(attr => attr.AttributeType.Name == "LogicRunOnMCUAttribute"))
            {
                var method = type.Methods.FirstOrDefault(m =>
                    m.Name == "Operation" && m.Parameters.Count == 1 && 
                    m.Parameters[0].ParameterType.FullName == "System.Int32");
                 
                if (method == null)     
                {
                    WriteWarning($"Type {type.Name} is RunOnMCU but doesn't have Operation(i) method");
                    continue;
                }
                  
                if (method.Body == null)
                {
                    WriteWarning($"Type {type.Name} is RunOnMCU but Operation(i) method is empty.");
                    continue; 
                }

                WriteWarning($">>> Processing {type.Name}.`");

                // foreach (var mm in type.Methods)
                // {
                //     }
                // }

                var zk=type.CustomAttributes.First(attr => attr.AttributeType.Name == "LogicRunOnMCUAttribute");
                var myinterval = 1000;
                foreach (var cn in zk.Fields)
                    if (cn.Name == "scanInterval") myinterval = (int)cn.Argument.Value;
                 
                var processor = new Processor() { bmw = this };
                var ei = processor.Process(method, scanInterval:myinterval);
                if (ei == null)
                    throw new WeavingException($"Please revise the code for {type.Name}::Operation(int i)");
                 
                // WriteWarning(
                //     $"Custom functions:\r\n{string.Join("\r\n", processor.SI.methods.Keys.Select(p => $"  => {p}"))}");
                var dll = ei.dll;
                WriteWarning(
                    $"mcu program sz={dll.bytes.Length}, interval={myinterval}, interface variables sequence=[{string.Join(",", dll.CartFields.Select(p => $"{p.FieldName}(desc:{p.DescriptorId})@{p.Offset}"))}]");
                foreach (var desc in dll.CartDescriptors)
                {
                    WriteWarning($"descriptor id={desc.Id} kind={desc.Kind} primitive={desc.PrimitiveTypeId} element={desc.ElementDescriptorId} class={desc.ClassId} fields={desc.Fields.Length}");
                }

                // File.WriteAllBytes($"{type.Name}.bin", dll.bytes);

                module.Resources.Add(new EmbeddedResource($"{type.Name}.bin", ManifestResourceAttributes.Public, dll.bytes));
                // Manually build JSON string using string interpolation
                var fieldsJson = string.Join(",",
                    dll.CartFields.Select(f =>
                        $"{{\"FieldName\":\"{f.FieldName}\",\"Offset\":{f.Offset},\"TypeTag\":{f.TypeTag},\"DescriptorId\":{f.DescriptorId}}}"
                    )
                );

                var descriptorsJson = string.Join(",",
                    dll.CartDescriptors.Select(d =>
                        $"{{" +
                        $"\"Id\":{d.Id}," +
                        $"\"Kind\":\"{d.Kind}\"," +
                        $"\"PrimitiveTypeId\":{d.PrimitiveTypeId}," +
                        $"\"ElementDescriptorId\":{d.ElementDescriptorId}," +
                        $"\"StructDataOffset\":{d.StructDataOffset}," +
                        $"\"ClassId\":{d.ClassId}," +
                        $"\"Fields\":[" +
                        string.Join(",",
                            d.Fields.Select(sf =>
                                $"{{\"Name\":\"{sf.Name}\",\"Offset\":{sf.Offset},\"DescriptorId\":{sf.DescriptorId}}}"
                            )
                        ) +
                        $"]" +
                        $"}}"
                    )
                );

                var json = "{\n" +
                           $"  \"fields\": [{fieldsJson}],\n" +
                           $"  \"descriptors\": [{descriptorsJson}]\n" +
                           "}";
                module.Resources.Add(new EmbeddedResource($"{type.Name}.bin.json", ManifestResourceAttributes.Public,
                    Encoding.UTF8.GetBytes(json)));
            }
        }  
         
        WriteWarning($"RunOnMCU Processor {ver} finished");
    }

    public override IEnumerable<string> GetAssembliesForScanning()
    {
        return Enumerable.Empty<string>();
    }
}