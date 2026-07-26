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

This creates `CurmanchiiAnton-debug.md` instead of a PDF. Debug generation still uses
LaTeX height measurement and the same page-fit admission rules as normal generation,
so its selected content has page-fit parity with the PDF path, but it skips final
LaTeX/PDF compilation. Sensitive phone information remains blurred.

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

The final page or range derives the exact page count, so `pageCount` and
`limitToOnePage` are redundant and rejected in this form. A controlled page break is
inserted between blocks. Within an event-based section, the heading stays with its first
visible event and is printed only once; later jobs, projects, or degrees may move to a
new page, but every complete event—including its description, links, and selected
bullets—remains atomic. `Languages` remains an atomic section.

Every block must naturally use its complete declared span. For example, a `pages:
"2-3"` block must contain enough selected content to occupy two pages without counting
the forced break, document header, or document footer. Underfilled blocks fail before
either a PDF or debug Markdown artifact is published. Selection is never padded and
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

- `normalizedRelevance` is the candidate's weighted tag-match score, including its
  recency multiplier, divided by the highest adjusted relevance among all candidates.
- `maxSimilarity` is the highest cosine similarity between the candidate's tag-score
  vector and any already selected bullet's vector.
- `saturation` is a weighted penalty for matching tags that have already appeared at
  least `saturationQuota` times.

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

With `saturationQuota: 2`, the first two selected bullets containing a tag incur no
saturation penalty from that tag. A third candidate containing it is penalized once, a
fourth twice, and so on. The contribution is proportional to how much of the candidate's
tag-match score comes from that repeated tag.

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
and per-bullet scores directly in the generated CV. The annotated Markdown preserves
the configured section order and selected content, making it easier to see whether tag
weights, MMR settings, or section budgets need the next adjustment.
