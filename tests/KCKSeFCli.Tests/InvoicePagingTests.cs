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

        List<InvoiceSummary> invoices = await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(
                requested,
                Page(hasMore: true, "K1", "K2"),
                Page(hasMore: true, "K3", "K4"),
                Page(hasMore: false, "K5")),
            startPageOffset: 0,
            CancellationToken.None);

        Assert.Equal(
            new[] { "K1", "K2", "K3", "K4", "K5" },
            invoices.Select(invoice => invoice.KsefNumber));
    }

    [Fact]
    public async Task StopsAfterTheFirstPageWhenNoMoreResultsFollow() {
        List<int> requested = new List<int>();

        List<InvoiceSummary> invoices = await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(requested, Page(hasMore: false, "K1")),
            startPageOffset: 0,
            CancellationToken.None);

        Assert.Equal(new[] { 0 }, requested);
        Assert.Single(invoices);
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
        List<InvoiceSummary> invoices = await InvoicePaging.CollectAllPagesAsync(
            FakeFetch(
                requested,
                new PagedInvoiceResponse { HasMore = true, Invoices = null },
                Page(hasMore: false, "K1")),
            startPageOffset: 0,
            CancellationToken.None);

        Assert.Equal(new[] { 0, 1 }, requested);
        Assert.Equal(new[] { "K1" }, invoices.Select(invoice => invoice.KsefNumber));
    }
}
