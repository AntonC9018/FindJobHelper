using System.Reflection;
using System.Runtime.Loader;
using System.Security;
using FindJobHelper.Core;

namespace MainCli;

internal static class ExperienceDatabaseProviderLoader
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

        Assembly assembly;
        try
        {
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
        }
        catch (BadImageFormatException ex)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL is not a valid .NET assembly: '{fullPath}'.",
                ex);
        }
        catch (FileNotFoundException ex)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"A dependency required by experience database DLL '{fullPath}' could not be loaded: {ex.Message}",
                ex);
        }
        catch (FileLoadException ex)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL '{fullPath}' could not be loaded: {ex.Message}",
                ex);
        }
        catch (Exception ex) when (ex is NotSupportedException or SecurityException)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL '{fullPath}' could not be loaded: {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            throw new ExperienceDatabaseProviderLoadException(
                $"Experience database DLL '{fullPath}' could not be loaded: {ex.Message}",
                ex);
        }

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
            throw new ExperienceDatabaseProviderLoadException(
                $"Types in experience database DLL '{fullPath}' could not be inspected"
                + (loaderMessage is null ? "." : $": {loaderMessage}"),
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
                type is { IsClass: true, IsAbstract: false }
                && typeof(IExperienceDatabaseProvider).IsAssignableFrom(type))
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
            return new(result, assembly);
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

internal sealed record LoadedExperienceDatabaseProvider(
    ExperienceDatabaseProviderResult Result,
    Assembly Assembly);

internal sealed class ExperienceDatabaseProviderLoadException : Exception
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
