# FindJobHelper

FindJobHelper is a reusable CV-selection and generation engine. The public repository contains the Core library, the `find-job-helper` .NET tool, a fictional Workspace template, automated tests, and retained (but unpackaged) TheirStack client source.

## Packages

- `Anton.FindJobHelper.Core` provides the experience model, provider contract, selection engine, and rendering support.
- `Anton.FindJobHelper.Cli` installs the `find-job-helper` command.
- `Anton.FindJobHelper.Templates` installs the `findjob-workspace` template.

All three packages use one synchronized version. A Workspace references Core and the CLI tool at the same exact version and supplies a compiled provider DLL implementing `IExperienceDatabaseProvider`.

Personal identity and contact values are supplied through the `PersonalInfo` configuration section (user secrets or `PersonalInfo__...` environment variables); they are not embedded in the CLI package.

## Build and test

```powershell
dotnet restore .\FindJobHelper.sln --use-lock-file
dotnet build .\FindJobHelper.sln --no-restore
dotnet test .\FindJobHelper.sln --no-build
```

TheirStack projects are retained for future development but are not referenced by the CLI and are never included in the published packages.

## Create a fictional Workspace

```powershell
dotnet new install Anton.FindJobHelper.Templates::0.1.0
dotnet new findjob-workspace -n ExampleWorkspace
```

The template contains only explicitly fictional sample data, including `202-555-0100` and `Example City, Example Country` contact fixtures.

Licensed under the [MIT License](LICENSE).
