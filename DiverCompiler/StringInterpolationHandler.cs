using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using CartActivator;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace MCURoutineCompiler;

/// <summary>
/// Handles transformation of string interpolation (which uses DefaultInterpolatedStringHandler) into String.Format calls
/// </summary>
public class StringInterpolationHandler
{
    private readonly ModuleDefinition _module;
    private readonly MethodReference _stringFormat1;
    private readonly MethodReference _stringFormat2;
    private readonly MethodReference _stringFormat3;
    private readonly MethodReference _stringFormatArray;
    private bool _debug = true; // Enable debugging

    private BaseModuleWeaver bmw;
    private ILProcessor processor;

    public StringInterpolationHandler(ModuleDefinition module, ModuleWeaver moduleWeaver)
    {
        _module = module;
        bmw = moduleWeaver;
        
        bmw.WriteWarning("StringInterpolationHandler constructor called");
        
        // Get references to String.Format methods
        var stringType = module.ImportReference(typeof(string)).Resolve();
        var objectType = module.ImportReference(typeof(object));
        var objectArrayType = module.ImportReference(typeof(object[]));

        _stringFormat1 = module.ImportReference(
            stringType.Methods.First(m => m.Name == "Format" && m.Parameters.Count == 2 && 
                                        m.Parameters[0].ParameterType.FullName == "System.String" &&
                                        m.Parameters[1].ParameterType.FullName == "System.Object"));

        _stringFormat2 = module.ImportReference(
            stringType.Methods.First(m => m.Name == "Format" && m.Parameters.Count == 3 && 
                                        m.Parameters[0].ParameterType.FullName == "System.String" &&
                                        m.Parameters[1].ParameterType.FullName == "System.Object" &&
                                        m.Parameters[2].ParameterType.FullName == "System.Object"));

        _stringFormat3 = module.ImportReference(
            stringType.Methods.First(m => m.Name == "Format" && m.Parameters.Count == 4 && 
                                        m.Parameters[0].ParameterType.FullName == "System.String" &&
                                        m.Parameters[1].ParameterType.FullName == "System.Object" &&
                                        m.Parameters[2].ParameterType.FullName == "System.Object" &&
                                        m.Parameters[3].ParameterType.FullName == "System.Object"));

        _stringFormatArray = module.ImportReference(
            stringType.Methods.First(m => m.Name == "Format" && m.Parameters.Count == 2 && 
                                        m.Parameters[0].ParameterType.FullName == "System.String" &&
                                        m.Parameters[1].ParameterType.FullName == "System.Object[]"));
        
        bmw.WriteWarning("String.Format references imported successfully");
    }

    /// <summary>
    /// Process a method, find string interpolation instances and replace with String.Format calls
    /// </summary>
    public void ProcessMethod(MethodDefinition method)
    {
        if (!method.HasBody)
            return;

        bmw.WriteWarning($"Processing method: {method.FullName}");
        
        // Dump all instructions for debugging
        DumpMethodInstructions(method);

        // Get the IL processor for the method
        processor = method.Body.GetILProcessor();
        
        // First, identify all complete string interpolation blocks
        var interpolationBlocks = FindInterpolationBlocks(method);
        
        // If we didn't find any blocks using the primary method, try the alternate approach
        if (interpolationBlocks.Count == 0)
        {
            bmw.WriteWarning("No interpolation blocks found with primary method, trying alternate approach");
            interpolationBlocks = FindInterpolationBlocksAlternate(method);
        }
        
        bmw.WriteWarning($"Found {interpolationBlocks.Count} complete interpolation blocks");
        
        // Process each block in reverse order (to avoid offset issues)
        foreach (var block in interpolationBlocks.OrderByDescending(b => b.StartIndex))
        {
            bmw.WriteWarning($"Processing interpolation block: {block.StartIndex} to {block.EndIndex}");
            ReplaceInterpolation(method, processor, method.Body.Instructions.ToList(), block.StartIndex, block.EndIndex);
        }
        
        // Replace DefaultInterpolatedStringHandler variables with int variables
        ReplaceStringHandlerVariables(method);
        
        bmw.WriteWarning($"Method {method.Name} processed. Replaced {interpolationBlocks.Count} interpolations");
    }

    // Replace DefaultInterpolatedStringHandler variables with int variables
    private void ReplaceStringHandlerVariables(MethodDefinition method)
    {
        if (!method.HasBody || method.Body.Variables == null || method.Body.Variables.Count == 0)
            return;
        
        var intType = _module.ImportReference(typeof(int));
        int replacedCount = 0;
        
        for (int i = 0; i < method.Body.Variables.Count; i++)
        {
            var variable = method.Body.Variables[i];
            if (variable?.VariableType?.FullName?.Contains("DefaultInterpolatedStringHandler") == true)
            {
                bmw.WriteWarning($"  Replacing DefaultInterpolatedStringHandler variable at index {i} with int");
                // Change the type to int but keep the same variable index
                method.Body.Variables[i] = new VariableDefinition(intType);
                replacedCount++;
            }
        }
        
        bmw.WriteWarning($"  Replaced {replacedCount} DefaultInterpolatedStringHandler variables with int variables");
    }

    private class InterpolationBlock
    {
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
    }

    private List<InterpolationBlock> FindInterpolationBlocks(MethodDefinition method)
    {
        var result = new List<InterpolationBlock>();
        var instructions = method.Body.Instructions.ToList();
        
        // First identify all constructor calls for DefaultInterpolatedStringHandler
        var ctorIndices = new List<int>();
        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode.Code == Mono.Cecil.Cil.Code.Call ||   
                instr.OpCode.Code == Mono.Cecil.Cil.Code.Callvirt || 
                instr.OpCode.Code == Mono.Cecil.Cil.Code.Newobj)
            {
                if (instr.Operand is MethodReference methodRef)
                { 
                    string declaringType = methodRef.DeclaringType?.FullName ?? "";
                    string methodName = methodRef.Name ?? "";
                    
                    bmw.WriteWarning($"Checking instruction {i}: {instr.OpCode} {methodName} in {declaringType}");
                    
                    if ((methodRef.Name == ".ctor" || methodRef.Name == "ctor") && 
                        declaringType.Contains("DefaultInterpolatedStringHandler"))
                    {
                        ctorIndices.Add(i);
                        bmw.WriteWarning($"Found DefaultInterpolatedStringHandler constructor at index {i}");
                    }
                }
            }
        }
        
        // Then find the corresponding ToStringAndClear calls
        foreach (var startIndex in ctorIndices)
        {
            for (int i = startIndex + 1; i < instructions.Count; i++)
            {
                var instr = instructions[i];
                if (instr.OpCode.Code == Mono.Cecil.Cil.Code.Call && 
                    instr.Operand is MethodReference methodRef &&
                    methodRef.Name == "ToStringAndClear" &&
                    methodRef.DeclaringType?.FullName?.Contains("DefaultInterpolatedStringHandler") == true)
                {
                    // Found a matching end
                    // Check that there's no other constructor between start and end
                    bool isValidBlock = true;
                    for (int j = startIndex + 1; j < i; j++)
                    {
                        var jInstr = instructions[j];
                        if (jInstr.OpCode.Code == Mono.Cecil.Cil.Code.Call || 
                            jInstr.OpCode.Code == Mono.Cecil.Cil.Code.Callvirt || 
                            jInstr.OpCode.Code == Mono.Cecil.Cil.Code.Newobj)
                        {
                            if (jInstr.Operand is MethodReference jMethodRef && 
                                (jMethodRef.Name == ".ctor" || jMethodRef.Name == "ctor") &&
                                jMethodRef.DeclaringType?.FullName?.Contains("DefaultInterpolatedStringHandler") == true)
                            {
                                isValidBlock = false;
                                break;
                            }
                        }
                    }
                    
                    if (isValidBlock)
                    {
                        result.Add(new InterpolationBlock
                        {
                            StartIndex = startIndex,
                            EndIndex = i
                        });
                        bmw.WriteWarning($"Found complete interpolation block from {startIndex} to {i}");
                        break;
                    }
                }
            }
        }
        
        return result;
    }

    // Alternative approach to find interpolation blocks by looking for constructor patterns
    private List<InterpolationBlock> FindInterpolationBlocksAlternate(MethodDefinition method)
    {
        var result = new List<InterpolationBlock>();
        var instructions = method.Body.Instructions.ToList();
        
        // Scan through the IL looking for the pattern of:
        // 1. ldc.i4.X (literal count)
        // 2. ldc.i4.Y (format item count)
        // 3. call .ctor DefaultInterpolatedStringHandler
        // Followed eventually by:
        // N. call ToStringAndClear
        
        for (int i = 0; i < instructions.Count - 2; i++)
        {
            // Look for two consecutive ldc.i4 instructions followed by a constructor call
            if (IsLoadConstantOpCode(instructions[i].OpCode) && 
                IsLoadConstantOpCode(instructions[i+1].OpCode) &&
                instructions.Count > i+2 && 
                instructions[i+2].OpCode.Code == Code.Call)
            {
                if (instructions[i+2].Operand is MethodReference methodRef)
                {
                    string typeName = methodRef.DeclaringType?.FullName ?? "";
                    
                    // Check if this is the DefaultInterpolatedStringHandler constructor
                    if (methodRef.Name == ".ctor" && typeName.Contains("DefaultInterpolatedStringHandler"))
                    {
                        int startIndex = i;
                        bmw.WriteWarning($"Found potential interpolation start at index {startIndex}");
                        
                        // Now find the corresponding ToStringAndClear call
                        for (int j = startIndex + 3; j < instructions.Count; j++)
                        {
                            if (instructions[j].OpCode.Code == Code.Call)
                            {
                                var endMethodRef = instructions[j].Operand as MethodReference;
                                if (endMethodRef != null && 
                                    endMethodRef.Name == "ToStringAndClear" && 
                                    endMethodRef.DeclaringType?.FullName == typeName)
                                {
                                    // Found a complete block
                                    result.Add(new InterpolationBlock
                                    {
                                        StartIndex = startIndex,
                                        EndIndex = j
                                    });
                                    bmw.WriteWarning($"Found complete interpolation block from {startIndex} to {j} (alternate method)");
                                    
                                    // Skip to the end of this block
                                    i = j;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        return result;
    }

    // Helper method to check if an opcode is a load constant instruction
    private bool IsLoadConstantOpCode(OpCode opCode)
    {
        return opCode.Code == Code.Ldc_I4 ||
               opCode.Code == Code.Ldc_I4_0 ||
               opCode.Code == Code.Ldc_I4_1 ||
               opCode.Code == Code.Ldc_I4_2 ||
               opCode.Code == Code.Ldc_I4_3 ||
               opCode.Code == Code.Ldc_I4_4 ||
               opCode.Code == Code.Ldc_I4_5 ||
               opCode.Code == Code.Ldc_I4_6 ||
               opCode.Code == Code.Ldc_I4_7 ||
               opCode.Code == Code.Ldc_I4_8 ||
               opCode.Code == Code.Ldc_I4_M1 ||
               opCode.Code == Code.Ldc_I4_S;
    }

    private void ReplaceInterpolation(MethodDefinition method, ILProcessor processor, 
                                     List<Instruction> instructions, 
                                     int startIndex, int endIndex)
    {
        // Extract information about literal parts and formatted expressions
        var interpolationInfo = AnalyzeInterpolation(instructions, startIndex, endIndex);
        
        bmw.WriteWarning($"  Analyzed interpolation: {interpolationInfo.Literals.Count} literals, {interpolationInfo.FormatItems.Count} format items");
        for (int i = 0; i < interpolationInfo.Literals.Count; i++)
        {
            bmw.WriteWarning($"    Literal {i}: '{interpolationInfo.Literals[i]}'");
        }
        for (int i = 0; i < interpolationInfo.FormatItems.Count; i++)
        {
            bmw.WriteWarning($"    Format item {i}: Type={interpolationInfo.FormatItems[i].Type?.FullName ?? "unknown"}, Format={interpolationInfo.FormatItems[i].FormatClause ?? "none"}");
        }
        
        // Validate the interpolation info before proceeding
        if (!ValidateInterpolationInfo(interpolationInfo))
        {
            bmw.WriteWarning("  SKIPPED replacement - invalid interpolation data");
            return;
        }
        
        // Ensure the method has a local variable of type System.Object for our temp values
        // This is needed for handling complex types
        EnsureObjectLocalVariable(method);
        
        // Find the next instruction after the ToStringAndClear call to insert our replacement
        var nextInstruction = endIndex < instructions.Count - 1 ? instructions[endIndex + 1] : null;
        
        if (nextInstruction == null)
        {
            bmw.WriteWarning("  SKIPPED replacement - cannot find insertion point");
            return;
        }
        
        if (interpolationInfo.FormatItems.Count == 0)
        {
            // If there are no format items, just replace with a simple string
            var formatString = string.Join("", interpolationInfo.Literals);
            
            bmw.WriteWarning($"  Replacing with literal string: '{formatString}'");
            
            try {
                // Create the new instruction
                var newInstruction = processor.Create(OpCodes.Ldstr, formatString);
                
                // Remove all the interpolation instructions
                for (int i = endIndex; i >= startIndex; i--)
                {
                    processor.Remove(instructions[i]);
                }
                
                // Insert the new instruction
                processor.InsertBefore(nextInstruction, newInstruction);
                bmw.WriteWarning("  Successfully replaced with literal string");
            }
            catch (Exception ex) {
                bmw.WriteWarning($"  ERROR replacing with literal string: {ex.Message}\n{ex.StackTrace}");
            }
            return;
        }
        
        try {
            // Create a new sequence of instructions
            var newInstructions = new List<Instruction>();
            
            // Now create the String.Format call
            if (interpolationInfo.FormatItems.Count <= 3)
            {
                // Use the specific String.Format overloads for 1, 2, or 3 arguments
                bmw.WriteWarning($"  Building specific String.Format call with {interpolationInfo.FormatItems.Count} arguments");
                BuildSpecificFormatCall(newInstructions, processor, interpolationInfo);
            }
            else
            {
                // Use the String.Format with object[] for more than 3 arguments
                bmw.WriteWarning($"  Building array String.Format call with {interpolationInfo.FormatItems.Count} arguments");
                BuildArrayFormatCall(newInstructions, processor, interpolationInfo);
            }
            
            // Remove all the interpolation instructions
            for (int i = endIndex; i >= startIndex; i--)
            {
                processor.Remove(instructions[i]);
            }
            
            // Insert all new instructions
            foreach (var instr in newInstructions)
            {
                processor.InsertBefore(nextInstruction, instr);
            }
            
            bmw.WriteWarning("  Successfully replaced with String.Format");
        }
        catch (Exception ex) {
            bmw.WriteWarning($"  ERROR replacing with String.Format: {ex.Message}\n{ex.StackTrace}");
        }
    }
    
    // Ensure the method has a local variable of type System.Object
    private void EnsureObjectLocalVariable(MethodDefinition method)
    {
        var objectType = _module.ImportReference(typeof(object));
        
        // Check if the method already has a local of type object
        foreach (var local in method.Body.Variables)
        {
            if (local.VariableType.FullName == objectType.FullName)
            {
                bmw.WriteWarning($"  Method already has object local at index {local.Index}");
                return;
            }
        }
        
        // Add a new local variable of type System.Object
        var newLocal = new VariableDefinition(objectType);
        method.Body.Variables.Add(newLocal);
        bmw.WriteWarning($"  Added new object local at index {newLocal.Index}");
    }
    
    private bool ValidateInterpolationInfo(InterpolationInfo info)
    {
        // Check if we have enough data to create a valid replacement
        if (info.Literals.Count == 0)
        {
            bmw.WriteWarning("  Validation failed: No literals found");
            return false;
        }
        
        if (info.FormatItems.Count > 0 && info.Literals.Count < info.FormatItems.Count + 1)
        {
            bmw.WriteWarning($"  Validation failed: Not enough literals ({info.Literals.Count}) for format items ({info.FormatItems.Count})");
            return false;
        }
        
        // Check format items
        for (int i = 0; i < info.FormatItems.Count; i++)
        {
            if (info.FormatItems[i].ValueInstruction == null)
            {
                bmw.WriteWarning($"  Validation failed: Format item {i} has no value instruction");
                return false;
            }
        }
        
        return true;
    }
    
    private class InterpolationInfo
    {
        public List<string> Literals { get; } = new List<string>();
        public List<FormatItem> FormatItems { get; } = new List<FormatItem>();
    }
    
    private class FormatItem
    {
        public Instruction ValueInstruction { get; set; }
        public string AlignmentClause { get; set; }
        public string FormatClause { get; set; }
        public TypeReference Type { get; set; }
        public List<Instruction> LoadInstructions { get; set; } = new List<Instruction>();
    }
    
    private InterpolationInfo AnalyzeInterpolation(List<Instruction> instructions, int startIndex, int endIndex)
    {
        var result = new InterpolationInfo();
        
        bmw.WriteWarning($"  Analyzing interpolation from index {startIndex} to {endIndex}");
        
        // First, let's print all instructions in the block to help with debugging
        bmw.WriteWarning("  Detailed instruction dump for interpolation block:");
        for (int i = startIndex; i <= endIndex; i++)
        {
            var instr = instructions[i];
            string operandStr = "null";
            
            if (instr.Operand != null)
            {
                if (instr.Operand is MethodReference mr)
                    operandStr = $"{mr.Name} in {mr.DeclaringType?.FullName}";
                else if (instr.Operand is TypeReference tr)
                    operandStr = tr.FullName;
                else if (instr.Operand is FieldReference fr)
                    operandStr = $"{fr.Name} in {fr.DeclaringType?.FullName}";
                else
                    operandStr = instr.Operand.ToString();
            }
            
            bmw.WriteWarning($"    [{i}] {instr.OpCode.Code} - {operandStr}");
        }
        
        try
        {
            // Start at the ctor call and work forward
            for (int i = startIndex; i <= endIndex; i++)
            {
                var instruction = instructions[i];
            
            if (instruction.OpCode.Code == Mono.Cecil.Cil.Code.Call || 
                instruction.OpCode.Code == Mono.Cecil.Cil.Code.Callvirt)
            {
                if (instruction.Operand is MethodReference methodRef)
                {
                        bmw.WriteWarning($"    Instruction {i}: {instruction.OpCode} {methodRef.Name}");
                        
                    if (methodRef.Name == "AppendLiteral" && 
                        methodRef.DeclaringType?.FullName?.Contains("DefaultInterpolatedStringHandler") == true)
                    {
                        // This is a literal part - find the string value
                        var ldstrIndex = FindPreviousLdstr(instructions, i);
                        if (ldstrIndex >= 0)
                        {
                            var literalValue = instructions[ldstrIndex].Operand as string;
                            if (literalValue != null)
                            {
                                result.Literals.Add(literalValue);
                                    bmw.WriteWarning($"      Found literal: '{literalValue}'");
                                }
                                else
                                {
                                    bmw.WriteWarning($"      Found ldstr but operand is not a string");
                                    // Add empty literal as a fallback
                                    result.Literals.Add(string.Empty);
                                }
                            }
                            else
                            {
                                bmw.WriteWarning($"      Could not find ldstr for AppendLiteral");
                                // Add empty literal as a fallback
                                result.Literals.Add(string.Empty);
                        }
                    }
                    else if (methodRef.Name.StartsWith("AppendFormatted") && 
                             methodRef.DeclaringType?.FullName?.Contains("DefaultInterpolatedStringHandler") == true)
                    {
                        // This is a formatted expression
                            bmw.WriteWarning($"      Found AppendFormatted call");
                        var formatItem = ExtractFormatItem(instructions, i);
                        if (formatItem != null)
                        {
                            result.FormatItems.Add(formatItem);
                                bmw.WriteWarning($"      Extracted format item: Type={formatItem.Type?.FullName ?? "unknown"}, Format={formatItem.FormatClause ?? "none"}");
                            }
                            else
                            {
                                bmw.WriteWarning($"      Failed to extract format item");
                            }
                        }
                    }
                }
            }
            
            // Special case: If we have format items but no literals, make sure we have enough literals
            if (result.FormatItems.Count > 0 && result.Literals.Count == 0)
            {
                // Add an empty literal at the beginning
                result.Literals.Add(string.Empty);
                bmw.WriteWarning($"    Added empty literal at the beginning for string with only format items");
            }
            
            // Make sure we have a literal after the last format item
            if (result.FormatItems.Count >= result.Literals.Count)
            {
                result.Literals.Add(string.Empty);
                bmw.WriteWarning("    Added empty literal at the end");
            }
        }
        catch (Exception ex)
        {
            bmw.WriteWarning($"  ERROR while analyzing interpolation: {ex.Message}\n{ex.StackTrace}");
        }
        
        return result;
    }
    
    private int FindPreviousLdstr(List<Instruction> instructions, int currentIndex)
    {
        // Look for the most recent Ldstr instruction
        for (int i = currentIndex - 1; i >= 0; i--)
        {
            var instruction = instructions[i];
            if (instruction.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr)
            {
                return i;
            }
        }
        return -1;
    }
    
    private FormatItem ExtractFormatItem(List<Instruction> instructions, int formatCallIndex)
    {
        var methodRef = instructions[formatCallIndex].Operand as MethodReference;
        if (methodRef == null)
            return null;
        
        bmw.WriteWarning($"    Extracting format item from call at index {formatCallIndex} to {methodRef.Name}");
        
        var formatItem = new FormatItem();
        
        if (methodRef.IsGenericInstance)
        {
            var genericMethod = methodRef as GenericInstanceMethod;
            if (genericMethod != null && genericMethod.GenericArguments.Count > 0)
            {
                formatItem.Type = genericMethod.GenericArguments[0];
                bmw.WriteWarning($"      Extracted type from generic argument: {formatItem.Type.FullName}");
            }
        }
        
        // Look for the ldloca_s instruction that precedes the value loading
        int ldlocaIndex = -1;
        for (int i = formatCallIndex - 1; i >= 0; i--)
        {
            if (instructions[i].OpCode.Code == Code.Ldloca_S || 
                instructions[i].OpCode.Code == Code.Ldloca)
            {
                ldlocaIndex = i;
                bmw.WriteWarning($"      Found ldloca instruction at index {i}");
                break;
            }
        }
        
        if (ldlocaIndex >= 0 && ldlocaIndex < formatCallIndex - 1)
        {
            // Get all instructions between ldloca and the call (excluding ldloca)
            // These are the exact instructions that load the value on the stack
            for (int i = ldlocaIndex + 1; i < formatCallIndex; i++)
            {
                if (instructions[i].OpCode.Code != Code.Nop)  // Skip nop instructions
                {
                    formatItem.LoadInstructions.Add(instructions[i]);
                    bmw.WriteWarning($"      Captured value-loading instruction: {instructions[i].OpCode}");
                }
            }
            
            if (formatItem.LoadInstructions.Count > 0)
            {
                formatItem.ValueInstruction = formatItem.LoadInstructions[0]; // Set first instruction as the primary one
            }
        }
        else
        {
            bmw.WriteWarning("      Could not find ldloca_s instruction before the AppendFormatted call");
        }
        
        // If we don't have a type yet, try to infer it
        if (formatItem.Type == null && formatItem.ValueInstruction != null)
        {
            InferTypeFromInstruction(formatItem, instructions, instructions.IndexOf(formatItem.ValueInstruction));
        }
        
        // Look for format string
        // Format strings are passed as a parameter after the value
        bool hasFormatStringParam = false;
        if (methodRef.Parameters.Count > 1)
        {
            for (int i = 1; i < methodRef.Parameters.Count; i++)
            {
                if (methodRef.Parameters[i].ParameterType.FullName == "System.String")
                {
                    hasFormatStringParam = true;
                    break;
                }
            }
        }
        
        if (hasFormatStringParam)
        {
            // If there's a format string parameter, look for the last string constant before the call
            for (int i = formatCallIndex - 1; i > ldlocaIndex; i--)
            {
                if (instructions[i].OpCode.Code == Code.Ldstr)
                {
                    formatItem.FormatClause = instructions[i].Operand as string;
                    bmw.WriteWarning($"      Found format clause: '{formatItem.FormatClause}'");
                    break;
                }
            }
        }
        
        return formatItem;
    }
    
    private void InferTypeFromInstruction(FormatItem formatItem, List<Instruction> instructions, int valueIndex)
    {
        if (formatItem.Type != null)
            return; // Already have a type
        
        var instruction = instructions[valueIndex];
        
        // Try to infer type from the instruction
        switch (instruction.OpCode.Code)
        {
            case Mono.Cecil.Cil.Code.Ldloc:
            case Mono.Cecil.Cil.Code.Ldloc_0:
            case Mono.Cecil.Cil.Code.Ldloc_1:
            case Mono.Cecil.Cil.Code.Ldloc_2:
            case Mono.Cecil.Cil.Code.Ldloc_3:
            case Mono.Cecil.Cil.Code.Ldloc_S:
                // Local variable load
                if (instruction.Operand is VariableDefinition vd)
                {
                    formatItem.Type = vd.VariableType;
                    bmw.WriteWarning($"      Inferred type from local variable: {formatItem.Type.FullName}");
                }
                break;
                
            case Mono.Cecil.Cil.Code.Ldarg:
            case Mono.Cecil.Cil.Code.Ldarg_0:
            case Mono.Cecil.Cil.Code.Ldarg_1:
            case Mono.Cecil.Cil.Code.Ldarg_2:
            case Mono.Cecil.Cil.Code.Ldarg_3:
            case Mono.Cecil.Cil.Code.Ldarg_S:
                // Argument load
                if (instruction.Operand is ParameterDefinition pd)
                {
                    formatItem.Type = pd.ParameterType;
                    bmw.WriteWarning($"      Inferred type from parameter: {formatItem.Type.FullName}");
                }
                break;
                
            case Mono.Cecil.Cil.Code.Ldfld:
            case Mono.Cecil.Cil.Code.Ldsfld:
                // Field load
                if (instruction.Operand is FieldReference fr)
                {
                    formatItem.Type = fr.FieldType;
                    bmw.WriteWarning($"      Inferred type from field: {formatItem.Type.FullName}");
                }
                break;
                
            case Mono.Cecil.Cil.Code.Call:
            case Mono.Cecil.Cil.Code.Callvirt:
                // Method call
                if (instruction.Operand is MethodReference mr && mr.ReturnType.FullName != "System.Void")
                {
                    formatItem.Type = mr.ReturnType;
                    bmw.WriteWarning($"      Inferred type from method return: {formatItem.Type.FullName}");
                }
                break;
                
            case Mono.Cecil.Cil.Code.Ldc_I4:
            case Mono.Cecil.Cil.Code.Ldc_I4_0:
            case Mono.Cecil.Cil.Code.Ldc_I4_1:
            case Mono.Cecil.Cil.Code.Ldc_I4_2:
            case Mono.Cecil.Cil.Code.Ldc_I4_3:
            case Mono.Cecil.Cil.Code.Ldc_I4_4:
            case Mono.Cecil.Cil.Code.Ldc_I4_5:
            case Mono.Cecil.Cil.Code.Ldc_I4_6:
            case Mono.Cecil.Cil.Code.Ldc_I4_7:
            case Mono.Cecil.Cil.Code.Ldc_I4_8:
            case Mono.Cecil.Cil.Code.Ldc_I4_M1:
            case Mono.Cecil.Cil.Code.Ldc_I4_S:
                // Integer constant
                formatItem.Type = _module.ImportReference(typeof(int));
                bmw.WriteWarning("      Inferred type as System.Int32 from integer constant");
                break;
                
            case Mono.Cecil.Cil.Code.Ldc_I8:
                // Long constant
                formatItem.Type = _module.ImportReference(typeof(long));
                bmw.WriteWarning("      Inferred type as System.Int64 from long constant");
                break;
                
            case Mono.Cecil.Cil.Code.Ldc_R4:
                // Float constant
                formatItem.Type = _module.ImportReference(typeof(float));
                bmw.WriteWarning("      Inferred type as System.Single from float constant");
                break;
                
            case Mono.Cecil.Cil.Code.Ldc_R8:
                // Double constant
                formatItem.Type = _module.ImportReference(typeof(double));
                bmw.WriteWarning("      Inferred type as System.Double from double constant");
                break;
                
            case Mono.Cecil.Cil.Code.Ldstr:
                // String constant
                formatItem.Type = _module.ImportReference(typeof(string));
                bmw.WriteWarning("      Inferred type as System.String from string constant");
                break;
        }
    }
    
    private void BuildSpecificFormatCall(List<Instruction> instructions, ILProcessor processor, InterpolationInfo info)
    {
        // Build the format string with placeholders
        var formatBuilder = new StringBuilder();
        int formatIndex = 0;
        
        // Interleave literals and format items
        for (int i = 0; i < info.Literals.Count; i++)
        {
            formatBuilder.Append(info.Literals[i]);
            
            if (i < info.FormatItems.Count)
            {
                var formatItem = info.FormatItems[i];
                formatBuilder.Append("{");
                formatBuilder.Append(formatIndex++);
                
                // Add format specifier if available
                if (!string.IsNullOrEmpty(formatItem.FormatClause))
                {
                    formatBuilder.Append(":");
                    formatBuilder.Append(formatItem.FormatClause);
                }
                
                formatBuilder.Append("}");
            }
        }
        
        var formatString = formatBuilder.ToString();
        bmw.WriteWarning($"    Built format string: '{formatString}'");
        
        try
        {
            // Load the format string
            instructions.Add(processor.Create(OpCodes.Ldstr, formatString));
            
            // Add the format items (arguments) - using the original instructions
            foreach (var formatItem in info.FormatItems)
            {
                try
                {
                    if (formatItem.LoadInstructions.Count == 0)
                    {
                        throw new Exception("Format item has no load instructions");
                    }
                    
                    // Copy the original value-loading instructions to preserve behavior
                    CopyInstructionSequence(instructions, processor, formatItem.LoadInstructions);
                    
                    // No boxing needed - String.Format accepts objects directly
                }
                catch (Exception ex)
                {
                    bmw.WriteWarning($"    ERROR adding format item: {ex.Message}");
                    throw;
                }
            }
            
            // Call the appropriate String.Format method
            MethodReference formatMethod = null;
            switch (info.FormatItems.Count)
            {
                case 1:
                    formatMethod = _stringFormat1;
                    break;
                case 2:
                    formatMethod = _stringFormat2;
                    break;
                case 3:
                    formatMethod = _stringFormat3;
                    break;
                default:
                    throw new InvalidOperationException($"Invalid number of format arguments: {info.FormatItems.Count}");
            }
            
            instructions.Add(processor.Create(OpCodes.Call, formatMethod));
            bmw.WriteWarning($"    Added call to String.Format with {info.FormatItems.Count} arguments");
        }
        catch (Exception ex)
        {
            bmw.WriteWarning($"    ERROR in BuildSpecificFormatCall: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }
    
    private void BuildArrayFormatCall(List<Instruction> instructions, ILProcessor processor, InterpolationInfo info)
    {
        // Build the format string with placeholders
        var formatBuilder = new StringBuilder();
        int formatIndex = 0;
        
        // Interleave literals and format items
        for (int i = 0; i < info.Literals.Count; i++)
        {
            formatBuilder.Append(info.Literals[i]);
            
            if (i < info.FormatItems.Count)
            {
                var formatItem = info.FormatItems[i];
                formatBuilder.Append("{");
                formatBuilder.Append(formatIndex++);
                
                // Add format specifier if available
                if (!string.IsNullOrEmpty(formatItem.FormatClause))
                {
                    formatBuilder.Append(":");
                    formatBuilder.Append(formatItem.FormatClause);
                }
                
                formatBuilder.Append("}"); 
            }
        }
        
        var formatString = formatBuilder.ToString();
        bmw.WriteWarning($"    Built format string: '{formatString}'");
        
        try
        {
            // Load the format string
            instructions.Add(processor.Create(OpCodes.Ldstr, formatString));
            
            // Create the object array
            instructions.Add(processor.Create(OpCodes.Ldc_I4, info.FormatItems.Count));
            var objectType = _module.ImportReference(typeof(object));
            instructions.Add(processor.Create(OpCodes.Newarr, objectType));
            
            // Fill the array with format items
            for (int i = 0; i < info.FormatItems.Count; i++)
            {
                var formatItem = info.FormatItems[i];
                
                try
                {
                    if (formatItem.LoadInstructions.Count == 0)
                    {
                        throw new Exception($"Format item {i} has no load instructions");
                    }
                    
                    // Duplicate the array reference for each item
                    instructions.Add(processor.Create(OpCodes.Dup));
                    
                    // Load the array index
                    instructions.Add(processor.Create(OpCodes.Ldc_I4, i));
                    
                    // Add the instructions that load the value
                    foreach (var loadInstr in formatItem.LoadInstructions)
                    {
                        instructions.Add(CopyInstruction(processor, loadInstr));
                        bmw.WriteWarning($"    Array item {i}: Added {loadInstr.OpCode}");
                    }
                    
                    // No boxing - let the runtime handle it
                    
                    // Store in the array as object reference
                    instructions.Add(processor.Create(OpCodes.Stelem_Ref));
                    bmw.WriteWarning($"    Array item {i}: Stored in array");
                }
                catch (Exception ex)
                {
                    throw new Exception($"ERROR adding array element {i}: {ex.Message}");
                    bmw.WriteError($"ERROR adding array element {i}: {ex.Message}");
                }
            }
            
            // Call String.Format with the array
            instructions.Add(processor.Create(OpCodes.Call, _stringFormatArray));
            bmw.WriteWarning($"    Added call to String.Format with object array of {info.FormatItems.Count} arguments");
        }
        catch (Exception ex)
        {
            bmw.WriteWarning($"    ERROR in BuildArrayFormatCall: {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    // Helper method to create a copy of an instruction
    private Instruction CopyInstruction(ILProcessor processor, Instruction source)
    {
        if (source == null)
            return null;
        
        try
        {
            switch (source.OpCode.OperandType)
            {
                case OperandType.InlineNone:
                    return processor.Create(source.OpCode);
                    
                case OperandType.InlineString:
                    return processor.Create(source.OpCode, (string)source.Operand);
                    
                case OperandType.InlineI:
                    return processor.Create(source.OpCode, (int)source.Operand);
                    
                case OperandType.InlineI8:
                    return processor.Create(source.OpCode, (long)source.Operand);
                    
                case OperandType.InlineR:
                case OperandType.ShortInlineR:
                    if (source.OpCode.Code == Code.Ldc_R4)
                        return processor.Create(source.OpCode, (float)source.Operand);
                    else
                        return processor.Create(source.OpCode, (double)source.Operand);
                    
                case OperandType.InlineField:
                    return processor.Create(source.OpCode, (FieldReference)source.Operand);
                    
                case OperandType.InlineMethod:
                    return processor.Create(source.OpCode, (MethodReference)source.Operand);
                    
                case OperandType.InlineType:
                    return processor.Create(source.OpCode, (TypeReference)source.Operand);
                    
                case OperandType.ShortInlineI:
                    if (source.Operand is byte b)
                        return processor.Create(source.OpCode, b);
                    else if (source.Operand is sbyte sb)
                        return processor.Create(source.OpCode, sb);
                    else
                        return processor.Create(source.OpCode, (int)source.Operand);
                    
                case OperandType.ShortInlineVar:
                case OperandType.InlineVar:
                    if (source.Operand is VariableDefinition vd)
                        return processor.Create(source.OpCode, vd);
                    else
                    {
                        int varIndex = source.Operand is byte byteIndex ? byteIndex : (int)source.Operand;
                        return processor.Create(source.OpCode, varIndex);
                    }
                    
                case OperandType.InlineArg:
                case OperandType.ShortInlineArg:
                    if (source.Operand is ParameterDefinition pd)
                        return processor.Create(source.OpCode, pd);
                    else
                    {
                        int paramIndex = source.Operand is byte byteIndex ? byteIndex : (int)source.Operand;
                        return processor.Create(source.OpCode, paramIndex);
                    }
                    
                case OperandType.InlineTok:
                    if (source.Operand is TypeReference tr)
                        return processor.Create(source.OpCode, tr);
                    else if (source.Operand is MethodReference mr)
                        return processor.Create(source.OpCode, mr);
                    else if (source.Operand is FieldReference fr)
                        return processor.Create(source.OpCode, fr);
                    else
                        throw new Exception($"Unsupported token type: {source.Operand?.GetType().Name ?? "null"}");
                    
                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineBrTarget:
                    // Branch instructions should be skipped for safety
                    throw new Exception($"Branch instructions cannot be copied: {source.OpCode}");
                    
                default:
                    throw new Exception($"Unsupported operand type: {source.OpCode.OperandType}");
            }
        }
        catch (Exception ex)
        {
            bmw.WriteWarning($"    Error copying instruction {source.OpCode}: {ex.Message}");
            return processor.Create(OpCodes.Ldnull); // Fallback
        }
    }

    // Helper to copy a sequence of instructions
    private void CopyInstructionSequence(List<Instruction> targetList, ILProcessor processor, List<Instruction> sourceInstructions)
    {
        if (sourceInstructions == null || sourceInstructions.Count == 0)
        {
            bmw.WriteWarning("    WARNING: No source instructions to copy, using default value");
            // Fallback to loading a simple default value
            targetList.Add(processor.Create(OpCodes.Ldnull));
            return;
        }
        
        foreach (var sourceInstr in sourceInstructions)
        {
            Instruction newInstr = null;
            
            try
            {
                // Create a copy of the instruction based on its opcode and operand
                switch (sourceInstr.OpCode.OperandType)
                {
                    case OperandType.InlineNone:
                        newInstr = processor.Create(sourceInstr.OpCode);
                        break;
                        
                    case OperandType.InlineString:
                        newInstr = processor.Create(sourceInstr.OpCode, (string)sourceInstr.Operand);
                        break;
                        
                    case OperandType.InlineI:
                        newInstr = processor.Create(sourceInstr.OpCode, (int)sourceInstr.Operand);
                        break;
                        
                    case OperandType.InlineI8:
                        newInstr = processor.Create(sourceInstr.OpCode, (long)sourceInstr.Operand);
                        break;
                        
                    case OperandType.InlineR:
                        if (sourceInstr.OpCode.Code == Code.Ldc_R4)
                            newInstr = processor.Create(sourceInstr.OpCode, (float)sourceInstr.Operand);
                        else
                            newInstr = processor.Create(sourceInstr.OpCode, (double)sourceInstr.Operand);
                        break;
                        
                    case OperandType.ShortInlineR:
                        // Handle ldc.r4 instruction
                        newInstr = processor.Create(sourceInstr.OpCode, (float)sourceInstr.Operand);
                        break;
                        
                    case OperandType.InlineField:
                        newInstr = processor.Create(sourceInstr.OpCode, (FieldReference)sourceInstr.Operand);
                        break;
                        
                    case OperandType.InlineMethod:
                        newInstr = processor.Create(sourceInstr.OpCode, (MethodReference)sourceInstr.Operand);
                        break;
                        
                    case OperandType.InlineType:
                        newInstr = processor.Create(sourceInstr.OpCode, (TypeReference)sourceInstr.Operand);
                        break;
                        
                    case OperandType.ShortInlineI:
                        if (sourceInstr.Operand is byte b)
                            newInstr = processor.Create(sourceInstr.OpCode, b);
                        else if (sourceInstr.Operand is sbyte sb)
                            newInstr = processor.Create(sourceInstr.OpCode, sb);
                        else
                            newInstr = processor.Create(sourceInstr.OpCode, (int)sourceInstr.Operand);
                        break;
                        
                    case OperandType.ShortInlineVar:
                    case OperandType.InlineVar:
                        if (sourceInstr.Operand is VariableDefinition vd)
                            newInstr = processor.Create(sourceInstr.OpCode, vd);
                        else 
                        {
                            int varIndex = sourceInstr.Operand is byte byteIndex ? byteIndex : (int)sourceInstr.Operand;
                            newInstr = processor.Create(sourceInstr.OpCode, varIndex);
                        }
                        break;
                        
                    case OperandType.InlineArg:
                    case OperandType.ShortInlineArg:
                        if (sourceInstr.Operand is ParameterDefinition pd)
                            newInstr = processor.Create(sourceInstr.OpCode, pd);
                        else
                        {
                            int paramIndex = sourceInstr.Operand is byte byteIndex ? byteIndex : (int)sourceInstr.Operand;
                            newInstr = processor.Create(sourceInstr.OpCode, paramIndex);
                        }
                        break;
                        
                    case OperandType.InlineTok:
                        if (sourceInstr.Operand is TypeReference tr)
                            newInstr = processor.Create(sourceInstr.OpCode, tr);
                        else if (sourceInstr.Operand is MethodReference mr)
                            newInstr = processor.Create(sourceInstr.OpCode, mr);
                        else if (sourceInstr.Operand is FieldReference fr)
                            newInstr = processor.Create(sourceInstr.OpCode, fr);
                        else
                        {
                            bmw.WriteWarning($"    WARNING: Unsupported token type: {sourceInstr.Operand?.GetType().Name ?? "null"}");
                            // Skip this instruction
                            continue;
                        }
                        break;
                        
                    case OperandType.InlineBrTarget:
                    case OperandType.ShortInlineBrTarget:
                        // Branch instructions are problematic to copy
                        bmw.WriteWarning($"    WARNING: Skipping branch instruction: {sourceInstr.OpCode}");
                        // Skip this instruction 
                        continue;
                        
                    default:
                        bmw.WriteWarning($"    WARNING: Unsupported operand type: {sourceInstr.OpCode.OperandType} for opcode {sourceInstr.OpCode}");
                        // Log detailed information about the instruction to help with debugging
                        if (sourceInstr.Operand != null)
                        {
                            bmw.WriteWarning($"    Operand type: {sourceInstr.Operand.GetType().Name}, Value: {sourceInstr.Operand}");
                        }
                        // Skip this instruction
                        continue;
                }
                
                if (newInstr != null)
                {
                    targetList.Add(newInstr);
                    bmw.WriteWarning($"    Copied instruction: {sourceInstr.OpCode}");
                }
            }
            catch (Exception ex)
            {
                bmw.WriteWarning($"    ERROR copying instruction {sourceInstr.OpCode}: {ex.Message}");
                // On error, use a simple null value as fallback
                targetList.Add(processor.Create(OpCodes.Ldnull));
                break;
            }
        }
    }

    // Helper method to dump all IL instructions for a method
    private void DumpMethodInstructions(MethodDefinition method)
    {
        if (!method.HasBody)
            return;
        
        bmw.WriteWarning($"IL dump for method {method.FullName}:");
        var instructions = method.Body.Instructions.ToList();
        
        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            string operandStr = "null";
            
            if (instr.Operand != null)
            {
                if (instr.Operand is MethodReference mr)
                    operandStr = $"{mr.Name} in {mr.DeclaringType?.FullName}";
                else if (instr.Operand is TypeReference tr)
                    operandStr = tr.FullName;
                else if (instr.Operand is FieldReference fr)
                    operandStr = $"{fr.Name} in {fr.DeclaringType?.FullName}";
                else
                    operandStr = instr.Operand.ToString();
            }
            
            bmw.WriteWarning($"[{i}] {instr.OpCode.Code} - {operandStr}");
        }
    }
} 