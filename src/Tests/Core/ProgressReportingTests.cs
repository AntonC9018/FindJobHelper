using System.Reflection;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class ProgressReportingTests
{
    [Theory]
    [InlineData(-1, 10, 0)]
    [InlineData(5, 10, 0.5)]
    [InlineData(20, 10, 1)]
    [InlineData(0, 0, 1)]
    public void ProgressFraction_Clamps(
        double completed,
        double total,
        double expected)
    {
        Assert.Equal(
            expected,
            ProgressMath.Fraction(new(completed, total)));
    }

    [Fact]
    public void GenerationApis_RequireProgressArguments()
    {
        Assert.All(
            typeof(ExperienceSearch)
                .GetMethods(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance)
                .Where(static method => method.Name == nameof(ExperienceSearch.Run)),
            static method => Assert.Contains(
                method.GetParameters(),
                static parameter =>
                    parameter.ParameterType == typeof(IProgressReporter)));

        AssertProgressParameter(
            typeof(LatexMeasurementService),
            nameof(LatexMeasurementService.MeasureAsync));
        AssertProgressParameter(
            typeof(CvTemplate),
            nameof(CvTemplate.Generate),
            typeof(LatexProgressReporters));
        AssertProgressParameter(
            typeof(CvMarkdownRenderer),
            "Render");
        AssertProgressParameter(
            typeof(ILatexMeasurementRunner),
            nameof(ILatexMeasurementRunner.MeasureAsync));
    }

    [Fact]
    public void NoOpReporter_IsReusableSingleton()
    {
        Assert.Same(
            NoOpProgressReporter.Instance,
            NoOpProgressReporter.Instance);

        NoOpProgressReporter.Instance.Report(new(1, 1, "done"));
    }

    private static void AssertProgressParameter(
        Type type,
        string methodName,
        Type? progressType = null)
    {
        var methods = type
            .GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.Instance)
            .Where(method => method.Name == methodName)
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(
            methods,
            method => Assert.Contains(
                method.GetParameters(),
                parameter => parameter.ParameterType
                    == (progressType ?? typeof(IProgressReporter))));
    }
}
