using System.ComponentModel;
using System.Diagnostics;
using FindJobHelper.WebUi;

namespace FindJobHelper.WebUi.Tests;

public sealed class FolderOpenerTests
{
    [Fact]
    public void OpenFolder_OnWindows_LaunchesExplorerWithTheFolderViaShell()
    {
        var started = new List<ProcessStartInfo>();
        var opener = new FolderOpener(
            DesktopEnvironment.Windows,
            startProcess: info => Record(started, info));

        opener.OpenFolder(@"C:\data\208_middle_java_developer_peratera");

        var launch = Assert.Single(started);
        Assert.Equal("explorer.exe", launch.FileName);
        Assert.Equal(
            @"C:\data\208_middle_java_developer_peratera",
            Assert.Single(launch.ArgumentList));
        Assert.True(launch.UseShellExecute);
    }

    [Fact]
    public void OpenFolder_OnMacOS_LaunchesOpenWithTheFolder()
    {
        var started = new List<ProcessStartInfo>();
        var opener = new FolderOpener(
            DesktopEnvironment.MacOS,
            startProcess: info => Record(started, info));

        opener.OpenFolder("/Users/anton/data/208_middle_java_developer_peratera");

        var launch = Assert.Single(started);
        Assert.Equal("open", launch.FileName);
        Assert.Equal(
            "/Users/anton/data/208_middle_java_developer_peratera",
            Assert.Single(launch.ArgumentList));
        Assert.False(launch.UseShellExecute);
    }

    [Fact]
    public void OpenFolder_UnderWsl_LaunchesExplorerWithTheConvertedWindowsPath()
    {
        var started = new List<ProcessStartInfo>();
        var opener = new FolderOpener(
            DesktopEnvironment.Wsl,
            startProcess: info => Record(started, info),
            resolveWindowsPath: folder => @"\\wsl.localhost\Ubuntu"
                + folder.Replace('/', '\\'));

        opener.OpenFolder("/home/anton/coding/cvs/FindJobWorkspace-webui/data/208");

        var launch = Assert.Single(started);
        Assert.Equal("explorer.exe", launch.FileName);
        Assert.Equal(
            @"\\wsl.localhost\Ubuntu\home\anton\coding\cvs\FindJobWorkspace-webui\data\208",
            Assert.Single(launch.ArgumentList));
        Assert.False(launch.UseShellExecute);
    }

    [Fact]
    public void OpenFolder_UnderWsl_WhenTheWindowsPathIsUnavailable_FallsBackToXdgOpen()
    {
        var started = new List<ProcessStartInfo>();
        var opener = new FolderOpener(
            DesktopEnvironment.Wsl,
            startProcess: info => Record(started, info),
            resolveWindowsPath: _ => null);

        opener.OpenFolder("/home/anton/data/208");

        var launch = Assert.Single(started);
        Assert.Equal("xdg-open", launch.FileName);
        Assert.Equal("/home/anton/data/208", Assert.Single(launch.ArgumentList));
        Assert.False(launch.UseShellExecute);
    }

    [Fact]
    public void OpenFolder_OnPlainLinux_UsesXdgOpenDirectly()
    {
        var started = new List<ProcessStartInfo>();
        var opener = new FolderOpener(
            DesktopEnvironment.Linux,
            startProcess: info => Record(started, info));

        opener.OpenFolder("/home/anton/data/208");

        var launch = Assert.Single(started);
        Assert.Equal("xdg-open", launch.FileName);
        Assert.Equal("/home/anton/data/208", Assert.Single(launch.ArgumentList));
    }

    [Fact]
    public void OpenFolder_UnderWsl_WhenInteropIsDown_FallsBackToXdgOpen()
    {
        var started = new List<ProcessStartInfo>();
        var opener = new FolderOpener(
            DesktopEnvironment.Wsl,
            startProcess: info =>
            {
                started.Add(info);
                if (info.FileName == "explorer.exe")
                {
                    // NativeErrorCode 8 is the ENOEXEC the UI reported when the
                    // WSLInterop binfmt handler was missing.
                    throw new Win32Exception(8, "Exec format error");
                }

                return null;
            },
            resolveWindowsPath: _ => @"\\wsl.localhost\Ubuntu\tmp");

        opener.OpenFolder("/tmp");

        Assert.Equal(2, started.Count);
        Assert.Equal("explorer.exe", started[0].FileName);
        Assert.Equal("xdg-open", started[1].FileName);
    }

    [Fact]
    public void OpenFolder_UnderWsl_WhenInteropIsDownAndXdgOpenAlsoFails_ReportsInteropRemediation()
    {
        const string explorerFailure = "Exec format error";
        var opener = new FolderOpener(
            DesktopEnvironment.Wsl,
            startProcess: info =>
            {
                if (info.FileName == "explorer.exe")
                {
                    throw new Win32Exception(8, explorerFailure);
                }

                throw new InvalidOperationException("no xdg-open");
            },
            resolveWindowsPath: _ => @"\\wsl.localhost\Ubuntu\tmp");

        var failure = Assert.Throws<InvalidOperationException>(() => opener.OpenFolder("/tmp"));

        Assert.Contains("wsl --shutdown", failure.Message, StringComparison.Ordinal);
        Assert.Contains(explorerFailure, failure.Message, StringComparison.Ordinal);
        Assert.Contains("no xdg-open", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryConvertToWindowsPath_WithoutWslDistroName_ReturnsNullWithoutStartingWslpath()
    {
        var started = new List<ProcessStartInfo>();
        var opener = new FolderOpener(
            DesktopEnvironment.Wsl,
            startProcess: info => Record(started, info),
            wslDistroName: () => null);

        Assert.Null(opener.TryConvertToWindowsPath("/tmp"));
        Assert.Empty(started);
    }

    [Fact]
    public void TryConvertToWindowsPath_WhenWslpathCannotStart_ReturnsNull()
    {
        var opener = new FolderOpener(
            DesktopEnvironment.Wsl,
            startProcess: _ => throw new Win32Exception(2, "No such file or directory"),
            wslDistroName: () => "Ubuntu");

        Assert.Null(opener.TryConvertToWindowsPath("/tmp"));
    }

    [Fact]
    public void TryConvertToWindowsPath_ConvertsAFolderUnderRealWsl()
    {
        if (Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") is null)
        {
            // Plain Linux (CI): wslpath does not exist, and returning null
            // without throwing is the correct behavior there as well.
            return;
        }

        var opener = new FolderOpener(DesktopEnvironment.Wsl);

        var converted = opener.TryConvertToWindowsPath("/tmp");

        Assert.NotNull(converted);
        Assert.StartsWith(@"\\", converted, StringComparison.Ordinal);
        Assert.EndsWith("tmp", converted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, @"\\wsl.localhost\Ubuntu\tmp", @"\\wsl.localhost\Ubuntu\tmp")]
    [InlineData(0, @"  \\wsl.localhost\Ubuntu\tmp  ", @"\\wsl.localhost\Ubuntu\tmp")]
    [InlineData(0, "", null)]
    [InlineData(0, "   ", null)]
    [InlineData(0, null, null)]
    [InlineData(1, @"\\wsl.localhost\Ubuntu\tmp", null)]
    public void ParseWslPathOutput_KeepsOnlySuccessfulNonEmptyConversions(
        int exitCode,
        string? firstOutputLine,
        string? expected)
    {
        Assert.Equal(expected, FolderOpener.ParseWslPathOutput(exitCode, firstOutputLine));
    }

    private static Process? Record(List<ProcessStartInfo> started, ProcessStartInfo info)
    {
        started.Add(info);
        return null;
    }
}
