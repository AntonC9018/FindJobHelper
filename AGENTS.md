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
- Keep collection transformations linear and readable. When a LINQ projection
  performs lookup or validation and constructs nested domain objects, use a block
  lambda with named intermediate values for each step instead of nesting the
  entire operation in constructors and conditional expressions. Shape the final
  collection explicitly (for example, with named key and value selectors).
