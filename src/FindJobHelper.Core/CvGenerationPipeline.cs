using System.Collections.Immutable;
using System.Text;
using CodegenCS;
using FindJobHelper.Core;
using FindJobHelper.Core.Helper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FindJobHelper.CVGeneration;

public sealed record CvGenerationPipelineRequest
{
    public required string ConfigPath { get; init; }

    public required string ExperienceDatabasePath { get; init; }

    public required string OutputDirectory { get; init; }

    public CvOutputFormat OutputFormat { get; init; } = CvOutputFormat.Tex;

    public bool Debug { get; init; }

    public string? LatexBinDirectory { get; init; }

    /// <summary>
    /// Font families and scales as raw strings. <see langword="null"/> roles fall
    /// back to the CV_*_FONT environment variables, then to the defaults.
    /// </summary>
    public LatexFontConfigurationValues? Fonts { get; init; }

    /// <summary>
    /// Personal info used for the CV header and artifact naming. When omitted,
    /// the values are resolved from user secrets of the experience database
    /// assembly and from PersonalInfo__* environment variables.
    /// </summary>
    public PersonalInfoOptions? PersonalInfo { get; init; }

    public string? TemplatePath { get; init; }

    public ICvGenerationProgressDisplay? ProgressDisplay { get; init; }
}

public sealed record CvGenerationPipelineResult
{
    public required ImmutableArray<CvPlannedArtifact> Artifacts { get; init; }

    public required CvArtifactKind OpenTarget { get; init; }

    public required ImmutableDictionary<CvArtifactKind, string> PublishedPaths { get; init; }

    public CvFailurePresentation? Failure { get; init; }

    public bool Success => Failure is null;
}

/// <summary>
/// Thrown when CV generation fails unexpectedly and staging files had to be
/// retained for diagnosis.
/// </summary>
public sealed class CvGenerationException : Exception
{
    public CvGenerationException(string message, Exception innerException, string? retainedStagingDirectory)
        : base(FormatMessage(message, innerException, retainedStagingDirectory), innerException)
    {
        RetainedStagingDirectory = retainedStagingDirectory;
    }

    public string? RetainedStagingDirectory { get; }

    private static string FormatMessage(
        string message,
        Exception innerException,
        string? retainedStagingDirectory)
    {
        var combined = $"{message} {innerException.Message}";
        return retainedStagingDirectory is null
            ? combined
            : $"{combined} Retained generation files: '{retainedStagingDirectory}'.";
    }
}

/// <summary>
/// The shared CV generation entry point. The CLI and other frontends are thin
/// layers over this pipeline: they resolve their own inputs and present the
/// result, while all generation orchestration lives here.
/// </summary>
public static class CvGenerationPipeline
{
    public static async Task<CvGenerationPipelineResult> RunAsync(
        CvGenerationPipelineRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConfigPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);

        var configuration = await CvSelectionConfigurationLoader.LoadAsync(
            request.ConfigPath,
            cancellationToken);
        var fullOutputDirectory = Path.GetFullPath(request.OutputDirectory);
        var loadedProvider = ExperienceDatabaseProviderLoader.Load(
            request.ExperienceDatabasePath);
        var providerResult = loadedProvider.Result;
        var searchConfiguration = configuration.BuildSearch(
            providerResult.TagsDatabase);
        var templatePath = ResolveTemplatePath(request.TemplatePath);
        var latexExecutables = LatexBinaryDirectoryResolver.Resolve(
            request.LatexBinDirectory);
        var fontConfiguration = ResolveFonts(request.Fonts);
        var latexExecutionOptions = CreateLatexExecutionOptions(fontConfiguration);

        await using var serviceProvider = await CvGenerationAppConfiguration.CreateApp(
            loadedProvider.Assembly,
            latexExecutables.Paths,
            cancellationToken);
        var personalInfo = request.PersonalInfo is { } providedPersonalInfo
            ? ClonePersonalInfo(providedPersonalInfo)
            : serviceProvider.GetRequiredService<IOptions<PersonalInfoOptions>>().Value;
        var profession = ResolveProfession(configuration, personalInfo);
        var artifactPlan = CvArtifactPlan.Create(
            request.OutputFormat,
            request.Debug,
            $"{personalInfo.LastName}{personalInfo.FirstName}");
        if (request.Debug)
        {
            personalInfo.Phone = Miscellanious.BlurPhone(new()
            {
                String = personalInfo.Phone,
                MaxVisibleLen = 6,
                MinVisibleLen = 3,
            });
        }

        var location = new Location(personalInfo.City, personalInfo.Country);
        var currentModel = new CvDataModel
        {
            Name = new()
            {
                First = personalInfo.FirstName,
                Last = personalInfo.LastName,
            },
            CategorizedInfoLists = CreateMetadataLists(
                searchConfiguration,
                configuration,
                personalInfo),
            CategorizedInfos =
            [
                new(Category.Location, location.FormatInfo()),
                new(Category.Email, personalInfo.Email),
                new(Category.Phone, personalInfo.Phone),
            ],
            Profession = new(profession),
            Languages = CreateDefaultLanguages(),
            Location = location,
            Summary = null,
            SectionOrder = searchConfiguration.SectionOrder,
        };

        var measurementService = serviceProvider.GetRequiredService<LatexMeasurementService>();
        var progressPlan = CreateProgressPlan(artifactPlan);
        var progressDisplay = request.ProgressDisplay
            ?? NullCvGenerationProgressDisplay.Instance;
        CvFailurePresentation? failurePresentation = null;
        var publishedArtifactPaths = await progressDisplay.RunAsync(
            progressPlan,
            async progress =>
            {
                progress.BeginModule(CvGenerationModule.ComputingHeights);
                var measurementResult = await measurementService.MeasureAsync(
                    providerResult.ExperienceDatabase,
                    currentModel,
                    templatePath,
                    progress.Reporter(CvGenerationModule.ComputingHeights),
                    fontConfiguration.Options,
                    latexExecutionOptions,
                    cancellationToken);
                if (measurementResult is not CvMeasurementSnapshot measurementSnapshot)
                {
                    failurePresentation = CvFailurePresenter.Present(measurementResult);
                    return ImmutableDictionary<CvArtifactKind, string>.Empty;
                }
                progress.BeginModule(CvGenerationModule.MatchingExperiences);
                var searchResult = searchConfiguration.Run(
                    providerResult.ExperienceDatabase,
                    measurementSnapshot,
                    progress.Reporter(CvGenerationModule.MatchingExperiences));

                searchConfiguration.Sections.Apply(searchResult, currentModel);
                var artifactResult = await GenerateAndPublishArtifactsAsync(
                    artifactPlan,
                    currentModel,
                    templatePath,
                    fullOutputDirectory,
                    searchConfiguration.PageCount,
                    searchConfiguration.PageLayout,
                    latexExecutables.Paths,
                    fontConfiguration.Options,
                    latexExecutionOptions,
                    progress,
                    cancellationToken);
                if (artifactResult is ArtifactGenerationFailure failure)
                {
                    failurePresentation = failure.Presentation;
                    return ImmutableDictionary<CvArtifactKind, string>.Empty;
                }
                if (artifactResult is PublishedArtifactPaths published)
                {
                    return published.Paths;
                }
                throw new InvalidOperationException(
                    $"Unsupported artifact generation result implementation '{artifactResult.GetType().FullName}'.");
            },
            cancellationToken);

        return new CvGenerationPipelineResult
        {
            Artifacts = artifactPlan.Artifacts,
            OpenTarget = artifactPlan.OpenTarget,
            PublishedPaths = publishedArtifactPaths,
            Failure = failurePresentation,
        };
    }

    private static string ResolveTemplatePath(string? requestedTemplatePath)
    {
        if (requestedTemplatePath is not null)
        {
            if (!File.Exists(requestedTemplatePath))
            {
                throw new FileNotFoundException(
                    "The requested CV template file was not found.",
                    requestedTemplatePath);
            }

            return requestedTemplatePath;
        }

        var shippedPath = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "cv_template_config.tex");
        if (File.Exists(shippedPath))
        {
            return shippedPath;
        }

        return ExtractEmbeddedTemplate();
    }

    private static readonly object TemplateExtractionSync = new();
    private static string? extractedTemplatePath;

    private static string ExtractEmbeddedTemplate()
    {
        lock (TemplateExtractionSync)
        {
            if (extractedTemplatePath is { } cachedPath)
            {
                return cachedPath;
            }

            var assembly = typeof(CvGenerationPipeline).Assembly;
            const string resourceName = "FindJobHelper.Core.data.cv_template_config.tex";
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"The embedded CV template resource '{resourceName}' is missing.");
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"FindJobHelper-cv-template-{assembly.GetName().Version}");
            Directory.CreateDirectory(directory);
            var templatePath = Path.Combine(directory, "cv_template_config.tex");
            using (var fileStream = File.Create(templatePath))
            {
                stream.CopyTo(fileStream);
            }

            extractedTemplatePath = templatePath;
            return templatePath;
        }
    }

    private static ResolvedLatexFontConfiguration ResolveFonts(
        LatexFontConfigurationValues? requestedFonts)
    {
        var families = requestedFonts?.Families
            ?? LatexFontRoleArray<string?>.Create(static _ => null);
        var scales = requestedFonts?.Scales
            ?? LatexFontRoleArray<string?>.Create(static _ => null);
        return LatexFontConfigurationResolver.Resolve(
            flags: new(Families: families, Scales: scales),
            environments: LatexFontConfigurationResolver.GetEnvironmentValues());
    }

    private static PersonalInfoOptions ClonePersonalInfo(PersonalInfoOptions source) => new()
    {
        FirstName = source.FirstName,
        LastName = source.LastName,
        Profession = source.Profession,
        City = source.City,
        Country = source.Country,
        Phone = source.Phone,
        Email = source.Email,
        GitHub = source.GitHub,
        LinkedIn = source.LinkedIn,
        YouTube = source.YouTube,
        Portfolio = source.Portfolio,
    };

    private static string ResolveProfession(
        CvSelectionConfiguration configuration,
        PersonalInfoOptions personalInfo)
    {
        var profession = configuration.Profession ?? personalInfo.Profession;
        if (profession is null)
        {
            throw new CvConfigurationException(
                "Profession must be supplied by 'profession' or 'PersonalInfo__Profession'.");
        }

        return profession;
    }

    private static ImmutableArray<LanguageProficiencyInfo> CreateDefaultLanguages() =>
    [
        new(
            Language.Russian,
            LanguageProficiencyLevel.Native),
        new(
            Language.English,
            LanguageProficiencyLevel.C2,
            Skills:
            [
                new("Technical Writing & Reading"),
                new("Conversational Fluency"),
            ]),
        new(
            Language.Romanian,
            LanguageProficiencyLevel.B2,
            Skills:
            [
                new("Technical Conversation"),
                new("Tutoring"),
            ]),
    ];

    private static ImmutableArray<CategorizedInfoList> CreateMetadataLists(
        ConfiguredCvSearch searchConfiguration,
        CvSelectionConfiguration configuration,
        PersonalInfoOptions personalInfo)
    {
        var lists = new List<CategorizedInfoList>
        {
            new(Category.Skills, searchConfiguration.Skills),
            new(Category.Technologies, searchConfiguration.Technologies),
        };
        var usesDefaultOrder = configuration.HeaderLinkOrder.IsDefault;
        var linkOrder = usesDefaultOrder
            ? DefaultHeaderLinkOrder
            : configuration.HeaderLinkOrder;
        var errors = new List<string>();
        foreach (var linkName in linkOrder)
        {
            if (!TryResolveHeaderLink(
                    linkName,
                    personalInfo,
                    out var category,
                    out var value))
            {
                errors.Add($"Header link '{linkName}' is not supported.");
                continue;
            }
            if (value is null)
            {
                if (usesDefaultOrder)
                {
                    continue;
                }

                errors.Add(
                    $"Header link '{linkName}' is required by 'header.links.order' but has no configured value.");
                continue;
            }

            lists.Add(new(category, [value]));
        }

        if (errors.Count > 0)
        {
            throw new CvConfigurationException(errors);
        }

        return [.. lists];
    }

    private static bool TryResolveHeaderLink(
        HeaderLinkName linkName,
        PersonalInfoOptions personalInfo,
        out Category category,
        out string? value)
    {
        if (linkName == HeaderLinkName.GitHub)
        {
            category = Category.GitHub;
            value = personalInfo.GitHub;
            return true;
        }
        if (linkName == HeaderLinkName.LinkedIn)
        {
            category = Category.LinkedIn;
            value = personalInfo.LinkedIn;
            return true;
        }
        if (linkName == HeaderLinkName.YouTube)
        {
            category = Category.YouTube;
            value = personalInfo.YouTube;
            return true;
        }
        if (linkName == HeaderLinkName.Portfolio)
        {
            category = Category.Portfolio;
            value = personalInfo.Portfolio;
            return true;
        }

        category = default;
        value = null;
        return false;
    }

    private static readonly ImmutableArray<HeaderLinkName> DefaultHeaderLinkOrder =
    [
        HeaderLinkName.GitHub,
        HeaderLinkName.LinkedIn,
        HeaderLinkName.YouTube,
        HeaderLinkName.Portfolio,
    ];

    private static CvGenerationProgressPlan CreateProgressPlan(
        CvArtifactPlan artifactPlan)
    {
        var modules = new List<CvGenerationProgressModule>
        {
            new(
                CvGenerationModule.ComputingHeights,
                "Computing heights"),
            new(
                CvGenerationModule.MatchingExperiences,
                "Matching experiences"),
        };

        if (artifactPlan.Artifacts.Any(
                static artifact => artifact.Kind == CvArtifactKind.Pdf))
        {
            modules.Add(new(
                CvGenerationModule.CreatingTexFile,
                "Creating TeX file"));
            modules.Add(new(
                CvGenerationModule.RenderingPdf,
                "Rendering PDF"));
        }
        else
        {
            modules.Add(new(
                CvGenerationModule.CreatingMarkdownFiles,
                "Creating Markdown files"));
        }

        return new(modules);
    }

    private static async Task<string> StageMarkdownAsync(
        CvDataModel model,
        CvMarkdownRenderMode renderMode,
        string fileName,
        string stagingDirectory,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var stagedArtifactPath = Path.Combine(stagingDirectory, fileName);
        using var writer = new CodegenTextWriter
        {
            NewLine = "\n",
            PreserveNonWhitespaceIndentBehavior =
                CodegenTextWriter.PreserveNonWhitespaceIndentBehaviorType.PreservePosition,
        };
        CvMarkdownRenderer.Render(model, renderMode, progress, writer);
        await File.WriteAllTextAsync(
            stagedArtifactPath,
            writer.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        var workUnits = CvMarkdownRenderer.GetWorkUnitCount(model);
        progress.Report(new(
            CompletedWorkUnits: workUnits,
            TotalWorkUnits: workUnits,
            Detail: "Creating Markdown files"));
        return stagedArtifactPath;
    }

    private static async Task<IArtifactGenerationResult>
        GenerateAndPublishArtifactsAsync(
            CvArtifactPlan artifactPlan,
            CvDataModel model,
            string templatePath,
            string outputDirectory,
            CvPageCount pageCount,
            CvPageLayout? pageLayout,
            LatexExecutablePaths latexExecutables,
            LatexFontOptions fontOptions,
            LatexExecutionOptions latexExecutionOptions,
            CvGenerationProgressContext progress,
            CancellationToken cancellationToken)
    {
        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        var retainStagingDirectory = false;

        try
        {
            var stagedArtifactPaths =
                new Dictionary<CvArtifactKind, string>(
                    artifactPlan.Artifacts.Length);
            var markdownFileCount = artifactPlan.Artifacts.Count(
                static artifact => artifact.Kind
                    is CvArtifactKind.CleanMarkdown
                    or CvArtifactKind.AnnotatedMarkdown);
            var markdownWorkUnits = CvMarkdownRenderer.GetWorkUnitCount(model);
            var allMarkdownWorkUnits = checked(
                markdownWorkUnits * markdownFileCount);
            var markdownFileIndex = 0;

            foreach (var artifact in artifactPlan.Artifacts)
            {
                switch (artifact.Kind)
                {
                    case CvArtifactKind.Pdf:
                        progress.BeginModule(
                            CvGenerationModule.CreatingTexFile);
                        var artifacts = await CvTemplate.Generate(
                            new()
                            {
                                Model = model,
                                CancellationToken = cancellationToken,
                                ConfigFilePath = templatePath,
                                OutputDirectory = stagingDirectory,
                                PageCount = pageCount,
                                PageLayout = pageLayout,
                                LatexExecutables = latexExecutables,
                                FontOptions = fontOptions,
                                ExecutionOptions = latexExecutionOptions,
                            },
                            new(
                                progress.Reporter(
                                    CvGenerationModule.CreatingTexFile),
                                progress.Reporter(
                                    CvGenerationModule.RenderingPdf)));
                        if (artifacts is not GeneratedCvArtifacts generatedArtifacts)
                        {
                            retainStagingDirectory = true;
                            var presentation = CvFailurePresenter.Present(artifacts);
                            return new ArtifactGenerationFailure(
                                presentation with
                                {
                                    Message = $"{presentation.Message} Retained generation files: '{stagingDirectory}'.",
                                });
                        }
                        stagedArtifactPaths.Add(
                            artifact.Kind,
                            generatedArtifacts.PdfPath);
                        break;
                    case CvArtifactKind.CleanMarkdown:
                    case CvArtifactKind.AnnotatedMarkdown:
                        if (markdownFileIndex == 0)
                        {
                            progress.BeginModule(
                                CvGenerationModule.CreatingMarkdownFiles);
                        }
                        var markdownProgress = new ProgressRangeReporter(
                            progress.Reporter(
                                CvGenerationModule.CreatingMarkdownFiles),
                            offset: markdownFileIndex * markdownWorkUnits,
                            length: markdownWorkUnits,
                            targetTotal: allMarkdownWorkUnits);
                        var renderMode = artifact.Kind == CvArtifactKind.CleanMarkdown
                            ? CvMarkdownRenderMode.Clean
                            : CvMarkdownRenderMode.Annotated;
                        var stagedMarkdownPath = await StageMarkdownAsync(
                            model,
                            renderMode,
                            artifact.FileName,
                            stagingDirectory,
                            markdownProgress,
                            cancellationToken);
                        stagedArtifactPaths.Add(
                            artifact.Kind,
                            stagedMarkdownPath);
                        markdownFileIndex++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(artifact),
                            artifact,
                            "Unsupported CV artifact kind.");
                }
            }

            Directory.CreateDirectory(outputDirectory);
            var publishedArtifactPaths =
                new Dictionary<CvArtifactKind, string>(
                    artifactPlan.Artifacts.Length);
            foreach (var artifact in artifactPlan.Artifacts)
            {
                var publishedArtifactPath = Path.Combine(
                    outputDirectory,
                    artifact.FileName);
                File.Move(
                    stagedArtifactPaths[artifact.Kind],
                    publishedArtifactPath,
                    overwrite: true);
                publishedArtifactPaths.Add(
                    artifact.Kind,
                    publishedArtifactPath);
            }

            return new PublishedArtifactPaths(publishedArtifactPaths.ToImmutableDictionary());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            retainStagingDirectory = true;
            throw new CvGenerationException(
                "CV generation failed.",
                ex,
                stagingDirectory);
        }
        finally
        {
            try
            {
                if (!retainStagingDirectory)
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Nothing remains to clean up.
            }
            catch
            {
                // A retained staging directory is the lesser problem when the
                // generation itself already failed; keep the original exception.
            }
        }
    }

    private static LatexExecutionOptions CreateLatexExecutionOptions(
        ResolvedLatexFontConfiguration fontConfiguration)
    {
        // Keep this declaration synchronized with scripts/setup-latex.sh. A match
        // means the supported installation is incomplete; custom resources that
        // are not declared here remain ordinary LaTeX compilation failures.
        List<ILatexRequirement> requirements =
        [
            new ExecutableLatexRequirement(new("xelatex")),
            new ExecutableLatexRequirement(new("latexmk")),
            new TexFileLatexRequirement(new("babel.sty")),
            new TexFileLatexRequirement(new("xifthen.sty")),
            new TexFileLatexRequirement(new("ifmtarg.sty")),
            new TexFileLatexRequirement(new("moresize.sty")),
            new TexFileLatexRequirement(new("zref-lastpage.sty")),
            new TexFileLatexRequirement(new("needspace.sty")),
            new TexFileLatexRequirement(new("multirow.sty")),
            new TexFileLatexRequirement(new("wrapfig.sty")),
            new TexFileLatexRequirement(new("varwidth.sty")),
            new TexFileLatexRequirement(new("environ.sty")),
            new BabelLanguageLatexRequirement(new("romanian")),
        ];
        requirements.AddRange(LatexFontRoles.All.Select(role => new FontLatexRequirement(
            fontConfiguration.Options[role],
            IsManuallySpecified: fontConfiguration.ManuallySpecified[role])));
        return new(requirements, setupCommandHint: "./scripts/setup-latex.sh");
    }

    private interface IArtifactGenerationResult;

    private sealed record PublishedArtifactPaths(
        ImmutableDictionary<CvArtifactKind, string> Paths) : IArtifactGenerationResult;

    private sealed record ArtifactGenerationFailure(
        CvFailurePresentation Presentation) : IArtifactGenerationResult;
}
