# FindJobWorkspace instructions

This Workspace consumes the public FindJobHelper Engine through NuGet. Engine source is not required for normal work; the compiled provider DLL is the integration point. Keep the Core package and local CLI tool pinned to the same exact version.

## LaTeX setup

On Linux or WSL, run `bash scripts/setup-latex.sh` before the first PDF generation.
The script prints any follow-up commands when it finishes. Safe reruns fill missing
requirements without updating installed packages.

- Default TeX Live root: `$HOME/.local/share/findjobhelper/texlive/2026`
- Setup override: `--install-root` or `FINDJOBHELPER_TEXLIVE_ROOT`
- Runtime override: `--latex-bin-directory` or `FINDJOBHELPER_LATEX_BIN_DIRECTORY`
- Fonts: Liberation Fonts 2.1.5 under the SIL Open Font License
- Requirements: network access and several GB of user-owned disk space

The CLI option wins over environment configuration. Both `latexmk` and `xelatex`
must come from the same selected directory. Native Windows installation is
unsupported, but an existing compatible distribution may be selected explicitly.

To remove the default installation, delete only the default TeX root and
`$HOME/.local/share/fonts/findjobhelper/liberation-fonts-2.1.5`, then run
`fc-cache -f`.

All checked-in examples are fictional. Replace fictional provider data only in a private Workspace. Never send an application, email anyone, or publish private CV data without explicit owner authorization.

Create per-application configuration with `dotnet tool run find-job-helper -- new-config`. Keep each edited configuration and its generated artifacts in its own private `sent/` directory. Do not commit generated artifacts in a public repository.

## Running the Workspace

```powershell
.\run.ps1
.\run.ps1 --force
.\run.ps1 -- --output-directory output
```

Use `./run.sh` on Linux, macOS, or WSL. Use `--force` after editing provider
source. The current directory supplies `config.json`; `sent/example` contains a
sample.

```powershell
dotnet user-secrets set "PersonalInfo:Email" "you@example.com" --project src/FindJobWorkspace.Provider
dotnet user-secrets set "PersonalInfo:Phone" "+1 202-555-0100" --project src/FindJobWorkspace.Provider
```
