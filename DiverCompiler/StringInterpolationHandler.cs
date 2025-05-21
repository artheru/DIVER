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
        
        // Get the IL processor for the method
        var processor = method.Body.GetILProcessor();
        
        // First pass: find all DefaultInterpolatedStringHandler operations
        var instructions = method.Body.Instructions.ToList();
        int interpolationCount = 0;
        int replacedCount = 0;
        
        for (int i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            
            // Look for calls to DefaultInterpolatedStringHandler methods
            if (instruction.OpCode.Code == Mono.Cecil.Cil.Code.Call || 
                instruction.OpCode.Code == Mono.Cecil.Cil.Code.Callvirt)
            {
                if (instruction.Operand is MethodReference methodRef && 
                    methodRef.DeclaringType?.FullName?.Contains("DefaultInterpolatedStringHandler") == true)
                {
                    interpolationCount++;
                    bmw.WriteWarning($"Found DefaultInterpolatedStringHandler method call: {methodRef.Name} at index {i}");
                    
                    // Find the start of interpolation (constructor call) 
                    var startIndex = FindInterpolationStart(instructions, i);
                    bmw.WriteWarning($"  Start index: {startIndex}, instruction: {(startIndex >= 0 ? instructions[startIndex].ToString() : "not found")}");
                    
                    // Find the end of interpolation (ToStringAndClear call)
                    var endIndex = FindInterpolationEnd(instructions, i);
                    bmw.WriteWarning($"  End index: {endIndex}, instruction: {(endIndex >= 0 ? instructions[endIndex].ToString() : "not found")}");
                    
                    if (startIndex >= 0 && endIndex > i)
                    {
                        // Replace the interpolation with String.Format
                        bmw.WriteWarning($"  Replacing interpolation from index {startIndex} to {endIndex}");
                        ReplaceInterpolation(method, processor, instructions, startIndex, endIndex);
                        replacedCount++;
                        
                        // Reload instructions as they've changed
                        instructions = method.Body.Instructions.ToList();
                        
                        // Start over from the beginning of our replacement
                        i = startIndex;
                        bmw.WriteWarning($"  Continuing from index {i} after replacement");
                    }
                    else
                    {
                        bmw.WriteWarning($"  SKIPPED replacement - invalid start or end index");
                    }
                }
            }
        }
        
        bmw.WriteWarning($"Method {method.Name} processed. Found {interpolationCount} interpolations, replaced {replacedCount}");
    }

    private int FindInterpolationStart(List<Instruction> instructions, int currentIndex)
    {
        // Look for constructor call to DefaultInterpolatedStringHandler
        for (int i = currentIndex - 1; i >= 0; i--)
        {
            var instruction = instructions[i];
            if (instruction.OpCode.Code == Mono.Cecil.Cil.Code.Newobj && 
                instruction.Operand is MethodReference methodRef &&
                methodRef.DeclaringType?.FullName?.Contains("DefaultInterpolatedStringHandler") == true)
            {
                // Found the constructor call - this is our start
                return i;
            }
        }
        return -1;
    }

    private int FindInterpolationEnd(List<Instruction> instructions, int currentIndex)
    {
        // Look for ToStringAndClear call
        for (int i = currentIndex; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (instruction.OpCode.Code == Mono.Cecil.Cil.Code.Call && 
                instruction.Operand is MethodReference methodRef &&
                methodRef.Name == "ToStringAndClear" &&
                methodRef.DeclaringType?.FullName?.Contains("DefaultInterpolatedStringHandler") == true)
            {
                // Found the end - this is where we get the final string
                return i;
            }
        }
        return -1;
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
        
        if (interpolationInfo.FormatItems.Count == 0)
        {
            // If there are no format items, just replace with a simple string
            var formatString = string.Join("", interpolationInfo.Literals);
            var firstInstruction_i = instructions[startIndex];
            
            bmw.WriteWarning($"  Replacing with literal string: '{formatString}'");
            
            try {
                // Remove all the interpolation instructions
                for (int i = endIndex; i >= startIndex; i--)
                {
                    processor.Remove(instructions[i]);
                }
                
                // Just load the literal string
                processor.InsertBefore(firstInstruction_i, processor.Create(OpCodes.Ldstr, formatString));
                bmw.WriteWarning("  Successfully replaced with literal string");
            }
            catch (Exception ex) {
                bmw.WriteWarning($"  ERROR replacing with literal string: {ex.Message}");
            }
            return;
        }
        
        // Otherwise, build a String.Format call
        var firstInstruction = instructions[startIndex];
        
        try {
            // Remove all instructions related to interpolation
            for (int i = endIndex; i >= startIndex; i--)
            {
                processor.Remove(instructions[i]);
            }
            
            // Now create the String.Format call
            if (interpolationInfo.FormatItems.Count <= 3)
            {
                // Use the specific String.Format overloads for 1, 2, or 3 arguments
                bmw.WriteWarning($"  Building specific String.Format call with {interpolationInfo.FormatItems.Count} arguments");
                BuildSpecificFormatCall(processor, firstInstruction, interpolationInfo);
            }
            else
            {
                // Use the String.Format with object[] for more than 3 arguments
                bmw.WriteWarning($"  Building array String.Format call with {interpolationInfo.FormatItems.Count} arguments");
                BuildArrayFormatCall(processor, firstInstruction, interpolationInfo);
            }
            bmw.WriteWarning("  Successfully replaced with String.Format");
        }
        catch (Exception ex) {
            bmw.WriteWarning($"  ERROR replacing with String.Format: {ex.Message}\n{ex.StackTrace}");
        }
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
    }
    
    private InterpolationInfo AnalyzeInterpolation(List<Instruction> instructions, int startIndex, int endIndex)
    {
        var result = new InterpolationInfo();
        
        bmw.WriteWarning($"  Analyzing interpolation from index {startIndex} to {endIndex}");
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
                            }
                        }
                        else
                        {
                            bmw.WriteWarning($"      Could not find ldstr for AppendLiteral");
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
        
        // Make sure we have a literal after the last format item
        if (result.FormatItems.Count >= result.Literals.Count)
        {
            result.Literals.Add(string.Empty);
            bmw.WriteWarning("    Added empty literal at the end");
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
        
        var formatItem = new FormatItem();
        
        // Find the value to format (the last pushed value before the call)
        int valueInstructionIndex = -1;
        for (int i = formatCallIndex - 1; i >= 0; i--)
        {
            var instruction = instructions[i];
            if (instruction.OpCode.Code != Mono.Cecil.Cil.Code.Nop)
            {
                valueInstructionIndex = i;
                break;
            }
        }
        
        if (valueInstructionIndex >= 0)
        {
            formatItem.ValueInstruction = instructions[valueInstructionIndex];
            bmw.WriteWarning($"      Found value instruction at index {valueInstructionIndex}: {formatItem.ValueInstruction.OpCode}");
            
            // Try to determine the type
            // This is simplified - in a real implementation you'd need more detailed analysis
            if (methodRef.Parameters.Count > 0)
            {
                formatItem.Type = methodRef.Parameters[0].ParameterType;
                bmw.WriteWarning($"      Extracted type from method parameters: {formatItem.Type.FullName}");
            }
        }
        else
        {
            bmw.WriteWarning("      Could not find value instruction");
        }
        
        // Extract format string if available
        var formatStringIndex = FindPreviousLdstr(instructions, formatCallIndex);
        if (formatStringIndex >= 0 && formatStringIndex > valueInstructionIndex)
        {
            formatItem.FormatClause = instructions[formatStringIndex].Operand as string;
            bmw.WriteWarning($"      Found format clause: '{formatItem.FormatClause}'");
        }
        
        return formatItem;
    }
    
    private void BuildSpecificFormatCall(ILProcessor processor, Instruction insertPoint, InterpolationInfo info)
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
        
        // Add any remaining literals
        if (info.Literals.Count > info.FormatItems.Count)
        {
            for (int i = info.FormatItems.Count; i < info.Literals.Count; i++)
            {
                formatBuilder.Append(info.Literals[i]);
            }
        }
        
        var formatString = formatBuilder.ToString();
        bmw.WriteWarning($"    Built format string: '{formatString}'");
        
        // Load the format string
        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldstr, formatString));
        
        // Add the format items (arguments)
        foreach (var formatItem in info.FormatItems)
        {
            // Clone the instruction that loads the value
            var clonedInstruction = CloneInstruction(processor, formatItem.ValueInstruction);
            processor.InsertBefore(insertPoint, clonedInstruction);
            bmw.WriteWarning($"    Added value instruction: {clonedInstruction.OpCode}");
            
            // If needed, box value types
            if (formatItem.Type != null && formatItem.Type.IsValueType)
            {
                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Box, formatItem.Type));
                bmw.WriteWarning($"    Added boxing for type: {formatItem.Type.FullName}");
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
        
        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Call, formatMethod));
        bmw.WriteWarning($"    Added call to String.Format with {info.FormatItems.Count} arguments");
    }
    
    private Instruction CloneInstruction(ILProcessor processor, Instruction original)
    {
        // Create a clone of an instruction
        switch (original.OpCode.OperandType)
        {
            case OperandType.InlineNone:
                return processor.Create(original.OpCode);
            case OperandType.InlineI:
                return processor.Create(original.OpCode, (int)original.Operand);
            case OperandType.InlineI8:
                return processor.Create(original.OpCode, (long)original.Operand);
            case OperandType.InlineR:
                return processor.Create(original.OpCode, (double)original.Operand);
            case OperandType.InlineString:
                return processor.Create(original.OpCode, (string)original.Operand);
            case OperandType.InlineType:
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineTok:
                return processor.Create(original.OpCode, (string)original.Operand);
            default:
                bmw.WriteWarning($"WARNING: Unsupported instruction operand type {original.OpCode.OperandType}");
                return processor.Create(original.OpCode);
        }
    }
    
    private void BuildArrayFormatCall(ILProcessor processor, Instruction insertPoint, InterpolationInfo info)
    {
        // Similar to BuildSpecificFormatCall, but use object array
        
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
        
        // Add any remaining literals
        if (info.Literals.Count > info.FormatItems.Count)
        {
            for (int i = info.FormatItems.Count; i < info.Literals.Count; i++)
            {
                formatBuilder.Append(info.Literals[i]);
            }
        }
        
        var formatString = formatBuilder.ToString();
        bmw.WriteWarning($"    Built format string: '{formatString}'");
        
        // Load the format string
        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldstr, formatString));
        
        // Create the object array
        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldc_I4, info.FormatItems.Count));
        var objectType = _module.ImportReference(typeof(object));
        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Newarr, objectType));
        
        // Fill the array with arguments
        for (int i = 0; i < info.FormatItems.Count; i++)
        {
            var formatItem = info.FormatItems[i];
            
            // Duplicate the array reference
            processor.InsertBefore(insertPoint, processor.Create(OpCodes.Dup));
            
            // Load the array index
            processor.InsertBefore(insertPoint, processor.Create(OpCodes.Ldc_I4, i));
            
            // Clone the instruction that loads the value
            var clonedInstruction = CloneInstruction(processor, formatItem.ValueInstruction);
            processor.InsertBefore(insertPoint, clonedInstruction);
            bmw.WriteWarning($"    Added array element {i} value instruction: {clonedInstruction.OpCode}");
            
            // If needed, box value types
            if (formatItem.Type != null && formatItem.Type.IsValueType)
            {
                processor.InsertBefore(insertPoint, processor.Create(OpCodes.Box, formatItem.Type));
                bmw.WriteWarning($"    Added boxing for type: {formatItem.Type.FullName}");
            }
            
            // Store in the array
            processor.InsertBefore(insertPoint, processor.Create(OpCodes.Stelem_Ref));
        }
        
        // Call String.Format with the array
        processor.InsertBefore(insertPoint, processor.Create(OpCodes.Call, _stringFormatArray));
        bmw.WriteWarning($"    Added call to String.Format with object array of {info.FormatItems.Count} arguments");
    }
} 