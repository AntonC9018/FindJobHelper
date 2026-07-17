using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FindJobHelper.Core;

namespace FindJobHelper.CVGeneration;

/// <summary>
/// Pure LaTeX fragment rendering shared by production generation and height
/// measurement. Layout decisions stay in cv_template_config.tex.
/// </summary>
internal static class CvLatexFragmentRenderer
{
    private static FormattableString Empty { get; } = FormattableStringFactory.Create(string.Empty);

    public static FormattableString RenderSectionInner(
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

    public static FormattableString RenderProductionSection(FormattableString innerLatex)
    {
        if (innerLatex.Format.Length == 0)
        {
            return Empty;
        }

        return $$"""
            \begin{flowblock}
            {{innerLatex}}
            \end{flowblock}
            """;
    }

    public static FormattableString RenderLanguagesSectionInner(
        ImmutableArray<LanguageProficiencyInfo> languages)
    {
        if (languages.IsEmpty)
        {
            return Empty;
        }

        var rows = languages.Select(static language => (FormattableString)
            $"{language.Language.Name} & {language.GeneralProficiencyLevel.Value} & {Join(language.Skills.Select(static skill => (FormattableString) $"{skill.Text}"), ", ")} \\\\");

        return $$"""
            \cvsection{Languages}

            \languagetable{
            {{Join(rows, Environment.NewLine)}}
            }
            """;
    }

    public static FormattableString RenderEventsSectionInner(
        ImmutableArray<Event> events,
        string sectionName,
        bool isDebug)
    {
        if (events.IsEmpty)
        {
            return Empty;
        }

        var renderedEvents = events.Select(@event => RenderEvent(@event, isDebug));
        return $$"""
            \cvsection{ {{new LatexEscapedString(sectionName)}} }

            {{Join(renderedEvents, Environment.NewLine + Environment.NewLine)}}
            """;
    }

    public static FormattableString RenderEvent(Event @event, bool isDebug)
    {
        var itemFragments = new List<FormattableString>(@event.SubItems.Length + (@event.Urls.IsEmpty ? 0 : 1));
        foreach (var item in @event.SubItems)
        {
            itemFragments.Add(RenderEventItem(
                $"{RenderDebugScore(item.DebugScore, item.DebugTagScores, isDebug)}{item.String}"));
        }

        if (!@event.Urls.IsEmpty)
        {
            var urls = Join(@event.Urls.Select(static url => (FormattableString) $@"\url{{{url}}}"), " | ");
            itemFragments.Add(RenderEventItem($@"\textbf{{Links:}} {urls}"));
        }

        FormattableString place = @event.Place.IsPersonal ? Empty : $"{@event.Place.Name}";
        FormattableString title = $"{RenderDebugScore(@event.DebugScore, @event.DebugTagScores, isDebug)}{@event.Title}";
        return RenderEventCore(
            $"{@event.DateRange}",
            title,
            place,
            $"{Join(itemFragments, Environment.NewLine)}",
            $"{@event.Text}");
    }

    public static FormattableString RenderExperienceChrome(ExperienceList list)
    {
        FormattableString place = list.Place.IsPersonal ? Empty : $"{list.Place.Name}";
        FormattableString permanentItems = Empty;
        if (!list.Urls.IsEmpty)
        {
            var urls = Join(list.Urls.Select(static url => (FormattableString) $@"\url{{{url}}}"), " | ");
            permanentItems = RenderEventItem($@"\textbf{{Links:}} {urls}");
        }

        return RenderEventCore(
            $"{list.DateRange}",
            $"{list.Title}",
            place,
            permanentItems,
            $"{list.Description}");
    }

    public static FormattableString RenderExperienceHeading(ExperienceList list)
    {
        FormattableString place = list.Place.IsPersonal ? Empty : $"{list.Place.Name}";
        return RenderEventCore(
            $"{list.DateRange}",
            $"{list.Title}",
            place,
            Empty,
            Empty);
    }

    public static FormattableString RenderExperienceItem(ExperienceListItem item)
        => $"{item.Text.ToLatexString()}";

    public static FormattableString RenderSectionChrome(Section section)
        => $@"\cvsection{{{new LatexEscapedString(GetSectionTitle(section))}}}";

    public static FormattableString RenderDocumentChrome(CvDataModel model)
        => $"{RenderDocumentHeader(model)}{RenderDocumentFooter(model)}";

    public static FormattableString RenderDocumentHeader(CvDataModel model)
    {
        FormattableString summary = Empty;
        if (!model.Summary.IsNull)
        {
            summary = $$"""
                \vspace{-6pt}
                \cvsection{Summary}
                {{model.Summary}}\\
                """;
        }

        return $$$"""
            \vspace{-8pt}
            \begin{center}
            \HUGE \textsc{ {{{model.Name.Last}}} {{{model.Name.First}}} } \textsc{Resume}\\[2pt]
            \small {{{model.Profession.Value}}}
            \end{center}
            \vspace{6pt}
            {{{RenderMetadata(model)}}}{{{summary}}}
            """;
    }

    private static FormattableString RenderEventCore(
        FormattableString date,
        FormattableString title,
        FormattableString place,
        FormattableString items,
        FormattableString description)
        => $@"\cvevent{{{date}}}{{{title}}}{{{place}}}{{{items}}}{{{description}}}";

    private static FormattableString RenderEventItem(FormattableString content)
        => $@"\cveventitem{{{content}}}";

    private static FormattableString RenderDebugScore(
        float score,
        ImmutableArray<DebugTagScore> tagScores,
        bool isDebug)
    {
        if (!isDebug)
        {
            return Empty;
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

        return $@"\debugscore{{{new LatexEscapedString(text.ToString())}}}";
    }

    private static string GetSectionTitle(Section section) => section switch
    {
        Section.Languages => "Languages",
        Section.WorkExperience => "Experience",
        Section.Education => "Education",
        Section.PersonalProjects => "Personal Projects",
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    private static FormattableString RenderMetadata(CvDataModel model)
    {
        var count = Math.Max(model.CategorizedInfos.Length, model.CategorizedInfoLists.Length);
        var rows = new List<FormattableString>(count);
        for (var i = 0; i < count; i++)
        {
            var info = i < model.CategorizedInfos.Length ? model.CategorizedInfos[i] : default;
            var list = i < model.CategorizedInfoLists.Length ? model.CategorizedInfoLists[i] : default;
            FormattableString infoText = info == default
                ? Empty
                : $@"\textbf{{{new LatexEscapedString(info.Category.DisplayName)}:}} {FormatCategoryValue(info.Category, info.Value)}";
            FormattableString listText = list == default
                ? Empty
                : $@"\textbf{{{new LatexEscapedString(list.Category.DisplayName)}:}} {Join(list.Values.Select(value => FormatCategoryValue(list.Category, value)), ", ")}";
            rows.Add($@"\metasection{{{infoText}}}{{{listText}}}");
        }

        FormattableString table = rows.Count == 0
            ? Empty
            : $$"""
                \begin{cvmetasectiontable}
                {{Join(rows, Environment.NewLine)}}
                \end{cvmetasectiontable}
                """;

        return $$"""
            {{table}}
            \vspace{-2pt}
            \textcolor{softcol}{\hrule}
            \vspace{6pt}
            \normalsize
            % Match the final event padding and trailing flow-block line that
            % precede every later section.
            \vspace{6pt}
            \vspace{\cvsectionspacing}
            """;
    }

    private static FormattableString FormatCategoryValue(Category category, RegularString value)
        => category.IsUrl ? (FormattableString) $@"\url{{{value}}}" : $"{value}";

    public static FormattableString RenderDocumentFooter(CvDataModel model)
    {
        var items = new List<FormattableString>(2);
        if (!model.Website.IsNull)
        {
            items.Add($$$"""\textnormal{\textcolor{sectcol}{ \url{ {{{model.Website}}} } }}""");
        }
        if (!model.GitHub.IsNull)
        {
            items.Add($$"""\textcolor{sectcol}{ \url{ {{model.GitHub}} } }""");
        }
        if (items.Count == 0)
        {
            return Empty;
        }

        return $$$"""
            \null
            \vspace*{\fill}
            \hspace{-0.25\linewidth}\colorbox{white}{\makebox[1.5\linewidth][c]{\mystrut {{{Join(items, " $\\cdot$ ")}}}}}
            """;
    }

    public static string Materialize(FormattableString fragment)
        => fragment.ToString(CultureInfo.InvariantCulture);

    private static JoinedFormattableStrings Join(
        IEnumerable<FormattableString> fragments,
        string separator)
        => new(fragments, separator);

    private sealed class JoinedFormattableStrings(
        IEnumerable<FormattableString> fragments,
        string separator) : IFormattable
    {
        private readonly IReadOnlyList<FormattableString> _fragments = fragments.ToArray();

        public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            var result = new StringBuilder();
            for (var i = 0; i < _fragments.Count; i++)
            {
                if (i > 0)
                {
                    result.Append(separator);
                }
                var fragment = _fragments[i];
                result.AppendFormat(formatProvider, fragment.Format, fragment.GetArguments());
            }
            return result.ToString();
        }
    }
}
