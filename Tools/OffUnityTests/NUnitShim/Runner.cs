// Tiny reflection test runner for the NUnit shim. Usage: dotnet run -- [filter]
// Exit code = number of failed tests (0 = all passed).
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

public static class OffUnityTestRunner
{
    public static int Main(string[] args)
    {
        string filter = args.FirstOrDefault() ?? "";
        int pass = 0, fail = 0, skip = 0;
        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetMethods().Any(m => m.GetCustomAttributes<TestAttribute>().Any() || m.GetCustomAttributes<TestCaseAttribute>().Any()))
            .OrderBy(t => t.FullName);
        foreach (var t in types)
        {
            var setUp = t.GetMethods().Where(m => m.GetCustomAttribute<SetUpAttribute>() != null).ToList();
            var tearDown = t.GetMethods().Where(m => m.GetCustomAttribute<TearDownAttribute>() != null).ToList();
            foreach (var m in t.GetMethods().OrderBy(m => m.MetadataToken))
            {
                var cases = m.GetCustomAttributes<TestCaseAttribute>().Select(c => c.Args).ToList();
                if (m.GetCustomAttribute<TestAttribute>() != null && cases.Count == 0) cases.Add(null);
                foreach (var args0 in cases)
                {
                    string name = $"{t.Name}.{m.Name}" + (args0 == null ? "" : "(" + string.Join(", ", args0.Select(a => a is string s ? "\"" + Trunc(s) + "\"" : a?.ToString() ?? "null")) + ")");
                    if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                    if (m.GetCustomAttribute<IgnoreAttribute>() != null || t.GetCustomAttribute<IgnoreAttribute>() != null) { skip++; Console.WriteLine("SKIP " + name); continue; }
                    try
                    {
                        var inst = Activator.CreateInstance(t);
                        foreach (var s in setUp) s.Invoke(inst, null);
                        try
                        {
                            var callArgs = args0 == null ? null : m.GetParameters().Select((p, i) => i < args0.Length ? Conv(args0[i], p.ParameterType) : p.DefaultValue).ToArray();
                            var r = m.Invoke(inst, callArgs);
                            if (r is System.Threading.Tasks.Task task) task.GetAwaiter().GetResult();
                        }
                        finally { foreach (var td in tearDown) td.Invoke(inst, null); }
                        pass++; Console.WriteLine("PASS " + name);
                    }
                    catch (Exception ex)
                    {
                        var e = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                        fail++; Console.WriteLine("FAIL " + name + "\n     " + e.GetType().Name + ": " + e.Message.Replace("\n", "\n     "));
                        if (!(e is AssertionException)) Console.WriteLine("     " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim());
                    }
                }
            }
        }
        Console.WriteLine($"\n{pass} passed, {fail} failed, {skip} skipped");
        return fail;
    }
    static string Trunc(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    static object Conv(object v, Type t) => v == null || t.IsInstanceOfType(v) ? v : t.IsEnum ? Enum.ToObject(t, v) : Convert.ChangeType(v, t);
}
