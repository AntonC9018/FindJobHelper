
using System.Text.Json.Serialization;


namespace TheirStack;

partial class TheirStackClient
{
    static partial void UpdateJsonSerializerSettings(System.Text.Json.JsonSerializerOptions settings)
    {
        settings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
        settings.Converters.Add(new EnumMemberConverterFactory());
    }
}
