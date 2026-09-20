using FindJobHelper.Configuration;

namespace FindJobHelper.Configuration.Json;

public sealed class JsonMasterCvConfiguration : JsonCvDocumentConfiguration
{
    internal MasterCvConfiguration ToDomain()
    {
        var errors = new List<string>();
        var headerLinkOrder = CollectDocumentValidationErrors(errors);
        if (SectionOrder?.IsExplicit == true)
        {
            errors.Add(
                "Master CV 'sectionOrder' must use the string-array form; page-layout objects are not allowed.");
        }

        if (errors.Count > 0)
        {
            throw new CvConfigurationException(errors);
        }

        return new(
            skills: [.. Skills!],
            technologies: [.. Technologies!],
            sectionOrder: SectionOrder!.Sections,
            profession: Profession,
            headerLinkOrder: headerLinkOrder);
    }
}
