using FindJobHelper.Core;

namespace ProviderFixtures.ReloadableProvider;

public sealed class ExperienceDatabaseProvider : IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create()
    {
        var tagsBuilder = new TagsDatabaseBuilder();
        tagsBuilder.Tag("second");
        var tagsDatabase = tagsBuilder.Build().GetResultOrThrow();
        var experienceDatabase = new ExperienceDatabaseBuilder().Build();
        return new(tagsDatabase, experienceDatabase);
    }
}
