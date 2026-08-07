# FindJobWorkspace instructions

This Workspace consumes the public FindJobHelper Engine through NuGet. Engine source is not required for normal work; the compiled provider DLL is the integration point. Keep the Core package and local CLI tool pinned to the same exact version.

All checked-in examples are fictional. Replace fictional provider data only in a private Workspace. Never send an application, email anyone, or publish private CV data without explicit owner authorization.

Create per-application configuration with `dotnet tool run find-job-helper -- new-config`. Keep each edited configuration and its generated artifacts in its own private `sent/` directory. Do not commit generated artifacts in a public repository.

Provide `PersonalInfo__FirstName`, `PersonalInfo__LastName`, `PersonalInfo__Profession`, `PersonalInfo__City`, `PersonalInfo__Country`, `PersonalInfo__Email`, `PersonalInfo__Phone`, `PersonalInfo__GitHub`, and `PersonalInfo__LinkedIn` through user secrets or environment variables. The public contact examples are `202-555-0100` and `Example City, Example Country` only.
