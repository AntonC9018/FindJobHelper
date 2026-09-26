# FindJobHelper Engine guidance

## Coding standards

- Do not assume the API must be backward compatible unless explicitly asked.
  Do not hesitate to break things and update code that refers to the code that
  changes its interface.
- Use named arguments where a function or constructor takes multiple arguments
  of the same type and it is not clear which argument is for which parameter.
- Do not add constructor overloads solely for dependency injection or test setup.
  Change the existing constructor and update callers/tests, or use a builder when
  construction genuinely needs multiple optional configurations.
- Keep each statement focused on one conceptual operation.
- An inline condition may contain at most two simple checks when both inspect the
  same value and express one invariant, such as a range. Split checks of different
  values into separate guards. Do not mix `&&` and `||` inline. Do not combine
  parsing, lookup, conversion, validation, or mutation in one condition. Move
  repeated alternatives to a helper that uses guards, a loop, or another linear
  structure.
- Do not nest one conceptual operation inside another operation's arguments.
  Calculate, convert, look up, validate, format, and join values before passing
  them to a constructor or method. Direct values and expressions containing at
  most one simple calculation operator may remain inline. Move conditional
  expressions to named variables.
- Linear LINQ pipelines, or pipelines built with other declarative APIs like it,
  are allowed. Prefer simple lambdas for their stages. When a projection needs
  several operations, use a block lambda with named intermediate values. Use a
  local function when the logic needs nested conditionals or several temporary
  values that the containing method does not otherwise use. A single intermediate
  value may remain local to the containing method. Move reusable logic to a
  private method. Shape final collections explicitly, for example with named key
  and value selectors.
- Apply these rules to all hand-written code, including tests and code generators.
  Generated files are exempt.

### Explicit return decisions

- After a conditional early return, express later return decisions with `if` or
  `switch` statements and explicit returns. Do not end the function with a
  boolean predicate, nullable result, ternary, or switch expression that can
  represent multiple outcomes. This applies to every return type. A direct
  return of a value already known to represent one outcome is fine. A lone
  conditional return with no earlier conditional return may remain a ternary.
- When a final lookup result can be absent, check it and return the found value
  from a branch, then explicitly return the fallback. Match the direction of
  earlier branches that return found values.

### Sentinel return values

- For application-owned functions beyond private helpers, represent absence or
  failure in the return type instead of using a primitive sentinel such as
  `-1`. A private helper may use a sentinel. Before forwarding its result
  through another primitive-returning function, check it and explicitly
  return that function's own sentinel. Signatures imposed by dependencies are
  exempt.

### Span-based text handling

- Prefer spans for text inspection, slicing, and trimming. Use spans for
  parameters and return values when string ownership is unnecessary and the
  span's lifetime permits it. Create a string when one is needed.

## CV generation entry point

`FindJobHelper.Generation.CvGenerationPipeline` is the single public entry
point for generating a CV. The CLI and the workspace web UI are thin frontends
over it: they load the domain config via `FindJobHelper.Configuration.Json`
and pass it to the pipeline. Keep orchestration (experience database
loading, LaTeX discovery, font resolution, measurement, matching, artifact
planning and publishing) in Generation; keep domain/search/rendering/
measurement internals and the embedded LaTeX template in Core;
keep console rendering, exit codes, and the Spectre progress display in the
CLI, and transport concerns (HTTP, job queuing) in the frontend.
