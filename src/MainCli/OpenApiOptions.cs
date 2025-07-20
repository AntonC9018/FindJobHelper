using System.ComponentModel.DataAnnotations;

public sealed class OpenApiOptions
{
    public const string SectionName = "OpenApi";

    [MinLength(1)]
    public required string SecretKey { get; set; }
}
