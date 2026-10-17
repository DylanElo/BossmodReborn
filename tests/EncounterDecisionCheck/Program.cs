using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class Program
{
    private static int Main(string[] args)
    {
        var dependencies = Environment.GetEnvironmentVariable("BMR_DEPENDENCIES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "addon", "Hooks", "dev");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(dependencies, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        try { return Run(); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run() => EncounterChecks.Run();
}
