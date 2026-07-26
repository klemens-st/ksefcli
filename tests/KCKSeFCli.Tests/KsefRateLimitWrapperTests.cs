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

using Xunit;

namespace KCKSeFCli.Tests;

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
}
