### When asked to generate a CV:

- `run.sh` or `run.ps1` is the core script used to generate a CV.
- Use `dotnet find-job-helper` to create a new CV config and to list the existing tags.
- Only include the language section where the position is local for the user
  or where it mentions that knowing a given language is required.
- Only include tags explicitly required in the position.
  Similar tags are going to match automatically.
- You may add new tags if those are not mentioned in the database but the experience does
  talk about them, or you've added bullets that talk about them from other projects of the user.
- You may edit the tag relations in the tag database, but make sure 
  to explain why with a comment above the lines you add.
- CV generally should be 2 pages, unless 1 page includes enough information.
- Put the generated CV PDF + JSON configuration named exactly `config.json` (+ cover letter if asked)
  in a directory under `data/` named as `{nr}_{title}_{company}`,
  and write a `metadata.json` next to it (see `metadata.json` below).
- Reserve yourself a number as soon as possible so there are no number conflicts 
  with other agents. In order to do this, just create a directory for the output.
- Only reserve consecutive numbers after the current largest, don't reinsert missing numbers.
- Don't send the CV on email, unless explicitly asked by the user. 
  You must only edit local files.
- You don't need to edit or view the experience database code when making a CV,
  unless it is to add new experiences or new tags.
- Don't try to force one particular experience on to the page.
  You need to only operate on tag basis. 
  And if too few things match, only then should you go into the experience database
  and add new experiences.
- Include each of the technologies as a separate item, don't group them using '/'.
  E.g. don't do "ASP.NET Core / EF Core" do "ASP.NET Core, EF Core".
  CI/CD, C/C++ and other common established patterns are fine though.
- Save the job description as a txt file in the new folder.
- Assume the program is behaving correctly and will print errors if something didn't go right.
- Assume the generated CV is the right number of pages and includes 
  the best fitting candidates according to the configuration.
- Include all the keywords you can, even if there is no proof of them in the experience database.
- Comment out keywords with least evidence in the JSON, if they take up too much space.
- When generating a cover letter, don't go over 120 characters per line.
- Pull down information on each of the companies and put it in a file next to the generated CV:
  * domain
  * latest projects
  * reviews if those are easily accessible
  Include a link to the company in that document.
- Don't access the link to the job posting if the user already gave you the job description.

## metadata.json

Each application folder under `data/` holds a `metadata.json` file — the
agent write path the web UI ingests into sqlite (`data/jobs.db`, gitignored,
created on first UI run) when the user presses Refresh. Agents create folders
plus this file:

```json
{
  "nr": "42",
  "title": "Senior .NET Developer",
  "company": "Example SRL",
  "company_url": "https://example.com",
  "job_url": "https://example.com/jobs/123",
  "state": "generated",
  "state_note": null,
  "recruiter": {
    "name": "Jane Doe",
    "title": "Talent Acquisition",
    "profile_url": "https://www.linkedin.com/in/jane-doe/",
    "location": "Chișinău",
    "notes": "Posted the job."
  }
}
```

- `nr` matches the `{nr}` prefix of the folder name. `title`, `company`,
  `company_url`, `job_url` describe the position.
- `state` keeps to the vocabulary below; an empty or missing state shows as
  `added`. `state_note` holds the reason for `n/a` / `other`.
- Omit `recruiter` (or leave name and profile_url empty) when nobody credible
  was found — the research/job-description files still record the attempt.
- Also record the recruiter in `metadata.json` under `recruiter` (name,
  title/headline, profile_url, location, notes); the web UI links
  applications that share a recruiter.

## Application states

The `state` field of `metadata.json` is a managed vocabulary shared with
the web UI (`run-webui.ps1`), which ingests the same files into sqlite when
the user presses Refresh. Agents writing folders must keep to this vocabulary:

- `added` — folder and metadata exist, no CV generated yet.
- `generated` — the CV was generated into the folder.
- `sent` — the application was submitted (email, website form, LinkedIn).
- `followed-up` — the user has texted the recruiter on LinkedIn about it.
- `n/a (<reason>)` — the position can't be applied to (closed, location, ...).
- `other (<reason>)` — anything else.

- Create new folders with state `added` (an empty state also shows as added).
- After you generate the CV for a folder, you may set its state to `generated`.
- Never set `sent` or `followed-up` yourself; the user makes those transitions
  in the web UI.
- State changes, recruiter links, and notes are kept as an append-only event
  log in the db; the UI timeline renders them verbatim.
- The sqlite db is rebuilt from the folders every time the user presses
  Refresh in the web UI, so never edit `data/jobs.db` directly.

## When adding experiences

- The experiences must come from real facts derived from the user's code,
  or supplied by the user in the prompt, or must already exist in the database.
  Other sources are allowed when pointed at by the user.
- You may clone down the repo that the user requests to extract information from.
  Clone it to a temp system directory, not locally.
- Each bullet point must include the accomplishment or the derivative that was produced,
  using which tools / libraries / methods, the problem it solved and the impact it produced.
- Make sure that if a detail is removed from the experiences,
  it is still recorded in a comment above it.
- If a user-provided detail is not used in an actual experience bullet,
  include it in a comment above it still if it is important context.
- If an experience is generated by you, an agent, you must write above it a comment that it's AI generated.
- When AI implemented most of the underlying work described by an experience, default every tag unrelated to
  AI-assisted development to a score of at most 7. This cap is about who performed the work; it does not apply
  merely because an AI agent wrote or rephrased the experience bullet. Only use a higher score for AI-implemented
  work when the user explicitly confirms that their own manual effort or expertise warrants it. Research, design,
  or setup that the user performed themselves may be scored normally even when AI assisted elsewhere.
- Make sure to look at the latest branches for each repo you inspect.
