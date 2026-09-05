namespace FindJobHelper.Configuration;

public readonly record struct CvPageCount
{
    public CvPageCount(int exactCount)
    {
        if (exactCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exactCount),
                "An exact CV page count must be positive.");
        }

        ExactCount = exactCount;
    }

    public int? ExactCount { get; }

    public bool IsExact => ExactCount.HasValue;

    public bool IsUnrestricted => !IsExact;

    public static CvPageCount OnePage { get; } = new(1);

    public static CvPageCount Unrestricted => default;

    public static CvPageCount Exact(int count) => new(count);

    public override string ToString()
        => ExactCount switch
        {
            1 => "Exactly 1 page",
            { } count => $"Exactly {count} pages",
            null => "Unrestricted page count",
        };
}
