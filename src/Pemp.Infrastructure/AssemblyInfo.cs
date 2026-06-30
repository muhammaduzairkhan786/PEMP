using System.Runtime.CompilerServices;

// The unscoped EngagementStore.GetAsync/ListAsync are internal (they cross every scope boundary and must
// never be reachable from a request path). The persistence test suite asserts post-state via them, so it
// is granted internals access here — keeping the production surface scoped while tests can still verify.
[assembly: InternalsVisibleTo("Pemp.Infrastructure.Tests")]
