using System;
using System.Collections.Generic;

namespace Coralinker_arch;

public static class Extensions
{
    public static bool IsSubclassOfRawGeneric(this Type toCheck, Type generic, out Type gType)
    {
        while (toCheck != null && toCheck != typeof(object))
        {
            var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
            if (generic == cur)
            {
                gType = toCheck;
                return true;
            }
            toCheck = toCheck.BaseType;
        }

        gType = null;
        return false;
    }
}

public sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
{
    public static ReferenceEqualityComparer<T> Instance { get; } = new ReferenceEqualityComparer<T>();
    private ReferenceEqualityComparer() {}
    public bool Equals(T x, T y) => ReferenceEquals(x, y);
    public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
