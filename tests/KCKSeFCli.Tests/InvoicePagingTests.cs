// Defends the page-walking behaviour behind SzukajFaktur, and behind PobierzFaktury, which
// inherits the same query.
//
// KSeF paginates by page index, not by record offset: the page after 0 is 1, whatever the page
// size. The loop used to advance by pageSize, so with the default --pageSize 10 it requested
// page 0, then page 10 — records 101-110 — then page 20. Against a context holding between 11
// and 100 invoices in the window that second request came back empty with HasMore false, the
// loop stopped, and the command reported exactly one page of results as if that were all of
// them. Above 100 invoices it was quieter and worse: pages 0, 10, 20 return real but
// non-consecutive results, so nine tenths of the matches went missing without a diagnostic.
//
// Nothing in the response distinguishes "you have reached the end" from "you asked for a page
// far past the end", which is why the defect read as an empty tail rather than an error. The
// contract is pinned here as the exact sequence of page indices requested, so re-introducing a
// pageSize-scaled advance fails loudly instead of silently under-reporting invoices.
//
// Two further ways the same query could return fewer invoices than exist, and say nothing, are
// pinned below:
//
//   * A negative --pageOffset. PaginationHelper appends the parameter only when it is > 0, so
//     the API fell back to page 0 for every negative index the walk passed through: --pageOffset
//     -3 fetched page 0 four times over before reaching page 1, and the duplicates went into the
//     result as if they were distinct invoices. A page size below 1 was dropped the same way,
//     leaving the option silently without effect.
//   * IsTruncated. KSeF caps a query at 10 000 results and says so in the response; the flag was
//     never read, so a capped result set was printed exactly like a complete one.
using KCKSeFCli.Utils;

using KSeF.Client.Core.Models.Invoices;

using Xunit;

namespace KCKSeFCli.Tests;

public class InvoicePagingTests {
    private static InvoiceSummary Invoice(string ksefNumber) =>
        new InvoiceSummary { KsefNumber = ksefNumber };

    // A page that claims more results follow; the last page in a fake sequence says otherwise.
    private static PagedInvoiceResponse Page(bool hasMore, params string[] ksefNumbers) =>
        new PagedInvoiceResponse {
            HasMore = hasMore,
            Invoices = ksefNumbers.Select(Invoice).ToList()
        };

    // A page on which KSeF reports that it stopped at the 10 000 result cap.
    private static PagedInvoiceResponse TruncatedPage(bool hasMore, params string[] ksefNumbers) =>
        new PagedInvoiceResponse {
            HasMore = hasMore,
            IsTruncated = true,
            Invoices = ksefNumbers.Select(Invoice).ToList()
        };

    // Records which page indices were asked for, and replays the given pages in order.
    private static Func<int, CancellationToken, Task<PagedInvoiceResponse>> FakeFetch(
        List<int> requested,
        params PagedInvoiceResponse[] pages) {
        int call = 0;
        return (pageOffset, _) => {
            requested.Add(pageOffset);
            PagedInvoiceResponse page = pages[call];
            call++;
            return Task.FromResult(page);
        };
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(9, 10)]
    [InlineData(41, 42)]
    public void NextPageOffsetAdvancesByOnePage(int current, int expected) {
        // The whole defect in one assertion: the successor of page 0 is page 1, never page 10.
        // NextPageOffset deliberately takes no page size, so the old arithmetic cannot be
        // expressed here at all.
        Assert.Equal(expected, InvoicePaging.NextPageOffset(current));
    }

    [Fact]
    public async Task RequestsConsecutivePageIndices() {
        List<int> requested = new List<int>();

        await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(
                requested,
                Page(hasMore: true, "K1", "K2"),
                Page(hasMore: true, "K3", "K4"),
                Page(hasMore: false, "K5")),
            startPageOffset: 0,
            CancellationToken.None);

        // Not 0, 10, 20 — that skipped pages 1-9 entirely and, on a smaller result set,
        // returned nothing at all for the second request.
        Assert.Equal(new[] { 0, 1, 2 }, requested);
    }

    [Fact]
    public async Task ReturnsInvoicesFromEveryPageInOrder() {
        List<int> requested = new List<int>();

        InvoiceQueryResult result = await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(
                requested,
                Page(hasMore: true, "K1", "K2"),
                Page(hasMore: true, "K3", "K4"),
                Page(hasMore: false, "K5")),
            startPageOffset: 0,
            CancellationToken.None);

        Assert.Equal(
            new[] { "K1", "K2", "K3", "K4", "K5" },
            result.Invoices.Select(invoice => invoice.KsefNumber));
    }

    [Fact]
    public async Task StopsAfterTheFirstPageWhenNoMoreResultsFollow() {
        List<int> requested = new List<int>();

        InvoiceQueryResult result = await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(requested, Page(hasMore: false, "K1")),
            startPageOffset: 0,
            CancellationToken.None);

        Assert.Equal(new[] { 0 }, requested);
        Assert.Single(result.Invoices);
    }

    [Fact]
    public async Task StartsFromTheRequestedPageOffset() {
        List<int> requested = new List<int>();

        // --pageOffset 3 means "start at page 3", so the walk continues 4, 5 — it does not
        // restart at 0 and it does not jump by the page size.
        await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(
                requested,
                Page(hasMore: true, "K1"),
                Page(hasMore: true, "K2"),
                Page(hasMore: false, "K3")),
            startPageOffset: 3,
            CancellationToken.None);

        Assert.Equal(new[] { 3, 4, 5 }, requested);
    }

    [Fact]
    public async Task TreatsAPageWithNoInvoiceListAsEmpty() {
        List<int> requested = new List<int>();

        // KSeF omits the array rather than sending [] on some empty pages; that must not throw
        // and must not be mistaken for the end of the results.
        InvoiceQueryResult result = await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(
                requested,
                new PagedInvoiceResponse { HasMore = true, Invoices = null },
                Page(hasMore: false, "K1")),
            startPageOffset: 0,
            CancellationToken.None);

        Assert.Equal(new[] { 0, 1 }, requested);
        Assert.Equal(new[] { "K1" }, result.Invoices.Select(invoice => invoice.KsefNumber));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    [InlineData(int.MinValue)]
    public void RejectsANegativePageOffset(int pageOffset) {
        string? error = InvoicePaging.ValidatePageOffset(pageOffset);

        Assert.NotNull(error);
        // The operator has to be told which option they got wrong; "invalid argument" alone
        // sends them looking through twenty other options.
        Assert.Contains("--pageOffset", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    public void AcceptsAPageOffsetFromZeroUp(int pageOffset) {
        // Zero is the first page and the default, not a missing value.
        Assert.Null(InvoicePaging.ValidatePageOffset(pageOffset));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void RejectsAPageSizeBelowOne(int pageSize) {
        // PaginationHelper appends pageSize only when it is > 0, so anything smaller was dropped
        // from the query and the API's own default silently applied instead.
        string? error = InvoicePaging.ValidatePageSize(pageSize);

        Assert.NotNull(error);
        Assert.Contains("--pageSize", error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void AcceptsAPageSizeOfOneOrMore(int pageSize) {
        // No upper bound here on purpose: the maximum is the server's to enforce, and pinning a
        // guess would reject a page size KSeF accepts.
        Assert.Null(InvoicePaging.ValidatePageSize(pageSize));
    }

    [Fact]
    public async Task ReportsAnUntruncatedResultAsComplete() {
        List<int> requested = new List<int>();

        InvoiceQueryResult result = await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(requested, Page(hasMore: true, "K1"), Page(hasMore: false, "K2")),
            startPageOffset: 0,
            CancellationToken.None);

        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task ReportsTheApisOwnTruncationFlag() {
        List<int> requested = new List<int>();

        // KSeF stops at 10 000 results and says so. Saying nothing back turns "here are the
        // first 10 000 of your matches" into "here are your matches".
        InvoiceQueryResult result = await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(requested, TruncatedPage(hasMore: false, "K1")),
            startPageOffset: 0,
            CancellationToken.None);

        Assert.True(result.Truncated);
        Assert.Equal(new[] { "K1" }, result.Invoices.Select(invoice => invoice.KsefNumber));
    }

    [Fact]
    public async Task RemembersTruncationReportedOnAnEarlierPage() {
        List<int> requested = new List<int>();

        // Truncation is a fact about the whole query, so a later page that does not repeat the
        // flag must not clear it. Which pages carry it is the server's business.
        InvoiceQueryResult result = await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(
                requested,
                TruncatedPage(hasMore: true, "K1"),
                Page(hasMore: false, "K2")),
            startPageOffset: 0,
            CancellationToken.None);

        Assert.True(result.Truncated);
    }

    [Fact]
    public void TruncationWarnsAndSignalsPartialSuccess() {
        // Exit 2 is this CLI's "partial success". A truncated query is exactly that: the invoices
        // returned are real, but they are not all of them, and a script reading stdout cannot
        // tell the difference from the JSON alone.
        Assert.Equal(2, InvoicePaging.ExitCodeFor(truncated: true));
        Assert.Equal(0, InvoicePaging.ExitCodeFor(truncated: false));

        Assert.Null(InvoicePaging.TruncationWarning(truncated: false));
        string? warning = InvoicePaging.TruncationWarning(truncated: true);
        Assert.NotNull(warning);
        // The number is the actionable part: it tells the operator the limit they hit.
        Assert.Contains("10 000", warning);
    }
}
