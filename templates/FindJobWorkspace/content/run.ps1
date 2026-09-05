$ErrorActionPreference = "Stop"

# Manual $args parsing (no param block): with [CmdletBinding()] the '--'
# separator and '--force' never reach the script body — the binder rejects
# them first — so the CLI-forwarding convention needs raw tokens, exactly
# like run.sh parses "$@" in bash.
$Port = 5058
$force = $false
$forwardedArguments = @()
$usage = "Use: run.ps1 [-Port <N>] [--force] [-- <CLI arguments...>]"
$argIndex = 0
while ($argIndex -lt $args.Count) {
    $token = $args[$argIndex]
    if ($token -eq '--force' -or $token -eq '-Force') {
        $force = $true
        $argIndex += 1
    } elseif ($token -eq '-Port' -or $token -eq '--port') {
        if ($argIndex + 1 -ge $args.Count) {
            throw "Missing value for '$token'. $usage"
        }
        $portCandidate = 0
        if (![int]::TryParse($args[$argIndex + 1], [ref]$portCandidate)) {
            throw "Port must be a number, got '$($args[$argIndex + 1])'. $usage"
        }
        $Port = $portCandidate
        $argIndex += 2
    } elseif ($token -eq '--') {
        $restStart = $argIndex + 1
        if ($restStart -lt $args.Count) {
            $forwardedArguments = @($args[$restStart..($args.Count - 1)])
        }
        break
    } else {
        throw "Unexpected argument '$token'. $usage"
    }
}

$providerProject = Join-Path $PSScriptRoot 'src/FindJobWorkspace.Provider'
$buildDirectory = Join-Path $PSScriptRoot 'build'
$providerDll = Join-Path $buildDirectory 'FindJobWorkspace.Provider.dll'

function Assert-LastCommandSucceeded([string]$description) {
    if ($LASTEXITCODE -ne 0) {
        throw "$description failed with exit code $LASTEXITCODE."
    }
}

if ($force -or -not (Test-Path -LiteralPath $providerDll -PathType Leaf)) {
    dotnet publish "$providerProject" --output "$buildDirectory"
    Assert-LastCommandSucceeded 'Publishing the experience database'
}

$env:PersonalInfo__FirstName = 'Alex'
$env:PersonalInfo__LastName = 'Example'
$env:PersonalInfo__Profession = 'Example Software Engineer'
$env:PersonalInfo__City = 'Example City'
$env:PersonalInfo__Country = 'Example Country'
$env:PersonalInfo__GitHub = 'https://example.test/github'
$env:PersonalInfo__LinkedIn = 'https://example.test/linkedin'
$env:PersonalInfo__YouTube = 'https://example.test/youtube'
$env:PersonalInfo__Portfolio = 'https://example.test/portfolio'
# Alternatively, remove these assignments and configure email and phone with
# user-secrets if you do not want to commit real contact information.
$env:PersonalInfo__Email = 'alex@example.test'
$env:PersonalInfo__Phone = '202-555-0100'

# Single-instance gate for the web UI: a bound port means a server is already
# up, so the caller must not spawn a second copy. (Best-effort like any
# port probe: two truly simultaneous launches can still race.)
function Test-UiPortOpen([int]$probePort) {
    $probe = New-Object Net.Sockets.TcpClient
    try {
        $connect = $probe.BeginConnect("localhost", $probePort, $null, $null)
        if (!$connect.AsyncWaitHandle.WaitOne(500)) {
            return $false
        }
        return $probe.Connected
    } catch {
        return $false
    } finally {
        $probe.Close()
    }
}

$uiUrl = "http://localhost:$Port"
if (Test-UiPortOpen $Port) {
    Write-Host "FindJob web UI is already running on $uiUrl."
} else {
    # Same shell that runs this script, detached: the UI outlives generation.
    # -NoBrowser because this script opens the browser itself below, so one
    # launch always means exactly one tab. Paths are quoted: Start-Process
    # flattens the argument list into one command line, so a workspace root
    # with spaces would otherwise split into several arguments.
    $shellPath = (Get-Process -Id $PID).Path
    $launcherScript = Join-Path $PSScriptRoot "run-webui.ps1"
    Start-Process -FilePath $shellPath -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$launcherScript`"", "-Workspace", "`"$PSScriptRoot`"", "-Port", "$Port", "-NoBrowser"
    Write-Host "Starting the FindJob web UI in the background on $uiUrl."
}

# Unconditional: a double-launch reuses the single server yet still opens the
# browser. On a cold start the server takes a while to build; refresh once.
Start-Process $uiUrl

$cliArguments = @(
    'tool', 'run', 'find-job-helper', '--',
    '--experience-database', $providerDll,
    '--open',
    '--config', 'config.json'
) + $forwardedArguments
dotnet @cliArguments
Assert-LastCommandSucceeded 'Running FindJobHelper'
