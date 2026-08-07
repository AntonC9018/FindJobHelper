using FindJobHelper.Core;

namespace ProviderFixtures.SyntheticProvider;

public sealed class ExperienceDatabaseProvider : IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create()
    {
        var (tags, tagsDatabase) = TagsDatabaseFactory.Create();
        return new(tagsDatabase, ExperienceDatabaseFactory.Create(tags));
    }
}
