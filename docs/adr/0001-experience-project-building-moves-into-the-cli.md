# Experience project building moves into the CLI and WebUI

The workspace relied on ad-hoc run scripts (`run-webui.sh`/`run-webui.ps1`,
`run.ps1`, `test-local-tool.ps1` in the workspace repo) to build and launch the
experience project. We decided this functionality moves into the products
themselves: the CLI gains an optional `experienceProject` mode, and building is
invoked with CLIWrap (`dotnet build` / `dotnet run`). Building and config
loading live in a separate reusable assembly shared by the CLI and the WebUI —
the two frontends must share it so both honor the same config and build rules.
After a build, the provider DLL is loaded through the existing DLL loader and
the requested operation continues. `dotnet run` only applies to executable
projects; the current experience project is a class library, so the flow is
build, then load DLL.

## Consequences

- Config file: `findjobhelper.config.json`, read automatically when present.
  The CLI walks up from its current directory to the nearest ancestor that
  contains it (the CLI can run from a subfolder); the WebUI reads it from its
  `--workspace` directory. A relative `experienceProject` path resolves from
  the directory containing the config file.
- `experienceProject` holds the path to the csproj. When it is set, generation
  and `list-tags` build automatically before loading the DLL.
- An explicit `--experience-database <dll>` selects that DLL and skips the
  build; an explicit `--experience-project <csproj>` overrides the config path.
- `--no-build` bypasses the build and errors if the DLL does not exist.
- The WebUI uses the same build rules: it builds on startup when the DLL is
  missing, and the explicit Rebuild button picks up later source changes. With
  `--no-build`, the startup build is skipped and an existing DLL is required,
  but Rebuild still works as an explicit request.
- The launcher and run scripts keep no build logic.
- No profiles: a single user of the database is assumed.
