using System.Collections.Immutable;

namespace FindJobHelper.Configuration;

public sealed class CvConfigurationException : Exception
{
    public ImmutableArray<string> Errors { get; }

    public CvConfigurationException(string message)
        : base(message)
    {
        Errors = [message];
    }

    public CvConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    public CvConfigurationException(IEnumerable<string> errors)
        : this([.. errors])
    {
    }

    private CvConfigurationException(ImmutableArray<string> errors)
        : base(FormatErrors(errors))
    {
        if (errors.IsEmpty)
        {
            throw new ArgumentException("At least one configuration error is required.", nameof(errors));
        }

        Errors = errors;
    }

    private static string FormatErrors(ImmutableArray<string> errors)
    {
        return string.Join(Environment.NewLine, errors.Select(static error => $"- {error}"));
    }
}
