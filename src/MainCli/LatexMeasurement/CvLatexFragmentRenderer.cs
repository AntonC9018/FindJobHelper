using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FindJobHelper.Core;

namespace FindJobHelper.CVGeneration;

/// <summary>
/// Pure LaTeX fragment rendering shared by production generation and height
/// measurement. Layout decisions stay in cv_template_config.tex.
/// </summary>
internal static class CvLatexFragmentRenderer
{
    public static string RenderSectionInner(
        Section section,
        CvDataModel model,
        bool isDebug = false)
    {
        return section switch
        {
            Section.Languages => RenderLanguagesSectionInner(model.Languages),
            Section.WorkExperience => RenderEventsSectionInner(
                model.WorkExperiences,
                "Experience",
                isDebug),
            Section.Education => RenderEventsSectionInner(
                model.Educations,
                "Education",
                isDebug),
            Section.PersonalProjects => RenderEventsSectionInner(
                model.PersonalProjects,
                "Personal Projects",
                isDebug),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };
    }

    public static bool IsSectionEmpty(Section section, CvDataModel model)
    {
        return section switch
        {
            Section.Languages => model.Languages.IsEmpty,
            Section.WorkExperience => model.WorkExperiences.IsEmpty,
            Section.Education => model.Educations.IsEmpty,
            Section.PersonalProjects => model.PersonalProjects.IsEmpty,
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };
    }

    public static string RenderProductionSection(string innerLatex)
    {
        if (string.IsNullOrWhiteSpace(innerLatex))
        {
            return string.Empty;
        }

        return $$"""
            \begin{flowblock}
            {{innerLatex}}
            \end{flowblock}
            """;
    }

    public static string RenderLanguagesSectionInner(
        ImmutableArray<LanguageProficiencyInfo> languages)
    {
        if (languages.IsEmpty)
        {
            return string.Empty;
        }

        var rows = languages.Select(static language =>
        {
            var skills = string.Join(", ", language.Skills.Select(static skill => skill.Text.ToString()));
            return $"{language.Language.Name} & {language.GeneralProficiencyLevel.Value} & {skills} \\\\";
        });

        return $$"""
            \cvsection{Languages}

            \languagetable{
            {{string.Join(Environment.NewLine, rows)}}
            }
            """;
    }

    public static string RenderEventsSectionInner(
        ImmutableArray<Event> events,
        string sectionName,
        bool isDebug)
    {
        if (events.IsEmpty)
        {
            return string.Empty;
        }

        var renderedEvents = events.Select(@event => RenderEvent(@event, isDebug));
        return $$"""
            \cvsection{ {{new LatexEscapedString(sectionName)}} }

            {{string.Join(Environment.NewLine + Environment.NewLine, renderedEvents)}}
            """;
    }

    public static string RenderEvent(Event @event, bool isDebug)
    {
        var itemFragments = new List<string>(@event.SubItems.Length + (@event.Urls.IsEmpty ? 0 : 1));
        foreach (var item in @event.SubItems)
        {
            itemFragments.Add(RenderEventItem(
                $"{RenderDebugScore(item.DebugScore, item.DebugTagScores, isDebug)}{item.String}"));
        }

        if (!@event.Urls.IsEmpty)
        {
            var urls = string.Join(" | ", @event.Urls.Select(static url => $"\\url{{{url}}}"));
            itemFragments.Add(RenderEventItem($"\\textbf{{Links:}} {urls}"));
        }

        var place = @event.Place.IsPersonal ? string.Empty : @event.Place.Name.ToString();
        var title = $"{RenderDebugScore(@event.DebugScore, @event.DebugTagScores, isDebug)}{@event.Title}";
        return RenderEventCore(
            @event.DateRange.ToString(),
            title,
            place,
            string.Join(Environment.NewLine, itemFragments),
            @event.Text.ToString());
    }

    public static string RenderExperienceChrome(ExperienceList list)
    {
        var place = list.Place.IsPersonal ? string.Empty : list.Place.Name.ToString();
        var permanentItems = string.Empty;
        if (!list.Urls.IsEmpty)
        {
            var urls = string.Join(" | ", list.Urls.Select(static url => $"\\url{{{url}}}"));
            permanentItems = RenderEventItem($"\\textbf{{Links:}} {urls}");
        }

        return RenderEventCore(
            list.DateRange.ToString(),
            list.Title.ToString(),
            place,
            permanentItems,
            list.Description.ToString());
    }

    public static string RenderExperienceItem(ExperienceListItem item)
        => $"\\cveventitems{{{RenderEventItem(item.Text.ToLatexString().ToString())}}}";

    public static string RenderSectionChrome(Section section)
        => $"\\cvsection{{{new LatexEscapedString(GetSectionTitle(section))}}}";

    public static string RenderDocumentChrome(CvDataModel model)
        => RenderDocumentHeader(model) + RenderDocumentFooter(model);

    public static string RenderDocumentHeader(CvDataModel model)
    {
        var result = new StringBuilder();
        result.AppendLine(@"\vspace{-8pt}");
        result.AppendLine(@"\begin{center}");
        result.Append("\\HUGE \\textsc{")
            .Append(model.Name.Last)
            .Append(' ')
            .Append(model.Name.First)
            .AppendLine(@"} \textcolor{sectcol}{\rule[-1mm]{1mm}{0.9cm}} \textsc{Resume}\\[2pt]");
        result.Append("\\small ").AppendLine(model.Profession.Value.ToString());
        result.AppendLine(@"\end{center}");
        result.AppendLine(@"\vspace{6pt}");
        result.Append(RenderMetadata(model));
        if (!model.Summary.IsNull)
        {
            result.AppendLine(@"\vspace{-6pt}");
            result.AppendLine(@"\cvsection{Summary}");
            result.Append(model.Summary).AppendLine(@"\");
        }
        return result.ToString();
    }

    private static string RenderEventCore(
        string date,
        string title,
        string place,
        string items,
        string description)
        => $"\\cvevent{{{date}}}{{{title}}}{{{place}}}{{{items}}}{{{description}}}";

    private static string RenderEventItem(string content) => $"\\item {content}";

    private static string RenderDebugScore(
        float score,
        ImmutableArray<DebugTagScore> tagScores,
        bool isDebug)
    {
        if (!isDebug)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        text.Append(score.ToString("0.##", CultureInfo.InvariantCulture));
        if (!tagScores.IsDefaultOrEmpty)
        {
            text.Append(" (");
            for (var i = 0; i < tagScores.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }
                text.Append(tagScores[i].Tag.Value)
                    .Append(':')
                    .Append(tagScores[i].Score.ToString("0.##", CultureInfo.InvariantCulture));
            }
            text.Append(')');
        }

        return $"\\debugscore{{{new LatexEscapedString(text.ToString())}}}";
    }

    private static string GetSectionTitle(Section section) => section switch
    {
        Section.Languages => "Languages",
        Section.WorkExperience => "Experience",
        Section.Education => "Education",
        Section.PersonalProjects => "Personal Projects",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    private static string RenderMetadata(CvDataModel model)
    {
        var count = Math.Max(model.CategorizedInfos.Length, model.CategorizedInfoLists.Length);
        var result = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var info = i < model.CategorizedInfos.Length ? model.CategorizedInfos[i] : default;
            var list = i < model.CategorizedInfoLists.Length ? model.CategorizedInfoLists[i] : default;
            var infoText = info == default ? string.Empty : FormatCategoryValue(info.Category, info.Value);
            var listText = list == default
                ? string.Empty
                : $"\\textbf{{{list.Category.DisplayName}:}} {string.Join(", ", list.Values.Select(value => FormatCategoryValue(list.Category, value)))}";
            result.Append("\\metasection{").Append(infoText).Append("}{").Append(listText).AppendLine("}");
        }

        result.AppendLine(@"\vspace{-2pt}");
        result.AppendLine(@"\textcolor{softcol}{\hrule}");
        result.AppendLine(@"\vspace{6pt}");
        result.AppendLine(@"\normalsize");
        return result.ToString();
    }

    private static string FormatCategoryValue(Category category, RegularString value)
        => category.IsUrl ? $"\\url{{{value}}}" : value.ToString();

    public static string RenderDocumentFooter(CvDataModel model)
    {
        var items = new List<string>(2);
        if (!model.Website.IsNull)
        {
            items.Add($"\\textnormal{{\\textcolor{{sectcol}}{{ \\url{{{model.Website}}} }}}}");
        }
        if (!model.GitHub.IsNull)
        {
            items.Add($"\\textcolor{{sectcol}}{{ \\url{{{model.GitHub}}} }}");
        }
        if (items.Count == 0)
        {
            return string.Empty;
        }

        return "\\null" + Environment.NewLine
            + "\\vspace*{\\fill}" + Environment.NewLine
            + "\\hspace{-0.25\\linewidth}\\colorbox{white}{\\makebox[1.5\\linewidth][c]{\\mystrut "
            + string.Join(" $\\cdot$ ", items)
            + "}}";
    }
}
