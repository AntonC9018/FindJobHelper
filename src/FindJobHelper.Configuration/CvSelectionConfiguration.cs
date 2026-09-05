using System.Collections.Immutable;

namespace FindJobHelper.Configuration;

public sealed class CvSelectionConfiguration
{
    internal CvSelectionConfiguration(
        CvPageCount pageCount,
        ImmutableArray<RequiredTagConfiguration> requiredTags,
        ImmutableArray<string> skills,
        ImmutableArray<string> technologies,
        MmrOptions mmr,
        SelectionConfiguration selection,
        ImmutableArray<Section> sectionOrder,
        string? profession,
        ImmutableArray<HeaderLinkName> headerLinkOrder,
        CvPageLayout? pageLayout = null)
    {
        PageCount = pageCount;
        RequiredTags = requiredTags;
        Skills = skills;
        Technologies = technologies;
        Mmr = mmr;
        Selection = selection;
        SectionOrder = sectionOrder;
        Profession = profession;
        HeaderLinkOrder = headerLinkOrder;
        PageLayout = pageLayout;
    }

    public CvPageCount PageCount { get; }

    public ImmutableArray<RequiredTagConfiguration> RequiredTags { get; }

    public ImmutableArray<string> Skills { get; }

    public ImmutableArray<string> Technologies { get; }

    public MmrOptions Mmr { get; }

    public SelectionConfiguration Selection { get; }

    public ImmutableArray<Section> SectionOrder { get; }

    public string? Profession { get; }

    public ImmutableArray<HeaderLinkName> HeaderLinkOrder { get; }

    public CvPageLayout? PageLayout { get; }
}
