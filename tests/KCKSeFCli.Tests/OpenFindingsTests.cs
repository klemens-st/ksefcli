// Tests for findings from the review of the hardening branch that are NOT yet fixed.
//
// Almost every test in this file is expected to FAIL against the current tree. Each one
// describes a defect that is still present; the test is the acceptance criterion for the
// corresponding fix. Once a finding is fixed, move its test into the file that owns that area
// (InvoiceTotalsTests.cs, KsefRateLimitWrapperTests.cs) so this file stays a list of open work
// and eventually disappears.
//
// The exception is SummarizeAccountsForEveryNetItWasGivenWhenRatesShareAFieldPair, which
// passes today. It is here because it constrains the fix rather than the defect: the cheapest
// way to satisfy the test above it would be to drop one of the two colliding bands, and that
// would trade a double-write for a silently missing band. Both must hold at once.
//
// Findings that are NOT represented here, and why:
//
//   * The P_13_4 comment in InvoiceTotals mislabels the band ("rolnik ryczałtowy"; the schema
//     says "ryczałt dla taksówek osobowych" at schemat_FA(3)_v1-0E.xsd:2558). A comment cannot
//     be asserted on. The 4% mapping itself is correct — the taxi flat rate is 4% — and is
//     already pinned by InvoiceTotalsTests.
//   * PobierzFaktury's filename collisions (SafeFileName is many-to-one, so two invoices can
//     land on one path and the second silently overwrites the first). SafeFileName is right to
//     be many-to-one; the fix belongs at the call site, as a disambiguator applied when the
//     target already exists. There is no pure function to aim a test at until that helper
//     exists, and the command itself needs the network and real invoices. Write the test
//     alongside the fix, against whatever helper it introduces.
//   * Downloader's delete-then-move window (File.Delete then File.Move is not atomic, so a
//     failure between them loses an already-verified cached generator). Reaching it means
//     making File.Move fail while File.Delete succeeds, which needs a filesystem seam the code
//     does not have. The fix — File.Move(temp, dest, overwrite: true) — removes the window
//     rather than handling it, so there would be nothing left to assert on afterwards.
//
// Findings retracted on investigation, recorded here so they are not "rediscovered":
//
//   * That Bands is missing an entry for rate 3, "the historical second reduced rate pairing
//     with P_13_3". There is no 3% VAT rate in Polish tax law, and the schema does not say
//     otherwise: TStawkaPodatku does list 3 among the values P_12 will accept, but no P_13_x
//     pair is documented against it anywhere. P_13_1 names "23% albo 22%" and P_13_4 names the
//     taxi flat rate by name rather than by percentage. The pairing was invented; the absence
//     of 3 from Bands is correct and should stay. Rates with no pair are refused by design.
//
//   * clitest_prod_upload_allowed_with_yes and clitest_test_env_upload_not_gated were reported
//     as vacuous, on the grounds that they grep a stderr-only refusal out of a stdout-only
//     capture. The stream claim is right but the conclusion is wrong: L_unittest_cmd merges
//     both streams when the command is prefixed with "!", which both tests do. Sabotaging
//     DangerousOperation.Evaluate to refuse unconditionally makes both tests fail, with or
//     without -j. See the note now in CLAUDE.md's test conventions.
using KCKSeFCli.Utils;

using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.RateLimits;
using KSeF.Client.Core.Models.TestData;

using Xunit;

namespace KCKSeFCli.Tests;

public class OpenFindingsTests {
    // High-limit endpoint (30/s, 120/min, 720/h) so the local throttle never sleeps here, the
    // same choice KsefRateLimitWrapperTests makes. The trackers are process-wide statics.
    private const KsefApiEndpoint FastEndpoint = KsefApiEndpoint.SessionInvoiceStatus;

    // ---------------------------------------------------------------------------------------
    // Finding: InvoiceTotals.Summarize keys bands by percent, not by field pair.
    //
    // FA(3) gives 23% and 22% the same P_13_1/P_14_1 pair (likewise 8% and 7% share P_13_2).
    // Summarize groups on band.Value.Percent, so an input carrying both rates of a pair yields
    // two BandTotals aimed at the same element. WystawKorekteCommand.RecalculateTotals then
    // assigns netField.Value twice and the second write wins, while P_15 still counts both
    // lines — the invoice does not add up.
    //
    // Polish VAT law makes this unreachable through the tool's own commands: 22% is the base
    // rate and 23% the (long-)raised base rate, so no lawful invoice carries both, and
    // WystawKorekte only edits quantities and amounts — it cannot introduce a second rate.
    // What is left is an input-validation gap: WystawKorekte reads arbitrary XML, and on a
    // malformed invoice it silently computes wrong totals instead of saying so. The house
    // style elsewhere (DodajPozycjeNaFakturze refusing unsupported rates) is to refuse.
    //
    // The assertion below is deliberately about the *shape of the result*, not about how the
    // conflict is reported: two totals must never target one element. A fix that instead
    // refuses by throwing is equally acceptable — change this to Assert.Throws if that is the
    // route taken.
    // ---------------------------------------------------------------------------------------
    [Theory]
    // Stawka podstawowa: 22% and 23% both map to P_13_1/P_14_1.
    [InlineData("23", "22")]
    // Stawka obniżona pierwsza: 7% and 8% both map to P_13_2/P_14_2.
    [InlineData("8", "7")]
    public void SummarizeNeverReturnsTwoTotalsForTheSameFieldPair(string first, string second) {
        InvoiceTotals.Summary summary = InvoiceTotals.Summarize([
            (first, 100.00m),
            (second, 100.00m),
        ]);

        List<string> netFields = summary.Bands.Select(b => b.Band.NetField).ToList();

        Assert.Equal(netFields.Count, netFields.Distinct().Count());
    }

    // The same defect stated as arithmetic, so a fix cannot satisfy the test above by dropping
    // a band. Whatever Summarize reports has to reconcile with the net it was given.
    [Fact]
    public void SummarizeAccountsForEveryNetItWasGivenWhenRatesShareAFieldPair() {
        InvoiceTotals.Summary summary = InvoiceTotals.Summarize([
            ("23", 100.00m),
            ("22", 100.00m),
        ]);

        // Nothing here is an unsupported rate, so all 200.00 has to be inside the bands.
        Assert.Empty(summary.UnsupportedRates);
        Assert.Equal(200.00m, summary.Bands.Sum(b => b.Net));
        Assert.Equal(200.00m, summary.TotalNet);
    }

    // ---------------------------------------------------------------------------------------
    // Finding: ExecuteWithRetryAsync resolves API limits on every single invocation.
    //
    // ResolveApiLimitsAsync issues a real GET /limits/rate through ILimitsClient, with no
    // cache. PobierzFaktury wraps the per-invoice download and SzukajFaktur wraps the per-page
    // query, so downloading 1000 invoices now makes 1000 extra API calls — none of them
    // counted by the local EndpointRateTracker. A change made to reduce 429s roughly doubles
    // request volume on exactly the unbounded paths.
    //
    // The existing KsefRateLimitWrapperTests all pass limitsClient: null, which is why this
    // went unnoticed.
    //
    // Either fix satisfies this: cache the resolved limits inside the wrapper, or hoist the
    // resolution to the call site and pass ApiLimits in. If the call-site route is taken, this
    // test moves to the call site and the signature change makes it fail to compile — which is
    // the intended signal, not a problem.
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
    // Finding: maxRetryAttempts below 1 falls through the retry loop into a generic throw.
    //
    // --retry-attempts is a plain int option with no lower bound on both PrzeslijFaktury and
    // SzukajFaktur. At 0 the for-loop body never runs and the method throws
    // InvalidOperationException("Nieoczekiwane zakończenie pętli powtórzeń dla ..."), which
    // Program.cs reports as exit 3, unhandled exception — a stack-trace-shaped answer to an
    // ordinary bad argument, with no mention of the flag that caused it.
    //
    // tests/unit.sh pins the CLI-level fix (reject the value at parse time). This pins the
    // wrapper itself so the confusing exception cannot come back by another route.
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
