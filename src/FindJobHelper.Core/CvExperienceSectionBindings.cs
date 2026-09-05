using System.Collections.Immutable;
using FindJobHelper.Configuration;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

public sealed class CvExperienceSectionBindings(
    ExperienceKey educationKey,
    ExperienceKey workKey,
    ExperienceKey personalProjectsKey)
{
    public ExperienceKey EducationKey { get; } = educationKey;
    public ExperienceKey WorkKey { get; } = workKey;
    public ExperienceKey PersonalProjectsKey { get; } = personalProjectsKey;

    public ImmutableArray<Section> Sections { get; } =
    [
        Section.Education,
        Section.WorkExperience,
        Section.PersonalProjects,
    ];

    public Section GetSection(ExperienceKey key)
    {
        if (key == EducationKey)
        {
            return Section.Education;
        }
        if (key == WorkKey)
        {
            return Section.WorkExperience;
        }
        if (key == PersonalProjectsKey)
        {
            return Section.PersonalProjects;
        }

        throw new KeyNotFoundException($"Experience key '{key}' is not bound to a CV section.");
    }

    public void Apply(SearchResult result, CvDataModel model)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(model);

        model.Educations = result.Get(EducationKey);
        model.WorkExperiences = result.Get(WorkKey);
        model.PersonalProjects = result.Get(PersonalProjectsKey);
    }
}
