namespace TwelveLegions.Server;

public sealed record L12AdminCommandPageView(IReadOnlyList<L12AdminCommandView> Items,
    int Page, int PageSize, int Total);

public sealed partial class L12PlatformStore
{
    // Explicit full-history query; legacy AdminCommands(limit) remains a window.
    // Project payload/result JSON only for the selected page, not every match.
    public L12AdminCommandPageView QueryAdminCommands(int page = 1, int pageSize = 10,
        string? status = null, string? type = null, string? actorId = null,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var matching = _data.AdminCommands.Where(row =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return MatchesAdminCommandQuery(row, status, type, actorId);
            }).OrderByDescending(row => row.RequestedAt).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
            var pages = Math.Max(1, (int)Math.Ceiling(matching.Length / (double)pageSize));
            page = Math.Clamp(page, 1, pages);
            var items = matching.Skip((page - 1) * pageSize).Take(pageSize).Select(row =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ToView(row);
            }).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            return new(items, page, pageSize, matching.Length);
        }
    }

    private static bool MatchesAdminCommandQuery(AdminCommandRow row, string? status, string? type, string? actorId)
        => (string.IsNullOrWhiteSpace(status) || row.Status == status)
           && (string.IsNullOrWhiteSpace(type) || row.Type == type)
           && (string.IsNullOrWhiteSpace(actorId) || row.ActorId == actorId);
}
