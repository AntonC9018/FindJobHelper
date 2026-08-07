using FindJobHelper.Core;

namespace ProviderFixtures.SyntheticProvider;

public sealed record SyntheticTags(Tag DotNet, Tag Testing, Tag Documentation);

public static class TagsDatabaseFactory
{
    public static (SyntheticTags Tags, TagsDatabase TagsDatabase) Create()
    {
        var builder = new TagsDatabaseBuilder();
        var dotnet = builder.Tag(".NET");
        var testing = builder.Tag("Testing");
        var documentation = builder.Tag("Documentation");
        testing.IsIncludedIn(dotnet).By(0.25f).WhichIsIncludedInIt().By(0.1f);
        documentation.IsIncludedIn(dotnet).By(0.1f).WhichIsIncludedInIt().By(0.05f);

        var result = builder.Build();
        if (result.Errors is not null)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
        }

        return (
            new(new(dotnet.Name), new(testing.Name), new(documentation.Name)),
            result.Database!);
    }
}
