using System.Collections.Concurrent;

namespace TwelveLegions.Server;

public sealed partial class L12WebSocketServer
{
    private const string PresenceResource = "presence";
    private const string FriendsResource = "friends";
    private const string IntegrityResource = "rankedIntegrity";
    private const string AlternateArtNotificationsResource = "alternateArtNotifications";
    private const string OperationsPolicyResource = "operationsPolicy";

    private readonly string _resourceEpoch = Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, long> _accountResourceRevisions =
        new(StringComparer.OrdinalIgnoreCase);
    private long _presenceResourceRevision;
    private long _operationsResourceRevision;
    private readonly object _operationsTransitionGate = new();
    private CancellationTokenSource? _operationsTransitionCancellation;
    private Task? _operationsTransitionTask;

    private string AccountResourceKey(string accountId, string resource)
        => $"{accountId.Trim().ToLowerInvariant()}:{resource}";

    private long AccountResourceRevision(string accountId, string resource)
        => _accountResourceRevisions.GetValueOrDefault(AccountResourceKey(accountId, resource));

    private long AdvanceAccountResource(string accountId, string resource)
        => _accountResourceRevisions.AddOrUpdate(AccountResourceKey(accountId, resource), 1, static (_, current) => current + 1);

    private object ResourceVersionsPayload(string accountId) => new
    {
        type = "resourceVersions",
        epoch = _resourceEpoch,
        revisions = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [PresenceResource] = Interlocked.Read(ref _presenceResourceRevision),
            [FriendsResource] = AccountResourceRevision(accountId, FriendsResource),
            [IntegrityResource] = AccountResourceRevision(accountId, IntegrityResource),
            [AlternateArtNotificationsResource] = AccountResourceRevision(accountId, AlternateArtNotificationsResource),
            [OperationsPolicyResource] = Interlocked.Read(ref _operationsResourceRevision),
        },
    };

    private IReadOnlyList<object> PresenceFor(string accountId)
    {
        var presence = _rooms.DescribeOnlinePresence(accountId);
        var friends = _platform.Friends(accountId)
            .ToDictionary(player => player.AccountId, player => player, StringComparer.OrdinalIgnoreCase);
        var pending = _platform.FriendRequests(accountId)
            .ToDictionary(player => player.AccountId, player => player, StringComparer.OrdinalIgnoreCase);
        return _platform.Accounts()
            .Where(player => presence.ContainsKey(player.Id))
            .OrderBy(player => player.Username)
            .Select(player =>
            {
                var state = presence[player.Id];
                var relationship = friends.GetValueOrDefault(player.Id) ?? pending.GetValueOrDefault(player.Id);
                return (object)new
                {
                    accountId = player.Id,
                    player.Username,
                    online = true,
                    state.Activity,
                    state.RoomCode,
                    state.CanInvite,
                    state.CanSpectate,
                    state.ActionReason,
                    friendStatus = player.Id == accountId ? "self" : relationship?.Status ?? "none",
                    friendDirection = relationship?.Direction ?? "none",
                };
            }).ToArray();
    }

    private OutgoingMessage PresenceSnapshotMessage(Guid sessionId, string accountId, long revision)
        => new(sessionId, new
        {
            type = "presenceSnapshot",
            resource = PresenceResource,
            epoch = _resourceEpoch,
            revision,
            items = PresenceFor(accountId),
        });

    private void NotifyPresenceChanged(IEnumerable<string>? accountIds = null)
    {
        var revision = Interlocked.Increment(ref _presenceResourceRevision);
        var targets = accountIds is null
            ? _activeAccountSockets.Keys.ToArray()
            : accountIds.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var messages = new List<OutgoingMessage>();
        foreach (var accountId in targets)
            if (_activeAccountSockets.TryGetValue(accountId, out var sessionId))
                messages.Add(PresenceSnapshotMessage(sessionId, accountId, revision));
        _ = SendManyAsync(messages, CancellationToken.None);
    }

    private void NotifyAccountResourceChanged(string resource, IEnumerable<string> accountIds)
    {
        var messages = new List<OutgoingMessage>();
        foreach (var accountId in accountIds.Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var revision = AdvanceAccountResource(accountId, resource);
            if (!_activeAccountSockets.TryGetValue(accountId, out var sessionId)) continue;
            messages.Add(new OutgoingMessage(sessionId, new
            {
                type = "resourceChanged",
                resource,
                epoch = _resourceEpoch,
                revision,
            }));
        }
        _ = SendManyAsync(messages, CancellationToken.None);
    }

    private void NotifyFriendsChanged(params string[] accountIds)
    {
        NotifyAccountResourceChanged(FriendsResource, accountIds);
        NotifyPresenceChanged(accountIds);
    }

    private void NotifyIntegrityChanged(IEnumerable<string> accountIds)
        => NotifyAccountResourceChanged(IntegrityResource, accountIds);

    private void NotifyAlternateArtNotificationsChanged(IEnumerable<string> accountIds)
        => NotifyAccountResourceChanged(AlternateArtNotificationsResource, accountIds);

    private void NotifyOperationsPolicyChanged(bool reschedule = true)
    {
        var revision = Interlocked.Increment(ref _operationsResourceRevision);
        var policy = _platform.EffectiveOperationsPolicy();
        var messages = _activeAccountSockets.Values.Select(sessionId => new OutgoingMessage(sessionId, new
        {
            type = "effectiveOperationsPolicy",
            resource = OperationsPolicyResource,
            epoch = _resourceEpoch,
            revision,
            policy,
        })).ToArray();
        _ = SendManyAsync(messages, CancellationToken.None);
        if (reschedule) ScheduleNextOperationsTransition();
    }

    private void ScheduleNextOperationsTransition()
    {
        var snapshot = _platform.CaptureOperationsPolicy();
        var now = DateTimeOffset.UtcNow;
        var candidates = new List<DateTimeOffset>();
        if (snapshot.Maintenance.Enabled && snapshot.Maintenance.StartsAt is { } startsAt)
        {
            candidates.Add(startsAt.AddHours(-snapshot.Maintenance.AdvanceBroadcastHours));
            candidates.Add(startsAt);
        }
        if (snapshot.Maintenance.Enabled && snapshot.Maintenance.EndsAt is { } endsAt) candidates.Add(endsAt);
        if (snapshot.Season.StartsAt is { } seasonStartsAt) candidates.Add(seasonStartsAt);
        if (snapshot.Season.EndsAt is { } seasonEndsAt) candidates.Add(seasonEndsAt);
        foreach (var announcement in snapshot.Announcements ?? [])
        {
            if (!announcement.Enabled) continue;
            if (announcement.StartsAt is { } announcementStartsAt) candidates.Add(announcementStartsAt);
            if (announcement.EndsAt is { } announcementEndsAt) candidates.Add(announcementEndsAt);
        }
        var next = candidates.Where(value => value > now.AddMilliseconds(100)).OrderBy(value => value).FirstOrDefault();
        lock (_operationsTransitionGate)
        {
            _operationsTransitionCancellation?.Cancel();
            _operationsTransitionCancellation?.Dispose();
            _operationsTransitionCancellation = null;
            _operationsTransitionTask = null;
            if (next == default) return;
            var cancellation = new CancellationTokenSource();
            _operationsTransitionCancellation = cancellation;
            _operationsTransitionTask = RunOperationsTransitionAsync(next, cancellation.Token);
        }
    }

    private async Task RunOperationsTransitionAsync(DateTimeOffset transitionAt, CancellationToken cancellationToken)
    {
        try
        {
            var delay = transitionAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            NotifyOperationsPolicyChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task StopOperationsTransitionAsync()
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (_operationsTransitionGate)
        {
            cancellation = _operationsTransitionCancellation;
            task = _operationsTransitionTask;
            _operationsTransitionCancellation = null;
            _operationsTransitionTask = null;
        }
        if (cancellation is null) return;
        cancellation.Cancel();
        if (task is not null)
            try { await task; }
            catch (OperationCanceledException) { }
        cancellation.Dispose();
    }
}
