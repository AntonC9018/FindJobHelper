using FindJobHelper.Core;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobWorkspace.Provider;

using static RichTextFactory;

public static class Tags
{
    public static Tag DotNet => new(".NET");
    public static Tag Microservices => new("microservices");
}

public sealed class ExperienceDatabaseProvider : IExperienceDatabaseProvider
{
    public ExperienceDatabaseProviderResult Create()
    {
        var tags = CreateTags();
        var experiences = CreateExperiences();
        return new(tags, experiences);
    }

    private TagsDatabase CreateTags()
    {
        var tags = new TagsDatabaseBuilder();
        var dotnet = tags.Tag(Tags.DotNet);
        var microservices = tags.Tag(Tags.Microservices);
        dotnet.IsIncludedIn(microservices).By(0.2f).WhichIsIncludedInIt().By(0.1f);
        return tags.Build().GetResultOrThrow();
    }

    private ExperienceDatabase CreateExperiences()
    {
        var experiences = new ExperienceDatabaseBuilder();
        var company = experiences.Place("Example Company");
        experiences.Job(job =>
        {
            job.Title("Example Software Engineer");
            job.Place(company);
            job.DateRange(
                DateRange.Completed(
                    new(Year: 2023, Month: 1),
                    new(Year: 2024, Month: 12)));
            job.Item(item =>
            {
                item.Text($"Built a fictional { Bold(".NET service") } for example users.");
                // Score 1-10 is recommended, though any scale may be used.
                item.Tag(Tags.DotNet, score: 10);
                item.Tag(Tags.Microservices, score: 4);
            });
            job.Item(item =>
            {
                item.Text($"Designed a { Bold("microservice") }");
                item.Tag(Tags.Microservices, score: 5);
            });
        });
        return experiences.Build();
    }
}
