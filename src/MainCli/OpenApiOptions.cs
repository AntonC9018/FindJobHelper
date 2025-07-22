using System.ComponentModel.DataAnnotations;

public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    [MinLength(1)]
    public required string SecretKey { get; set; }
}

public sealed class TheirStackOptions
{
    public const string SectionName = "TheirStack";

    [MinLength(1)]
    public required string SecretKey { get; set; }
}
