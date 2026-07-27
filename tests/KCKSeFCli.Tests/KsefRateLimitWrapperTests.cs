// Defends the retry/backoff behaviour that PobierzFaktury, SzukajFaktur and PrzeslijFaktury
// all rely on to survive KSeF's HTTP 429 responses.
//
// Rate limiting used to reach exactly one call site (PobierzFaktury), so an agent driving the
// paginated search or a batch upload could hammer the API with no local backoff and no retry.
// Extending the wrapper to those paths made its behaviour load-bearing in three places, and it
// is a verbatim copy of an upstream test helper that may be re-synced later, so it is pinned
// here: retry only on 429, give up after the configured number of attempts, and never swallow
// anything else.
//
// The non-generic overload exists because the batch upload path calls methods returning a bare
// Task (SendBatchPartsAsync, CloseBatchSessionAsync); without it those calls could not be
// rate-limited at all.
using KCKSeFCli.Utils;

using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.RateLimits;
using KSeF.Client.Core.Models.TestData;

using Xunit;

namespace KCKSeFCli.Tests;

[Collection(RateLimitTrackerCollection.Name)]
public class KsefRateLimitWrapperTests {
    // High-limit endpoint (30/s, 120/min, 720/h) so the local throttle never sleeps during
    // these tests. The trackers are process-wide statics, so tests share this budget.
    private const KsefApiEndpoint FastEndpoint = KsefApiEndpoint.SessionInvoiceStatus;

    // retryAfterSeconds: 0 makes RecommendedDelay zero, so retries are instant here.
    private static KsefRateLimitException RateLimited() =>
        new("429 Too Many Requests", retryAfterSeconds: 0);

    [Fact]
    public async Task ReturnsResultWithoutRetryingWhenTheCallSucceeds() {
        int calls = 0;

        string result = await KsefRateLimitWrapper.ExecuteWithRetryAsync(
            _ => {
                calls++;
                return Task.FromResult("ok");
            },
            FastEndpoint,
            limitsClient: null,
            maxRetryAttempts: 3);

        Assert.Equal("ok", result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RetriesAfterRateLimitAndReturnsTheLaterSuccess() {
        int calls = 0;

        string result = await KsefRateLimitWrapper.ExecuteWithRetryAsync(
            _ => {
                calls++;
                if (calls < 3) {
                    throw RateLimited();
                }
                return Task.FromResult("ok");
            },
            FastEndpoint,
            limitsClient: null,
            maxRetryAttempts: 3);

        Assert.Equal("ok", result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task GivesUpAfterMaxRetryAttemptsAndRethrows() {
        int calls = 0;

        await Assert.ThrowsAsync<KsefRateLimitException>(() =>
            KsefRateLimitWrapper.ExecuteWithRetryAsync<string>(
                _ => {
                    calls++;
                    throw RateLimited();
                },
                FastEndpoint,
                limitsClient: null,
                maxRetryAttempts: 3));

        // The failure must surface rather than retrying forever; an agent needs the error.
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task DoesNotRetryErrorsOtherThanRateLimiting() {
        int calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            KsefRateLimitWrapper.ExecuteWithRetryAsync<string>(
                _ => {
                    calls++;
                    throw new InvalidOperationException("boom");
                },
                FastEndpoint,
                limitsClient: null,
                maxRetryAttempts: 3));

        // Retrying a non-429 failure would turn one rejected upload into several.
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PassesTheCancellationTokenToTheCall() {
        using CancellationTokenSource cts = new();
        CancellationToken observed = default;

        await KsefRateLimitWrapper.ExecuteWithRetryAsync(
            ct => {
                observed = ct;
                return Task.FromResult("ok");
            },
            FastEndpoint,
            limitsClient: null,
            maxRetryAttempts: 3,
            accessToken: null,
            cancellationToken: cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    [Fact]
    public async Task NonGenericOverloadRunsTheCall() {
        int calls = 0;

        await KsefRateLimitWrapper.ExecuteWithRetryAsync(
            _ => {
                calls++;
                return Task.CompletedTask;
            },
            FastEndpoint,
            limitsClient: null,
            maxRetryAttempts: 3);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task NonGenericOverloadRetriesAfterRateLimit() {
        int calls = 0;

        await KsefRateLimitWrapper.ExecuteWithRetryAsync(
            _ => {
                calls++;
                if (calls < 2) {
                    throw RateLimited();
                }
                return Task.CompletedTask;
            },
            FastEndpoint,
            limitsClient: null,
            maxRetryAttempts: 3);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NonGenericOverloadGivesUpAfterMaxRetryAttempts() {
        int calls = 0;

        await Assert.ThrowsAsync<KsefRateLimitException>(() =>
            KsefRateLimitWrapper.ExecuteWithRetryAsync(
                _ => {
                    calls++;
                    throw RateLimited();
                },
                FastEndpoint,
                limitsClient: null,
                maxRetryAttempts: 2));

        Assert.Equal(2, calls);
    }

    // ---------------------------------------------------------------------------------------
    // ResolveApiLimitsAsync used to issue a real GET /limits/rate on every single invocation,
    // with no cache. PobierzFaktury wraps the per-invoice download and SzukajFaktur the
    // per-page query, so downloading 1000 invoices made 1000 extra API calls — none of them
    // counted by the local EndpointRateTracker. A change made to reduce 429s roughly doubled
    // request volume on exactly the unbounded paths.
    //
    // Every other test in this file passes limitsClient: null, which is why it went unnoticed.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task DoesNotRefetchApiLimitsOnEveryCall() {
        CountingLimitsClient limits = new();

        for (int i = 0; i < 10; i++) {
            await KsefRateLimitWrapper.ExecuteWithRetryAsync(
                _ => Task.FromResult("ok"),
                FastEndpoint,
                limits,
                maxRetryAttempts: 3,
                accessToken: "token");
        }

        // Ten wrapped calls must not cost ten extra requests to the limits endpoint.
        Assert.True(
            limits.Calls < 10,
            $"GetRateLimitsAsync was called {limits.Calls} times for 10 wrapped calls; "
            + "the limits lookup is being repeated per call.");
    }

    // The limits lookup sits outside the retry loop, so anything it throws escapes before the
    // wrapped call is ever attempted. On a bulk download that aborts the whole run part-way
    // through because of a failure on an endpoint that is not even the one being called.
    // Static limits are a perfectly good fallback — ResolveApiLimitsAsync already falls back
    // to them when no client is supplied.
    [Fact]
    public async Task StillRunsTheCallWhenTheLimitsEndpointFails() {
        FailingLimitsClient limits = new();
        int calls = 0;

        string result = await KsefRateLimitWrapper.ExecuteWithRetryAsync(
            _ => {
                calls++;
                return Task.FromResult("ok");
            },
            FastEndpoint,
            limits,
            maxRetryAttempts: 3,
            accessToken: "token");

        Assert.Equal("ok", result);
        Assert.Equal(1, calls);
    }

    // ---------------------------------------------------------------------------------------
    // maxRetryAttempts below 1 used to fall through the retry loop into a generic throw.
    //
    // --retry-attempts is a plain int option on both PrzeslijFaktury and SzukajFaktur. At 0 the
    // for-loop body never ran and the method threw InvalidOperationException("Nieoczekiwane
    // zakończenie pętli powtórzeń dla ..."), which Program.cs reports as exit 3, unhandled
    // exception — a stack-trace-shaped answer to an ordinary bad argument, with no mention of
    // the flag that caused it.
    //
    // tests/unit.sh pins the CLI-level half (reject the value at parse time, before any
    // authentication). This pins the wrapper itself so the confusing exception cannot come back
    // by another route.
    // ---------------------------------------------------------------------------------------
    [Fact]
    public async Task RejectsANonPositiveRetryBudgetAsABadArgument() {
        ArgumentOutOfRangeException ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            KsefRateLimitWrapper.ExecuteWithRetryAsync(
                _ => Task.FromResult("ok"),
                FastEndpoint,
                limitsClient: null,
                maxRetryAttempts: 0));

        Assert.Equal("maxRetryAttempts", ex.ParamName);
    }

    private sealed class CountingLimitsClient : ILimitsClient {
        public int Calls { get; private set; }

        public Task<EffectiveApiRateLimits> GetRateLimitsAsync(
            string accessToken, CancellationToken cancellationToken = default) {
            Calls++;
            return Task.FromResult(new EffectiveApiRateLimits());
        }

        public Task<SessionLimitsInCurrentContextResponse> GetLimitsForCurrentContextAsync(
            string accessToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CertificatesLimitInCurrentSubjectResponse> GetLimitsForCurrentSubjectAsync(
            string accessToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailingLimitsClient : ILimitsClient {
        public Task<EffectiveApiRateLimits> GetRateLimitsAsync(
            string accessToken, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("limits endpoint unavailable");

        public Task<SessionLimitsInCurrentContextResponse> GetLimitsForCurrentContextAsync(
            string accessToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CertificatesLimitInCurrentSubjectResponse> GetLimitsForCurrentSubjectAsync(
            string accessToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
