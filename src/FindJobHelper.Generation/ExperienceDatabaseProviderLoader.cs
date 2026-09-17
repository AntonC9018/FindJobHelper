using System.Reflection;
using System.Runtime.Loader;
using System.Security;
using FindJobHelper.Core;

namespace FindJobHelper.Generation;

public static class ExperienceDatabaseProviderLoader
{
    public static LoadedExperienceDatabaseProvider Load(string path)
    {
        string fullPath;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            fullPath = Path.GetFullPath(path, Environment.CurrentDirectory);
        }
        catch (Exception ex) when (
            ex is ArgumentException
                or IOException
                or NotSupportedException
                or SecurityException
                or UnauthorizedAccessException)
        {
            throw new ExperienceDatabaseProviderLoadException(
                "The experience database DLL path is invalid.",
                ex);
        }

        if (!string.Equals(
                Path.GetExtension(fullPath),
                ".dll",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database path must have a .dll extension: '{fullPath}'.");
        }

        if (!File.Exists(fullPath))
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL was not found: '{fullPath}'.");
        }

        ExperienceDatabaseAssemblyLoadContext? loadContext = null;
        Assembly assembly;
        try
        {
            loadContext = new(fullPath);
            assembly = loadContext.LoadFromAssemblyPath(fullPath);
        }
        catch (BadImageFormatException ex)
        {
            loadContext?.Unload();
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL is not a valid .NET assembly: '{fullPath}'.",
                ex);
        }
        catch (FileNotFoundException ex)
        {
            loadContext?.Unload();
            throw new ExperienceDatabaseProviderLoadException(
                $"A dependency required by experience database DLL '{fullPath}' could not be loaded: {ex.Message}",
                ex);
        }
        catch (FileLoadException ex)
        {
            loadContext?.Unload();
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL '{fullPath}' could not be loaded: {ex.Message}",
                ex);
        }
        catch (Exception ex) when (ex is NotSupportedException or SecurityException)
        {
            loadContext?.Unload();
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL '{fullPath}' could not be loaded: {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            loadContext?.Unload();
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL '{fullPath}' could not be loaded: {ex.Message}",
                ex);
        }

        try
        {
            return CreateLoadedProvider(fullPath, assembly, loadContext);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }

    private static LoadedExperienceDatabaseProvider CreateLoadedProvider(
        string fullPath,
        Assembly assembly,
        AssemblyLoadContext loadContext)
    {
        Type[] exportedTypes;
        try
        {
            exportedTypes = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var dependencyException = ex.LoaderExceptions
                .FirstOrDefault(static exception =>
                    exception is FileNotFoundException or FileLoadException);
            if (dependencyException is not null)
            {
                throw new ExperienceDatabaseProviderLoadException(
                    $"A dependency required by experience database DLL '{fullPath}' could not be loaded: {dependencyException.Message}",
                    ex);
            }

            var loaderMessage = ex.LoaderExceptions
                .FirstOrDefault(static exception => exception is not null)?.Message;
            var messageSuffix = loaderMessage is null ? "." : $": {loaderMessage}";
            var message =
                $"Types in experience database DLL '{fullPath}' could not be inspected"
                + messageSuffix;
            throw new ExperienceDatabaseProviderLoadException(
                message,
                ex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"A dependency required by experience database DLL '{fullPath}' could not be loaded: {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Types in experience database DLL '{fullPath}' could not be inspected: {ex.Message}",
                ex);
        }

        var providerTypes = exportedTypes
            .Where(static type =>
            {
                if (type is not { IsClass: true, IsAbstract: false })
                {
                    return false;
                }

                return typeof(IExperienceDatabaseProvider).IsAssignableFrom(type);
            })
            .ToArray();
        if (providerTypes.Length == 0)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL '{fullPath}' contains no exported concrete implementation of {nameof(IExperienceDatabaseProvider)}.");
        }

        if (providerTypes.Length > 1)
        {
            var names = string.Join(
                ", ",
                providerTypes.Select(static type => type.FullName).Order());
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL '{fullPath}' contains multiple provider implementations: {names}.");
        }

        var providerType = providerTypes[0];
        ConstructorInfo? constructor;
        try
        {
            constructor = providerType.GetConstructor(Type.EmptyTypes);
        }
        catch (Exception ex)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database provider '{providerType.FullName}' could not be inspected: {ex.Message}",
                ex);
        }
        if (constructor is null)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database provider '{providerType.FullName}' must have a public parameterless constructor.");
        }

        IExperienceDatabaseProvider provider;
        try
        {
            provider = (IExperienceDatabaseProvider)constructor.Invoke(null);
        }
        catch (Exception ex)
        {
            var cause = UnwrapInvocationException(ex);
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database provider '{providerType.FullName}' could not be constructed: {cause.Message}",
                cause);
        }

        try
        {
            var result = provider.Create()
                ?? throw new ExperienceDatabaseProviderLoadException(
                    $"Experience database provider '{providerType.FullName}' returned a null result.");
            return new(result, assembly, loadContext);
        }
        catch (ExperienceDatabaseProviderLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var cause = UnwrapInvocationException(ex);
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database provider '{providerType.FullName}' failed while creating the databases: {cause.Message}",
                cause);
        }
    }

    private static Exception UnwrapInvocationException(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } inner }
            ? inner
            : exception;
}

public sealed class LoadedExperienceDatabaseProvider : IDisposable
{
    private AssemblyLoadContext? _loadContext;

    public LoadedExperienceDatabaseProvider(
        ExperienceDatabaseProviderResult result,
        Assembly assembly)
    {
        Result = result;
        Assembly = assembly;
    }

    internal LoadedExperienceDatabaseProvider(
        ExperienceDatabaseProviderResult result,
        Assembly assembly,
        AssemblyLoadContext loadContext)
        : this(result, assembly)
    {
        _loadContext = loadContext;
    }

    public ExperienceDatabaseProviderResult Result { get; }

    public Assembly Assembly { get; }

    public void Dispose()
    {
        var loadContext = Interlocked.Exchange(ref _loadContext, null);
        loadContext?.Unload();
    }
}

public sealed class ExperienceDatabaseProviderLoadException : Exception
{
    public ExperienceDatabaseProviderLoadException(string message)
        : base(message)
    {
    }

    public ExperienceDatabaseProviderLoadException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
