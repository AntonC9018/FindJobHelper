using FindJobHelper.Core;
using FindJobHelper.CVGeneration;

namespace FindJobWorkspace.Provider;

internal static class ExperienceDatabaseFactory
{
    internal static ExperienceDatabase Create(Tag dotnet)
    {
        var builder = new ExperienceDatabaseBuilder();
        var company = builder.Place("Example Company");
        builder.Job(job =>
        {
            job.Title("Example Software Engineer");
            job.Place(company);
            job.DateRange(DateRange.Completed(new(2023, 1), new(2024, 12)));
            job.Item(item =>
            {
                item.Text($"Built a fictional .NET service for example users.");
                item.Tag(dotnet, 10);
            });
        });
        return builder.Build();
    }
}
