using FindJobHelper.Core;

namespace ProviderFixtures.ConstructorThrows;

public sealed class ThrowingConstructorProvider : IExperienceDatabaseProvider
{
    public ThrowingConstructorProvider() =>
        throw new InvalidOperationException("constructor failure");

    public ExperienceDatabaseProviderResult Create() =>
        throw new InvalidOperationException("unreachable");
}
