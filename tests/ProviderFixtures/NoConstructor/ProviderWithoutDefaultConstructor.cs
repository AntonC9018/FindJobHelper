using FindJobHelper.Core;

namespace ProviderFixtures.NoConstructor;

public sealed class ProviderWithoutDefaultConstructor(string value)
    : IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create() =>
        throw new InvalidOperationException(value);
}
