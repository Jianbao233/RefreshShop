using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class Bootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Some game dependencies are referenced only by the game's module initializer,
        // before Runner._Ready can execute. Historical SDKs omit those shared DLLs.
        var scriptContext = AssemblyLoadContext.GetLoadContext(typeof(Bootstrap).Assembly);
        scriptContext.Resolving += (context, name) =>
        {
            var directory = Environment.GetEnvironmentVariable("REFRESHSHOP_TEST_BIN")
                ?? Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location);
            if (string.IsNullOrEmpty(directory)) return null; // Godot may load scripts from bytes.
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }
}
