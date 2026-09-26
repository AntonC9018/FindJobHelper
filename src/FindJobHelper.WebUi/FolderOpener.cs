using System.ComponentModel;
using System.Diagnostics;

namespace FindJobHelper.WebUi;

/// <summary>Desktop environment the web UI process runs in.</summary>
public enum DesktopEnvironment
{
    Windows,
    MacOS,
    Linux,

    /// <summary>Linux under the Windows Subsystem for Linux.</summary>
    Wsl,
}

/// <summary>
/// Opens application folders in the OS file manager. Under WSL the folder is
/// converted to its \\wsl.localhost form and handed to Windows Explorer via
/// interop, so it opens on the Windows side. Interop can be down while the
/// web UI runs: the WSLInterop binfmt handler disappears after sleep/resume
/// and long VM sessions, and every Windows binary then fails to exec with
/// "Exec format error" (ENOEXEC). Since nothing inside WSL can start a
/// Windows process at that point, the opener degrades to xdg-open and, when
/// that fails too, surfaces the interop cause with its remediation instead
/// of the raw ENOEXEC message.
/// </summary>
public sealed class FolderOpener
{
    /// <summary>Seam for starting processes; <see cref="Process.Start(ProcessStartInfo)"/> by default.</summary>
    public delegate Process? StartProcess(ProcessStartInfo info);

    /// <summary>Seam for converting a Linux path to its Windows form; null means no conversion is available.</summary>
    public delegate string? ResolveWindowsPath(string linuxPath);

    private readonly DesktopEnvironment _environment;
    private readonly StartProcess _startProcess;
    private readonly ResolveWindowsPath _resolveWindowsPath;
    private readonly Func<string?> _wslDistroName;

    public FolderOpener(
        DesktopEnvironment environment,
        StartProcess? startProcess = null,
        ResolveWindowsPath? resolveWindowsPath = null,
        Func<string?>? wslDistroName = null)
    {
        _environment = environment;
        _startProcess = startProcess ?? Process.Start;
        _resolveWindowsPath = resolveWindowsPath ?? TryConvertToWindowsPath;
        _wslDistroName = wslDistroName
            ?? (() => Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"));
    }

    /// <summary>Builds an opener for the environment the process runs in.</summary>
    public static FolderOpener Detect() => new(DetectEnvironment());

    /// <summary>Detects the desktop environment, recognizing WSL via WSL_DISTRO_NAME.</summary>
    public static DesktopEnvironment DetectEnvironment()
    {
        if (OperatingSystem.IsWindows())
        {
            return DesktopEnvironment.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return DesktopEnvironment.MacOS;
        }

        if (OperatingSystem.IsLinux()
            && Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") is not null)
        {
            return DesktopEnvironment.Wsl;
        }

        return DesktopEnvironment.Linux;
    }

    /// <summary>Opens <paramref name="folder"/> in the environment's file manager.</summary>
    public void OpenFolder(string folder)
    {
        switch (_environment)
        {
            case DesktopEnvironment.Windows:
                _startProcess(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    ArgumentList = { folder },
                    UseShellExecute = true,
                });
                return;
            case DesktopEnvironment.MacOS:
                _startProcess(new ProcessStartInfo
                {
                    FileName = "open",
                    ArgumentList = { folder },
                    UseShellExecute = false,
                });
                return;
        }

        string? interopFailure = null;
        if (_environment == DesktopEnvironment.Wsl)
        {
            var windowsPath = _resolveWindowsPath(folder);
            if (windowsPath is not null)
            {
                try
                {
                    _startProcess(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        ArgumentList = { windowsPath },
                        UseShellExecute = false,
                    });
                    return;
                }
                catch (Win32Exception ex)
                {
                    interopFailure = ex.Message;
                }
            }
        }

        try
        {
            _startProcess(new ProcessStartInfo
            {
                FileName = "xdg-open",
                ArgumentList = { folder },
                UseShellExecute = false,
            });
        }
        catch (Exception ex) when (interopFailure is not null)
        {
            throw new InvalidOperationException(
                $"Windows Explorer could not be started from WSL ({interopFailure}); "
                + "WSL interop is down — run 'wsl --shutdown' in PowerShell, reopen the "
                + $"workspace shell, and try again. The xdg-open fallback failed too: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Converts a Linux path to its Windows form via wslpath. Null means no
    /// Windows path is available, which sends the caller to the xdg-open
    /// fallback instead of launching explorer.exe with a bogus argument.
    /// </summary>
    public string? TryConvertToWindowsPath(string folder)
    {
        if (_wslDistroName() is null)
        {
            return null;
        }

        try
        {
            using var conversion = _startProcess(new ProcessStartInfo
            {
                FileName = "wslpath",
                ArgumentList = { "-w", folder },
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
            if (conversion is null)
            {
                return null;
            }

            if (!conversion.WaitForExit(5000))
            {
                try
                {
                    conversion.Kill();
                }
                catch
                {
                }

                return null;
            }

            return ParseWslPathOutput(conversion.ExitCode, conversion.StandardOutput.ReadLine());
        }
        catch
        {
            // Missing wslpath binary, spawn failure, or unreadable output all
            // mean the conversion is unavailable, not that the folder is bad.
            return null;
        }
    }

    /// <summary>Pure form of the wslpath output handling so tests need no real process.</summary>
    internal static string? ParseWslPathOutput(int exitCode, string? firstOutputLine)
    {
        if (exitCode != 0)
        {
            return null;
        }

        var line = firstOutputLine?.Trim();
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }
}
