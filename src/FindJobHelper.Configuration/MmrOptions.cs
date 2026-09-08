namespace FindJobHelper.Configuration;

public sealed record class MmrOptions(
    float RelevanceWeight,
    int SaturationQuota,
    float SaturationPenalty)
{
    public static MmrOptions Default { get; } = new(
        RelevanceWeight: 0.72f,
        SaturationQuota: 2,
        SaturationPenalty: 0.18f);

    public void Validate()
    {
        if (float.IsNaN(RelevanceWeight) || RelevanceWeight is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RelevanceWeight),
                RelevanceWeight,
                "MMR relevance weight must be between 0 and 1.");
        }

        if (SaturationQuota < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SaturationQuota),
                SaturationQuota,
                "Saturation quota must be at least 1.");
        }

        if (float.IsNaN(SaturationPenalty) || SaturationPenalty < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SaturationPenalty),
                SaturationPenalty,
                "Saturation penalty must be non-negative.");
        }
    }
}
