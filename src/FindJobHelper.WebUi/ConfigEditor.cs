using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FindJobHelper.Configuration;
using FindJobHelper.Configuration.Json;
using FindJobHelper.CVGeneration;
using FindJobHelper.Generation;

namespace FindJobHelper.WebUi;

public sealed record SaveConfigOutcome(
    bool Saved,
    IReadOnlyList<string> Errors);

/// <summary>
/// Production config-editor backend (fjw-w4u.6, folded from the fjw-c9m.6
/// prototype). Serves a runtime-generated JSON Schema for config.json plus
/// loader-grounded validate/save-back and tag-name completion. The schema is
/// generated in-process from the pinned Configuration.Json model; validation
/// and saving round-trip through
/// <see cref="CvSelectionConfigurationLoader"/> itself, so the loader stays
/// the single ground truth and the browser schema stays UX-only.
/// </summary>
public sealed class ConfigEditor
{
    /// <summary>
    /// No backup is kept on save: history lives in source control.
    /// <see cref="ApplicationCatalog"/> still excludes <c>*.bak</c> names
    /// when scanning folders, so backups left by older builds never leak
    /// into listings.
    /// </summary>
    private const string ConfigFileName = "config.json";

    /// <summary>
    /// Mirrors <c>CvSelectionConfigurationLoader</c>'s private
    /// <c>JsonOptions</c> (camelCase, comments/trailing commas allowed,
    /// case-sensitive, unknown members rejected,
    /// <c>JsonStringEnumConverter&lt;Section&gt;</c>). Used only for schema
    /// export; validation never uses these options directly — it round-trips
    /// through the real loader instead, so option drift cannot silently
    /// desync validation. If the loader's options change, the schema export
    /// here and the w4u.7 snapshot test surface it.
    /// </summary>
    private static readonly JsonSerializerOptions SchemaExportOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<Section>(allowIntegerValues: false),
        },
    };

    private readonly WebUiOptions _options;
    private readonly ILogger<ConfigEditor> _logger;
    private readonly object _tagsSync = new();
    private string? _tagsCacheKey;
    private IReadOnlyList<string>? _tagsCache;
    private string? _cachedSchemaJson;

    public ConfigEditor(WebUiOptions options, ILogger<ConfigEditor> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Generates the schema in-process from the pinned Configuration.Json
    /// model, mirroring the research/reference implementation
    /// (research/config-schema on fjw-c9m.2).
    /// </summary>
    public string GetSchemaJson()
    {
        if (_cachedSchemaJson is not null)
        {
            return _cachedSchemaJson;
        }

        var options = new JsonSerializerOptions(SchemaExportOptions)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var typeInfo = options.GetTypeInfo(typeof(JsonCvSelectionConfiguration));
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = Transform,
        };
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(typeInfo, exporterOptions);
        _cachedSchemaJson = schema.ToJsonString();
        return _cachedSchemaJson;
    }

    /// <summary>
    /// Validates raw editor content with the real loader semantics by
    /// round-tripping through a temp file and
    /// <see cref="CvSelectionConfigurationLoader.LoadAsync"/>. Temp paths
    /// are rewritten to <c>config.json</c> in messages so users never see
    /// scratch locations.
    /// </summary>
    public async Task<IReadOnlyList<string>> ValidateAsync(
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            await CvSelectionConfigurationLoader.LoadAsync(temporaryPath, cancellationToken);
            return [];
        }
        catch (CvConfigurationException ex)
        {
            return SanitizeErrors(ex.Errors, temporaryPath);
        }
        finally
        {
            TryDeleteTemporary(temporaryPath);
        }
    }

    /// <summary>
    /// Validates with the loader, then overwrites <c>config.json</c>
    /// directly (no backup — history lives in source control). Invalid
    /// content is
    /// rejected before anything is written. The write is UTF-8 without BOM
    /// and otherwise byte-verbatim (comments and formatting survive because
    /// the editor text is stored raw, never re-serialized); a pre-existing
    /// BOM is dropped on the first save, which the loader tolerates on read.
    /// </summary>
    public async Task<SaveConfigOutcome> SaveAsync(
        string folder,
        string content,
        CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(content, cancellationToken);
        if (errors.Count > 0)
        {
            return new SaveConfigOutcome(
                Saved: false,
                Errors: errors);
        }

        var configPath = Path.Combine(folder, ConfigFileName);
        await File.WriteAllTextAsync(configPath, content, cancellationToken);
        return new SaveConfigOutcome(
            Saved: true,
            Errors: []);
    }

    /// <summary>
    /// Lists every tag name in the experience database (for editor
    /// completion), loading the DLL through a content-hashed shadow copy so
    /// the original never stays locked. Cached per database write; a missing
    /// or unloadable database yields no completions instead of an error.
    /// </summary>
    public IReadOnlyList<string> GetTagNames()
    {
        var databasePath = _options.DatabasePathOrDefault;
        var stamp = BuildDatabaseStamp(databasePath);
        var key = $"{databasePath}@{stamp}";
        lock (_tagsSync)
        {
            if (key == _tagsCacheKey && _tagsCache is not null)
            {
                return _tagsCache;
            }
        }

        var names = LoadTagNames(databasePath);
        lock (_tagsSync)
        {
            _tagsCacheKey = key;
            _tagsCache = names;
        }

        return names;
    }

    private IReadOnlyList<string> LoadTagNames(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return [];
        }

        try
        {
            var shadowPath = ExperienceDatabaseShadow.Copy(databasePath);
            var loaded = ExperienceDatabaseProviderLoader.Load(shadowPath);
            return loaded.Result.TagsDatabase.TagsGraph.Keys
                .Select(static tag => tag.Name)
                .Distinct()
                .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static name => name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is ExperienceDatabaseProviderLoadException
            or CvLayoutException
            or IOException
            or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Tag-name completion fell back to an empty list.");
            return [];
        }
    }

    private static string BuildDatabaseStamp(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return "missing";
        }

        var stamp = File.GetLastWriteTimeUtc(databasePath).Ticks;
        return stamp.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> SanitizeErrors(
        IEnumerable<string> errors,
        string temporaryPath)
    {
        return errors
            .Select(error => error.Replace(temporaryPath, ConfigFileName, StringComparison.Ordinal))
            .ToList();
    }

    private static void TryDeleteTemporary(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static JsonNode Transform(JsonSchemaExporterContext context, JsonNode schema)
    {
        // Direct type equality (not a name string): a model rename breaks the
        // build instead of silently dropping the sectionOrder hand-build.
        if (context.PropertyInfo?.PropertyType == typeof(SectionOrderCollection))
        {
            return BuildSectionOrderSchema();
        }

        if (schema is not JsonObject obj)
        {
            return schema;
        }

        var declaringTypeName = context.PropertyInfo?.DeclaringType?.Name;
        var jsonName = context.PropertyInfo?.Name;
        var isRoot = context.PropertyInfo is null
            && string.Equals(
                context.TypeInfo?.Type?.FullName,
                typeof(JsonCvSelectionConfiguration).FullName,
                StringComparison.Ordinal);

        if (isRoot)
        {
            obj["title"] = "CV selection configuration (config.json)";
            obj["description"] =
                "Generated from FindJobHelper.Configuration.Json JsonCvSelectionConfiguration. "
                + "The loader allows // comments and trailing commas; camelCase property names are required (case-sensitive); "
                + "unknown properties are rejected (UnmappedMemberHandling.Disallow).";
        }

        if (obj.ContainsKey("properties"))
        {
            obj["additionalProperties"] = false;
        }

        ApplyConstraints(obj, declaringTypeName, jsonName);
        return obj;
    }

    private static void ApplyConstraints(
        JsonObject obj,
        string? declaringTypeName,
        string? jsonName)
    {
        // Type names are nameof() constants (compile-checked); JSON names are
        // literals (the CLR names are PascalCase) and pinned by the w4u.7
        // snapshot test, so a model rename surfaces instead of silently
        // dropping constraints.
        switch (declaringTypeName, jsonName)
        {
            case (nameof(JsonCvSelectionConfiguration), "limitToOnePage"):
                obj["default"] = true;
                break;
            case (nameof(JsonCvSelectionConfiguration), "pageCount"):
                obj["minimum"] = 1;
                break;
            case (nameof(JsonCvSelectionConfiguration), "requiredTags"):
            case (nameof(JsonCvSelectionConfiguration), "skills"):
            case (nameof(JsonCvSelectionConfiguration), "technologies"):
                obj["minItems"] = 1;
                break;
            case (nameof(RequiredTagConfiguration), "name"):
                obj["minLength"] = 1;
                break;
            case (nameof(RequiredTagConfiguration), "weight"):
                obj["exclusiveMinimum"] = 0;
                break;
            case (nameof(MmrConfiguration), "relevanceWeight"):
                obj["minimum"] = 0;
                obj["maximum"] = 1;
                break;
            case (nameof(MmrConfiguration), "saturationQuota"):
                obj["minimum"] = 1;
                break;
            case (nameof(MmrConfiguration), "saturationPenalty"):
                obj["minimum"] = 0;
                break;
            case (nameof(SelectionOptionsConfiguration), "minItemBudget"):
            case (nameof(SelectionOptionsConfiguration), "itemBudget"):
            case (nameof(SelectionOptionsConfiguration), "scoreLowerBound"):
            case (nameof(SelectionOptionsConfiguration), "recencyBoost"):
            case (nameof(SelectionOptionsConfiguration), "directMatchBoost"):
                obj["minimum"] = 0;
                break;
            default:
                break;
        }
    }

    private static JsonNode BuildSectionOrderSchema()
    {
        var sectionNames = Enum.GetNames<Section>();
        var legacyItems = BuildSectionEnum();
        var legacy = new JsonObject
        {
            ["type"] = "array",
            ["minItems"] = 1,
            ["items"] = legacyItems,
            ["description"] = "Legacy form: case-sensitive section names.",
        };
        var explicitForm = new JsonObject
        {
            ["type"] = "array",
            ["minItems"] = 1,
            ["items"] = BuildLayoutBlock(),
            ["description"] =
                "Explicit page-layout form; pages must cover page 1..N contiguously (not expressible in JSON Schema).",
        };
        return new JsonObject
        {
            ["anyOf"] = new JsonArray(legacy, explicitForm),
            ["description"] =
                "Either section-name strings or page-layout objects; mixing forms is rejected by the loader.",
        };

        JsonObject BuildSectionEnum()
        {
            var names = sectionNames.Select(static name => (JsonNode)name).ToArray();
            return new JsonObject
            {
                ["enum"] = new JsonArray(names),
            };
        }

        JsonObject BuildLayoutBlock()
        {
            var names = sectionNames.Select(static name => (JsonNode)name).ToArray();
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray("sections"),
                ["properties"] = new JsonObject
                {
                    ["page"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
                    ["pages"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["pattern"] = "^[0-9]+-[0-9]+$",
                    },
                    ["sections"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["items"] = new JsonObject
                        {
                            ["enum"] = new JsonArray(names),
                        },
                    },
                },
                ["oneOf"] = new JsonArray(
                    new JsonObject { ["required"] = new JsonArray("page") },
                    new JsonObject { ["required"] = new JsonArray("pages") }),
            };
        }
    }
}
