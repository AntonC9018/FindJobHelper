# FindJobHelper

This is a tool used to generate a CV by matching the given tags to your experience database.

## Usage

### Basic idea

```json
// config.json
{
    "requiredTags": [
        { "name": ".NET", "weight": 1.0 },
        { "name": "Microservices", "weight": 1.0 },
    ],
    "sections": {
        "workExperience": {
            // Only include a single bullet into the CV
            "itemBudget": 1
        }
    }
    // Includes more configuration here, like the
    // listed skills & technologies, page layout, matching parameters.
}
```

```csharp
// Experience Database (some code omitted for brevity)
// Must be compiled into a dll to be used by the generator.
var builder = new ExperienceDatabaseBuilder();
var company = builder.Place("Example Company");
builder.Job(job =>
{
    job.Title("Example Software Engineer");
    job.Place(company);
    job.DateRange(DateRange.Completed(new(2023, 1), new(2024, 12)));
    
    // Good match, because of the tag match
    job.Item(item =>
    {
        item.Text($"Built a fictional .NET service for example users.");
        item.Tag(Tags.DotNet, 10);
    });
    
    // Bad match, no matching tags
    job.Item(item =>
    {
        item.Text($"Wrote app in C++");
        item.Tag(Tags.Cpp, 10);
    });
});
```

Then you run the generator:
```bash
dotnet find-job-helper --config config.json --experience-database compiled_database.dll
```

And get a `FirstLast.pdf` rendered CV, containing the best-matching experiences according to your configuration.

In this case, the .NET bullet is going to appear in the output, while the C++ one is going to be skipped.
This is because we set `itemBudget` to `1`.
In principle, without that setting, it takes as many items as fit the page, ensuring the best matches are selected.

### Setting up the workspace

1. Install and instantiate the [templates package](https://www.nuget.org/packages/Anton.FindJobHelper.Templates/): 
   ```bash
   dotnet new install Anton.FindJobHelper.Templates
   dotnet new findjob-workspace -n ExampleWorkspace
   cd ExampleWorkspace
   dotnet tool restore
   ```

2. Install Latex and the default fonts using either the installation script provided with the template, or manually.
   Default fonts can be overwritten, you don't necessarily have to install them.
   ```bash
   ./scripts/setup-latex.sh
   source "$HOME/.local/share/findjobhelper/texlive/2026/findjobhelper-env.sh"
   ./scripts/setup-latex.sh --check
   ```
   
   > The installation script has only been tested on Ubuntu.

3. Inspect and edit the experience database at `src/FindJobWorkspace.Provider/ExperienceDatabaseProvider.cs`.
   Inspect and edit the run script (`run.sh` or `run.ps1`)
   to specify your personal data and overwrite default fonts.

#### Font configuration

The generator accepts separate family and scale settings for its main, sans-serif,
and monospaced fonts. A command-line option takes precedence over its environment
variable.

| Command-line option | Environment variable | Default |
| --- | --- | --- |
| `--main-font` | `CV_MAIN_FONT` | `Liberation Serif` |
| `--sans-font` | `CV_SANS_FONT` | `Liberation Sans` |
| `--mono-font` | `CV_MONO_FONT` | `Liberation Mono` |
| `--main-font-size` | `CV_MAIN_FONT_SIZE` | No `Scale` option |
| `--sans-font-size` | `CV_SANS_FONT_SIZE` | No `Scale` option |
| `--mono-font-size` | `CV_MONO_FONT_SIZE` | `0.92` |

The font-size options are dimensionless positive, finite scale factors. The
generator passes them to `fontspec` as `Scale=<value>`; they are not point sizes.

To generate a CV, do the following:

1. Make a config by using `dotnet find-job-helper new-config` in a new directory for the current run.
   Edit the `config.json` to fit the tags of the target job position.

2. Run `run.sh` or `run.ps1` from the directory with the config.

### Usage with agents

I personally mostly use this with an agent. 
Just tell it *to generate a CV* from the workspace folder and *give it a link to the job posting*.

If your experience database is not initialized yet, and you want a starting point,
tell the agent *to scan your GitHub repositories or your contributions in repositories at work,
and record the experiences and all new relevant tags in the database*.
I recommend **reviewing each experience the agent adds**,
carefully evaluating the right values for the tag weights,
and rephrasing the sentences until they are sound.
Also, the agent may decide to include statistics that it found important, 
but which are actually irrelevant, so be careful of those.

## Hardcoded defaults

* User parameters are configured per invocation via environment variables or user-secrets. A config can override the profession and the displayed link order for one CV.
* Header links use `PersonalInfo__GitHub`, `PersonalInfo__LinkedIn`, `PersonalInfo__YouTube`, and `PersonalInfo__Portfolio`. Missing links are omitted in the default GitHub, LinkedIn, YouTube, Portfolio order.
* If education is included, each education experience is forced to appear, unless no bullets pass an earlier filter.
* All job experiences are included bare even if they have no matching bullets.
* Personal projects are not included unless they have at least one selected bullet.


## Configuration parameters influencing the matching algorithm

Below is an AI-generated summary of the parameters specified in `config.json`.

### Search parameters

| Parameter | Default | Effect |
| --- | --- | --- | --- |
| `minItemBudget` | `0` | Tries to select at least this many bullets in the section. Minimum filling may accept a candidate even when its MMR score is non-positive, but cannot invent matching candidates or bypass page-layout admission. |
| `itemBudget` | Unlimited | Maximum number of bullets in the section. Dependencies and other required companion bullets count toward the budget. `minItemBudget` cannot exceed it. |
| `scoreLowerBound` | `0` | Removes a bullet before MMR ranking when its tag relevance, including the direct-match bonus, is below this value. The later recency bonus remains excluded. |
| `directMatchBoost` | `0` | Adds a contribution-based bonus for exact configured tags and bidirectional full-overlap aliases. It must be finite and non-negative. |
| `recencyBoost` | `0` | Adds a bonus for newer experience lists within the same section. The oldest list receives no bonus; the newest receives `max(0, baseRelevance) × recencyBoost`; dates in between are linearly interpolated. |

`selection.default.directMatchBoost` is inherited when a section omits the property.
A section value overrides it, and an explicit `0` disables the inherited boost. This
nullable inheritance rule applies only to `directMatchBoost`; all existing selection
properties keep their previous overlay behavior. Omitting the property everywhere
resolves it to `0` and preserves existing clean-CV selection.

### MMR parameters

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

- Too many similar bullets: lower `relevanceWeight`, lower `saturationQuota`, or raise `saturationPenalty`.
- Important requirements disappear: raise their `requiredTags` weights or raise `relevanceWeight`.
- One section dominates: lower that section's `itemBudget`.
- Older but more relevant work is displaced: reduce `recencyBoost`.
- Weakly related bullets appear: raise `scoreLowerBound`. 
  Tune this carefully because it is a hard pre-MMR filter.
- Use `--debug` after each change to inspect aggregate and per-bullet scores directly in the generated CV.

## Contributing

Note that more than 50% of this repo is AI-generated slop, including 90% of the tests.
I did the foundation, including the initial experience database, 
the basic tag matching algorithm, 
the tags database,
the rich text module,
and the initial latex rendering module,
while the rest is slop which I did not verify very carefully.
