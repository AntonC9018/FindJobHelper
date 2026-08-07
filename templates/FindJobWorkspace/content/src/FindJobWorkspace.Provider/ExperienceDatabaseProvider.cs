using FindJobHelper.Core;

namespace FindJobWorkspace.Provider;

public sealed class ExperienceDatabaseProvider : IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create()
    {
        var (dotnet, database) = TagDatabaseFactory.Create();
        return new(database, ExperienceDatabaseFactory.Create(dotnet));
    }
}
