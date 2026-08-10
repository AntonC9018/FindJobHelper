using FindJobHelper.Core;
using ProviderFixtures.SyntheticProvider;
using ProviderFixtures.ConstructorThrows;
using ProviderFixtures.CreateThrows;
using ProviderFixtures.MultipleProviders;
using ProviderFixtures.NoConstructor;
using ProviderFixtures.NullResult;

namespace MainCli.Tests;

public sealed class ExperienceDatabaseProviderLoaderTests
{
    [Fact]
    public void Load_AcceptsAbsoluteSyntheticProviderDllPath()
    {
        var result = ExperienceDatabaseProviderLoader.Load(SyntheticProviderDllPath);

        Assert.NotEmpty(result.Result.TagsDatabase.TagsGraph);
        Assert.NotEmpty(result.Result.ExperienceDatabase.Experiences);
        Assert.Equal(typeof(ExperienceDatabaseProvider).Assembly, result.Assembly);
    }

    [Fact]
    public void Load_ResolvesRelativePathAgainstCurrentDirectory()
    {
        var relativePath = Path.GetRelativePath(Environment.CurrentDirectory, SyntheticProviderDllPath);

        var result = ExperienceDatabaseProviderLoader.Load(relativePath);

        Assert.NotEmpty(result.Result.ExperienceDatabase.Experiences);
    }

    [Fact]
    public void Load_RejectsMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll");

        var exception = Assert.Throws<ExperienceDatabaseProviderLoadException>(
            () => ExperienceDatabaseProviderLoader.Load(path));

        Assert.Contains("was not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsWrongExtension()
    {
        var exception = Assert.Throws<ExperienceDatabaseProviderLoadException>(
            () => ExperienceDatabaseProviderLoader.Load("provider.csproj"));

        Assert.Contains(".dll extension", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsInvalidBinary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"invalid-{Guid.NewGuid():N}.dll");
        File.WriteAllText(path, "not an assembly");
        try
        {
            var exception = Assert.Throws<ExperienceDatabaseProviderLoadException>(
                () => ExperienceDatabaseProviderLoader.Load(path));

            Assert.Contains("not a valid .NET assembly", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RejectsAssemblyWithoutProvider()
    {
        var exception = Assert.Throws<ExperienceDatabaseProviderLoadException>(
            () => ExperienceDatabaseProviderLoader.Load(typeof(Tag).Assembly.Location));

        Assert.Contains("contains no", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsMultipleProviders()
    {
        var exception = Assert.Throws<ExperienceDatabaseProviderLoadException>(
            () => ExperienceDatabaseProviderLoader.Load(
                typeof(FirstProvider).Assembly.Location));

        Assert.Contains("multiple provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsProviderWithoutPublicParameterlessConstructor()
    {
        var exception = Assert.Throws<ExperienceDatabaseProviderLoadException>(
            () => ExperienceDatabaseProviderLoader.Load(
                typeof(ProviderWithoutDefaultConstructor).Assembly.Location));

        Assert.Contains("public parameterless constructor", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WrapsConstructorFailure()
    {
        var exception = Assert.Throws<ExperienceDatabaseProviderLoadException>(
            () => ExperienceDatabaseProviderLoader.Load(
                typeof(ThrowingConstructorProvider).Assembly.Location));

        Assert.Contains("could not be constructed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("constructor failure", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_WrapsCreateFailure()
    {
        var exception = Assert.Throws<ExperienceDatabaseProviderLoadException>(
            () => ExperienceDatabaseProviderLoader.Load(
                typeof(ThrowingCreateProvider).Assembly.Location));

        Assert.Contains("failed while creating", exception.Message, StringComparison.Ordinal);
        Assert.Contains("create failure", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsNullProviderResult()
    {
        var exception = Assert.Throws<ExperienceDatabaseProviderLoadException>(
            () => ExperienceDatabaseProviderLoader.Load(
                typeof(NullResultProvider).Assembly.Location));

        Assert.Contains("returned a null result", exception.Message, StringComparison.Ordinal);
    }

    private static string SyntheticProviderDllPath =>
        typeof(ExperienceDatabaseProvider).Assembly.Location;
}
