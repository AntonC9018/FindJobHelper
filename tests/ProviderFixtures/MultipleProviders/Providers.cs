using FindJobHelper.Core;

namespace ProviderFixtures.MultipleProviders;

public sealed class FirstProvider : IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create() =>
        throw new InvalidOperationException("unreachable");
}

public sealed class SecondProvider : IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create() =>
        throw new InvalidOperationException("unreachable");
}
