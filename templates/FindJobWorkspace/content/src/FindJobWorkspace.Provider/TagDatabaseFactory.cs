using FindJobHelper.Core;

namespace FindJobWorkspace.Provider;

internal static class TagDatabaseFactory
{
    internal static (Tag DotNet, TagsDatabase Database) Create()
    {
        var builder = new TagsDatabaseBuilder();
        var dotnet = builder.Tag(".NET");
        var result = builder.Build();
        if (result.Errors is not null)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
        }
        return (new(dotnet.Name), result.Database!);
    }
}
