namespace TwelveLegions.Server;

public sealed record L12AdminAccountPageView(IReadOnlyList<L12AccountView> Items,
    int Page, int PageSize, int Total, int ActiveCount, int DeletedCount);

public sealed partial class L12PlatformStore
{
    // Compatibility Accounts() remains unchanged. Only this explicit query
    // projects one bounded page; searching never hydrates private deck bodies.
    public L12AdminAccountPageView QueryAccounts(int page = 1, int pageSize = 10,
        string? search = null, bool deleted = false, CancellationToken cancellationToken = default)
    {
        search = search?.Trim() ?? string.Empty;
        if (search.Length > 200) throw new ArgumentException("账号搜索最多200个字符", nameof(search));
        pageSize = Math.Clamp(pageSize, 1, 100);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var deletedCount = _data.Accounts.Count(row => row.Deleted);
            var matching = _data.Accounts.Where(row =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return row.Deleted == deleted && (search.Length == 0
                    || PublicUsername(row).Contains(search, StringComparison.OrdinalIgnoreCase)
                    || row.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || MaskEmail(row.Email)?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
                    || row.DisabledReason?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
            }).OrderBy(row => row.CreatedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
            var pages = Math.Max(1, (int)Math.Ceiling(matching.Length / (double)pageSize));
            page = Math.Clamp(page, 1, pages);
            cancellationToken.ThrowIfCancellationRequested();
            return new(matching.Skip((page - 1) * pageSize).Take(pageSize).Select(ToView).ToArray(),
                page, pageSize, matching.Length, _data.Accounts.Count - deletedCount, deletedCount);
        }
    }
}
