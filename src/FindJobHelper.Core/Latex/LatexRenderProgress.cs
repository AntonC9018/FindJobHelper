using System.Collections.Immutable;

namespace FindJobHelper.CVGeneration;

internal readonly record struct LatexRenderBullet(
    LatexProgressMarkerId MarkerId,
    string Section,
    string ExperienceTitle,
    int ItemNumber,
    int ItemCount);

internal sealed class LatexRenderProgressPlan
{
    public static LatexRenderProgressPlan Empty { get; } = new([]);

    public LatexRenderProgressPlan(
        ImmutableArray<LatexRenderBullet> bullets)
    {
        Bullets = bullets.IsDefault ? [] : bullets;
    }

    public ImmutableArray<LatexRenderBullet> Bullets { get; }

    public bool TryGetBullet(
        LatexProgressMarkerId markerId,
        out LatexRenderBullet bullet)
    {
        bullet = default;
        if (markerId.Category != LatexProgressMarkerCategory.RenderBullet
            || markerId.Value <= 0
            || markerId.Value > Bullets.Length)
        {
            return false;
        }

        bullet = Bullets[markerId.Value - 1];
        return bullet.MarkerId == markerId;
    }
}

internal sealed class LatexRenderProgressBuilder
{
    private readonly ImmutableArray<LatexRenderBullet>.Builder _bullets =
        ImmutableArray.CreateBuilder<LatexRenderBullet>();
    private bool _built;

    public FormattableString WrapBullet(
        string section,
        string experienceTitle,
        int itemNumber,
        int itemCount,
        FormattableString renderedBullet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(experienceTitle);
        ArgumentNullException.ThrowIfNull(renderedBullet);
        if (_built)
        {
            throw new InvalidOperationException(
                "LaTeX render progress markers cannot be registered after the plan is built.");
        }
        if (itemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemCount),
                itemCount,
                "The experience bullet count must be positive.");
        }
        if (itemNumber <= 0 || itemNumber > itemCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemNumber),
                itemNumber,
                "The bullet number must be within the experience's bullet count.");
        }

        var markerId = new LatexProgressMarkerId(
            LatexProgressMarkerCategory.RenderBullet,
            checked(_bullets.Count + 1));
        _bullets.Add(new(
            MarkerId: markerId,
            Section: section,
            ExperienceTitle: experienceTitle,
            ItemNumber: itemNumber,
            ItemCount: itemCount));

        var started = LatexProgressMarkerProtocol.RenderTypeout(
            LatexProgressMarkerEvent.Started,
            markerId);
        var completed = LatexProgressMarkerProtocol.RenderTypeout(
            LatexProgressMarkerEvent.Completed,
            markerId);
        return $$"""
            {{started}}
            {{renderedBullet}}
            {{completed}}
            """;
    }

    public LatexRenderProgressPlan Build()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "The LaTeX render progress plan has already been built.");
        }

        _built = true;
        return new(_bullets.ToImmutable());
    }
}
