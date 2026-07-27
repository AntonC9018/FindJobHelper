using System.Collections.Immutable;
using System.Globalization;
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
        IProgressReporter progress,
        ICodegenTextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(writer);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported Markdown render mode.");
        }

        var totalWorkUnits = GetWorkUnitCount(model);
        var completedWorkUnits = 0;
        progress.Report(new(
            CompletedWorkUnits: completedWorkUnits,
            TotalWorkUnits: totalWorkUnits,
            Detail: "Creating Markdown files"));
        var blocks = new List<FormattableString>
        {
            $$"""
            # {{Text(model.Name.First)}} {{Text(model.Name.Last)}}

            {{Text(model.Profession.Value)}}
            """,
        };
        ReportBlock("Creating Markdown files — document header");

        if (!model.CategorizedInfos.IsEmpty || !model.CategorizedInfoLists.IsEmpty)
        {
            blocks.Add(RenderMetadata(model));
        }
        ReportBlock("Creating Markdown files — metadata");
        if (model.Summary is not null)
        {
            blocks.Add($$"""
                ## Summary

                {{model.Summary.ToMarkdownString()}}
                """);
        }
        ReportBlock("Creating Markdown files — summary");

        foreach (var section in model.SectionOrder)
        {
            if (!CvLatexFragmentRenderer.IsSectionEmpty(section, model))
            {
                blocks.Add(RenderSection(section, model, mode));
            }
            ReportBlock("Creating Markdown files — section");
        }

        var footer = RenderFooter(model);
        if (footer is not null)
        {
            blocks.Add(footer);
        }
        ReportBlock("Creating Markdown files — footer");

        FormattableString document =
            $"{blocks.Render(RenderEnumerableOptions.LineBreaksWithSpacer)}";
        writer.WriteLine(document);

        void ReportBlock(string detail)
        {
            completedWorkUnits++;
            progress.Report(new(
                CompletedWorkUnits: completedWorkUnits,
                TotalWorkUnits: totalWorkUnits,
                Detail: detail));
        }
    }

    internal static int GetWorkUnitCount(CvDataModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return checked(model.SectionOrder.Length + 5);
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
        return mode switch
        {
            CvMarkdownRenderMode.Clean => RenderCleanEvent(@event),
            CvMarkdownRenderMode.Annotated => RenderAnnotatedEvent(@event),
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported Markdown render mode."),
        };
    }

    private static FormattableString RenderCleanEvent(Event @event)
    {
        var blocks = new List<FormattableString>
        {
            $"### {Text(@event.Title)}",
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
            .Select(RenderCleanSubItem)
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

    private static FormattableString RenderAnnotatedEvent(Event @event)
    {
        var blocks = new List<FormattableString>
        {
            $"### {Text(@event.Title)}",
            $"{FormatDiagnosticDetails(FormatEventMetrics(@event.DebugInfo))}",
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
            .Select(RenderAnnotatedSubItem)
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

    private static FormattableString RenderCleanSubItem(SubEvent item)
    {
        var content = item.Text.ToMarkdownString();
        return $"- {content}";
    }

    private static FormattableString RenderAnnotatedSubItem(SubEvent item)
    {
        var content =
            $"{FormatDiagnosticDetails(FormatBulletMetrics(item))}\n\n" +
            item.Text.ToMarkdownString();
        var listItem = "- " + IndentMultiline(
            value: content,
            indentation: "  ");
        return $"{listItem}";
    }

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

    private static string FormatEventMetrics(EventDebugInfo debugInfo)
    {
        var lines = new List<string>
        {
            $"rank: {FormatScore(debugInfo.Score)}",
            $"raw: {FormatScore(debugInfo.RawScore)}",
        };
        AppendCoverage(lines, debugInfo.RequirementCoverage);
        AppendMatches(lines, debugInfo.TagMatches);
        return string.Join("\n", lines);
    }

    private static string FormatBulletMetrics(SubEvent item)
    {
        var lines = new List<string>();
        if (item.DebugMmrScoreBreakdown is not { } breakdown)
        {
            lines.Add($"rank: {FormatScore(item.DebugScore)}");
            lines.Add($"raw: {FormatScore(item.DebugRawScore)}");
        }
        else
        {
            lines.Add($"rank: {FormatScore(breakdown.RawEquivalentRankScore)}");
            lines.Add($"raw: {FormatScore(breakdown.RawRelevance)}");
            lines.Add($"mmr: {FormatScore(breakdown.NormalizedMmrScore)}");
            lines.Add("MMR terms:");
            lines.Add(
                $"  relevance: {FormatSignedScore(breakdown.WeightedRelevanceTerm)}");
            lines.Add(
                $"  similarity: {FormatSignedScore(-breakdown.WeightedSimilarityPenalty)}");
            lines.Add(
                $"  saturation: {FormatSignedScore(-breakdown.WeightedSaturationPenalty)}");
        }

        AppendCoverage(lines, item.DebugRequirementCoverage);
        AppendMatches(lines, item.DebugTagMatches);
        return string.Join("\n", lines);
    }

    private static void AppendCoverage(
        List<string> lines,
        ImmutableArray<DebugRequirementCoverage> coverage)
    {
        if (coverage.IsEmpty)
        {
            return;
        }

        lines.Add("coverage:");
        lines.AddRange(coverage.Select(x =>
            $"  {FormatRequirementLabel(x.Requirement)}: {FormatScore(x.Score)}"));
    }

    private static void AppendMatches(
        List<string> lines,
        ImmutableArray<DebugTagMatch> matches)
    {
        if (matches.IsEmpty)
        {
            return;
        }

        lines.Add("matches:");
        foreach (var match in matches)
        {
            lines.Add($"  {match.TargetTag.Name}:");
            lines.Add(
                $"    raw contribution: {FormatScore(match.RawContribution)}");
            if (match.Origins.IsEmpty)
            {
                continue;
            }

            var bestContribution = match.Origins.Max(static x => x.Contribution);
            var bestOrigins = match.Origins
                .Where(x => x.Contribution == bestContribution)
                .ToImmutableArray();
            var additionalOrigins = match.Origins
                .Where(x => x.Contribution != bestContribution)
                .ToImmutableArray();

            lines.Add(bestOrigins.Length == 1
                ? "    best requirement:"
                : "    best requirements:");
            lines.AddRange(bestOrigins.Select(origin =>
                $"      {FormatRequirementLabel(origin.Requirement)}: " +
                FormatScore(origin.Contribution)));

            if (!additionalOrigins.IsEmpty)
            {
                lines.Add("    additional requirement coverage:");
                lines.AddRange(additionalOrigins.Select(origin =>
                    $"      {FormatRequirementLabel(origin.Requirement)}: " +
                    FormatScore(origin.Contribution)));
            }
        }
    }

    private static string FormatFencedBlock(string contents) =>
        $"```text\n{contents}\n```";

    private static string FormatDiagnosticDetails(string contents) =>
        $"<details>\n<summary>Diagnostics</summary>\n\n" +
        $"{FormatFencedBlock(contents)}\n</details>";

    private static string IndentMultiline(
        string value,
        string indentation)
    {
        var lines = value
            .ReplaceLineEndings("\n")
            .Split('\n');
        return string.Join(
            "\n",
            lines.Select((line, index) =>
                index == 0 || line.Length == 0
                    ? line
                    : indentation + line));
    }

    private static string FormatRequirementLabel(RequiredTagGroup requirement) =>
        requirement.ConfiguredTags[0].Tag.Name;

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
