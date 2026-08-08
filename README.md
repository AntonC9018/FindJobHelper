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

## LaTeX toolchain (Linux and WSL)

PDF generation requires TeX Live 2026 and Liberation Serif/Sans. Install the
minimal user-local toolchain before the first generation:

```bash
./scripts/setup-latex.sh
source "$HOME/.local/share/findjobhelper/texlive/2026/findjobhelper-env.sh"
./scripts/setup-latex.sh --check
```

The default root is `$HOME/.local/share/findjobhelper/texlive/2026`. Use
`--install-root PATH` (or `FINDJOBHELPER_TEXLIVE_ROOT`) to select another root;
the option wins over the environment variable. `--check` is non-mutating, and a
normal rerun safely fills missing requirements without updating installed
packages. Setup downloads TeX Live's rolling 2026 repository and the Liberation
Fonts 2.1.5 TTF archive, so allow network access and several GB of user-owned disk
space.

The fonts come from Liberation Fonts 2.1.5 under the SIL Open Font License. The
script downloads the [upstream archive](https://github.com/liberationfonts/liberation-fonts/files/7261482/liberation-fonts-ttf-2.1.5.tar.gz),
requires SHA-256 `7191c669bf38899f73a2094ed00f7b800553364f90e2637010a69c0e268f25d0`,
and installs it under `$HOME/.local/share/fonts/findjobhelper`.

The script prints an exact current-session `PATH` command and never edits a shell
profile. Existing compatible distributions can be selected with
`--latex-bin-directory PATH` or `FINDJOBHELPER_LATEX_BIN_DIRECTORY`; the CLI option
wins. The selected directory must provide both `latexmk` and `xelatex`, so tools
are never mixed between installations.

To remove the default installation, delete only
`$HOME/.local/share/findjobhelper/texlive/2026` and
`$HOME/.local/share/fonts/findjobhelper/liberation-fonts-2.1.5`, then run
`fc-cache -f`. The installer supports Linux and WSL (Linux x86-64 is CI-tested).
Native Windows setup is unsupported; users may still select an existing compatible
LaTeX binary directory.

## Create a fictional Workspace

```powershell
dotnet new install Anton.FindJobHelper.Templates::0.1.0
dotnet new findjob-workspace -n ExampleWorkspace
```

The template contains only explicitly fictional sample data, including `202-555-0100` and `Example City, Example Country` contact fixtures.

Licensed under the [MIT License](LICENSE).
