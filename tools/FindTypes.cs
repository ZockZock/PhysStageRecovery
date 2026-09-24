using System;
using System.IO;
using System.Linq;
using System.Reflection;

class FindTypes
{
    static int Main(string[] args)
    {
        string directory = args[0];
        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (s, e) => {
            string path = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.ReflectionOnlyLoadFrom(path) : Assembly.ReflectionOnlyLoad(e.Name);
        };
        Assembly a = Assembly.ReflectionOnlyLoadFrom(Path.Combine(directory, "Assembly-CSharp.dll"));
        Type[] types;
        try { types = a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
        foreach (string pattern in args.Skip(1))
        {
            Console.WriteLine("=== " + pattern + " ===");
            foreach (Type t in types.Where(t => t.Name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(t => t.Name))
            {
                Console.WriteLine("TYPE " + t.FullName + " : " + t.BaseType);
                foreach (MemberInfo m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m is MethodInfo)
                    {
                        MethodInfo mi = (MethodInfo)m;
                        if (mi.IsSpecialName) continue;
                        Console.WriteLine("   M " + mi.ReturnType.Name + " " + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray()) + ")");
                    }
                    else if (m is FieldInfo)
                    {
                        FieldInfo fi = (FieldInfo)m;
                        Console.WriteLine("   F " + fi.FieldType.Name + " " + fi.Name);
                    }
                    else if (m is PropertyInfo)
                    {
                        PropertyInfo pi = (PropertyInfo)m;
                        Console.WriteLine("   P " + pi.PropertyType.Name + " " + pi.Name);
                    }
                }
            }
        }
        return 0;
    }
}
