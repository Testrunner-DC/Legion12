using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

// 玩家名字是展示属性，不是历史事实（用户 2026-11-26：各处显示的名字都要跟随改名）。
// 因此改名成功后，把所有"存了副本"的地方一次改掉；历史荣誉等读时解析的地方本来就跟。
// 审计条目也一起跟随——用户明确要求"都改"，这里按该口径执行。
public sealed partial class L12PlatformStore
{
    private void PropagateUsernameChangeLocked(string accountId, string nextUsername)
    {
        var changed = false;
        foreach (var row in _data.BugReports ?? [])
        {
            if (!string.Equals(row.ReporterId, accountId, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(row.ReporterName, nextUsername, StringComparison.Ordinal)) continue;
            row.ReporterName = nextUsername;
            changed = true;
        }
        // 审计行不改写：独立审计存储按「同一 id 必须是同一正文」做冲突检测，改写会让整个提交失败。
        // 审计里的名字改为读取时按 ActorId 解析当前名字（见 AdminAudit 投影）。
        foreach (var row in _data.PlayerMatchReports ?? [])
        {
            if (string.Equals(row.ReporterId, accountId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(row.ReporterName, nextUsername, StringComparison.Ordinal))
            { row.ReporterName = nextUsername; changed = true; }
            if (string.Equals(row.ReportedId, accountId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(row.ReportedName, nextUsername, StringComparison.Ordinal))
            { row.ReportedName = nextUsername; changed = true; }
        }
        // 不在这里 Save()：两个改名入口本身就在各自的事务里，重复落地会重入持久化。
        // 聊天副本在 platform.db 的独立连接上写，必须等本事务提交后再动，否则会撞库锁。
        _ = changed;
        _pendingUsernamePropagation.Add((accountId, nextUsername));
    }

    private readonly List<(string AccountId, string Username)> _pendingUsernamePropagation = [];

    /// <summary>改名在事务提交后广播一次，供持有独立存储（对局记录库）的组件同步展示名。</summary>
    internal event Action<string, string>? UsernameChanged;

    // 审计正文不可改写（独立审计存储按 id+正文判冲突），所以名字在读取时按 ActorId 解析成当前名。
    private IReadOnlyList<L12AdminAuditView> ResolveAuditActorNames(IReadOnlyList<L12AdminAuditView> views)
    {
        List<L12AdminAuditView>? resolved = null;
        for (var index = 0; index < views.Count; index++)
        {
            var view = views[index];
            if (string.IsNullOrWhiteSpace(view.ActorId)) continue;
            var account = _data.Accounts.FirstOrDefault(item => item.Id == view.ActorId);
            if (account is null) continue;
            var current = PublicUsername(account);
            if (string.Equals(current, view.ActorName, StringComparison.Ordinal)) continue;
            resolved ??= [.. views];
            resolved[index] = view with { ActorName = current };
        }
        return resolved ?? views;
    }

    /// <summary>在改名事务提交之后调用：把聊天表里的发送者名字副本一起改掉。</summary>
    private void FlushUsernamePropagationLocked()
    {
        if (_pendingUsernamePropagation.Count == 0) return;
        foreach (var (accountId, username) in _pendingUsernamePropagation)
        {
            PropagateUsernameToChatLocked(accountId, username);
            // 对局记录库（matches.db）由 MatchRecorder 自己持有，通过事件让它一并改名。
            UsernameChanged?.Invoke(accountId, username);
        }
        _pendingUsernamePropagation.Clear();
    }

    // 聊天记录在 platform.db 的独立表里：消息行存了发送者名字的副本。
    private void PropagateUsernameToChatLocked(string accountId, string nextUsername)
    {
        if (string.IsNullOrWhiteSpace(_databasePath) || !File.Exists(_databasePath)) return;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE chat_messages SET sender_name = $name WHERE sender_id = $id AND sender_name <> $name";
            command.Parameters.AddWithValue("$name", nextUsername);
            command.Parameters.AddWithValue("$id", accountId);
            command.ExecuteNonQuery();
        }
        catch (SqliteException error) when (error.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            // 老库还没建聊天表：没有副本可改，不是失败。
        }
    }
}
