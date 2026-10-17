namespace TwelveLegions.Server;

public sealed partial class L12GameEngine
{
    // A paused table must not consume its five-second response lease. The resume is a
    // recorded command, so replay and recovery see the identical authoritative deadline.
    internal CommandResult ResumeTournamentResponseClock(DateTimeOffset pausedAtUtc,
        DateTimeOffset resumedAtUtc)
        => ExecuteRecordedCommand(() =>
        {
            if (resumedAtUtc < pausedAtUtc) return CommandResult.Reject("暂停时间范围无效");
            if (State.ResponseWindow?.AutoCloseDeadlineUtc is not { } deadline) return CommandResult.Ok();
            // GM may have created a new response while paused. Never grant it more than
            // its original five seconds, or charge time before that window existed.
            var windowStartedAt = deadline.AddSeconds(-5);
            var frozenFrom = windowStartedAt > pausedAtUtc ? windowStartedAt : pausedAtUtc;
            if (resumedAtUtc > frozenFrom)
            {
                State.ResponseWindow.AutoCloseDeadlineUtc = deadline.Add(resumedAtUtc - frozenFrom);
                State.Revision++;
            }
            return CommandResult.Ok();
        }, resumedAtUtc);
}
