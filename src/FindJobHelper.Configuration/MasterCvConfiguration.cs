using System.Collections.Immutable;

namespace FindJobHelper.Configuration;

public sealed class MasterCvConfiguration : ICvDocumentConfiguration
{
    internal MasterCvConfiguration(
        ImmutableArray<string> skills,
        ImmutableArray<string> technologies,
        ImmutableArray<Section> sectionOrder,
        string? profession,
        ImmutableArray<HeaderLinkName> headerLinkOrder)
    {
        Skills = skills;
        Technologies = technologies;
        SectionOrder = sectionOrder;
        Profession = profession;
        HeaderLinkOrder = headerLinkOrder;
    }

    public ImmutableArray<string> Skills { get; }

    public ImmutableArray<string> Technologies { get; }

    public ImmutableArray<Section> SectionOrder { get; }

    public string? Profession { get; }

    public ImmutableArray<HeaderLinkName> HeaderLinkOrder { get; }
}
