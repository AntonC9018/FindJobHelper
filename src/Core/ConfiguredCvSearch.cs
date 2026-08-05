using System.Collections.Immutable;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

public sealed record ConfiguredCvSearch(
    ExperienceSearch Search,
    CvExperienceSectionBindings Sections,
    ImmutableArray<RegularString> Skills,
    ImmutableArray<RegularString> Technologies,
    ImmutableArray<Section> SectionOrder,
    CvPageCount PageCount,
    CvPageLayout? PageLayout)
{
    public SearchResult Run(
        ExperienceDatabase database,
        CvMeasurementSnapshot measurements,
        IProgressReporter progress)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(progress);

        var admissionPolicy = new PageLayoutSelectionAdmissionPolicy(
            database,
            measurements,
            Sections,
            SectionOrder,
            PageCount,
            PageLayout);
        var result = Search.Run(database, admissionPolicy, progress);
        if (PageLayout is null)
        {
            admissionPolicy.RequireExactPageCount();
        }
        else
        {
            admissionPolicy.RequireCompletePageLayout();
        }

        return result;
    }
}
