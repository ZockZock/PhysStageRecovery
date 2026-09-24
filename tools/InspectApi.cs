using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

class InspectApi
{
    static string directory;
    static int Main(string[] args)
    {
        directory = args[0];
        AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (s, e) => {
            string path = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.ReflectionOnlyLoadFrom(path) : Assembly.ReflectionOnlyLoad(e.Name);
        };
        Assembly a = Assembly.ReflectionOnlyLoadFrom(Path.Combine(directory, "Assembly-CSharp.dll"));
        foreach (string query in args.Skip(1))
        {
            string[] pieces = query.Split(':');
            Type t = a.GetType(pieces[0]);
            if (t == null) { Console.WriteLine("Missing " + query); continue; }
            Console.WriteLine("TYPE " + t.FullName + " : " + t.BaseType);
            foreach (var attribute in CustomAttributeData.GetCustomAttributes(t)) Console.WriteLine("ATTRIBUTE " + attribute);
            foreach (MemberInfo m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                if (pieces.Length == 1 || m.Name.IndexOf(pieces[1], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine(m.MemberType + " " + m);
                    if (m is MethodInfo) Console.WriteLine("  parameters: " + string.Join(", ", ((MethodInfo)m).GetParameters().Select(p => p.Name).ToArray()));
                    if (pieces.Length > 2 && m is MethodInfo) Calls((MethodInfo)m, pieces.Length > 3);
                }
        }
        return 0;
    }
    static void Calls(MethodInfo m, bool trace)
    {
        var body = m.GetMethodBody();
        if (body == null) return;
        byte[] bytes = body.GetILAsByteArray();
        var codes = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => unchecked((ushort)c.Value));
        int i = 0;
        while (i < bytes.Length)
        {
            ushort n = bytes[i++];
            if (n == 0xfe) n = (ushort)(0xfe00 | bytes[i++]);
            OpCode op = codes[n];
            int size = 0;
            switch(op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: size = 1; break;
                case OperandType.InlineVar: size = 2; break;
                case OperandType.InlineI8: case OperandType.InlineR: size = 8; break;
                case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(bytes, i); break;
                default: size = 4; break;
            }
            if (op.OperandType == OperandType.InlineMethod)
            {
                try { var target = m.Module.ResolveMethod(BitConverter.ToInt32(bytes, i)); Console.WriteLine("  " + op.Name + " " + target.DeclaringType + "." + target); }
                catch { }
            }
            else if (trace)
            {
                string operand = "";
                try
                {
                    if (op.OperandType == OperandType.InlineField) operand = m.Module.ResolveField(BitConverter.ToInt32(bytes, i)).ToString();
                    else if (op.OperandType == OperandType.InlineBrTarget) operand = "IL_" + (i + 4 + BitConverter.ToInt32(bytes, i)).ToString("x4");
                    else if (op.OperandType == OperandType.ShortInlineBrTarget) operand = "IL_" + (i + 1 + unchecked((sbyte)bytes[i])).ToString("x4");
                    else if (size > 0 && size <= 8) operand = BitConverter.ToString(bytes, i, size);
                }
                catch { }
                Console.WriteLine("  IL_" + (i - (n > 255 ? 2 : 1)).ToString("x4") + ": " + op.Name + " " + operand);
            }
            i += size;
        }
    }
}
