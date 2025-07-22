using System.Diagnostics;
using System.Text;
using CommandDotNet;
using NJsonSchema;
using NJsonSchema.CodeGeneration;
using NJsonSchema.CodeGeneration.CSharp;
using NSwag;
using NSwag.CodeGeneration.CSharp;
using NSwag.CodeGeneration.OperationNameGenerators;
using ScheduleLib.Parsing;

public sealed class Logic
{
    public static int Main(string[] args)
    {
        return new AppRunner<Logic>().Run(args);
    }

    [DefaultCommand]
    public async Task Generate(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        OpenApiDocument document;
        {
            using var httpClient = new HttpClient();
            var json = await httpClient.GetStringAsync("https://api.theirstack.com/openapi.json", cancellationToken);
            document = await OpenApiDocument.FromJsonAsync(json, cancellationToken: cancellationToken);
        }
        document.BasePath = "https://api.theirstack.com";

        OpenApiSchemaPatcher.FixMisclassifiedPrimitiveSchemas(document);

        // string[] ints = [
        //     "Date posted max age in days",
        //     "in the current day. If 1, from today and yesterday",
        //     "until yesterday. If 2, until 2 days ago",
        // ];
        // string[] dates = [
        //     "on this date or datetime or after will be returned",
        //     "ISO 8601 date string (yyyy-mm-dd)",
        // ];
        // OpenApiSchemaPatcher.ReplaceTypeByDescription(
        //     document,
        //     JsonObjectType.Integer,
        //     JsonFormatStrings.Integer);
        // OpenApiSchemaPatcher.ReplaceTypeByDescription(
        //     document,
        //     JsonObjectType.String,
        //     JsonFormatStrings.Date);

        const string className = "TheirStackClient";
        var settings = new CSharpClientGeneratorSettings
        {
            ClassName = className,
            CSharpGeneratorSettings =
            {
                Namespace = "TheirStack",
                JsonLibrary = CSharpJsonLibrary.SystemTextJson,
                GenerateNullableReferenceTypes = true,
                PropertyNameGenerator = new PropertyNameGenerator(),
                EnumNameGenerator = new EnumNameGenerator(),
                TypeNameGenerator = new TypeNameGenerator(),
            },
            OperationNameGenerator = new OperationNameGenerator(),
            WrapDtoExceptions = false,
        };

        var generator = new CSharpClientGenerator(document, settings);
        var code = generator.GenerateFile();
        var fileName = Path.Combine(outputDirectory, className + ".cs");
        await File.WriteAllTextAsync(fileName, code, cancellationToken);
    }
}

sealed class OperationNameGenerator : IOperationNameGenerator
{
    public bool SupportsMultipleClients => true;

    public string GetClientName(
        OpenApiDocument document,
        string path,
        string httpMethod,
        OpenApiOperation operation)
    {
        return "";
    }

    public string GetOperationName(
        OpenApiDocument document,
        string path,
        string httpMethod,
        OpenApiOperation operation)
    {
        var parser = new Parser(operation.OperationId);

        // ReadOnlySpan<char> action;
        // {
        //     var bparser = parser.BufferedView();
        //     if (!bparser.SkipUntilAny("_").SkippedAny)
        //     {
        //         throw new InvalidOperationException($"Name {parser} didn't parse");
        //     }
        //     action = parser.PeekSpanUntilPosition(bparser.Position);
        //     parser.MovePast(bparser.Position);
        // }

        ReadOnlySpan<char> op;
        {
            var bparser = parser.BufferedView();
            if (!bparser.SkipUntilSequence(["_v"]).SkippedAny)
            {
                throw new InvalidOperationException($"Name {parser} didn't parse");
            }
            op = parser.PeekSpanUntilPosition(bparser.Position);
            parser.MoveTo(bparser.Position);
            parser.Move("_v".Length);
        }

        int version;
        {
            var result = parser.ConsumePositiveInt(length: 1);
            if (result.Status != ConsumeIntStatus.Ok)
            {
                throw new InvalidOperationException($"Name {parser} didn't parse");
            }

            version = (int) result.Value;
        }

        if (!parser.ConsumeExactString("_"))
        {
            throw new InvalidOperationException($"Name {parser} didn't parse");
        }


        ReadOnlySpan<char> category = "";
        ReadOnlySpan<char> action = "";
        {
            const string separator = "__";
            var bparser = parser.BufferedView();
            var result = bparser.SkipUntilSequence([separator]);

            bool isMoreThanOneSegment = !result.EndOfInput;

            // more than one segment
            if (isMoreThanOneSegment)
            {
                category = parser.PeekSpanUntilPosition(bparser.Position);
                parser.MoveTo(bparser.Position);

                if (parser.SkipToStartOfLastSegment(separator).IsEmptySegment)
                {
                    throw new InvalidOperationException($"Name {parser} didn't parse, last segment is empty");
                }
            }

            bparser = parser.BufferedView();

            var lastResult = bparser.SkipToStartOfLastSegment("_");
            if (lastResult.IsEmptySegment || lastResult.IsEmpty)
            {
                throw new InvalidOperationException($"Name {parser} didn't parse, no action");
            }

            if (!isMoreThanOneSegment)
            {
                category = parser.PeekSpanUntilPosition(lastResult.PreviousSegmentEnd);
            }

            parser.MoveTo(bparser.Position);
            action = parser.PeekSpanUntilEnd();
        }
        _ = action;

        // there's more stuff, but we ignore it.
        var sb = new StringBuilder();
        // FromSnakeToPascal(action, sb);
        if (category.Length > 0)
        {
            CasingHelper.FromSnakeToPascal(category, sb);
            sb.Append('_');
        }
        CasingHelper.FromSnakeToPascal(op, sb);
        sb.Append($"_V{version}");
        _ = op;

        var ret = sb.ToString();
        Console.WriteLine($"{operation.OperationId} -> {ret}");
        return ret;
    }
}

sealed class PropertyNameGenerator : IPropertyNameGenerator
{
    private readonly CSharpPropertyNameGenerator _g = new();

    public string Generate(JsonSchemaProperty property)
    {
        var ret = _g.Generate(property);
        var sb = new StringBuilder();
        CasingHelper.FromSnakeToPascal(ret, sb);
        return sb.ToString();
    }
}

sealed class EnumNameGenerator : IEnumNameGenerator
{
    private readonly DefaultEnumNameGenerator _g = new();

    public string Generate(int index, string? name, object? value, JsonSchema schema)
    {
        var ret = _g.Generate(index, name, value, schema);
        var sb = new StringBuilder();
        CasingHelper.FromSnakeToPascal(ret, sb);
        return sb.ToString();
    }
}

sealed class TypeNameGenerator : ITypeNameGenerator
{
    private readonly DefaultTypeNameGenerator _g = new();

    public string Generate(JsonSchema schema, string? typeNameHint, IEnumerable<string> reservedTypeNames)
    {
        var ret = _g.Generate(schema, typeNameHint, reservedTypeNames);
        var sb = new StringBuilder();
        CasingHelper.FromSnakeToPascal(ret, sb);
        return sb.ToString();
    }
}

public static class OpenApiSchemaPatcher
{
    /// <summary>
    /// Replaces the type and format of properties that match a given description.
    /// </summary>
    /// <param name="document">The OpenAPI document.</param>
    /// <param name="descriptionToMatch">The exact description to search for.</param>
    /// <param name="newType">The new JSON object type to assign.</param>
    /// <param name="newFormat">Optional format (e.g., "int32", "date").</param>
    public static void ReplaceTypeByDescription(
        OpenApiDocument document,
        string descriptionToMatch,
        JsonObjectType newType,
        string? newFormat = null)
    {
        foreach (var schemaPair in document.Components.Schemas)
        {
            ReplaceInSchema(schemaPair.Key, schemaPair.Value, descriptionToMatch, newType, newFormat);
        }

        // Optional: if schema definitions are in Definitions (NSwag pre-OAS3)
        foreach (var schemaPair in document.Definitions)
        {
            ReplaceInSchema(schemaPair.Key, schemaPair.Value, descriptionToMatch, newType, newFormat);
        }
    }

    private static void ReplaceInSchema(
        string schemaName,
        JsonSchema schema,
        string descriptionToMatch,
        JsonObjectType newType,
        string? newFormat)
    {
        foreach (var propPair in schema.Properties)
        {
            var propName = propPair.Key;
            var prop = propPair.Value;

            if (prop.Description is not { } desc)
            {
                continue;
            }
            desc = desc.Trim();
            if (!desc.Contains(descriptionToMatch, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Console.WriteLine($"Updated type of '{propName}' in schema '{schemaName}' to {newType} ({newFormat ?? "no format"})");
            prop.Type = newType;
            prop.Format = newFormat;
        }
    }

    public static void FixMisclassifiedPrimitiveSchemas(OpenApiDocument doc)
    {
        var defs = doc.Components.Schemas.Concat(doc.Definitions);
        foreach (var (key, schema) in defs)
        {
            if (schema.Type != JsonObjectType.Object)
            {
                continue;
            }

            foreach (var propPair in schema.Properties)
            {
                var prop = propPair.Value;

                if (prop.Description is not { } desc)
                {
                    continue;
                }
                if (desc.Length == 0)
                {
                    continue;
                }
                if (prop.Type != JsonObjectType.None)
                {
                    continue;
                }
                if (prop.ActualTypeSchema.Properties.Count != 0)
                {
                    continue;
                }
                desc = desc.Trim();

                UpdateProp(desc, prop);
            }

            foreach (var propPair in schema.Properties)
            {
                if (!propPair.Value.IsNullable(SchemaType.Swagger2))
                {
                    continue;
                }
                if (!propPair.Value.IsDeprecated)
                {
                    continue;
                }
                propPair.Value.IsNullableRaw = true;
                propPair.Value.IsRequired = false;
                propPair.Value.Default = null;
            }
        }
    }

    private static void UpdateProp(string description, JsonSchemaProperty p)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(description));

        var descriptionWords = description.Split();

        bool ContainsWordsInOrder(string words)
        {
            var searchWords = words.Split();
            for (int indexInTarget = 0; indexInTarget < descriptionWords.Length - searchWords.Length + 1; indexInTarget++)
            {
                if (IsMatch())
                {
                    return true;
                }
                continue;

                bool IsMatch()
                {
                    for (int indexInSearch = 0; indexInSearch < searchWords.Length; indexInSearch++)
                    {
                        var searchWord = searchWords[indexInSearch].AsSpan();
                        var targetWord = descriptionWords[indexInTarget + indexInSearch].AsSpan();
                        ReadOnlySpan<char> RemovePunctuationAfter(ReadOnlySpan<char> t)
                        {
                            while (true)
                            {
                                if (t.Length == 0)
                                {
                                    return t;
                                }
                                if (t.EndsWith(":") || t.EndsWith(".") || t.EndsWith(","))
                                {
                                    t = t[.. ^1];
                                    continue;
                                }
                                return t;
                            }
                        }

                        targetWord = RemovePunctuationAfter(targetWord);
                        Debug.Assert(searchWord.Length == RemovePunctuationAfter(searchWord).Length);
                        if (!searchWord.Equals(targetWord, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                    return true;
                }
            }
            return false;
        }

        if (description.Any(char.IsDigit))
        {
            p.Type = JsonObjectType.Integer;
            p.Format = JsonFormatStrings.Integer;
            return;
        }

        bool containsDateFormat = ContainsWordsInOrder("ISO 8601")
            || ContainsWordsInOrder("UTC")
            || description.Contains("'YYYY-MM-DD'", StringComparison.OrdinalIgnoreCase);

        if (containsDateFormat && ContainsWordsInOrder("datetime")
            || ContainsWordsInOrder("date and time"))
        {
            p.Type = JsonObjectType.String;
            p.Format = JsonFormatStrings.DateTime;
            return;
        }
        if (containsDateFormat && ContainsWordsInOrder("date")
            || ContainsWordsInOrder("date when")
            || ContainsWordsInOrder("billing cycle period"))
        {
            p.Type = JsonObjectType.String;
            p.Format = JsonFormatStrings.Date;
            return;
        }
        if (description.StartsWith("whether", StringComparison.OrdinalIgnoreCase)
            || ContainsWordsInOrder("only return")
            || description.StartsWith("Is a", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Is the", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Is this", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("If the", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Indicates whether", StringComparison.OrdinalIgnoreCase))
        {
            p.Type = JsonObjectType.Boolean;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("True") || ContainsWordsInOrder("False"))
        {
            p.Type = JsonObjectType.Boolean;
            p.Format = null;
            bool allowsNone = ContainsWordsInOrder("None");
            p.IsNullableRaw = allowsNone;
            p.IsRequired = !allowsNone;
            return;
        }
        if (ContainsWordsInOrder("URL"))
        {
            p.Type = JsonObjectType.String;
            p.Format = JsonFormatStrings.Uri;
            return;
        }
        if (ContainsWordsInOrder("number")
            || ContainsWordsInOrder("value")
            || ContainsWordsInOrder("amount")
            || ContainsWordsInOrder("frequency"))
        {
            p.Type = JsonObjectType.Number;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("order") || ContainsWordsInOrder("country name"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("ID") || ContainsWordsInOrder("identifying string"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("Type of the list"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("USD") || ContainsWordsInOrder("salary"))
        {
            p.Type = JsonObjectType.Number;
            p.Format = JsonFormatStrings.Double;
            return;
        }
        if (ContainsWordsInOrder("Postal Code"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("Filter by"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("Deprecated"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("Company")
            || ContainsWordsInOrder("Country")
            || ContainsWordsInOrder("City")
            || ContainsWordsInOrder("Industry")
            || ContainsWordsInOrder("companies")
            || ContainsWordsInOrder("countries")
            || ContainsWordsInOrder("cities")
            || ContainsWordsInOrder("industries")
            || ContainsWordsInOrder("continents"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("description"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("options"))
        {
            return;
        }
        if (ContainsWordsInOrder("Alexa") || ContainsWordsInOrder("dataset"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("title")
            || ContainsWordsInOrder("the state code"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        if (ContainsWordsInOrder("latitude") || ContainsWordsInOrder("longitude"))
        {
            p.Type = JsonObjectType.Number;
            p.Format = JsonFormatStrings.Double;
            return;
        }
        if (ContainsWordsInOrder("seniority level"))
        {
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }
        {
            Console.WriteLine($"Classifying as default: {description}");
            p.Type = JsonObjectType.String;
            p.Format = null;
            return;
        }

    }
}

