[CmdletBinding()]
param(
    # The workspace whose `data/` applications the UI manages. Defaults to this script's
    # own root; pass the main workspace when running from a worktree checkout.
    # Template workspaces have no WebUi source, so the UI runs from the
    # find-job-webui dotnet tool instead of a local build.
    [string] $Workspace = $PSScriptRoot,
    [int] $Port = 5058,
    [switch] $RebuildDatabase,
    # Skip the browser auto-open. Used when another script (run.ps1) starts the UI
    # in the background and opens the browser itself, so one launch means one tab.
    [switch] $NoBrowser
)

$ErrorActionPreference = "Stop"

$providerProject = Join-Path $PSScriptRoot "src/FindJobWorkspace.Provider"
$buildDirectory = Join-Path $PSScriptRoot "build"
$providerDll = Join-Path $buildDirectory "FindJobWorkspace.Provider.dll"

# Same personal data the CV generator consumes via run.ps1.
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

if ($RebuildDatabase -or !(Test-Path -Path $providerDll -PathType Leaf)) {
    dotnet publish "$providerProject" --output "$buildDirectory"
    if ($LASTEXITCODE -ne 0) {
        throw "Building the experience database failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Starting FindJob web UI on http://localhost:$Port (workspace: $Workspace)"

# The server blocks the foreground, so a background poller opens the browser
# once the port answers (a cold build takes a while). The open is unconditional:
# even a start that never becomes healthy surfaces in the browser instead of
# failing silently in this console.
$browserJob = $null
if (!$NoBrowser) {
    $uiUrl = "http://localhost:$Port"
    $browserJob = Start-Job -ScriptBlock {
        param($browserUrl, $browserPort)
        $deadline = [DateTime]::UtcNow.AddSeconds(120)
        while ([DateTime]::UtcNow -lt $deadline) {
            $probe = New-Object Net.Sockets.TcpClient
            try {
                $connect = $probe.BeginConnect("localhost", $browserPort, $null, $null)
                if ($connect.AsyncWaitHandle.WaitOne(500) -and $probe.Connected) {
                    break
                }
            } catch {
            } finally {
                $probe.Close()
            }
            Start-Sleep -Milliseconds 500
        }
        Start-Process $browserUrl
    } -ArgumentList $uiUrl, $Port
}

try {
    # The sqlite job store (data/jobs.db) is created on first run by the tool
    # itself from these defaults. --database points at this workspace's
    # provider build because the default ExperienceDatabase.dll name from a
    # real workspace does not exist here.
    dotnet tool run find-job-webui -- --workspace "$Workspace" --database "$providerDll" --urls "http://localhost:$Port"
    if ($LASTEXITCODE -ne 0) {
        throw "The web UI exited with code $LASTEXITCODE."
    }
} finally {
    if ($browserJob -ne $null) {
        Stop-Job $browserJob -ErrorAction SilentlyContinue | Out-Null
        Remove-Job $browserJob -Force -ErrorAction SilentlyContinue
    }
}
