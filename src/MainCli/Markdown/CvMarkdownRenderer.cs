using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CodegenCS;
using FindJobHelper.Core;
using FindJobHelper.Core.Helper;

namespace FindJobHelper.CVGeneration;

internal enum CvMarkdownRenderMode
{
    Clean,
    Annotated,
}

internal static class CvMarkdownRenderer
{
    internal static void Render(
        CvDataModel model,
        CvMarkdownRenderMode mode,
        ICodegenTextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(writer);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported Markdown render mode.");
        }

        var blocks = new List<FormattableString>
        {
            $$"""
            # {{Text(model.Name.First)}} {{Text(model.Name.Last)}}

            {{Text(model.Profession.Value)}}
            """,
        };

        if (!model.CategorizedInfos.IsEmpty || !model.CategorizedInfoLists.IsEmpty)
        {
            blocks.Add(RenderMetadata(model));
        }
        if (model.Summary is not null)
        {
            blocks.Add($$"""
                ## Summary

                {{model.Summary.ToMarkdownString()}}
                """);
        }

        blocks.AddRange(model.SectionOrder
            .Where(section => !CvLatexFragmentRenderer.IsSectionEmpty(section, model))
            .Select(section => RenderSection(section, model, mode)));

        var footer = RenderFooter(model);
        if (footer is not null)
        {
            blocks.Add(footer);
        }

        FormattableString document =
            $"{blocks.Render(RenderEnumerableOptions.LineBreaksWithSpacer)}";
        writer.WriteLine(document);
    }

    private static FormattableString RenderMetadata(CvDataModel model)
    {
        var items = model.CategorizedInfos
            .Select(RenderMetadataItem)
            .Concat(model.CategorizedInfoLists.Select(RenderMetadataList));
        return $"{items.Render(RenderEnumerableOptions.LineBreaksWithoutSpacer)}";
    }

    private static FormattableString RenderMetadataItem(CategorizedInfo info) =>
        $"**{Text(info.Category.DisplayName)}:** {CategoryValue(info.Category, info.Value)}";

    private static FormattableString RenderMetadataList(CategorizedInfoList list)
    {
        var values = string.Join(
            ", ",
            list.Values.Select(value => CategoryValue(list.Category, value)));
        return $"**{Text(list.Category.DisplayName)}:** {values}";
    }

    private static FormattableString RenderSection(
        Section section,
        CvDataModel model,
        CvMarkdownRenderMode mode)
    {
        var contents = model.DispatchSection(
            section,
            renderLanguages: RenderLanguages,
            renderEvents: events => RenderEvents(events, mode));
        return $$"""
            ## {{section.ToDisplayString()}}

            {{contents}}
            """;
    }

    private static FormattableString RenderLanguages(
        ImmutableArray<LanguageProficiencyInfo> languages)
    {
        var items = languages.Select(static language =>
        {
            var skills = language.Skills.IsEmpty
                ? string.Empty
                : $" · {string.Join(", ", language.Skills.Select(static skill => Text(skill.Text)))}";
            return (FormattableString)
                $"- **{Text(language.Language.Name)}:** {Text(language.GeneralProficiencyLevel.Value)}{skills}";
        });
        return $"{items.Render(RenderEnumerableOptions.LineBreaksWithoutSpacer)}";
    }

    private static FormattableString RenderEvents(
        ImmutableArray<Event> events,
        CvMarkdownRenderMode mode)
    {
        var items = events.Select(@event => RenderEvent(@event, mode));
        return $"{items.Render(RenderEnumerableOptions.LineBreaksWithSpacer)}";
    }

    private static FormattableString RenderEvent(
        Event @event,
        CvMarkdownRenderMode mode)
    {
        var blocks = new List<FormattableString>
        {
            $"### {AnnotationPrefix(mode, @event.DebugInfo)}{Text(@event.Title)}",
        };

        var place = @event.Place.IsPersonal
            ? string.Empty
            : $" · {Text(@event.Place.Name)}";
        blocks.Add($"*{Text(@event.DateRange)}{place}*");

        if (@event.Text is not null)
        {
            blocks.Add($"{@event.Text.ToMarkdownString()}");
        }

        var bullets = @event.SubItems
            .Select(item => RenderSubItem(item, mode))
            .ToList();
        if (!@event.Urls.IsEmpty)
        {
            var links = string.Join(
                " | ",
                @event.Urls.Select(static url => Autolink(url.Value)));
            bullets.Add($"- **Links:** {links}");
        }
        if (bullets.Count > 0)
        {
            blocks.Add($"{bullets.Render(RenderEnumerableOptions.LineBreaksWithoutSpacer)}");
        }

        return $"{blocks.Render(RenderEnumerableOptions.LineBreaksWithSpacer)}";
    }

    private static FormattableString RenderSubItem(
        SubEvent item,
        CvMarkdownRenderMode mode)
    {
        var content =
            $"{AnnotationPrefix(mode, item)}{item.Text.ToMarkdownString()}";
        return $"- {content}";
    }

    private static string AnnotationPrefix(
        CvMarkdownRenderMode mode,
        EventDebugInfo debugInfo) =>
        mode switch
        {
            CvMarkdownRenderMode.Clean => string.Empty,
            CvMarkdownRenderMode.Annotated => $"{ScoreAnnotation(debugInfo)} ",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported Markdown render mode."),
        };

    private static string AnnotationPrefix(
        CvMarkdownRenderMode mode,
        SubEvent item) =>
        mode switch
        {
            CvMarkdownRenderMode.Clean => string.Empty,
            CvMarkdownRenderMode.Annotated => $"{ScoreAnnotation(item)} ",
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported Markdown render mode."),
        };

    private static FormattableString? RenderFooter(CvDataModel model)
    {
        var links = new List<string>(2);
        if (!model.Website.IsNull)
        {
            links.Add(Autolink(model.Website.Value!));
        }
        if (!model.GitHub.IsNull)
        {
            links.Add(Autolink(model.GitHub.Value!));
        }
        return links.Count == 0
            ? null
            : (FormattableString) $"{string.Join(" · ", links)}";
    }

    private static string ScoreAnnotation(EventDebugInfo debugInfo)
    {
        var annotations = new List<string>
        {
            CodeSpan(
                $"rank: {FormatScore(debugInfo.Score)}; " +
                $"raw: {FormatScore(debugInfo.RawScore)}"),
        };
        if (!debugInfo.RequirementCoverage.IsEmpty)
        {
            annotations.Add(CodeSpan(
                $"coverage: {FormatCoverage(debugInfo.RequirementCoverage)}"));
        }

        if (!debugInfo.TagMatches.IsEmpty)
        {
            annotations.Add(CodeSpan(
                $"matches: {FormatMatches(debugInfo.TagMatches)}"));
        }

        return string.Join(" ", annotations);
    }

    private static string ScoreAnnotation(SubEvent item)
    {
        if (item.DebugMmrScoreBreakdown is not { } breakdown)
        {
            return CodeSpan(
                $"rank: {FormatScore(item.DebugScore)}; " +
                $"raw: {FormatScore(item.DebugRawScore)}");
        }

        var annotations = new List<string>
        {
            CodeSpan(
                $"rank: {FormatScore(breakdown.RawEquivalentRankScore)}; " +
                $"raw: {FormatScore(breakdown.RawRelevance)}; " +
                $"mmr: {FormatScore(breakdown.NormalizedMmrScore)}"),
            CodeSpan(
                $"MMR terms: {FormatSignedScore(breakdown.WeightedRelevanceTerm)} relevance " +
                $"{FormatSignedScore(-breakdown.WeightedSimilarityPenalty)} similarity " +
                $"{FormatSignedScore(-breakdown.WeightedSaturationPenalty)} saturation"),
        };
        if (!item.DebugRequirementCoverage.IsEmpty)
        {
            annotations.Add(CodeSpan(
                $"coverage: {FormatCoverage(item.DebugRequirementCoverage)}"));
        }

        if (!item.DebugTagMatches.IsEmpty)
        {
            annotations.Add(CodeSpan(
                $"matches: {FormatMatches(item.DebugTagMatches)}"));
        }

        return string.Join(" ", annotations);
    }

    private static string FormatCoverage(
        ImmutableArray<DebugRequirementCoverage> coverage)
    {
        return string.Join(
            "; ",
            coverage.Select(x =>
                $"{FormatRequirementLabel(x.Requirement)}={FormatScore(x.Score)}"));
    }

    private static string FormatMatches(
        ImmutableArray<DebugTagMatch> matches)
    {
        return string.Join(
            "; ",
            matches.Select(match =>
            {
                var value =
                    $"{match.TargetTag.Name}={FormatScore(match.RawContribution)}";
                if (match.Origins.IsEmpty)
                {
                    return value;
                }

                return value
                       + " via "
                       + string.Join(
                           ", ",
                           match.Origins.Select(origin =>
                               $"{origin.Requirement.CanonicalTag.Name}=" +
                               $"{FormatScore(origin.Contribution)}"));
            }));
    }

    private static string FormatRequirementLabel(RequiredTagGroup requirement)
    {
        var configuredNames = requirement.ConfiguredTags
            .Select(x => x.Tag.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (configuredNames.Length == 1
            && string.Equals(
                configuredNames[0],
                requirement.CanonicalTag.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return requirement.CanonicalTag.Name;
        }

        return requirement.CanonicalTag.Name
               + " [configured: "
               + string.Join(", ", configuredNames)
               + "]";
    }

    private static string CodeSpan(string value)
    {
        var output = new StringBuilder(value.Length + 2);
        MarkdownConverter.AppendMarkdownCodeSpan(output, value);
        return output.ToString();
    }

    private static string FormatScore(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatSignedScore(float value) =>
        value.ToString("+0.###;-0.###;+0", CultureInfo.InvariantCulture);

    private static string CategoryValue(Category category, RegularString value) =>
        category.IsUrl ? Autolink(value.Value) : Text(value);

    private static string Autolink(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"The given URL '{value}' must be absolute.", nameof(value));
        }
        return $"<{uri.AbsoluteUri}>";
    }

    private static string Text(RegularString value) => Text(value.Value);

    private static string Text<T>(T value)
        where T : ISpanFormattable =>
        Text($"{value}");

    private static string Text(string value) =>
        MarkdownConverter.ToMarkdownStructuralText(value);
}
