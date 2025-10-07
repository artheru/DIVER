namespace DiverTest.DIVER.CoralinkerAdaption
{

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

        static string JoinWrapper<T>(string a, IEnumerable<T> b)
        {
            return string.Join(a, b);
        }

        public static string DepackObject(this object obj) =>
            string.Join("; ", obj.GetType().GetFields().Select(p =>
            {
                Func<string> func;
                if (p.FieldType
                    .GetInterfaces()
                    .Any(t => t.IsGenericType
                              && t.GetGenericTypeDefinition() == typeof(ICollection<>)))
                    func = new Func<string>(() =>
                    {
                        try
                        {
                            dynamic ls = p?.GetValue(obj);
                            //var arr = ls.GetType().GetMethod("ToArray").Invoke(ls,null);
                            return JoinWrapper(",", ls);
                        }
                        catch
                        {
                            return "";
                        }

                        ;
                    });
                else if (p.FieldType == typeof(DateTime))
                    func = () => ((DateTime)p?.GetValue(obj)).ToString("HH:mm:ss.fff");
                else
                    func = () => p?.GetValue(obj)?.ToString();

                // if (IEnumerable<T>)
                // var valstr=
                return $"{p.Name}={func()}";
            }));

        public static void Deconstruct<T>(this IList<T> list, out T first, out IList<T> rest)
        {
            first = list.Count > 0 ? list[0] : default(T); // or throw
            rest = list.Skip(1).ToList();
        }

        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out IList<T> rest)
        {
            first = list.Count > 0 ? list[0] : default(T); // or throw
            second = list.Count > 1 ? list[1] : default(T); // or throw
            rest = list.Skip(2).ToList();
        }

        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out T third, out IList<T> rest)
        {
            first = list.Count > 0 ? list[0] : default(T); // or throw
            second = list.Count > 1 ? list[1] : default(T); // or throw
            third = list.Count > 2 ? list[2] : default(T); // or throw
            rest = list.Skip(3).ToList();
        }

    }
}