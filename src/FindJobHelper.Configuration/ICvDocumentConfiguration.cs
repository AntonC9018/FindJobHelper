using System.Collections.Immutable;

namespace FindJobHelper.Configuration;

public interface ICvDocumentConfiguration
{
    ImmutableArray<string> Skills { get; }

    ImmutableArray<string> Technologies { get; }

    ImmutableArray<Section> SectionOrder { get; }

    string? Profession { get; }

    ImmutableArray<HeaderLinkName> HeaderLinkOrder { get; }
}
