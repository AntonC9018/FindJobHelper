namespace FindJobHelper.Core;

public interface IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create();
}

public sealed record ExperienceDatabaseProviderResult
{
    public ExperienceDatabaseProviderResult(
        TagsDatabase tagsDatabase,
        ExperienceDatabase experienceDatabase)
    {
        TagsDatabase = tagsDatabase
            ?? throw new ArgumentNullException(nameof(tagsDatabase));
        ExperienceDatabase = experienceDatabase
            ?? throw new ArgumentNullException(nameof(experienceDatabase));
    }

    public TagsDatabase TagsDatabase { get; }

    public ExperienceDatabase ExperienceDatabase { get; }
}
