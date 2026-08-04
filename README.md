# FindJobHelper

FindJobHelper generates a job-specific CV from a database of experience bullets. A JSON
configuration describes the job's important tags, the visible skills and technologies,
and how the search should balance relevance, variety, recency, section sizes, and page
count.

## Generate a CV

From the repository root, run:

```powershell
dotnet run --project src/MainCli/MainCli.csproj -- `
  --config path/to/config.json `
  --output-directory path/to/output
```

The default output format is `tex`: it uses the LaTeX renderer and publishes the
compiled `CurmanchiiAnton.pdf`. The equivalent explicit invocation adds
`--output-format tex`.

To publish a clean Markdown CV instead, use:

```powershell
dotnet run --project src/MainCli/MainCli.csproj -- `
  --config path/to/config.json `
  --output-directory path/to/output `
  --output-format md
```

This publishes `CurmanchiiAnton.md` without compiling the final PDF. Markdown still
requires the LaTeX template and LaTeX measurement tooling: it uses the same measured
heights, selection admission, exact-page checks, and explicit-layout validation as the
PDF path. It does not implement separate pagination and contains no page-boundary
markers.

To print every tag accepted by `requiredTags`, run:

```powershell
dotnet run --project src/MainCli/MainCli.csproj -- list-tags
```

To print an example JSON configuration, run:

```powershell
dotnet run --project src/MainCli/MainCli.csproj -- example-config
```

To inspect why experiences were selected, publish an annotated Markdown CV:

```powershell
dotnet run --project src/MainCli/MainCli.csproj -- `
  --config path/to/config.json `
  --output-directory path/to/output `
  --debug
```

`--debug` overrides `--output-format`, skips final LaTeX/PDF compilation, and publishes
both the clean `CurmanchiiAnton.md` and annotated `CurmanchiiAnton-debug.md` from the
same selected CV model. Sensitive phone information remains blurred in both files.

| Invocation | Published artifacts | `--open` target |
| --- | --- | --- |
| Default or `--output-format tex` | `CurmanchiiAnton.pdf` | PDF |
| `--output-format md` | `CurmanchiiAnton.md` | Clean Markdown |
| `--debug`, with either output format | `CurmanchiiAnton.md`, `CurmanchiiAnton-debug.md` | Debug Markdown |

### Generation progress

In an interactive terminal, generation keeps two live rows visible: `Overall` shows
equal-share progress across the modules applicable to the run, while `Current task`
shows the active module's local progress from 0–100%.

| PDF module | Overall range |
| --- | ---: |
| Computing heights | 0–25% |
| Matching experiences | 25–50% |
| Creating TeX file | 50–75% |
| Rendering PDF | 75–100% |

Markdown and debug runs use three equal modules: computing heights, matching
experiences, and creating Markdown files. Each contributes one third of overall
progress. The two Markdown files in a debug run share the single Markdown creation
module. For example, 50% local progress in the first PDF module appears as 50% in
`Current task` and 12.5% in `Overall`. The completed 100% display remains visible
after generation.

When standard output is redirected or the console is non-interactive, the CLI emits
plain line-oriented status instead of progress-bar or ANSI animation. It writes on
every module transition, warning, failure, and final completion, plus a heartbeat
every five seconds even if progress has not changed. The percentage is the scaled
overall progress:

```text
Progress: 75% — Rendering PDF
```

PDF generation estimates two XeLaTeX passes and one PDF conversion pass. During each
expected XeLaTeX pass, generated progress markers report the experience title and
bullet being processed. The markers are logical bullet milestones: repeated processing
of the same bullet for current-page and fresh-page layout candidates is counted once per
pass. `latexmk` may legitimately require more passes; the CLI keeps rendering, holds the
percentage at the last expected milestone, and displays a “taking longer than expected”
detail. Progress behavior is automatic and adds no command-line flags.

## Configuration

Comments and trailing commas are allowed, but unknown properties are rejected.

```jsonc
{
  "pageCount": 1,
  "requiredTags": [
    { "name": ".NET", "weight": 1.0 },
    { "name": "SQL", "weight": 0.8 }
  ],
  "skills": [
    "Backend Development"
  ],
  "technologies": [
    ".NET",
    "SQL"
  ],
  "mmr": {
    "relevanceWeight": 0.72,
    "saturationQuota": 2,
    "saturationPenalty": 0.18
  },
  "selection": {
    "default": {
      "directMatchBoost": 0.25
    },
    "workExperience": {
      "minTotalItemBudget": 2,
      "totalItemBudget": 8,
      "scoreLowerBound": 0,
      "recencyBoost": 0.25,
      "directMatchBoost": 0.5
    },
    "personalProjects": {
      "totalItemBudget": 2,
      "directMatchBoost": 0
    }
  },
  "sectionOrder": [
    "WorkExperience",
    "PersonalProjects",
    "Education"
  ]
}
```

### Section order and page layouts

`sectionOrder` supports two additive forms. The legacy string form keeps its existing
behavior:

```jsonc
{
  "pageCount": 2,
  "sectionOrder": [
    "WorkExperience",
    "PersonalProjects",
    "Education"
  ]
}
```

`pageCount` requires the selected CV to use exactly that many pages.
`limitToOnePage` is the older boolean control: it defaults to `true`, while `false`
allows an unrestricted number of pages. Do not supply both properties.

The object form assigns sections to exact pages or inclusive page ranges:

```jsonc
{
  "pageCount": 4,
  "sectionOrder": [
    { "page": 1, "sections": ["Languages", "Education"] },
    { "pages": "2-3", "sections": ["WorkExperience"] },
    { "page": 4, "sections": ["PersonalProjects"] }
  ]
}
```

Explicit blocks must already be ordered, start at page 1, and cover every page
contiguously without gaps or overlaps. A section can occur only once across the full
layout. `page` is a positive integer; `pages` is a strict inclusive `start-end` range
whose start is less than its end. Empty section lists, unknown properties or section
names, malformed ranges, and mixtures of strings and objects are configuration errors.

The final page or range derives the exact page count. `pageCount` is optional in this
form; when supplied, it must match the derived count. `limitToOnePage` remains
redundant and is rejected. A controlled page break is inserted between blocks. Within
an event-based section, the heading stays with its first visible event and is printed
only once; later jobs, projects, or degrees may move to a new page, but every complete
event—including its description, links, and selected bullets—remains atomic.
`Languages` remains an atomic section.

Every block must naturally use its complete declared span. For example, a `pages:
"2-3"` block must contain enough selected content to occupy two pages without counting
the forced break, document header, or document footer. Underfilled blocks fail before
any requested PDF or Markdown artifact is published. Selection is never padded and
does not bypass MMR, score thresholds, section budgets, or required-item rules merely
to fill a range.

## Search parameters

| Parameter | Default | Effect |
| --- | ---: | --- | --- |
| `minTotalItemBudget` | `0` | Tries to select at least this many bullets in the section. Minimum filling may accept a candidate even when its MMR score is non-positive, but cannot invent matching candidates or bypass page-layout admission. |
| `totalItemBudget` | Unlimited | Maximum number of bullets in the section. Dependencies and other required companion bullets count toward the budget. `minTotalItemBudget` cannot exceed it. |
| `scoreLowerBound` | `0` | Removes a bullet before MMR ranking when its tag relevance, including the direct-match bonus, is below this value. The later recency bonus remains excluded. |
| `directMatchBoost` | `0` | Adds a contribution-based bonus for exact configured tags and bidirectional full-overlap aliases. It must be finite and non-negative. |
| `recencyBoost` | `0` | Adds a bonus for newer experience lists within the same section. The oldest list receives no bonus; the newest receives `max(0, baseRelevance) × recencyBoost`; dates in between are linearly interpolated. |

`selection.default.directMatchBoost` is inherited when a section omits the property.
A section value overrides it, and an explicit `0` disables the inherited boost. This
nullable inheritance rule applies only to `directMatchBoost`; all existing selection
properties keep their previous overlay behavior. Omitting the property everywhere
resolves it to `0` and preserves existing clean-CV selection.

## MMR parameters

MMR (Maximal Marginal Relevance) re-ranks the eligible bullets after tag matching. It
rewards relevance while discouraging bullets that repeat the same tag profile. This
helps a CV cover several requirements instead of spending most of its space on several
nearly identical bullets.

For a candidate, the implementation calculates:

```text
MMR score =
    relevanceWeight × normalizedRelevance
  - (1 - relevanceWeight) × maxSimilarity
  - saturationPenalty × saturation
```

Where:

- `normalizedRelevance` is the candidate's fully adjusted, pre-MMR relevance divided by
  the highest such relevance among all candidates. When several requirements reach the
  same experience tag, base relevance uses only the largest effective coefficient, so
  aliases and overlapping relation paths do not inflate it.
- `maxSimilarity` is the highest cosine similarity between the candidate's explicit
  requirement-coverage vector and any already selected bullet's vector.
- `saturation` is a weighted penalty for explicit requirements that have already
  appeared in at least `saturationQuota` selected bullets.

Configured tags that are bidirectional full-overlap aliases form one requirement group.
The first-declared database tag is its canonical name, and the group's configured weight
is the maximum alias weight. For example, configuring both `C#` and `.NET` at `1.5`
creates one canonical `.NET` requirement with weight `1.5`, not `3`.

Every matched experience tag retains all explicit requirements that reached it. Its
largest effective coefficient contributes to base relevance. A separate direct
coefficient exists only for an exact configured tag or a bidirectional full-overlap
alias, and uses the largest configured weight in that alias group:

```text
baseTagContribution =
    evidence score × maximum effective requirement coefficient

directContribution =
    evidence score × maximum direct-or-alias coefficient

directBonus =
    max(0, directContribution) × directMatchBoost

tagRelevance = baseTagContribution + directBonus

coverage for an explicit requirement =
    sum of its effective contributions across matched experience tags
```

Partial, one-way, and transitive relations are indirect and receive no direct bonus.
Each bullet tag earns its own bonus; the aggregate bullet score is never multiplied once
per matching tag. If a bullet tag is a stronger indirect match for one requirement and a
weaker direct match for another, only the weaker direct contribution is boosted. For
example, base relevance `10`, direct contribution `4`, and `directMatchBoost: 0.5`
produce `10 + 4 × 0.5 = 12`, not `15`.

After tag relevance is complete, recency is added from unboosted base relevance:

```text
appliedRecencyBoost = configuredRecencyBoost × normalizedRecency
recencyBonus = max(0, baseRelevance) × appliedRecencyBoost

adjustedPreMmrRelevance =
    baseRelevance + directBonus + recencyBonus
```

The bonuses are additive rather than compounded. With base relevance `10`, direct
contribution `4`, direct boost `0.5`, and applied recency boost `0.25`, the result entering
MMR is `10 + 2 + 2.5 = 14.5`, not `(10 + 2) × 1.25 = 15`.

This means an indirect `Unity` or `Game Programming` match reached from `.NET` still
occupies the `.NET` MMR dimension. Transitive intermediate tags do not become MMR
dimensions unless they were explicitly configured as requirements. Direct and recency
bonuses do not change requirement coverage, cosine-similarity vectors, saturation
proportions, or selected-requirement counts. MMR subtracts its similarity and saturation
terms only after both positive relevance bonuses have been applied; no later conversion
rescales the signed result.

The configuration requires all three MMR parameters:

| Parameter | Valid values | Effect |
| --- | --- | --- |
| `relevanceWeight` | `0` to `1` | Controls the relevance-versus-redundancy trade-off. Near `1`, raw job fit dominates. Near `0`, similarity avoidance dominates. |
| `saturationQuota` | Integer ≥ `1` | Number of selected bullets that may contain a tag before the extra saturation penalty begins for the next candidate containing that tag. |
| `saturationPenalty` | Finite number ≥ `0` | Strength of the additional repeated-tag penalty. `0` disables saturation while retaining cosine-similarity diversity. |

The standard starting values are:

```json
{
  "relevanceWeight": 0.72,
  "saturationQuota": 2,
  "saturationPenalty": 0.18
}
```

With `saturationQuota: 2`, the first two selected bullets covering a requirement incur no
saturation penalty from that requirement. A third candidate covering it is penalized once, a
fourth twice, and so on. Each selected bullet increments a canonical requirement at
most once. A candidate's contribution is proportional to how much of its total
requirement coverage comes from that repeated requirement.

The similarity and saturation penalties are global across the selected experience
bullets, not reset for each section. Selection stops normally when no remaining
candidate has a positive MMR score, unless a section minimum still needs to be filled.

### Tuning guide

- Too many similar bullets: lower `relevanceWeight`, lower `saturationQuota`, or raise
  `saturationPenalty`.
- Important requirements disappear: raise their `requiredTags` weights or raise
  `relevanceWeight`.
- One section dominates: lower that section's `totalItemBudget`, or set minimum and
  maximum budgets for the other sections.
- Older but more relevant work is displaced: reduce `recencyBoost`.
- Weakly related bullets appear: raise `scoreLowerBound`. Tune this carefully because
  it is a hard pre-MMR filter.

Change one dimension at a time. Use `--debug` after each change to inspect aggregate
and per-bullet scores directly in the generated CV. Per-bullet annotations emit one
normalized signed `rank`, show base relevance, both additive bonuses, adjusted relevance,
and the MMR terms, and keep the unboosted requirement-origin values used by similarity
and saturation visible. Negative normalized ranks remain unchanged when page filling or
a section minimum forces their selection. Event rank aggregates sum those normalized
bullet ranks; they do not imply an event-level MMR formula. The
annotated Markdown preserves the configured section order and selected content, making
it easier to see whether tag weights, MMR settings, or section budgets need the next
adjustment.
