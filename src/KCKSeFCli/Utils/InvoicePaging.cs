using KSeF.Client.Core.Models.Invoices;

namespace KCKSeFCli.Utils;

/// <summary>
/// Walks the pages of a KSeF invoice metadata query.
///
/// KSeF paginates by <b>page index</b>, not by record offset: <c>pageOffset</c> is the number of
/// the page, so the page after 0 is 1 regardless of <c>pageSize</c>. The upstream client
/// documents it that way on the method itself
/// (<c>IInvoiceDownloadClient.QueryInvoiceMetadataAsync</c>, "Numer strony wyników"), and its own
/// paging loops advance with <c>pageOffset++</c>.
/// </summary>
public static class InvoicePaging {
    /// <summary>
    /// The page after <paramref name="currentPageOffset"/>.
    ///
    /// Takes no page size on purpose. Scaling the advance by the page size is the one mistake
    /// this helper exists to prevent, and it cannot be written here.
    /// </summary>
    public static int NextPageOffset(int currentPageOffset) => currentPageOffset + 1;

    /// <summary>
    /// Requests consecutive pages from <paramref name="startPageOffset"/> for as long as the API
    /// reports that more results follow, and returns every invoice from every page.
    /// </summary>
    /// <param name="fetchPage">
    /// Fetches one page by its index. Takes the page index alone so that the caller's page size
    /// cannot leak into the advance.
    /// </param>
    /// <param name="startPageOffset">Index of the first page to request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<List<InvoiceSummary>> CollectAllPagesAsync(
        Func<int, CancellationToken, Task<PagedInvoiceResponse>> fetchPage,
        int startPageOffset,
        CancellationToken cancellationToken) {
        List<InvoiceSummary> allInvoices = new List<InvoiceSummary>();
        int currentPageOffset = startPageOffset;
        PagedInvoiceResponse pagedInvoicesResponse;

        do {
            Log.Information($"Pobieranie strony wyników nr {currentPageOffset}");
            pagedInvoicesResponse = await fetchPage(currentPageOffset, cancellationToken).ConfigureAwait(false);

            // An absent list is an empty page, not the end of the results; HasMore decides that.
            if (pagedInvoicesResponse.Invoices != null) {
                allInvoices.AddRange(pagedInvoicesResponse.Invoices);
            }

            currentPageOffset = NextPageOffset(currentPageOffset);
        } while (pagedInvoicesResponse.HasMore == true);

        return allInvoices;
    }
}
