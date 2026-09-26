# Personal info lives in the tracked workspace config

Personal info values resolve in order: environment variable, then
`findjobhelper.config.json`, then .NET user secrets. `PersonalInfo:Phone` and
`PersonalInfo:Email` move out of both user-secret stores (WSL and Windows) into
the tracked `findjobhelper.config.json` in the workspace repo, after verifying
the copied values; they already appear in the tracked generated PDFs, so
tracking them adds no exposure. The template example config has the phone and
email lines commented out. `TheirStack:SecretKey` stays in user secrets.
