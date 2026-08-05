using FindJobHelper.Core;

namespace ProviderFixtures.CreateThrows;

public sealed class ThrowingCreateProvider : IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create() =>
        throw new InvalidOperationException("create failure");
}
