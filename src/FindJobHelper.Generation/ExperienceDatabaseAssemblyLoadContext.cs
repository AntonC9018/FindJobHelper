using System.Reflection;
using System.Runtime.Loader;
using FindJobHelper.Configuration;
using FindJobHelper.Core;

namespace FindJobHelper.Generation;

internal sealed class ExperienceDatabaseAssemblyLoadContext : AssemblyLoadContext
{
    private static readonly IReadOnlyDictionary<string, Assembly> SharedAssemblies =
        CreateSharedAssemblies();

    private readonly AssemblyDependencyResolver _resolver;

    public ExperienceDatabaseAssemblyLoadContext(string mainAssemblyPath)
        : base(isCollectible: true)
    {
        _resolver = new(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var simpleName = assemblyName.Name;
        if (simpleName is null)
        {
            return null;
        }

        if (SharedAssemblies.TryGetValue(simpleName, out var sharedAssembly))
        {
            return sharedAssembly;
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath is null)
        {
            return null;
        }

        return LoadFromAssemblyPath(assemblyPath);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath is null)
        {
            return nint.Zero;
        }

        return LoadUnmanagedDllFromPath(libraryPath);
    }

    private static IReadOnlyDictionary<string, Assembly> CreateSharedAssemblies()
    {
        Assembly[] assemblies =
        [
            typeof(IExperienceDatabaseProvider).Assembly,
            typeof(CvSelectionConfiguration).Assembly,
        ];
        var result = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            var simpleName = assembly.GetName().Name;
            if (simpleName is null)
            {
                throw new InvalidOperationException(
                    $"Shared assembly '{assembly.FullName}' has no simple name.");
            }

            result.Add(simpleName, assembly);
        }

        return result;
    }
}
