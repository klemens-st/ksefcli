// Serialises the test classes that drive KsefRateLimitWrapper.
//
// KsefRateLimitWrapper.Trackers is a process-wide static ConcurrentDictionary of sliding
// windows, keyed by endpoint. xUnit parallelises across test classes by default, so two
// classes exercising the same KsefApiEndpoint share one rate budget: whichever runs second can
// find the window already spent by the first and sleep, or trip an assertion about call counts
// that is really about interleaving. The tests here are about the wrapper's logic, not about
// its behaviour under concurrent load, so the sharing is noise.
//
// Both KsefRateLimitWrapperTests and OpenFindingsTests use SessionInvoiceStatus and one of them
// fires ten calls in a tight loop; a one-off flake was observed before this was added.
using Xunit;

namespace KCKSeFCli.Tests;

[CollectionDefinition(Name)]
public class RateLimitTrackerCollection {
    public const string Name = "RateLimitTrackers";
}
