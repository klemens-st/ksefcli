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
    /// Exit code for a query KSeF truncated: the invoices returned are real, but they are not
    /// all of the matches, which is this CLI's definition of a partial success.
    /// </summary>
    public const int TruncatedExitCode = 2;

    /// <summary>
    /// The page after <paramref name="currentPageOffset"/>.
    ///
    /// Takes no page size on purpose. Scaling the advance by the page size is the one mistake
    /// this helper exists to prevent, and it cannot be written here.
    /// </summary>
    public static int NextPageOffset(int currentPageOffset) => currentPageOffset + 1;

    /// <summary>
    /// Rejects a page index below zero, before the query is built.
    ///
    /// <c>PaginationHelper</c> appends <c>pageOffset</c> only when it is greater than zero, so a
    /// negative index was dropped from the URL and served as page 0 — the walk then requested
    /// page 0 once per negative index it passed through and returned those invoices repeatedly.
    /// </summary>
    public static string? ValidatePageOffset(int value) =>
        value < 0
            ? $"Błąd: --pageOffset nie może być ujemny (podano {value}). Jest to numer strony wyników, liczony od zera."
            : null;

    /// <summary>
    /// Rejects a page size below one. Dropped from the URL by the same rule as
    /// <see cref="ValidatePageOffset"/>, leaving the option with no effect at all.
    ///
    /// Deliberately has no upper bound: the maximum is the server's to enforce, and a guess here
    /// would refuse a page size KSeF accepts.
    /// </summary>
    public static string? ValidatePageSize(int value) =>
        value < 1
            ? $"Błąd: --pageSize musi być liczbą większą od zera (podano {value})."
            : null;

    /// <summary>
    /// The message for a truncated query, or null when the result set is complete.
    /// </summary>
    public static string? TruncationWarning(bool truncated) =>
        truncated
            ? "Uwaga: KSeF osiągnął maksymalny zakres wyników zapytania (10 000) i obciął resztę. "
              + "Wynik jest niekompletny — zawęź kryteria, np. dziel zapytanie na krótsze zakresy dat."
            : null;

    /// <summary>
    /// Exit code for a completed query. A truncated result reports partial success so that a
    /// caller reading stdout can tell it apart from a complete one — nothing in the JSON does.
    /// </summary>
    public static int ExitCodeFor(bool truncated) => truncated ? TruncatedExitCode : 0;

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
    public static async Task<InvoiceQueryResult> CollectAllPagesAsync(
        Func<int, CancellationToken, Task<PagedInvoiceResponse>> fetchPage,
        int startPageOffset,
        CancellationToken cancellationToken) {
        List<InvoiceSummary> allInvoices = new List<InvoiceSummary>();
        int currentPageOffset = startPageOffset;
        bool truncated = false;
        PagedInvoiceResponse pagedInvoicesResponse;

        do {
            Log.Information($"Pobieranie strony wyników nr {currentPageOffset}");
            pagedInvoicesResponse = await fetchPage(currentPageOffset, cancellationToken).ConfigureAwait(false);

            // An absent list is an empty page, not the end of the results; HasMore decides that.
            if (pagedInvoicesResponse.Invoices != null) {
                allInvoices.AddRange(pagedInvoicesResponse.Invoices);
            }

            // Truncation is a fact about the whole query, so it accumulates: which page carries
            // the flag is the server's business, and a later page must not clear it.
            truncated |= pagedInvoicesResponse.IsTruncated;

            currentPageOffset = NextPageOffset(currentPageOffset);
        } while (pagedInvoicesResponse.HasMore == true);

        return new InvoiceQueryResult(allInvoices, truncated);
    }
}

/// <summary>
/// Every invoice a query matched, and whether KSeF returned all of them.
///
/// The flag travels with the invoices rather than being logged and forgotten: the JSON on stdout
/// looks identical either way, so a caller has no other way to tell a capped result set from a
/// complete one.
/// </summary>
/// <param name="Invoices">The invoices from every page walked.</param>
/// <param name="Truncated">
/// True when KSeF reported hitting the 10 000 result cap on any page of the query.
/// </param>
public sealed record InvoiceQueryResult(List<InvoiceSummary> Invoices, bool Truncated);
