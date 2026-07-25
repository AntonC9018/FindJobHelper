using System.Collections.Immutable;
using System.Globalization;
using FindJobHelper.Core.Helper;

namespace FindJobHelper.CVGeneration;

internal static class CvMarkdownRenderer
{
    internal static void Render(CvDataModel model, StreamWriter writer)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(writer);

        WriteLine(writer, $"# {Text(model.Name.First)} {Text(model.Name.Last)}");
        WriteLine(writer);
        WriteLine(writer, Text(model.Profession.Value));
        WriteLine(writer);
        RenderMetadata(model, writer);

        if (model.Summary is not null)
        {
            WriteLine(writer);
            WriteLine(writer, "## Summary");
            WriteLine(writer);
            WriteMarkdown(writer, model.Summary.ToMarkdownString());
        }

        foreach (var section in model.SectionOrder)
        {
            if (CvLatexFragmentRenderer.IsSectionEmpty(section, model))
            {
                continue;
            }

            WriteLine(writer);
            WriteLine(writer, $"## {SectionTitle(section)}");
            WriteLine(writer);
            switch (section)
            {
                case Section.Languages:
                    RenderLanguages(model.Languages, writer);
                    break;
                case Section.WorkExperience:
                    RenderEvents(model.WorkExperiences, writer);
                    break;
                case Section.Education:
                    RenderEvents(model.Educations, writer);
                    break;
                case Section.PersonalProjects:
                    RenderEvents(model.PersonalProjects, writer);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section), section, null);
            }
        }

        RenderFooter(model, writer);
    }

    private static void RenderMetadata(CvDataModel model, StreamWriter writer)
    {
        var count = Math.Max(model.CategorizedInfos.Length, model.CategorizedInfoLists.Length);
        for (var index = 0; index < count; index++)
        {
            if (index < model.CategorizedInfos.Length)
            {
                var info = model.CategorizedInfos[index];
                WriteLine(
                    writer,
                    $"**{Text(info.Category.DisplayName)}:** {CategoryValue(info.Category, info.Value)}");
            }

            if (index < model.CategorizedInfoLists.Length)
            {
                var list = model.CategorizedInfoLists[index];
                var values = string.Join(
                    ", ",
                    list.Values.Select(value => CategoryValue(list.Category, value)));
                WriteLine(writer, $"**{Text(list.Category.DisplayName)}:** {values}");
            }
        }
    }

    private static void RenderLanguages(
        ImmutableArray<LanguageProficiencyInfo> languages,
        StreamWriter writer)
    {
        foreach (var language in languages)
        {
            var skills = language.Skills.IsEmpty
                ? string.Empty
                : $" · {string.Join(", ", language.Skills.Select(static skill => Text(skill.Text)))}";
            WriteLine(
                writer,
                $"- **{Text(language.Language.Name)}:** {Text(language.GeneralProficiencyLevel.Value)}{skills}");
        }
    }

    private static void RenderEvents(ImmutableArray<Event> events, StreamWriter writer)
    {
        for (var index = 0; index < events.Length; index++)
        {
            if (index > 0)
            {
                WriteLine(writer);
            }

            RenderEvent(events[index], writer);
        }
    }

    private static void RenderEvent(Event @event, StreamWriter writer)
    {
        WriteLine(
            writer,
            $"### {ScoreAnnotation(@event.DebugScore, @event.DebugTagScores)} {Text(@event.Title)}");
        WriteLine(writer);

        var place = @event.Place.IsPersonal
            ? string.Empty
            : $" · {Text(@event.Place.Name)}";
        WriteLine(writer, $"*{Text(@event.DateRange.ToString())}{place}*");

        if (@event.Text is not null)
        {
            WriteLine(writer);
            WriteMarkdown(writer, @event.Text.ToMarkdownString());
        }

        if (!@event.SubItems.IsEmpty || !@event.Urls.IsEmpty)
        {
            WriteLine(writer);
        }

        foreach (var item in @event.SubItems)
        {
            WriteBullet(
                writer,
                $"{ScoreAnnotation(item.DebugScore, item.DebugTagScores)} {item.Text.ToMarkdownString()}");
        }

        if (!@event.Urls.IsEmpty)
        {
            WriteBullet(
                writer,
                $"**Links:** {string.Join(" | ", @event.Urls.Select(static url => Autolink(url.Value)))}");
        }
    }

    private static void RenderFooter(CvDataModel model, StreamWriter writer)
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
        if (links.Count == 0)
        {
            return;
        }

        WriteLine(writer);
        WriteLine(writer, string.Join(" · ", links));
    }

    private static string ScoreAnnotation(float score, ImmutableArray<DebugTagScore> tagScores)
    {
        var value = $"score: {FormatScore(score)}";
        if (!tagScores.IsEmpty)
        {
            value += $" ({string.Join(", ", tagScores.Select(static tagScore =>
                $"{tagScore.Tag.Value}:{FormatScore(tagScore.Score)}"))})";
        }
        return MarkdownConverter.ToMarkdownCodeSpan(value);
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

    private static string Text(string value) =>
        MarkdownConverter.ToMarkdownStructuralText(value);

    private static string SectionTitle(Section section) => section switch
    {
        Section.Languages => "Languages",
        Section.WorkExperience => "Experience",
        Section.Education => "Education",
        Section.PersonalProjects => "Personal Projects",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    private static void WriteBullet(StreamWriter writer, string markdown)
    {
        var lines = NormalizeLineEndings(markdown).Split('\n');
        WriteLine(writer, $"- {lines[0]}");
        for (var index = 1; index < lines.Length; index++)
        {
            WriteLine(writer, $"  {lines[index]}");
        }
    }

    private static void WriteMarkdown(StreamWriter writer, string markdown)
    {
        foreach (var line in NormalizeLineEndings(markdown).Split('\n'))
        {
            WriteLine(writer, line);
        }
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static void WriteLine(StreamWriter writer, string value = "") =>
        writer.WriteLine(value);
}
