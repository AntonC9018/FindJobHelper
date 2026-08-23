$force = $args.Count -gt 0 -and $args[0] -eq '--force'
$remainingArguments = @($args)
if ($force) {
    $remainingArguments = @($remainingArguments | Select-Object -Skip 1)
}

if ($remainingArguments.Count -gt 0 -and $remainingArguments[0] -ne '--') {
    throw "Unexpected argument '$($remainingArguments[0])'. Use: run.ps1 [--force] [-- <CLI arguments...>]"
}
$forwardedArguments = @($remainingArguments | Select-Object -Skip 1)

$providerProject = Join-Path $PSScriptRoot 'src/FindJobWorkspace.Provider'
$buildDirectory = Join-Path $PSScriptRoot 'build'
$providerDll = Join-Path $buildDirectory 'FindJobWorkspace.Provider.dll'

function Assert-LastCommandSucceeded([string]$description) {
    if ($LASTEXITCODE -ne 0) {
        throw "$description failed with exit code $LASTEXITCODE."
    }
}

if ($force -or -not (Test-Path -LiteralPath $providerDll -PathType Leaf)) {
    dotnet publish $providerProject --output $buildDirectory
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

$cliArguments = @(
    'tool', 'run', 'find-job-helper', '--',
    '--experience-database', $providerDll,
    '--open',
    '--config', 'config.json'
) + $forwardedArguments
dotnet @cliArguments
Assert-LastCommandSucceeded 'Running FindJobHelper'
