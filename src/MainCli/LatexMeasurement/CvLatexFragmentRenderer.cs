using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FindJobHelper.Core;
using FindJobHelper.Core.Helper;

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
        CvDataModel model)
    {
        return model.DispatchSection(
            section,
            renderLanguages: RenderLanguagesSectionInner,
            renderEvents: events => RenderEventsSectionInner(events, section.ToDisplayString()));
    }

    public static bool IsSectionEmpty(Section section, CvDataModel model)
    {
        return model.DispatchSection(
            section,
            renderLanguages: static languages => languages.IsEmpty,
            renderEvents: static events => events.IsEmpty);
    }

    public static FormattableString RenderProductionSection(
        Section section,
        FormattableString innerLatex)
        => RenderProductionSection(section.ToString(), innerLatex);

    public static FormattableString RenderExplicitSection(
        Section section,
        CvDataModel model)
    {
        return model.DispatchSection(
            section,
            renderLanguages: languages =>
            {
                var inner = RenderLanguagesSectionInner(languages);
                return inner.Format.Length == 0
                    ? Empty
                    : RenderExplicitUnit(
                        section,
                        eventDiagnostic: null,
                        currentPrefix: Literal(@"\cvflowblockfitskip"),
                        freshPrefix: Literal(@"\cvflowblocknewpageskip\cvflowblockfitskip"),
                        body: inner,
                        suffix: Literal(@"\cvexplicitsectionend"));
            },
            renderEvents: events => RenderExplicitEventsSection(section, events));
    }

    private static FormattableString RenderProductionSection(
        string sectionLabel,
        FormattableString innerLatex)
    {
        if (innerLatex.Format.Length == 0)
        {
            return Empty;
        }

        return $$"""
            \begin{flowblock}{ {{LatexConverter.ToLatexString(sectionLabel)}} }
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
            $"{LatexConverter.ToLatexString(language.Language.Name)} & {LatexConverter.ToLatexString(language.GeneralProficiencyLevel.Value)} & {Join(language.Skills.Select(static skill => (FormattableString) $"{LatexConverter.ToLatexString(skill.Text)}"), ", ")} \\\\");

        return $$"""
            \cvsection{Languages}

            \languagetable{
            {{Join(rows, Environment.NewLine)}}
            }
            """;
    }

    public static FormattableString RenderEventsSectionInner(
        ImmutableArray<Event> events,
        string sectionName)
    {
        if (events.IsEmpty)
        {
            return Empty;
        }

        var renderedEvents = events.Select(RenderEvent);
        return $$"""
            \cvsection{ {{LatexConverter.ToLatexString(sectionName)}} }

            {{Join(renderedEvents, Environment.NewLine + Environment.NewLine)}}
            """;
    }

    private static FormattableString RenderExplicitEventsSection(
        Section section,
        ImmutableArray<Event> events)
    {
        if (events.IsEmpty)
        {
            return Empty;
        }

        var units = new List<FormattableString>(events.Length);
        for (var index = 0; index < events.Length; index++)
        {
            var isFirst = index == 0;
            var isLast = index == events.Length - 1;
            FormattableString currentPrefix = isFirst
                ? $@"\cvflowblockfitskip{RenderSectionChrome(section)}"
                : Empty;
            FormattableString freshPrefix = isFirst
                ? $@"\cvflowblocknewpageskip\cvflowblockfitskip{RenderSectionChrome(section)}"
                : Literal(@"\cvflowblocknewpageskip");
            FormattableString suffix = isLast
                ? Literal(@"\cvexplicitsectionend")
                : Empty;
            units.Add(RenderExplicitUnit(
                section,
                (index + 1).ToString(CultureInfo.InvariantCulture),
                currentPrefix,
                freshPrefix,
                RenderEvent(events[index]),
                suffix));
        }

        return $"{Join(units, Environment.NewLine + Environment.NewLine)}";
    }

    private static FormattableString RenderExplicitUnit(
        Section section,
        string? eventDiagnostic,
        FormattableString currentPrefix,
        FormattableString freshPrefix,
        FormattableString body,
        FormattableString suffix)
        => $$"""
            \begin{cvexplicitunit}
            { {{LatexConverter.ToLatexString(section.ToString())}} }
            { {{LatexConverter.ToLatexString(eventDiagnostic ?? string.Empty)}} }
            { {{currentPrefix}} }
            { {{freshPrefix}} }
            { {{suffix}} }
            {{body}}
            \end{cvexplicitunit}
            """;

    public static FormattableString RenderEvent(Event @event)
    {
        var itemFragments = new List<FormattableString>(@event.SubItems.Length + (@event.Urls.IsEmpty ? 0 : 1));
        foreach (var item in @event.SubItems)
        {
            itemFragments.Add(RenderEventItem(RenderRichText(item.Text)));
        }

        if (!@event.Urls.IsEmpty)
        {
            var urls = Join(@event.Urls.Select(static url => (FormattableString) $@"\url{{{LatexConverter.ToLatexString(url)}}}"), " | ");
            itemFragments.Add(RenderEventItem($@"\textbf{{Links:}} {urls}"));
        }

        FormattableString place = @event.Place.IsPersonal ? Empty : $"{LatexConverter.ToLatexString(@event.Place.Name)}";
        return RenderEventCore(
            $"{@event.DateRange}",
            $"{LatexConverter.ToLatexString(@event.Title)}",
            place,
            $"{Join(itemFragments, Environment.NewLine)}",
            RenderRichText(@event.Text));
    }

    public static FormattableString RenderExperienceChrome(ExperienceList list)
    {
        FormattableString place = list.Place.IsPersonal ? Empty : $"{LatexConverter.ToLatexString(list.Place.Name)}";
        FormattableString permanentItems = Empty;
        if (!list.Urls.IsEmpty)
        {
            var urls = Join(list.Urls.Select(static url => (FormattableString) $@"\url{{{LatexConverter.ToLatexString(url)}}}"), " | ");
            permanentItems = RenderEventItem($@"\textbf{{Links:}} {urls}");
        }

        return RenderEventCore(
            $"{list.DateRange}",
            $"{LatexConverter.ToLatexString(list.Title)}",
            place,
            permanentItems,
            RenderRichText(list.Description));
    }

    public static FormattableString RenderExperienceHeading(ExperienceList list)
    {
        FormattableString place = list.Place.IsPersonal ? Empty : $"{LatexConverter.ToLatexString(list.Place.Name)}";
        return RenderEventCore(
            $"{list.DateRange}",
            $"{LatexConverter.ToLatexString(list.Title)}",
            place,
            Empty,
            Empty);
    }

    public static FormattableString RenderExperienceItem(ExperienceListItem item)
        => RenderRichText(item.Text);

    public static FormattableString RenderSectionChrome(Section section)
        => $@"\cvsection{{{LatexConverter.ToLatexString(section.ToDisplayString())}}}";

    public static FormattableString RenderDocumentHeader(CvDataModel model)
    {
        FormattableString summary = Empty;
        if (model.Summary is not null)
        {
            summary = $$"""
                \vspace{-6pt}
                \cvsection{Summary}
                {{RenderRichText(model.Summary)}}\\
                """;
        }

        return $$$"""
            \vspace{-8pt}
            \begin{center}
            \HUGE \textsc{ {{{LatexConverter.ToLatexString(model.Name.Last)}}} {{{LatexConverter.ToLatexString(model.Name.First)}}} } \textsc{Resume}\\[2pt]
            \small {{{LatexConverter.ToLatexString(model.Profession.Value)}}}
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

    private static FormattableString RenderRichText(IRichTextNode? text)
        => text is null ? Empty : $"{LatexConverter.ToLatexString(text)}";

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
                : $@"\textbf{{{LatexConverter.ToLatexString(info.Category.DisplayName)}:}} {FormatCategoryValue(info.Category, info.Value)}";
            FormattableString listText = list == default
                ? Empty
                : $@"\textbf{{{LatexConverter.ToLatexString(list.Category.DisplayName)}:}} {Join(list.Values.Select(value => FormatCategoryValue(list.Category, value)), ", ")}";
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
        => category.IsUrl
            ? (FormattableString) $@"\url{{{LatexConverter.ToLatexString(value)}}}"
            : $"{LatexConverter.ToLatexString(value)}";

    public static FormattableString RenderDocumentFooter(CvDataModel model)
    {
        var items = new List<FormattableString>(2);
        if (!model.Website.IsNull)
        {
            items.Add($$$"""\textnormal{\textcolor{sectcol}{ \url{ {{{LatexConverter.ToLatexString(model.Website)}}} } }}""");
        }
        if (!model.GitHub.IsNull)
        {
            items.Add($$"""\textcolor{sectcol}{ \url{ {{LatexConverter.ToLatexString(model.GitHub)}} } }""");
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

    private static FormattableString Literal(string value)
        => FormattableStringFactory.Create(value);

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
