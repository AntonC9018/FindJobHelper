using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FindJobHelper.Core.Helper;
using MainCli;

namespace FindJobHelper.Core.Tests;

#pragma warning disable CS0618 // Frozen report intentionally exercises the legacy budget alias.

internal static class SelectionDebugReport
{
    private static readonly ExperienceKey EducationKey = new("Education");
    private static readonly ExperienceKey WorkKey = new("Work");
    private static readonly ExperienceKey PersonalProjectsKey = new("PersonalProjects");

    public static async Task<ImmutableArray<SelectionDebugRun>> RunAll(
        CancellationToken cancellationToken)
    {
        var db = await LoadFrozenDb(cancellationToken);
        var tagsDatabase = TagsDatabaseFactory.Create().TagsDatabase;
        var runs = ImmutableArray.CreateBuilder<SelectionDebugRun>();

        foreach (var scenario in Scenarios())
        {
            foreach (var preset in Presets())
            {
                var builder = new SearchBuilder();
                builder.Tags(tagsDatabase.Weighted(scenario.Tags));
                builder.Mmr(preset.Options);
                ConfigureCliLikeSections(builder);

                var result = builder.Build().Run(db.Experiences);
                runs.Add(new(
                    scenario.Name,
                    preset.Name,
                    result));
            }
        }

        return runs.DrainToImmutable();
    }

    public static string ToMarkdown(ImmutableArray<SelectionDebugRun> runs)
    {
        var ret = new StringBuilder();
        ret.AppendLine("| scenario | preset | section | event | selected item | reason | selection | raw | rank | mmr | MMR terms | coverage | matches | dependency notes | budget minimum/maximum vs actual |");
        ret.AppendLine("| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- | --- | --- |");

        foreach (var run in runs)
        {
            var budgetBySection = run.Result.Diagnostics.Budgets
                .ToDictionary(x => x.Section);

            foreach (var trace in run.Result.Diagnostics.Items)
            {
                var dependencyNotes = FormatDependencyNotes(trace);
                ret.Append("| ");
                ret.Append(EscapeCell(run.Scenario));
                ret.Append(" | ");
                ret.Append(EscapeCell(run.Preset));
                ret.Append(" | ");
                ret.Append(EscapeCell(trace.Section.Value));
                ret.Append(" | ");
                ret.Append(EscapeCell(trace.Event.Title.Value));
                ret.Append(" | ");
                ret.Append(EscapeCell(trace.Item.Text.ToMarkdownString()));
                ret.Append(" | ");
                ret.Append(EscapeCell(trace.Reason.ToString().ToLowerInvariant()));
                ret.Append(" | ");
                ret.Append(trace.ScoreBreakdown.SelectionOrdinal);
                ret.Append(" | ");
                ret.Append(FormatFloat(trace.RawScore));
                ret.Append(" | ");
                ret.Append(FormatFloat(trace.DebugScore));
                ret.Append(" | ");
                ret.Append(FormatFloat(
                    trace.ScoreBreakdown.NormalizedMmrScore));
                ret.Append(" | ");
                ret.Append(EscapeCell(FormatMmrTerms(trace.ScoreBreakdown)));
                ret.Append(" | ");
                ret.Append(EscapeCell(FormatCoverage(trace.Matches)));
                ret.Append(" | ");
                ret.Append(EscapeCell(FormatMatches(trace.Matches)));
                ret.Append(" | ");
                ret.Append(EscapeCell(dependencyNotes));
                ret.Append(" | ");
                ret.Append(EscapeCell(FormatBudget(budgetBySection[trace.Section])));
                ret.AppendLine(" |");
            }
        }

        return ret.ToString();
    }

    private static async Task<ExperienceDatabase> LoadFrozenDb(
        CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead("data/selection-frozen-experience.json");
        return await ExperienceDatabaseSerializer.Deserialize(input, cancellationToken);
    }

    private static void ConfigureCliLikeSections(SearchBuilder builder)
    {
        builder.ConfigureDefaults(opts =>
        {
            opts.TotalItemBudget = 3;
            opts.ScoreLowerBound = 0f;
        });
        builder.Configure(
            EducationKey,
            predicate: e => e.Type.IsDegree(),
            opts =>
            {
                opts.TotalItemBudget = 2;
                opts.ScoreLowerBound = 0;
            });
        builder.Configure(
            WorkKey,
            predicate: e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 8;
                opts.ScoreLowerBound = 5;
            });
        builder.Configure(
            PersonalProjectsKey,
            predicate: e => e.Type == ExperienceType.Project,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.ScoreLowerBound = 5;
            });
    }

    private static ImmutableArray<SelectionScenario> Scenarios()
    {
        return
        [
            new(
                "cli-current",
                [
                    (".NET", 1.0f),
                    ("Thesis", 0.01f),
                    ("Image Processing", 1.0f),
                    ("Multithreading", 1.0f),
                    ("Concurrency", 0.8f),
                    ("SQL", 0.9f),
                    ("SQL Server", 0.8f),
                    ("PostgreSQL", 0.6f),
                    ("EF Core", 0.5f),
                    ("PNG", 1.0f),
                    ("TIFF", 1.0f),
                    ("3D", 0.5f),
                    ("JPEG", 0.5f),
                    ("GRPC", 0.9f),
                ]),
            new(
                "cli-no-image",
                [
                    (".NET", 1.0f),
                    ("Thesis", 0.01f),
                    ("Multithreading", 1.0f),
                    ("Concurrency", 0.8f),
                    ("SQL", 0.9f),
                    ("SQL Server", 0.8f),
                    ("PostgreSQL", 0.6f),
                    ("EF Core", 0.5f),
                    ("GRPC", 0.9f),
                ]),
            new(
                "cli-image-heavy",
                [
                    ("Image Processing", 1.0f),
                    ("PNG", 1.0f),
                    ("TIFF", 0.8f),
                    ("JPEG", 0.8f),
                    ("3D", 0.4f),
                    ("Parser", 1.0f),
                    ("Compression", 1.0f),
                    (".NET", 0.2f),
                    ("Thesis", 0.2f),
                ]),
            new(
                "cli-backend-sql",
                [
                    (".NET", 1.0f),
                    ("SQL", 1.0f),
                    ("SQL Server", 1.0f),
                    ("PostgreSQL", 0.7f),
                    ("EF Core", 0.8f),
                    ("GRPC", 0.7f),
                    ("API Design", 1.0f),
                ]),
        ];
    }

    private static ImmutableArray<SelectionPreset> Presets()
    {
        return
        [
            new("current-fixed", MmrOptions.Default),
            new(
                "balanced",
                new(
                    RelevanceWeight: 0.76f,
                    SaturationQuota: 2,
                    SaturationPenalty: 0.16f)),
            new(
                "diverse",
                new(
                    RelevanceWeight: 0.68f,
                    SaturationQuota: 1,
                    SaturationPenalty: 0.24f)),
        ];
    }

    private static string FormatDependencyNotes(SelectionItemTrace trace)
    {
        if (trace.DependencyOf is { } dependencyOf)
        {
            return $"required by: {dependencyOf.Text.ToMarkdownString()}";
        }

        return trace.Item.DependsOn.IsDefaultOrEmpty
            ? ""
            : $"requires {trace.Item.DependsOn.Length}";
    }

    private static string FormatMmrTerms(MmrScoreBreakdown breakdown)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{breakdown.WeightedRelevanceTerm:+0.###;-0.###;+0} relevance " +
            $"{-breakdown.WeightedSimilarityPenalty:+0.###;-0.###;+0} similarity " +
            $"{-breakdown.WeightedSaturationPenalty:+0.###;-0.###;+0} saturation");
    }

    private static string FormatCoverage(ScoredTags matches)
    {
        return string.Join(
            "; ",
            matches.RequirementGroupCoverage
                .OrderByDescending(x => x.Value)
                .ThenBy(
                    x => x.Key.CanonicalTag.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Select(x =>
                    $"{FormatRequirementLabel(x.Key)}={FormatFloat(x.Value)}"));
    }

    private static string FormatMatches(ScoredTags matches)
    {
        return string.Join(
            "; ",
            matches.Matches
                .OrderByDescending(x => x.RawContribution)
                .ThenBy(
                    x => x.TargetTag.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Select(match =>
                {
                    var origins = string.Join(
                        ", ",
                        match.Projection.Origins
                            .Select(origin => (
                                Origin: origin,
                                Contribution:
                                match.EvidenceScore * origin.Coefficient))
                            .OrderByDescending(x => x.Contribution)
                            .ThenBy(
                                x => x.Origin.RequiredTagGroup.CanonicalTag.Name,
                                StringComparer.OrdinalIgnoreCase)
                            .Select(x =>
                                $"{x.Origin.RequiredTagGroup.CanonicalTag.Name}=" +
                                $"{FormatFloat(x.Contribution)}"));
                    return origins.Length == 0
                        ? $"{match.TargetTag.Name}={FormatFloat(match.RawContribution)}"
                        : $"{match.TargetTag.Name}={FormatFloat(match.RawContribution)} via {origins}";
                }));
    }

    private static string FormatRequirementLabel(RequiredTagGroup requirement)
    {
        var configuredNames = requirement.ConfiguredTags
            .Select(x => x.Tag.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return configuredNames.Length == 1
               && string.Equals(
                   configuredNames[0],
                   requirement.CanonicalTag.Name,
                   StringComparison.OrdinalIgnoreCase)
            ? requirement.CanonicalTag.Name
            : $"{requirement.CanonicalTag.Name} [configured: {string.Join(", ", configuredNames)}]";
    }

    private static string FormatBudget(SelectionBudgetTrace budget)
    {
        var over = Math.Max(0, budget.ActualCount - budget.RequestedMaximum);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"minimum {budget.RequestedMinimum}, maximum {budget.RequestedMaximum}, " +
            $"actual {budget.ActualCount}, remaining {budget.RemainingMaximumBudget}, over +{over}");
    }

    private static string EscapeCell(string value)
    {
        return value
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private sealed record SelectionScenario(
        string Name,
        (string Tag, float Weight)[] Tags);

    private sealed record SelectionPreset(
        string Name,
        MmrOptions Options);
}

internal sealed record SelectionDebugRun(
    string Scenario,
    string Preset,
    SearchResult Result);
