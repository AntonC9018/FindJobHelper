using FindJobHelper.Core;
using FindJobHelper.UserSecrets;

namespace ProviderFixtures.SyntheticProvider;

public sealed class ExperienceDatabaseProvider : UserSecretsIdProviderBase, IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create()
    {
        var (tags, tagsDatabase) = TagsDatabaseFactory.Create();
        return new(tagsDatabase, ExperienceDatabaseFactory.Create(tags));
    }
}
