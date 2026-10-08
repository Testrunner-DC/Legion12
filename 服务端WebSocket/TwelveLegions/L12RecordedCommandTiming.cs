namespace TwelveLegions.Server;

internal sealed record L12RecordedCommandTiming(
    DateTimeOffset OccurredUtc,
    long Revision,
    bool Accepted);

internal static class L12RecordedCommandOrigin
{
    internal static bool AllowsInternalReplay(string type, int playerIndex, bool accepted)
    {
        if (!accepted) return false;
        if (string.Equals(type, "setResponsePreference", StringComparison.OrdinalIgnoreCase))
            return playerIndex is 0 or 1;
        return playerIndex == -1
               && (string.Equals(type, "authorityConclusion", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(type, "responseAutoClose", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed partial class L12GameEngine
{
    private DateTimeOffset? _activeRecordedCommandUtc;
    private L12RecordedCommandTiming? _pendingRecordedCommandTiming;

    private CommandResult ExecuteRecordedCommand(Func<CommandResult> execute,
        DateTimeOffset? occurredUtc = null, bool publishTiming = true)
    {
        if (_activeRecordedCommandUtc is not null) return execute();

        _pendingRecordedCommandTiming = null;
        _activeRecordedCommandUtc = (occurredUtc ?? _utcNow()).ToUniversalTime();
        try
        {
            var result = execute();
            if (publishTiming)
                _pendingRecordedCommandTiming = new L12RecordedCommandTiming(
                    _activeRecordedCommandUtc.Value, State.Revision, result.Accepted);
            return result;
        }
        finally
        {
            _activeRecordedCommandUtc = null;
        }
    }

    private bool ExecuteRecordedResponseAutoClose(DateTimeOffset observedAtUtc,
        Func<bool> execute)
    {
        if (_activeRecordedCommandUtc is not null) return execute();

        _pendingRecordedCommandTiming = null;
        _activeRecordedCommandUtc = observedAtUtc.ToUniversalTime();
        try
        {
            var applied = execute();
            _pendingRecordedCommandTiming = new L12RecordedCommandTiming(
                _activeRecordedCommandUtc.Value, State.Revision, Accepted: true);
            return applied;
        }
        finally
        {
            _activeRecordedCommandUtc = null;
        }
    }

    internal CommandResult ReplayRecordedCommand(DateTimeOffset occurredUtc,
        Func<CommandResult> execute)
        => ExecuteRecordedCommand(execute, occurredUtc, publishTiming: false);

    private DateTimeOffset RecordedCommandUtcNow()
        => (_activeRecordedCommandUtc ?? _utcNow()).ToUniversalTime();

    internal L12RecordedCommandTiming? TakeRecordedCommandTiming(CommandResult result,
        bool validateResult)
    {
        var timing = _pendingRecordedCommandTiming;
        _pendingRecordedCommandTiming = null;
        if (timing is null) return null;
        if (validateResult
            && (timing.Revision != State.Revision || timing.Accepted != result.Accepted))
            throw new InvalidOperationException("待持久化命令时刻与当前权威结果不一致");
        return timing;
    }

    internal void DiscardRecordedCommandTiming()
        => _pendingRecordedCommandTiming = null;
}
