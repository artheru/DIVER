using System;
using System.Linq;
using Mono.Cecil;

namespace MCURoutineCompiler;

internal partial class Processor
{
    private const string DefaultInterpolatedStringHandlerFullName = "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler";

    private void EnsureDefaultInterpolatedStringHandlerSupport(ModuleDefinition module)
    {
        if (SI.defaultInterpolatedStringHandlerPrepared)
        {
            return;
        }

        var handlerType = new TypeReference("System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler", module, module.TypeSystem.CoreLibrary);
        handlerType = module.ImportReference(handlerType);

        var int32Type = module.TypeSystem.Int32;
        var handlerFields = new shared_info.class_fields
        {
            tr = handlerType,
            size = tMap.vInt32.size + 1,
            baseInitialized = true
        };
        handlerFields.field_offset["__ctx"] = (0, int32Type, tMap.vInt32.typeid, handlerType);
        SI.class_ifield_offset[handlerType.FullName] = handlerFields;

        var keysToRemove = SI.referenced_typefield.Keys
            .Where(k => k.StartsWith(handlerType.FullName + "::", StringComparison.Ordinal))
            .ToArray();
        foreach (var key in keysToRemove)
        {
            SI.referenced_typefield.Remove(key);
        }

        SI.defaultInterpolatedStringHandlerPrepared = true;
    }
}

