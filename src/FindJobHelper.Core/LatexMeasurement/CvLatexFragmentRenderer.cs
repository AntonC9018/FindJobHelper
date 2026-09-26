using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using FindJobHelper.Configuration;
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
        CvDataModel model,
        LatexRenderProgressBuilder? progress = null)
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
            renderEvents: events => RenderExplicitEventsSection(
                section,
                events,
                progress));
    }

    public static FormattableString RenderFlowingSection(
        Section section,
        CvDataModel model,
        LatexRenderProgressBuilder? progress = null)
    {
        return model.DispatchSection(
            section,
            renderLanguages: languages =>
            {
                var inner = RenderLanguagesSectionInner(languages);
                return RenderProductionSection(section, inner);
            },
            renderEvents: events => RenderFlowingEventsSection(
                section,
                events,
                progress));
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

        var rows = languages.Select(static language =>
        {
            var languageName = LatexConverter.ToLatexString(language.Language.Name);
            var proficiency = LatexConverter.ToLatexString(
                language.GeneralProficiencyLevel.Value);
            var renderedSkills = language.Skills.Select(static skill =>
            {
                var renderedSkill = LatexConverter.ToLatexString(skill.Text);
                return (FormattableString) $"{renderedSkill}";
            });
            var skills = Join(renderedSkills, ", ");
            return (FormattableString)
                $"{languageName} & {proficiency} & {skills} \\\\";
        });

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
        LatexRenderProgressBuilder? progress = null)
    {
        if (events.IsEmpty)
        {
            return Empty;
        }

        var renderedEvents = events.Select(
            @event => RenderEvent(@event, sectionName, progress));
        return $$"""
            \cvsection{ {{LatexConverter.ToLatexString(sectionName)}} }

            {{Join(renderedEvents, Environment.NewLine + Environment.NewLine)}}
            """;
    }

    private static FormattableString RenderExplicitEventsSection(
        Section section,
        ImmutableArray<Event> events,
        LatexRenderProgressBuilder? progress)
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
                RenderEvent(
                    events[index],
                    section.ToDisplayString(),
                    progress),
                suffix));
        }

        return $"{Join(units, Environment.NewLine + Environment.NewLine)}";
    }

    private static FormattableString RenderFlowingEventsSection(
        Section section,
        ImmutableArray<Event> events,
        LatexRenderProgressBuilder? progress)
    {
        if (events.IsEmpty)
        {
            return Empty;
        }

        var renderedEvents = new List<FormattableString>(events.Length);
        for (var index = 0; index < events.Length; index++)
        {
            var currentPrefix = index == 0
                ? $@"\cvflowblockfitskip{RenderSectionChrome(section)}"
                : Empty;
            var freshPrefix = index == 0
                ? $@"\cvflowblocknewpageskip\cvflowblockfitskip{RenderSectionChrome(section)}"
                : Literal(@"\cvflowblocknewpageskip");
            renderedEvents.Add(RenderFlowingEvent(
                events[index],
                section.ToDisplayString(),
                currentPrefix,
                freshPrefix,
                progress));
        }

        return $$"""
            {{Join(renderedEvents, Environment.NewLine + Environment.NewLine)}}
            \cvflowblocktrailingglue
            """;
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

    public static FormattableString RenderEvent(
        Event @event,
        string? sectionName = null,
        LatexRenderProgressBuilder? progress = null)
    {
        var itemFragments = RenderEventItems(@event, sectionName, progress);

        FormattableString place = @event.Place.IsPersonal ? Empty : $"{LatexConverter.ToLatexString(@event.Place.Name)}";
        return RenderEventCore(
            $"{@event.DateRange}",
            $"{LatexConverter.ToLatexString(@event.Title)}",
            place,
            $"{Join(itemFragments, Environment.NewLine)}",
            RenderRichText(@event.Text));
    }

    private static FormattableString RenderFlowingEvent(
        Event @event,
        string sectionName,
        FormattableString currentPrefix,
        FormattableString freshPrefix,
        LatexRenderProgressBuilder? progress)
    {
        var items = RenderEventItems(@event, sectionName, progress);
        var firstItem = items.Count == 0 ? Empty : items[0];
        var remainingItems = items.Skip(1);
        FormattableString place = @event.Place.IsPersonal
            ? Empty
            : $"{LatexConverter.ToLatexString(@event.Place.Name)}";
        var start = $$"""
            \begin{cvflowingopening}
            { {{currentPrefix}} }
            { {{freshPrefix}} }
            \cvflowingeventstart
            { {{@event.DateRange}} }
            { {{LatexConverter.ToLatexString(@event.Title)}} }
            { {{place}} }
            { {{RenderRichText(@event.Text)}} }
            {{firstItem}}
            \end{cvflowingopening}
            """;
        return $$"""
            {{start}}
            {{Join(remainingItems, Environment.NewLine)}}
            \cvflowingeventend
            """;
    }

    private static List<FormattableString> RenderEventItems(
        Event @event,
        string? sectionName,
        LatexRenderProgressBuilder? progress)
    {
        var itemFragments = new List<FormattableString>(
            @event.SubItems.Length + (@event.Urls.IsEmpty ? 0 : 1));
        for (var index = 0; index < @event.SubItems.Length; index++)
        {
            var renderedItem = RenderEventItem(
                RenderRichText(@event.SubItems[index].Text));
            if (progress is not null)
            {
                var requiredSectionName = sectionName
                    ?? throw new InvalidOperationException(
                        "A section name is required when rendering bullet progress markers.");
                renderedItem = progress.WrapBullet(
                    section: requiredSectionName,
                    experienceTitle: @event.Title.Value,
                    itemNumber: index + 1,
                    itemCount: @event.SubItems.Length,
                    renderedBullet: renderedItem);
            }
            itemFragments.Add(renderedItem);
        }

        if (!@event.Urls.IsEmpty)
        {
            var urls = Join(@event.Urls.Select(static url => (FormattableString) $@"\url{{{LatexConverter.ToLatexString(url)}}}"), " | ");
            itemFragments.Add(RenderEventItem($@"\textbf{{Links:}} {urls}"));
        }

        return itemFragments;
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
            \HUGE \textsc{ {{{LatexConverter.ToLatexString(model.Name.Last)}}} {{{LatexConverter.ToLatexString(model.Name.First)}}} } \textsc{ {{{LatexConverter.ToLatexString(model.DocumentTitle)}}} }\\[2pt]
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
        var contactCells = CollectContactCells(model);
        var expertiseRows = CollectExpertiseRows(model);
        var contactTable = RenderContactTable(contactCells);
        var expertiseTable = RenderExpertiseTable(expertiseRows);
        return $$"""
            {{contactTable}}
            \vspace{-2pt}
            \textcolor{softcol}{\hrule}
            \vspace{6pt}
            {{expertiseTable}}
            \normalsize
            % Match the final event padding and trailing flow-block line that
            % precede every later section.
            \vspace{6pt}
            \vspace{\cvsectionspacing}
            """;
    }

    private static List<FormattableString> CollectContactCells(CvDataModel model)
    {
        var cells = new List<FormattableString>();
        foreach (var info in model.CategorizedInfos)
        {
            var isEmpty = info == default;
            if (isEmpty)
            {
                continue;
            }

            var cell = RenderMetadataInfo(info);
            cells.Add(cell);
        }

        foreach (var list in model.CategorizedInfoLists)
        {
            var isEmpty = list == default;
            if (isEmpty)
            {
                continue;
            }

            var isExpertise = IsExpertiseCategory(list.Category);
            if (isExpertise)
            {
                continue;
            }

            var hasValues = !list.Values.IsEmpty;
            if (!hasValues)
            {
                continue;
            }

            var cell = RenderMetadataList(list);
            cells.Add(cell);
        }

        return cells;
    }

    private static List<FormattableString> CollectExpertiseRows(CvDataModel model)
    {
        var rows = new List<FormattableString>();
        foreach (var list in model.CategorizedInfoLists)
        {
            var isEmpty = list == default;
            if (isEmpty)
            {
                continue;
            }

            var isExpertise = IsExpertiseCategory(list.Category);
            if (!isExpertise)
            {
                continue;
            }

            var hasValues = !list.Values.IsEmpty;
            if (!hasValues)
            {
                continue;
            }

            var row = RenderExpertiseRow(list);
            rows.Add(row);
        }

        return rows;
    }

    private static bool IsExpertiseCategory(Category category)
    {
        var isSkills = category.Equals(Category.Skills);
        if (isSkills)
        {
            return true;
        }

        var isTechnologies = category.Equals(Category.Technologies);
        if (isTechnologies)
        {
            return true;
        }

        return false;
    }

    private static FormattableString RenderContactTable(List<FormattableString> cells)
    {
        var hasCells = cells.Count > 0;
        if (!hasCells)
        {
            return Empty;
        }

        var rows = BuildContactRows(cells);
        var joinedRows = Join(rows, Environment.NewLine);
        return $$"""
            \begin{cvcontactmetadatatable}
            {{joinedRows}}
            \end{cvcontactmetadatatable}
            """;
    }

    private static List<FormattableString> BuildContactRows(List<FormattableString> cells)
    {
        const int columns = 3;
        var paddedCount = cells.Count + columns;
        var adjustedCount = paddedCount - 1;
        var rowCount = adjustedCount / columns;
        var rows = new List<FormattableString>(rowCount);
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var row = BuildContactRow(cells, rowIndex, columns);
            rows.Add(row);
        }

        return rows;
    }

    private static FormattableString BuildContactRow(
        List<FormattableString> cells,
        int rowIndex,
        int columns)
    {
        var start = rowIndex * columns;
        var first = GetCellOrEmpty(cells, start);
        var secondIndex = start + 1;
        var second = GetCellOrEmpty(cells, secondIndex);
        var thirdIndex = start + 2;
        var third = GetCellOrEmpty(cells, thirdIndex);
        return $@"{first} & {second} & {third}\\[1pt]";
    }

    private static FormattableString GetCellOrEmpty(List<FormattableString> cells, int index)
    {
        var inRange = index < cells.Count;
        if (!inRange)
        {
            return Empty;
        }

        var cell = cells[index];
        return cell;
    }

    private static FormattableString RenderExpertiseTable(List<FormattableString> rows)
    {
        var hasRows = rows.Count > 0;
        if (!hasRows)
        {
            return Empty;
        }

        var joinedRows = Join(rows, Environment.NewLine);
        return $$"""
            \begin{cvexpertisetable}
            {{joinedRows}}
            \end{cvexpertisetable}
            """;
    }

    private static FormattableString RenderExpertiseRow(CategorizedInfoList list)
    {
        var label = LatexConverter.ToLatexString(list.Category.DisplayName);
        var category = list.Category;
        var renderedValues = list.Values.Select(value => FormatCategoryValue(category, value));
        var values = Join(renderedValues, ", ");
        return $@"\expertiserow{{{label}}}{{{values}}}";
    }

    private static FormattableString RenderMetadataInfo(CategorizedInfo info)
    {
        var isEmpty = info == default;
        if (isEmpty)
        {
            return Empty;
        }

        var category = LatexConverter.ToLatexString(info.Category.DisplayName);
        var value = FormatCategoryValue(info.Category, info.Value);
        return $@"\textbf{{{category}:}} {value}";
    }

    private static FormattableString RenderMetadataList(CategorizedInfoList list)
    {
        var isEmpty = list == default;
        if (isEmpty)
        {
            return Empty;
        }

        var category = LatexConverter.ToLatexString(list.Category.DisplayName);
        var listCategory = list.Category;
        var renderedValues = list.Values.Select(value =>
            FormatCategoryValue(listCategory, value));
        var values = Join(renderedValues, ", ");
        return $@"\textbf{{{category}:}} {values}";
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
