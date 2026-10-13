namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Coalesce only owned session revocations. There is still one existing full
    // transaction, committed projection and post-commit notification authority.
    private const int SessionRevocationQueueLimit = 128;
    private readonly object _sessionRevocationQueueGate = new();
    private readonly Queue<QueuedSessionRevocation> _sessionRevocationQueue = new();
    private int _sessionRevocationsOutstanding;
    private bool _sessionRevocationDrainActive;

    private sealed record QueuedSessionRevocation(L12AuthenticatedSession Actor, string? SessionId,
        bool Others, L12AdminAuditContext? Context, CancellationToken Cancellation,
        TaskCompletionSource<L12SessionRevocationResult> Completion);

    public Task<L12SessionRevocationResult> RevokeOwnSessionAsync(L12AuthenticatedSession actor,
        string sessionId, L12AdminAuditContext? context = null, CancellationToken cancellationToken = default)
        => QueueOwnedSessionRevocation(actor, sessionId, false, context, cancellationToken);

    public Task<L12SessionRevocationResult> RevokeOwnSessionsAsync(L12AuthenticatedSession actor,
        bool others = false, L12AdminAuditContext? context = null, CancellationToken cancellationToken = default)
        => QueueOwnedSessionRevocation(actor, null, others, context, cancellationToken);

    private Task<L12SessionRevocationResult> QueueOwnedSessionRevocation(L12AuthenticatedSession actor,
        string? sessionId, bool others, L12AdminAuditContext? context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<L12SessionRevocationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sessionRevocationQueueGate)
        {
            if (_sessionRevocationsOutstanding >= SessionRevocationQueueLimit)
                throw new L12PlatformStorageUnavailableException("会话撤销队列繁忙，请稍后重试");
            _sessionRevocationQueue.Enqueue(new(actor, sessionId, others, context, cancellationToken, completion));
            _sessionRevocationsOutstanding++;
            if (!_sessionRevocationDrainActive)
            {
                _sessionRevocationDrainActive = true;
                _ = Task.Run(DrainOwnedSessionRevocationsAsync);
            }
        }
        // Cancellation before execution discards this command. Once staged, the
        // durable transaction must finish, even if its HTTP caller disconnects.
        return completion.Task.WaitAsync(cancellationToken);
    }

    private async Task DrainOwnedSessionRevocationsAsync()
    {
        while (true)
        {
            // A small bounded admission window combines a burst without making
            // one blocked ThreadPool worker per incoming HTTP request.
            await Task.Delay(5).ConfigureAwait(false);
            QueuedSessionRevocation[] batch;
            lock (_sessionRevocationQueueGate)
            {
                if (_sessionRevocationQueue.Count == 0)
                {
                    _sessionRevocationDrainActive = false;
                    return;
                }
                batch = _sessionRevocationQueue.ToArray();
                _sessionRevocationQueue.Clear();
            }
            var results = new L12SessionRevocationResult?[batch.Length];
            var errors = new Exception?[batch.Length];
            try
            {
                ExecuteAdminTransaction(() =>
                {
                    // Validate all admitted callers against the same staged
                    // authority before any revocation in this atomic batch.
                    for (var i = 0; i < batch.Length; i++)
                    {
                        var item = batch[i];
                        if (item.Cancellation.IsCancellationRequested)
                        {
                            errors[i] = new OperationCanceledException(item.Cancellation);
                            continue;
                        }
                        var account = _data.Accounts.FirstOrDefault(row => row.Id == item.Actor.Account.Id);
                        var session = _data.Sessions.FirstOrDefault(row => row.Id == item.Actor.SessionId
                            && row.AccountId == item.Actor.Account.Id);
                        if (account is null || account.Disabled || account.Deleted
                            || account.PermissionVersion != item.Actor.Account.PermissionVersion
                            || account.Role != item.Actor.Account.Role || session is null
                            || session.RevokedAt is not null || session.ExpiresAt <= DateTimeOffset.UtcNow)
                            errors[i] = new UnauthorizedAccessException("会话已失效，请重新登录");
                    }
                    for (var i = 0; i < batch.Length; i++)
                    {
                        if (errors[i] is not null) continue;
                        var item = batch[i];
                        results[i] = item.SessionId is { } id
                            ? RevokeOwnSession(item.Actor, id, item.Context)
                            : item.Others ? RevokeOtherOwnSessions(item.Actor, item.Context)
                            : RevokeOwnSessions(item.Actor, item.Context);
                    }
                    return true;
                });
            }
            catch (Exception error)
            {
                // No partial success or notification if the shared commit fails.
                for (var i = 0; i < batch.Length; i++)
                    if (errors[i] is null) errors[i] = error;
            }
            lock (_sessionRevocationQueueGate) _sessionRevocationsOutstanding -= batch.Length;
            for (var i = 0; i < batch.Length; i++)
            {
                if (errors[i] is OperationCanceledException)
                    batch[i].Completion.TrySetCanceled(batch[i].Cancellation);
                else if (errors[i] is { } error) batch[i].Completion.TrySetException(error);
                else batch[i].Completion.TrySetResult(results[i]!);
            }
        }
    }
}
