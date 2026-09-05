using System.Collections.Immutable;

namespace FindJobHelper.Configuration;

public sealed record CvPageLayoutBlock
{
    public CvPageLayoutBlock(
        int firstPage,
        int lastPage,
        ImmutableArray<Section> sections)
    {
        if (firstPage <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstPage),
                "The first page must be positive.");
        }
        if (lastPage < firstPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lastPage),
                "The last page must not precede the first page.");
        }
        if (sections.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A page-layout block must contain at least one section.",
                nameof(sections));
        }
        if (sections.Any(static section => !Enum.IsDefined(section)))
        {
            throw new ArgumentException(
                "A page-layout block contains an invalid section.",
                nameof(sections));
        }
        if (sections.Distinct().Count() != sections.Length)
        {
            throw new ArgumentException(
                "A page-layout block cannot contain duplicate sections.",
                nameof(sections));
        }

        FirstPage = firstPage;
        LastPage = lastPage;
        Sections = sections;
    }

    public int FirstPage { get; }

    public int LastPage { get; }

    public int AllocatedPageCount => checked(LastPage - FirstPage + 1);

    public ImmutableArray<Section> Sections { get; }

    public string ConfiguredPages => FirstPage == LastPage
        ? FirstPage.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : $"{FirstPage.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{LastPage.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

public sealed record CvPageLayout
{
    public CvPageLayout(ImmutableArray<CvPageLayoutBlock> blocks)
    {
        if (blocks.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "An explicit page layout must contain at least one block.",
                nameof(blocks));
        }

        var expectedFirstPage = 1;
        var sections = ImmutableArray.CreateBuilder<Section>();
        var seenSections = new HashSet<Section>();
        for (var blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
        {
            var block = blocks[blockIndex];
            ArgumentNullException.ThrowIfNull(block);
            if (block.FirstPage != expectedFirstPage)
            {
                throw new ArgumentException(
                    "Explicit page-layout blocks must begin at page 1 and form a contiguous, ordered, non-overlapping sequence.",
                    nameof(blocks));
            }

            foreach (var section in block.Sections)
            {
                if (!seenSections.Add(section))
                {
                    throw new ArgumentException(
                        $"Section '{section}' occurs more than once in the explicit page layout.",
                        nameof(blocks));
                }
                sections.Add(section);
            }

            if (blockIndex < blocks.Length - 1)
            {
                expectedFirstPage = checked(block.LastPage + 1);
            }
        }

        Blocks = blocks;
        SectionOrder = sections.DrainToImmutable();
        PageCount = blocks[^1].LastPage;
    }

    public ImmutableArray<CvPageLayoutBlock> Blocks { get; }

    public ImmutableArray<Section> SectionOrder { get; }

    public int PageCount { get; }
}
