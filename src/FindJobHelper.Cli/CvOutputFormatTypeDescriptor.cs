using FindJobHelper.CVGeneration;
using CommandDotNet;
using CommandDotNet.TypeDescriptors;

internal sealed class CvOutputFormatTypeDescriptor :
    IArgumentTypeDescriptor,
    IAllowedValuesTypeDescriptor
{
    // CommandDotNet also uses this collection for allowed-value validation,
    // so retain a case-insensitive comparer here as well as in ParseString.
    private static readonly HashSet<string> AllowedValues =
        new(["tex", "md"], StringComparer.OrdinalIgnoreCase);

    public bool CanSupport(Type type) => type == typeof(CvOutputFormat);

    public string GetDisplayName(IArgument argument) => nameof(CvOutputFormat);

    public object ParseString(IArgument argument, string value)
    {
        if (string.Equals(value, "tex", StringComparison.OrdinalIgnoreCase))
        {
            return CvOutputFormat.Tex;
        }
        if (string.Equals(value, "md", StringComparison.OrdinalIgnoreCase))
        {
            return CvOutputFormat.Md;
        }

        throw new FormatException();
    }

    public IEnumerable<string> GetAllowedValues(IArgument argument) =>
        AllowedValues;
}
