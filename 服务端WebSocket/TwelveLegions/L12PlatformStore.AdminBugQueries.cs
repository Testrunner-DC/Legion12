namespace TwelveLegions.Server;

public sealed record L12AdminBugPageView(IReadOnlyList<L12BugReportView> Items,
    int Page, int PageSize, int Total, long PlatformVersion);

public sealed partial class L12PlatformStore
{
    // Legacy Bugs() stays complete for intake/closure consumers. Only the
    // explicit page query copies history for selected rows. Version and rows
    // are sampled together for the existing bug.update business-version CAS.
    public L12AdminBugPageView QueryBugs(int page = 1, int pageSize = 10,
        string? status = null, string? priority = null, string? assignee = null,
        string? search = null, CancellationToken cancellationToken = default)
    {
        search = search?.Trim() ?? string.Empty;
        if (search.Length > 200) throw new ArgumentException("Bug搜索最多200个字符", nameof(search));
        pageSize = Math.Clamp(pageSize, 1, 100);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var matching = _data.BugReports.Where(row =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return MatchesBugQuery(row, status, priority, assignee, search);
            }).OrderByDescending(row => row.CreatedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
            var pages = Math.Max(1, (int)Math.Ceiling(matching.Length / (double)pageSize));
            page = Math.Clamp(page, 1, pages);
            var items = matching.Skip((page - 1) * pageSize).Take(pageSize).Select(row =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ToView(row);
            }).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return new(items, page, pageSize, matching.Length, Version);
        }
    }

    private static bool MatchesBugQuery(BugRow row, string? status, string? priority,
        string? assignee, string? search)
        => (string.IsNullOrWhiteSpace(status) || row.Status == status)
           && (string.IsNullOrWhiteSpace(priority) || row.Priority == priority)
           && (string.IsNullOrWhiteSpace(assignee)
               || string.Equals(row.Assignee, assignee.Trim(), StringComparison.OrdinalIgnoreCase))
           && MatchesBugSearch(row, search);
}
