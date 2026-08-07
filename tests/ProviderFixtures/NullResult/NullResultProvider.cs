using FindJobHelper.Core;

namespace ProviderFixtures.NullResult;

public sealed class NullResultProvider : IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create() => null!;
}
