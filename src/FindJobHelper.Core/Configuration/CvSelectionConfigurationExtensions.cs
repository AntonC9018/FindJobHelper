using System.Collections.Immutable;
using FindJobHelper.Configuration;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

// Engine-facing glue over the loaded domain configuration: converts the plain
// skill/technology strings to RegularString values and reports unknown tags
// with CvConfigurationException from the Configuration assembly.
public static class CvSelectionConfigurationExtensions
{
    extension(CvSelectionConfiguration configuration)
    {
        public ConfiguredCvSearch BuildSearch(TagsDatabase tagsDatabase)
        {
            ArgumentNullException.ThrowIfNull(tagsDatabase);
            var tagInputs = configuration.RequiredTags
                .Select(tag => (tag.Name, tag.Weight))
                .ToArray();

            WeightedTags weightedTags;
            var unknownTags = new List<string>();
            foreach (var tag in configuration.RequiredTags)
            {
                try
                {
                    _ = tagsDatabase.Find(tag.Name);
                }
                catch (InvalidOperationException)
                {
                    unknownTags.Add($"Required tag '{tag.Name}' was not found in the tag database.");
                }
            }

            if (unknownTags.Count > 0)
            {
                throw new CvConfigurationException(unknownTags);
            }

            try
            {
                weightedTags = tagsDatabase.Weighted(tagInputs);
            }
            catch (InvalidOperationException ex)
            {
                throw new CvConfigurationException(ex.Message, ex);
            }

            try
            {
                configuration.Mmr.Validate();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new CvConfigurationException($"Invalid MMR configuration: {ex.Message}", ex);
            }

            var educationKey = new ExperienceKey("Education");
            var workKey = new ExperienceKey("Work");
            var personalProjectsKey = new ExperienceKey("PersonalProjects");

            var builder = new SearchBuilder();
            builder.Tags(weightedTags);
            builder.Mmr(configuration.Mmr);
            var defaultSelection = configuration.Selection.Default;
            var educationSelection = configuration.Selection.Education;
            builder.ConfigureDefaults(options => defaultSelection.Apply(options));
            builder.Configure(
                educationKey,
                predicate: static experience => experience.Type.IsDegree(),
                configure: options => educationSelection.Apply(options));
            builder.Configure(
                workKey,
                predicate: static experience => experience.Type == ExperienceType.Job,
                options =>
                {
                    configuration.Selection.WorkExperience.Apply(options);
                    // Keep every job heading, but let its bullets compete globally.
                    // A job without a selected bullet is still rendered as an empty list.
                    options.IncludeEmptyLists = true;
                    options.PreserveOneItemPerList = false;
                });
            builder.Configure(
                personalProjectsKey,
                predicate: static experience => experience.Type == ExperienceType.Project,
                options =>
                {
                    configuration.Selection.PersonalProjects.Apply(options);
                    options.PreserveOneItemPerList = false;
                });

            try
            {
                var search = builder.Build();
                var bindings = new CvExperienceSectionBindings(
                    educationKey,
                    workKey,
                    personalProjectsKey);
                var skills = configuration.Skills
                    .Select(static skill => new RegularString(skill))
                    .ToImmutableArray();
                var technologies = configuration.Technologies
                    .Select(static technology => new RegularString(technology))
                    .ToImmutableArray();
                return new(
                    search,
                    bindings,
                    skills,
                    technologies,
                    configuration.SectionOrder,
                    configuration.PageCount,
                    configuration.PageLayout);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new CvConfigurationException($"Invalid selection configuration: {ex.Message}", ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new CvConfigurationException($"Invalid selection configuration: {ex.Message}", ex);
            }
        }
    }
}

public static class SelectionOptionsConfigurationExtensions
{
    extension(SelectionOptionsConfiguration configuration)
    {
        public void Apply(SearchPredicateOptions options)
        {
            if (configuration.SpecifiedFields.MinItemBudget)
            {
                options.MinItemBudget = configuration.MinItemBudget;
            }
            if (configuration.SpecifiedFields.ItemBudget)
            {
                options.ItemBudget = configuration.ItemBudget ?? int.MaxValue;
            }
            if (configuration.SpecifiedFields.ScoreLowerBound)
            {
                options.ScoreLowerBound = configuration.ScoreLowerBound;
            }
            if (configuration.SpecifiedFields.RecencyBoost)
            {
                options.RecencyBoost = new(configuration.RecencyBoost);
            }
            if (!configuration.SpecifiedFields.DirectMatchBoost)
            {
                return;
            }
            if (configuration.DirectMatchBoost is not { } directMatchBoost)
            {
                return;
            }

            options.DirectMatchBoost = new(directMatchBoost);
        }
    }
}
