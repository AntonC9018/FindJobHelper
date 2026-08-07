using FindJobHelper.Core;
using FindJobHelper.CVGeneration;

namespace ProviderFixtures.SyntheticProvider;

public static class ExperienceDatabaseFactory
{
    public static ExperienceDatabase Create(SyntheticTags tags)
    {
        var builder = new ExperienceDatabaseBuilder();
        var exampleCompany = builder.Place("Example Company");
        var exampleUniversity = builder.Place("Example University");
        var personal = builder.Place(Place.Personal);

        builder.Job(job =>
        {
            job.Title("Example Software Engineer");
            job.Place(exampleCompany);
            job.DateRange(DateRange.Completed(new(2022, 1), new(2024, 12)));
            job.Item(item =>
            {
                item.Text($"Built a fictional .NET service for example users.");
                item.Tag(tags.DotNet, 10);
            });
            job.Item(item =>
            {
                item.Text($"Added deterministic automated tests.");
                item.Tag(tags.Testing, 8);
            });
        });

        builder.Job(job =>
        {
            job.Title("Example Documentation Engineer");
            job.Place(exampleCompany);
            job.DateRange(DateRange.Completed(new(2020, 1), new(2021, 12)));
            job.Item(item =>
            {
                item.Text($"Maintained fictional documentation for example readers.");
                item.Tag(tags.Documentation, 6);
            });
        });

        builder.PersonalProject(project =>
        {
            project.Title("Example Portfolio Project");
            project.Place(personal);
            project.DateRange(DateRange.Ongoing(new(2025, 1)));
            project.Item(item =>
            {
                item.Text($"Documented a fictional public sample.");
                item.Tag(tags.Documentation, 7);
            });
        });

        builder.BachelorsDegree(degree =>
        {
            degree.Title("Example Computer Science Degree");
            degree.Place(exampleUniversity);
            degree.DateRange(DateRange.Completed(new(2018, 9), new(2022, 6)));
            degree.Item(item =>
            {
                item.Text($"Studied software engineering with fictional coursework.");
                item.Tag(tags.DotNet, 4);
            });
        });

        return builder.Build();
    }
}
