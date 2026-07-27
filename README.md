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
work-unit-weighted progress for the complete run, while `Current task` shows the active
operation. The applicable operations are computing heights, matching experiences,
creating the TeX source and rendering the PDF, or creating the planned Markdown files.
The completed 100% display remains visible after generation.

When standard output is redirected or the console is non-interactive, the CLI emits
plain line-oriented status instead of progress-bar or ANSI animation. It writes on
every task transition, warning, failure, and final completion, plus a heartbeat every
five seconds even if progress has not changed:

```text
Progress: 42% — Rendering PDF
```

PDF generation estimates two XeLaTeX passes and one PDF conversion pass. `latexmk` may
legitimately require more; the CLI keeps rendering, holds the percentage at the last
expected milestone, and displays a “taking longer than expected” detail. Progress
behavior is automatic and adds no command-line flags.

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
    "workExperience": {
      "minTotalItemBudget": 2,
      "totalItemBudget": 8,
      "scoreLowerBound": 0,
      "recencyBoost": 0.25
    },
    "personalProjects": {
      "totalItemBudget": 2
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
| `scoreLowerBound` | `0` | Removes a bullet before MMR ranking when its raw weighted tag-match score is below this value. It is applied before the recency boost. |
| `recencyBoost` | `0` | Favors newer experience lists within the same section. The oldest list receives a multiplier of `1`; the newest receives `1 + recencyBoost`; dates in between are linearly interpolated. |

For example, `recencyBoost: 0.25` leaves the oldest job's relevance unchanged and
multiplies the newest job's relevance by `1.25`.

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

- `normalizedRelevance` is the candidate's raw weighted tag-match score, including its
  recency multiplier, divided by the highest adjusted relevance among all candidates.
  When several requirements reach the same experience tag, raw relevance uses only the
  largest effective coefficient, so aliases and overlapping relation paths do not
  inflate relevance.
- `maxSimilarity` is the highest cosine similarity between the candidate's explicit
  requirement-coverage vector and any already selected bullet's vector.
- `saturation` is a weighted penalty for explicit requirements that have already
  appeared in at least `saturationQuota` selected bullets.

Configured tags that are bidirectional full-overlap aliases form one requirement group.
The first-declared database tag is its canonical name, and the group's configured weight
is the maximum alias weight. For example, configuring both `C#` and `.NET` at `1.5`
creates one canonical `.NET` requirement with weight `1.5`, not `3`.

Every matched experience tag retains all explicit requirements that reached it. Its
largest effective coefficient contributes to raw relevance, while every positive origin
contributes to requirement coverage:

```text
raw contribution for an experience tag =
    evidence score × maximum effective requirement coefficient

coverage for an explicit requirement =
    sum of its effective contributions across matched experience tags
```

This means an indirect `Unity` or `Game Programming` match reached from `.NET` still
occupies the `.NET` MMR dimension. Transitive intermediate tags do not become MMR
dimensions unless they were explicitly configured as requirements.

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
and per-bullet scores directly in the generated CV. Per-bullet annotations keep raw
matches separate from the signed MMR rank and show the relevance, similarity, and
saturation terms, canonical requirement coverage, configured aliases, and
target-to-origin contributions. Negative scores remain visible when page filling or a
section minimum forces their selection. Event headings aggregate only raw relevance,
signed rank, coverage, and matches; they do not imply an event-level MMR formula. The
annotated Markdown preserves the configured section order and selected content, making
it easier to see whether tag weights, MMR settings, or section budgets need the next
adjustment.
