using System.Globalization;

namespace FindJobHelper.CVGeneration;

internal enum LatexProgressMarkerEvent
{
    Started,
    Completed,
}

internal enum LatexProgressMarkerCategory
{
    Measurement,
    RenderBullet,
}

internal readonly record struct LatexProgressMarkerId
{
    private const int MaximumValue = 99_999_999;

    public LatexProgressMarkerId(
        LatexProgressMarkerCategory category,
        int value)
    {
        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "The LaTeX progress marker category is invalid.");
        }
        if (value is <= 0 or > MaximumValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"The LaTeX progress marker value must be between 1 and {MaximumValue}.");
        }

        Category = category;
        Value = value;
    }

    public LatexProgressMarkerCategory Category { get; }

    public int Value { get; }

    public override string ToString()
        => $"{CategoryPrefix(Category)}{Value.ToString("D8", CultureInfo.InvariantCulture)}";

    public static bool TryParse(
        ReadOnlySpan<char> token,
        out LatexProgressMarkerId markerId)
    {
        markerId = default;
        if (token.Length != 9
            || !TryParseCategory(token[0], out var category)
            || !int.TryParse(
                token[1..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value)
            || value <= 0)
        {
            return false;
        }

        markerId = new(category, value);
        return true;
    }

    private static char CategoryPrefix(LatexProgressMarkerCategory category)
    {
        return category switch
        {
            LatexProgressMarkerCategory.Measurement => 'M',
            LatexProgressMarkerCategory.RenderBullet => 'B',
            _ => throw new ArgumentOutOfRangeException(
                nameof(category),
                category,
                "The LaTeX progress marker category is invalid."),
        };
    }

    private static bool TryParseCategory(
        char prefix,
        out LatexProgressMarkerCategory category)
    {
        switch (prefix)
        {
            case 'M':
                category = LatexProgressMarkerCategory.Measurement;
                return true;
            case 'B':
                category = LatexProgressMarkerCategory.RenderBullet;
                return true;
            default:
                category = default;
                return false;
        }
    }
}

internal readonly record struct LatexProgressMarker(
    LatexProgressMarkerEvent Event,
    LatexProgressMarkerId Id);

internal static class LatexProgressMarkerProtocol
{
    private const string StartedPrefix = "FJH_PROGRESS_STARTED:";
    private const string CompletedPrefix = "FJH_PROGRESS_COMPLETED:";

    public static string RenderTypeout(
        LatexProgressMarkerEvent markerEvent,
        LatexProgressMarkerId markerId)
        => $@"\typeout{{{FormatLogLine(markerEvent, markerId)}}}";

    public static string FormatLogLine(
        LatexProgressMarkerEvent markerEvent,
        LatexProgressMarkerId markerId)
        => $"{Prefix(markerEvent)}{markerId}";

    public static bool TryParse(
        string line,
        out LatexProgressMarker marker)
    {
        ArgumentNullException.ThrowIfNull(line);

        marker = default;
        foreach (var markerEvent in Enum.GetValues<LatexProgressMarkerEvent>())
        {
            var prefix = Prefix(markerEvent);
            var markerIndex = line.IndexOf(prefix, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                continue;
            }

            var token = line.AsSpan(markerIndex + prefix.Length).Trim();
            if (!LatexProgressMarkerId.TryParse(token, out var markerId))
            {
                return false;
            }

            marker = new(markerEvent, markerId);
            return true;
        }

        return false;
    }

    private static string Prefix(LatexProgressMarkerEvent markerEvent)
    {
        return markerEvent switch
        {
            LatexProgressMarkerEvent.Started => StartedPrefix,
            LatexProgressMarkerEvent.Completed => CompletedPrefix,
            _ => throw new ArgumentOutOfRangeException(
                nameof(markerEvent),
                markerEvent,
                "The LaTeX progress marker event is invalid."),
        };
    }
}
