using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using MCURoutineCompiler;

namespace DiverCompiler
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(@"Dotnet integrated vehicle embedded runtime: 
-g to generate extramethods handler: It read ExtraMethods.cs(or you can specify one) and generate: txt and h file for weaver, and a dll for reference import(use it in MCU C# project).
-c to put me into weaverfile of the MCU C# project: Run in csproj folder. You also need to manually add FodyWeaver to your MCU C# project.");

            // Handle command-line arguments
            if (args.Length > 0)
            {
                switch (args[0])
                {
                    case "-g":
                        var csfile = args.Length > 1 ? args[1] : "ExtraMethods.cs";
                        GenerateBuiltinHeader(csfile);
                        break;
                    case "-c":
                        var csprojFile = args.Length > 1 ? args[1] : FindCsprojInCurrentDirectory();
                        if (!string.IsNullOrEmpty(csprojFile))
                        {
                            AddWeaverFile(csprojFile);
                        }
                        else
                        {
                            Console.WriteLine("No .csproj file found in the current directory.");
                        }
                        break;
                    default:
                        Console.WriteLine("Invalid option.");
                        break;
                }
            }
            else
            {
                Console.WriteLine("No arguments provided.");
            }

            Console.ReadLine();
        }

        static void AddWeaverFile(string csprojFile)
        {
            // Check if the csproj file exists
            if (!File.Exists(csprojFile))
            {
                Console.WriteLine($"The file '{csprojFile}' does not exist.");
                return;
            }

            try
            {
                // Load the csproj file
                var csprojXml = XDocument.Load(csprojFile);

                // Check if the Fody package reference already exists
                var fodyPackage = csprojXml.Descendants("PackageReference")
                    .FirstOrDefault(x => (string)x.Attribute("Include") == "Fody");

                if (fodyPackage == null)
                {
                    // Add the Fody package reference
                    var itemGroup = new XElement("ItemGroup");
                    itemGroup.Add(new XElement("PackageReference",
                        new XAttribute("Include", "Fody"),
                        new XAttribute("Version", "6.6.4"),
                        new XElement("PrivateAssets", "all"),
                        new XElement("IncludeAssets", "runtime; build; native; contentfiles; analyzers; buildtransitive")));

                    csprojXml.Root?.Add(itemGroup);
                    Console.WriteLine("Fody package reference added.");
                }
                else
                {
                    Console.WriteLine("Fody package reference already exists.");
                }

                // Check if the weaver file reference exists
                var weaverFile = csprojXml.Descendants("WeaverFiles")
                    .FirstOrDefault(x => (string)x.Attribute("Include") == "DiverCompiler.exe");

                if (weaverFile == null)
                {
                    // Add the WeaverFiles element
                    var weaverFileElement = new XElement("WeaverFiles",
                        new XAttribute("Include", "DiverCompiler.exe"));
                    csprojXml.Root?.Add(weaverFileElement);
                    Console.WriteLine("Weaver file reference added.");
                }
                else
                {
                    Console.WriteLine("Weaver file reference already exists.");
                }

                // Save the modified csproj file
                csprojXml.Save(csprojFile);
                Console.WriteLine($"Updated {csprojFile} successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update {csprojFile}: {ex.Message}");
            }
        }

        static void GenerateBuiltinHeader(string extraMethodsCs)
        {
            // Check if the ExtraMethods.cs file exists
            if (!File.Exists(extraMethodsCs))
            {
                Console.WriteLine($"The file '{extraMethodsCs}' does not exist.");
                return;
            }

            try
            {
                // Read the ExtraMethods.cs file
                var csContent = File.ReadAllText(extraMethodsCs);

                // Extract namespace
                var namespaceRegex = new Regex(@"namespace\s+([\w\.]+)");
                var namespaceMatch = namespaceRegex.Match(csContent);
                var namespaceName = namespaceMatch.Success ? namespaceMatch.Groups[1].Value : "UnknownNamespace";

                // Extract class name
                var classRegex = new Regex(@"public\s+static\s+class\s+(\w+)");
                var classMatch = classRegex.Match(csContent);
                var className = classMatch.Success ? classMatch.Groups[1].Value : "UnknownClass";

                // Use regex to find static methods
                var methodRegex = new Regex(@"public\s+static\s+(\w+)\s+(\w+)\s*\(([^)]*)\)");
                var matches = methodRegex.Matches(csContent);

                // Prepare the output for additional_builtins.h
                var headerContent = @"// Auto-generated header
// How to write builtin functions:
//
// 1. Function signature:
//    void builtin_YourFunction(uchar** reptr)
//    The reptr points to the current evaluation stack pointer
//
// 2. Reading arguments:
//    - Arguments are on the stack in reverse order (last argument first)
//    - Use POP macro to move stack pointer: POP;
//    - Use helper functions to get values:
//      * pop_int(reptr)      - for Int32
//      * pop_float(reptr)    - for Single/float
//      * pop_bool(reptr)     - for Boolean
//      * pop_reference(reptr) - for object references
//      * pop_short(reptr)    - for Int16
//      * pop_byte(reptr)     - for Byte
//      * pop_sbyte(reptr)    - for SByte
//
// 3. Returning values:
//    - Use helper functions to push return values:
//      * push_int(reptr, value)      - for Int32
//      * push_float(reptr, value)    - for Single/float
//      * push_bool(reptr, value)     - for Boolean
//      * PUSH_STACK_REFERENCEID(id)  - for object references
//
// 4. Working with objects:
//    - Use heap_obj[id].pointer to access object data
//    - Check object types with header byte:
//      * ArrayHeader (11)   - for arrays
//      * StringHeader (12)  - for strings
//      * ObjectHeader (13)  - for other objects
//
// 5. Error handling:
//    - Use DOOM(""message"") for fatal errors
//    - Check null references and array bounds
//
// Example:
// void builtin_Math_Add(uchar** reptr) {
//     int b = pop_int(reptr);
//     int a = pop_int(reptr);
//     push_int(reptr, a + b);
// }
//
";
                var methodSignatures = new List<string>();

                // Read existing method signatures from extra_methods.txt
                Dictionary<string, string> existingMethods = new Dictionary<string, string>();
                if (File.Exists("extra_methods.txt"))
                {
                    var existingSignatures = File.ReadAllLines("extra_methods.txt");
                    foreach (var sig in existingSignatures)
                    {
                        // Extract method name and parameters from signature like "Namespace.Class.Method(param1, param2)"
                        var match = Regex.Match(sig, @".*\.(\w+)\((.*?)\)");
                        if (match.Success)
                        {
                            var methodName = match.Groups[1].Value;
                            var parameters = match.Groups[2].Value;
                            existingMethods[methodName] = parameters;
                        }
                    }
                }

                // Read existing implementations to preserve user code
                Dictionary<string, string> existingImplementations = new Dictionary<string, string>();
                if (File.Exists("additional_builtins.h"))
                {
                    var content = File.ReadAllText("additional_builtins.h");
                    var methodBlocks = Regex.Matches(content, @"void\s+builtin_(\w+)\s*\(uchar\*\*\s*reptr\)\s*{([^}]+)}",
                        RegexOptions.Singleline);
                    
                    foreach (Match methodBlock in methodBlocks)
                    {
                        var methodName = methodBlock.Groups[1].Value;
                        var methodBody = methodBlock.Groups[2].Value;
                        existingImplementations[methodName] = methodBody;
                    }
                }

                // Generate C built-in methods and collect method signatures
                foreach (Match match in matches)
                {
                    var returnType = match.Groups[1].Value;
                    var methodName = match.Groups[2].Value;
                    var parameters = match.Groups[3].Value;

                    // Collect method signature (using original parameterTypes calculation)
                    var parameterTypes = parameters.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => Regex.Replace(p.Trim().Split(' ')[0],
                            @"\b(int|uint|short|ushort|long|ulong|byte|sbyte|float|double|bool)\b",
                            m => m.Value switch
                            {
                                "int" => "Int32",
                                "uint" => "UInt32",
                                "short" => "Int16",
                                "ushort" => "UInt16",
                                "long" => "Int64",
                                "ulong" => "UInt64",
                                "byte" => "Byte",
                                "sbyte" => "SByte",
                                "float" => "Single",
                                "double" => "Double",
                                "bool" => "Boolean",
                                _ => m.Value
                            }))
                        .ToArray();

                    var standardizedParams = string.Join(", ", parameterTypes);

                    // Check if method exists and parameters changed
                    if (existingMethods.TryGetValue(methodName, out string existingParams))
                    {
                        if (existingParams.Replace(" ", "") != standardizedParams.Replace(" ", ""))
                        {
                            // Parameters changed, preserve implementation but add warning
                            headerContent += $"void builtin_{methodName}(uchar** reptr) {{\n";
                            headerContent += $"    // TODO: signature has changed to ({standardizedParams}) @ {DateTime.Now:yyyy/MM/dd HH:mm:ss}\n";
                            if (existingImplementations.TryGetValue(methodName, out string existingBody))
                            {
                                headerContent += existingBody;
                            }
                            else
                            {
                                headerContent += $"    // TODO: Add logic for {methodName}\n";
                            }
                            headerContent += "}\n\n";
                        }
                        else if (existingImplementations.TryGetValue(methodName, out string existingBody))
                        {
                            // Parameters unchanged, keep existing implementation
                            headerContent += $"void builtin_{methodName}(uchar** reptr) {{";
                            headerContent += existingBody;
                            headerContent += "}\n\n";
                        }
                        else
                        {
                            // Method exists in signatures but no implementation
                            headerContent += $"void builtin_{methodName}(uchar** reptr) {{\n";
                            headerContent += $"    // Implementation for {methodName}({standardizedParams})\n";
                            headerContent += $"    // TODO: Add logic for {methodName}\n";
                            headerContent += "}\n\n";
                        }
                    }
                    else
                    {
                        // New method
                        headerContent += $"void builtin_{methodName}(uchar** reptr) {{\n";
                        headerContent += $"    // Implementation for {methodName}({standardizedParams})\n";
                        headerContent += $"    // TODO: Add logic for {methodName}\n";
                        headerContent += "}\n\n";
                    }

                    var signature = $"{namespaceName}.{className}.{methodName}({standardizedParams})";
                    methodSignatures.Add(signature);
                }

                // Add the built-in methods to the core
                headerContent += "void add_additional_builtins() {\n";
                headerContent += "    // Start adding methods from index bn\n";
                foreach (Match match in matches)
                {
                    var methodName = match.Groups[2].Value;
                    headerContent += $"    if (bn >= NUM_BUILTIN_METHODS) {{\n";
                    headerContent += $"        DOOM(\"Too many built-in methods when adding {methodName}!\");\n";
                    headerContent += $"    }}\n";
                    headerContent += $"    builtin_methods[bn++] = builtin_{methodName};\n";
                }
                headerContent += "}\n";

                // Write to additional_builtins.h
                var outputPath = "additional_builtins.h";
                File.WriteAllText(outputPath, headerContent);
                Console.WriteLine($"Header file '{outputPath}' generated successfully."); 

                // Write method signatures to extra_methods.txt
                var jsonOutputPath = "extra_methods.txt";
                File.WriteAllLines(jsonOutputPath, methodSignatures);
                Console.WriteLine($"Method signatures written to '{jsonOutputPath}' successfully.");

                // Generate reference assembly using dotnet
                var tmpDir = @".\tmp_ref";
                Directory.CreateDirectory(tmpDir);
                var dllOutputPath = Path.Combine(tmpDir, $"temp.dll");

                // Create a temporary project file
                var tempCsproj = Path.Combine(tmpDir, "temp.csproj");
                File.WriteAllText(tempCsproj, $@" 
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""{Path.GetFileName(extraMethodsCs)}"" />
  </ItemGroup>
</Project>");

                // Copy the source file to temp directory
                var tempCs = Path.Combine(tmpDir, Path.GetFileName(extraMethodsCs));
                File.Copy(extraMethodsCs, tempCs, true);

                var processInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build {tempCsproj} -o {tmpDir}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    process.WaitForExit();
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    if (process.ExitCode == 0)
                    {
                        // Copy the generated DLL to the output directory
                        var finalDllPath = Path.Combine(
                            Path.GetDirectoryName(extraMethodsCs),
                            $"{Path.GetFileNameWithoutExtension(extraMethodsCs)}.dll"
                        );
                        File.Copy(dllOutputPath, finalDllPath, true);
                        Console.WriteLine($"Reference assembly generated and copied to '{finalDllPath}'");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to generate reference assembly: {error}");
                        if (!string.IsNullOrEmpty(output))
                        {
                            Console.WriteLine($"Compiler output: {output}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to generate builtin header: {ex.Message}");
            }
        }

        static string FindCsprojInCurrentDirectory()
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var csprojFiles = Directory.GetFiles(currentDirectory, "*.csproj");

            return csprojFiles.Length > 0 ? csprojFiles[0] : string.Empty;
        }
    }
}
