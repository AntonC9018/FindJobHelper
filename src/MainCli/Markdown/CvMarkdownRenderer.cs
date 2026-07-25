using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CodegenCS;
using FindJobHelper.Core.Helper;

namespace FindJobHelper.CVGeneration;

internal static class CvMarkdownRenderer
{
    internal static void Render(CvDataModel model, ICodegenTextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(writer);

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
            .Select(section => RenderSection(section, model)));

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

    private static FormattableString RenderSection(Section section, CvDataModel model)
    {
        var contents = model.DispatchSection(
            section,
            renderLanguages: RenderLanguages,
            renderEvents: RenderEvents);
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

    private static FormattableString RenderEvents(ImmutableArray<Event> events)
    {
        var items = events.Select(RenderEvent);
        return $"{items.Render(RenderEnumerableOptions.LineBreaksWithSpacer)}";
    }

    private static FormattableString RenderEvent(Event @event)
    {
        var blocks = new List<FormattableString>
        {
            $"### {ScoreAnnotation(@event.DebugScore, @event.DebugTagScores)} {Text(@event.Title)}",
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
            .Select(RenderSubItem)
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

    private static FormattableString RenderSubItem(SubEvent item)
    {
        var content =
            $"{ScoreAnnotation(item.DebugScore, item.DebugTagScores)} {item.Text.ToMarkdownString()}";
        return $"- {content}";
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

    private static string ScoreAnnotation(float score, ImmutableArray<DebugTagScore> tagScores)
    {
        var value = new StringBuilder()
            .Append("score: ")
            .Append(FormatScore(score));
        if (!tagScores.IsEmpty)
        {
            value.Append(" (");
            for (var index = 0; index < tagScores.Length; index++)
            {
                if (index > 0)
                {
                    value.Append(", ");
                }
                var tagScore = tagScores[index];
                value.Append(tagScore.Tag.Value)
                    .Append(':')
                    .Append(FormatScore(tagScore.Score));
            }
            value.Append(')');
        }

        var output = new StringBuilder(value.Length + 2);
        MarkdownConverter.AppendMarkdownCodeSpan(output, value.ToString());
        return output.ToString();
    }

    private static string FormatScore(float value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

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
