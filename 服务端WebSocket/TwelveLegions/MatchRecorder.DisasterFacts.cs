using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

internal enum L12DisasterDecisionOrigin { Player, ServerSetupTimeout }
internal sealed record L12DisasterDecisionFact(int Step, string Action, int ActorIndex,
    long OfferedSequence, long OfferedRevision, string[] Candidates, long? ResolvedSequence,
    long? ResolvedRevision, string? ChosenCardId, string? Origin);
internal sealed record L12DisasterSetupFactsView(string Coverage, string Status, string? Mode,
    string[] Pool, string? PublicCardId, L12DisasterCompositionCard[] Composition,
    L12DisasterDecisionFact[] Decisions, string? EffectVersion = null);
internal sealed record L12DisasterSetupCoverage(long Total, long Exact, long Unknown,
    long Incomplete, long NotApplicable);

public sealed partial class MatchRecorder
{
    private sealed record DisasterReceipt(long Sequence, long Revision, string Origin, string Digest);
    private sealed record DisasterCapture(string Mode, string EffectVersion, string PoolJson,
        string SeedHash, string? PublicCard, long? PublicSequence, long? PublicRevision,
        string? CompositionJson, long? CompositionSequence, long? CompositionRevision,
        string Status, string Coverage, string ReceiptsJson);

    private static SqliteCommand DisasterCommand(SqliteConnection connection, SqliteTransaction? transaction,
        string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private async Task InitializeDisasterFactsAsync(SqliteConnection connection)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        using (var version = DisasterCommand(connection, transaction,
                   "SELECT version FROM match_recorder_schema WHERE component='disaster-setup-analytics';"))
            if (await version.ExecuteScalarAsync() is long existingVersion && existingVersion > 1)
                throw new InvalidDataException("天灾准备事实结构版本不兼容");
        using var command = DisasterCommand(connection, transaction, """
            CREATE TABLE IF NOT EXISTS match_disaster_captures (
                match_id TEXT PRIMARY KEY REFERENCES matches(match_id) ON DELETE CASCADE,
                capture_version INTEGER NOT NULL CHECK(capture_version=1),
                source TEXT NOT NULL CHECK(source='native'),
                effect_version TEXT NOT NULL, disaster_mode TEXT NOT NULL,
                pool_json TEXT NOT NULL, seed_hash TEXT NOT NULL,
                public_card_id TEXT, public_sequence INTEGER, public_revision INTEGER,
                composition_json TEXT, composition_sequence INTEGER, composition_revision INTEGER,
                setup_status TEXT NOT NULL CHECK(setup_status IN ('pending','complete','interrupted')),
                coverage TEXT NOT NULL CHECK(coverage IN ('partial','exact','not-applicable')),
                receipts_json TEXT NOT NULL DEFAULT '[]'
            );
            CREATE TABLE IF NOT EXISTS match_disaster_decisions (
                match_id TEXT NOT NULL REFERENCES match_disaster_captures(match_id) ON DELETE CASCADE,
                step INTEGER NOT NULL CHECK(step IN (0,1,2,4,5)),
                action TEXT NOT NULL CHECK(action IN ('ban','pick')),
                actor_index INTEGER NOT NULL CHECK(actor_index IN (0,1)),
                offered_sequence INTEGER NOT NULL, offered_revision INTEGER NOT NULL,
                resolved_sequence INTEGER, resolved_revision INTEGER, chosen_card_id TEXT,
                origin TEXT CHECK(origin IN ('player','server-setup-timeout')),
                PRIMARY KEY(match_id,step),
                CHECK((resolved_sequence IS NULL AND resolved_revision IS NULL AND chosen_card_id IS NULL AND origin IS NULL)
                   OR (resolved_sequence IS NOT NULL AND resolved_revision IS NOT NULL AND chosen_card_id IS NOT NULL AND origin IS NOT NULL))
            );
            CREATE TABLE IF NOT EXISTS match_disaster_candidates (
                match_id TEXT NOT NULL, step INTEGER NOT NULL, card_id TEXT NOT NULL,
                PRIMARY KEY(match_id,step,card_id),
                FOREIGN KEY(match_id,step) REFERENCES match_disaster_decisions(match_id,step) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ix_match_disaster_candidates_card
                ON match_disaster_candidates(card_id,match_id,step);
            CREATE INDEX IF NOT EXISTS ix_match_disaster_decisions_actor
                ON match_disaster_decisions(match_id,actor_index,action);
            """);
        await command.ExecuteNonQueryAsync();
        StorageFailureInjector?.Invoke("before-disaster-schema-marker");
        using var marker = DisasterCommand(connection, transaction, """
            INSERT INTO match_recorder_schema(component,version,migrated_utc)
            VALUES('disaster-setup-analytics',1,$utc)
            ON CONFLICT(component) DO UPDATE SET version=MAX(version,excluded.version);
            """, ("$utc", _utcNow().ToUniversalTime().ToString("O")));
        await marker.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }

    private static string DisasterSeedHash(string mode, string version, string poolJson)
        => DisasterHash(JsonSerializer.Serialize(new { mode, version, poolJson }));
    private static string DisasterHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string DisasterDigest(IEnumerable<L12DisasterSetupObservation> observations, string origin)
        => DisasterHash(JsonSerializer.Serialize(new
        {
            origin,
            observations = observations.OrderBy(item => item.Step).ThenBy(item => item.Kind, StringComparer.Ordinal),
        }));
    private static T ReadDisasterJson<T>(string value)
        => JsonSerializer.Deserialize<T>(value) ?? throw new InvalidDataException("天灾准备事实 JSON 缺失");
    private static InvalidDataException DisasterConflict() => new("天灾准备事实缺失或与权威命令冲突");

    private async Task PersistDisasterStartAsync(SqliteConnection connection, SqliteTransaction transaction,
        L12GameEngine? engine, string mode)
    {
        if (engine is null) return;
        var seed = engine.DisasterCaptureSeed;
        var eligible = UsesJournalV2(engine) && seed is not null && engine.State.Phase == L12Phase.Initiative
            && mode is "ranked" or "casual" or "friendly" or "tournament"
            && seed.Mode is "all" or "season" or "random" or "none";
        engine.DisasterCaptureRegistered = eligible;
        if (!eligible) return;
        var pool = JsonSerializer.Serialize(seed!.Pool);
        using var insert = DisasterCommand(connection, transaction, """
            INSERT INTO match_disaster_captures(match_id,capture_version,source,effect_version,
                disaster_mode,pool_json,seed_hash,setup_status,coverage)
            VALUES($id,1,'native',$version,$mode,$pool,$hash,'pending','partial');
            """, ("$id", engine.State.MatchId), ("$version", seed.EffectVersion),
            ("$mode", seed.Mode), ("$pool", pool), ("$hash", DisasterSeedHash(seed.Mode, seed.EffectVersion, pool)));
        await insert.ExecuteNonQueryAsync();
        StorageFailureInjector?.Invoke("after-disaster-start-write");
    }

    private static async Task<DisasterCapture?> LoadDisasterCaptureAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string matchId)
    {
        using var command = DisasterCommand(connection, transaction, """
            SELECT disaster_mode,effect_version,pool_json,seed_hash,public_card_id,public_sequence,
                   public_revision,composition_json,composition_sequence,composition_revision,
                   setup_status,coverage,receipts_json,capture_version,source
            FROM match_disaster_captures WHERE match_id=$id;
            """, ("$id", matchId));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        if (reader.GetInt32(13) != 1 || reader.GetString(14) != "native") throw DisasterConflict();
        string? Text(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
        long? Number(int index) => reader.IsDBNull(index) ? null : reader.GetInt64(index);
        var capture = new DisasterCapture(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), Text(4), Number(5), Number(6), Text(7), Number(8), Number(9),
            reader.GetString(10), reader.GetString(11), reader.GetString(12));
        if (capture.SeedHash != DisasterSeedHash(capture.Mode, capture.EffectVersion, capture.PoolJson))
            throw DisasterConflict();
        return capture;
    }

    private static async Task<L12DisasterDecisionFact[]> LoadDisasterDecisionsAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string matchId)
    {
        var candidates = new Dictionary<int, List<string>>();
        using (var select = DisasterCommand(connection, transaction, """
                   SELECT step,card_id FROM match_disaster_candidates WHERE match_id=$id ORDER BY step,card_id;
                   """, ("$id", matchId)))
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                var step = reader.GetInt32(0);
                if (!candidates.TryGetValue(step, out var cards)) candidates[step] = cards = [];
                cards.Add(reader.GetString(1));
            }
        var result = new List<L12DisasterDecisionFact>();
        using var command = DisasterCommand(connection, transaction, """
            SELECT step,action,actor_index,offered_sequence,offered_revision,
                   resolved_sequence,resolved_revision,chosen_card_id,origin
            FROM match_disaster_decisions WHERE match_id=$id ORDER BY step;
            """, ("$id", matchId));
        await using var rows = await command.ExecuteReaderAsync();
        while (await rows.ReadAsync())
        {
            var step = rows.GetInt32(0);
            if (!candidates.Remove(step, out var cards) || cards.Count == 0) throw DisasterConflict();
            result.Add(new(step, rows.GetString(1), rows.GetInt32(2), rows.GetInt64(3), rows.GetInt64(4),
                cards.ToArray(), rows.IsDBNull(5) ? null : rows.GetInt64(5),
                rows.IsDBNull(6) ? null : rows.GetInt64(6), rows.IsDBNull(7) ? null : rows.GetString(7),
                rows.IsDBNull(8) ? null : rows.GetString(8)));
        }
        if (candidates.Count != 0) throw DisasterConflict();
        return result.ToArray();
    }

    private static L12DisasterSetupObservation[] DisasterObservationsAt(DisasterCapture capture,
        IReadOnlyList<L12DisasterDecisionFact> decisions, long sequence)
    {
        var facts = new List<L12DisasterSetupObservation>();
        foreach (var decision in decisions)
        {
            if (decision.OfferedSequence == sequence)
                facts.Add(new("offer", decision.Step, decision.ActorIndex, decision.Candidates));
            if (decision.ResolvedSequence == sequence)
                facts.Add(new("decision", decision.Step, decision.ActorIndex, CardId: decision.ChosenCardId));
        }
        if (capture.PublicSequence == sequence) facts.Add(new("public", 3, CardId: capture.PublicCard));
        if (capture.CompositionSequence == sequence)
            facts.Add(new("complete", 6, Composition:
                ReadDisasterJson<L12DisasterCompositionCard[]>(capture.CompositionJson!)));
        return facts.ToArray();
    }

    private static DisasterReceipt[] ValidateDisasterReceipts(DisasterCapture capture,
        IReadOnlyList<L12DisasterDecisionFact> decisions)
    {
        var receipts = ReadDisasterJson<DisasterReceipt[]>(capture.ReceiptsJson);
        if (receipts.Length > 10 || receipts.Select(row => row.Sequence).Distinct().Count() != receipts.Length)
            throw DisasterConflict();
        var sequences = decisions.Select(row => row.OfferedSequence)
            .Concat(decisions.Where(row => row.ResolvedSequence.HasValue).Select(row => row.ResolvedSequence!.Value))
            .Concat(capture.PublicSequence.HasValue ? [capture.PublicSequence.Value] : Array.Empty<long>())
            .Concat(capture.CompositionSequence.HasValue ? [capture.CompositionSequence.Value] : Array.Empty<long>())
            .Distinct().Order().ToArray();
        if (!sequences.SequenceEqual(receipts.Select(row => row.Sequence).Order())) throw DisasterConflict();
        foreach (var receipt in receipts)
        {
            if (receipt.Sequence <= 0 || receipt.Origin is not ("player" or "server-setup-timeout")
                || receipt.Digest != DisasterDigest(DisasterObservationsAt(capture, decisions, receipt.Sequence), receipt.Origin))
                throw DisasterConflict();
            foreach (var decision in decisions)
                if (decision.OfferedSequence == receipt.Sequence && decision.OfferedRevision != receipt.Revision
                    || decision.ResolvedSequence == receipt.Sequence
                    && (decision.ResolvedRevision != receipt.Revision || decision.Origin != receipt.Origin))
                    throw DisasterConflict();
            if (capture.PublicSequence == receipt.Sequence && capture.PublicRevision != receipt.Revision
                || capture.CompositionSequence == receipt.Sequence && capture.CompositionRevision != receipt.Revision)
                throw DisasterConflict();
        }
        return receipts;
    }

    private async Task PersistDisasterObservationsAsync(SqliteConnection connection, SqliteTransaction transaction,
        L12GameEngine engine, long sequence, bool accepted, L12DisasterDecisionOrigin origin, bool duplicate = false)
    {
        if (engine.DisasterCaptureRegistered == false
            || !duplicate && engine.DisasterSetupObservations.Count == 0) return;
        var capture = await LoadDisasterCaptureAsync(connection, transaction, engine.State.MatchId);
        if (capture is null)
        {
            if (engine.DisasterCaptureRegistered == true) throw DisasterConflict();
            engine.DisasterCaptureRegistered = false;
            return; // Legacy restored games are never promoted into native exact coverage.
        }
        engine.DisasterCaptureRegistered = true;
        var decisions = await LoadDisasterDecisionsAsync(connection, transaction, engine.State.MatchId);
        var receipts = ValidateDisasterReceipts(capture, decisions);
        ValidateDisasterStructure(capture, decisions, engine.State.FirstPlayer);
        var observations = engine.DisasterSetupObservations.ToArray();
        var originText = origin == L12DisasterDecisionOrigin.ServerSetupTimeout ? "server-setup-timeout" : "player";
        if (duplicate)
        {
            var existing = receipts.SingleOrDefault(row => row.Sequence == sequence);
            if (observations.Length > 0 && (!accepted || existing is null
                || existing.Digest != DisasterDigest(observations, originText))) throw DisasterConflict();
            if (existing is not null && (existing.Origin != originText || existing.Revision != engine.State.Revision))
                throw DisasterConflict();
            return;
        }
        if (!accepted)
        {
            if (observations.Length != 0) throw DisasterConflict();
            return;
        }
        if (observations.Length == 0) return;
        if (receipts.Any(row => row.Sequence == sequence)) throw DisasterConflict();
        foreach (var observation in observations)
        {
            if (observation.Kind is "offer" or "decision")
            {
                var expectedActor = observation.Step is 1 or 5 ? 1 - engine.State.FirstPlayer : engine.State.FirstPlayer;
                if (observation.Step is not (0 or 1 or 2 or 4 or 5) || observation.Actor != expectedActor)
                    throw DisasterConflict();
            }
            if (observation.Kind == "offer")
            {
                var cards = observation.Candidates ?? [];
                if (cards.Length == 0 || cards.Distinct(StringComparer.Ordinal).Count() != cards.Length
                    || cards.Any(card => !ReadDisasterJson<string[]>(capture.PoolJson).Contains(card, StringComparer.Ordinal)))
                    throw DisasterConflict();
                using var offer = DisasterCommand(connection, transaction, """
                    INSERT INTO match_disaster_decisions(match_id,step,action,actor_index,offered_sequence,offered_revision)
                    VALUES($id,$step,$action,$actor,$seq,$rev);
                    """, ("$id", engine.State.MatchId), ("$step", observation.Step),
                    ("$action", observation.Step < 3 ? "ban" : "pick"), ("$actor", observation.Actor),
                    ("$seq", sequence), ("$rev", engine.State.Revision));
                await offer.ExecuteNonQueryAsync();
                foreach (var card in cards)
                {
                    using var candidate = DisasterCommand(connection, transaction, """
                        INSERT INTO match_disaster_candidates(match_id,step,card_id) VALUES($id,$step,$card);
                        """, ("$id", engine.State.MatchId), ("$step", observation.Step), ("$card", card));
                    await candidate.ExecuteNonQueryAsync();
                }
            }
            else if (observation.Kind == "decision")
            {
                using var resolve = DisasterCommand(connection, transaction, """
                    UPDATE match_disaster_decisions SET resolved_sequence=$seq,resolved_revision=$rev,
                        chosen_card_id=$card,origin=$origin
                    WHERE match_id=$id AND step=$step AND actor_index=$actor AND resolved_sequence IS NULL
                      AND EXISTS(SELECT 1 FROM match_disaster_candidates c
                          WHERE c.match_id=$id AND c.step=$step AND c.card_id=$card);
                    """, ("$id", engine.State.MatchId), ("$step", observation.Step), ("$actor", observation.Actor),
                    ("$seq", sequence), ("$rev", engine.State.Revision), ("$card", observation.CardId), ("$origin", originText));
                if (await resolve.ExecuteNonQueryAsync() != 1) throw DisasterConflict();
            }
            else if (observation.Kind == "public")
            {
                using var reveal = DisasterCommand(connection, transaction, """
                    UPDATE match_disaster_captures SET public_card_id=$card,public_sequence=$seq,public_revision=$rev
                    WHERE match_id=$id AND public_card_id IS NULL;
                    """, ("$id", engine.State.MatchId), ("$card", observation.CardId), ("$seq", sequence),
                    ("$rev", engine.State.Revision));
                if (await reveal.ExecuteNonQueryAsync() != 1) throw DisasterConflict();
            }
            else if (observation.Kind == "complete")
            {
                using var complete = DisasterCommand(connection, transaction, """
                    UPDATE match_disaster_captures SET composition_json=$composition,composition_sequence=$seq,
                        composition_revision=$rev,setup_status='complete',coverage=$coverage
                    WHERE match_id=$id AND composition_sequence IS NULL;
                    """, ("$id", engine.State.MatchId), ("$composition", JsonSerializer.Serialize(observation.Composition)),
                    ("$seq", sequence), ("$rev", engine.State.Revision),
                    ("$coverage", capture.Mode == "none" ? "not-applicable" : "exact"));
                if (await complete.ExecuteNonQueryAsync() != 1) throw DisasterConflict();
            }
            else throw DisasterConflict();
        }
        using var receipt = DisasterCommand(connection, transaction, """
            UPDATE match_disaster_captures SET receipts_json=$receipts WHERE match_id=$id;
            """, ("$id", engine.State.MatchId), ("$receipts", JsonSerializer.Serialize(receipts.Append(
                new DisasterReceipt(sequence, engine.State.Revision, originText, DisasterDigest(observations, originText))))));
        await receipt.ExecuteNonQueryAsync();
        var written = (await LoadDisasterCaptureAsync(connection, transaction, engine.State.MatchId))!;
        var writtenDecisions = await LoadDisasterDecisionsAsync(connection, transaction, engine.State.MatchId);
        ValidateDisasterReceipts(written, writtenDecisions);
        ValidateDisasterStructure(written, writtenDecisions, engine.State.FirstPlayer);
        StorageFailureInjector?.Invoke("after-disaster-facts-write");
    }

    private static void ValidateDisasterStructure(DisasterCapture capture, L12DisasterDecisionFact[] decisions, int first)
    {
        var pool = ReadDisasterJson<string[]>(capture.PoolJson);
        if (pool.Distinct(StringComparer.Ordinal).Count() != pool.Length
            || pool.Contains(L12ActiveDisasterRules.AnnihilationCardId)) throw DisasterConflict();
        var remaining = pool.ToHashSet(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (decision.ActorIndex != (decision.Step is 1 or 5 ? 1 - first : first)
                || decision.Action != (decision.Step < 3 ? "ban" : "pick")
                || decision.Candidates.Any(card => !remaining.Contains(card))) throw DisasterConflict();
            if (decision.Step < 3 && !remaining.SetEquals(decision.Candidates)
                || decision.Step == 4 && decision.Candidates.Length != Math.Min(3, remaining.Count)
                || decision.Step == 5 && decision.Candidates.Length != Math.Min(2, remaining.Count)) throw DisasterConflict();
            if (decision.ChosenCardId is not null)
            {
                if (!decision.Candidates.Contains(decision.ChosenCardId, StringComparer.Ordinal)
                    || decision.ResolvedSequence < decision.OfferedSequence) throw DisasterConflict();
                if (decision.Step < 3) remaining.Remove(decision.ChosenCardId);
                else remaining.ExceptWith(decision.Candidates);
            }
            if (decision.Step == 2 && decision.ChosenCardId is not null && capture.PublicCard is not null)
                if (!remaining.Remove(capture.PublicCard)) throw DisasterConflict();
        }
        if (capture.Status != "complete") return;
        var composition = ReadDisasterJson<L12DisasterCompositionCard[]>(capture.CompositionJson!);
        if (composition.Select(card => card.CardId).Distinct(StringComparer.Ordinal).Count() != composition.Length)
            throw DisasterConflict();
        if (capture.Mode is "all" or "season")
        {
            if (!decisions.Select(row => row.Step).SequenceEqual(new[] { 0, 1, 2, 4, 5 })
                || decisions.Any(row => row.ChosenCardId is null) || capture.PublicCard is null) throw DisasterConflict();
            var expected = decisions.Where(row => row.Action == "pick").Select(row =>
                    new L12DisasterCompositionCard(row.ChosenCardId!, "player-pick", row.ActorIndex))
                .Append(new(capture.PublicCard, "random-public", null))
                .Append(new(L12ActiveDisasterRules.AnnihilationCardId, "fixed", null))
                .OrderBy(card => card.CardId, StringComparer.Ordinal).ToArray();
            if (!composition.SequenceEqual(expected)) throw DisasterConflict();
        }
        else if (capture.Mode == "none")
        {
            if (composition.Length != 0 || decisions.Length != 0 || capture.Coverage != "not-applicable") throw DisasterConflict();
        }
        else if (capture.Mode == "random")
        {
            if (decisions.Length != 0 || composition.Length != Math.Min(3, pool.Length) + 1
                || composition.Count(card => card.CardId == L12ActiveDisasterRules.AnnihilationCardId
                    && card.Source == "fixed" && card.OwnerIndex is null) != 1
                || composition.Where(card => card.Source != "fixed").Any(card => card.Source != "random-mode"
                    || card.OwnerIndex is not null || !pool.Contains(card.CardId))) throw DisasterConflict();
        }
        else throw DisasterConflict();
    }

    private static async Task FinalizeDisasterCaptureAsync(SqliteConnection connection,
        SqliteTransaction transaction, string matchId)
    {
        using var command = DisasterCommand(connection, transaction, """
            UPDATE match_disaster_captures SET setup_status='interrupted',coverage='partial'
            WHERE match_id=$id AND setup_status='pending';
            """, ("$id", matchId));
        await command.ExecuteNonQueryAsync();
    }

    // No endpoint calls this in phase one. Staff detail never grants live private candidates.
    internal async Task<L12DisasterSetupFactsView> ReadDisasterSetupFactsAsync(string matchId,
        L12AccountView viewer, CancellationToken cancellationToken = default)
    {
        if (viewer.Disabled || viewer.Deleted || !L12Authorization.HasPermission(viewer, L12Permission.AdminMatchesRead))
            throw new UnauthorizedAccessException("无权读取对局禁选明细");
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var match = DisasterCommand(connection, transaction,
            "SELECT ended_utc,first_player FROM matches WHERE match_id=$id;", ("$id", matchId));
        int first;
        await using (var reader = await match.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return new("unknown", "unavailable", null, [], null, [], []);
            if (reader.IsDBNull(0)) return new("private", "active", null, [], null, [], []);
            first = reader.IsDBNull(1) ? -1 : reader.GetInt32(1);
        }
        var result = await ReadDisasterSetupFactsInSnapshotAsync(connection, transaction, matchId, first,
            cancellationToken);
        transaction.Commit();
        return result;
    }

    private static async Task<L12DisasterSetupFactsView> ReadDisasterSetupFactsInSnapshotAsync(
        SqliteConnection connection, SqliteTransaction transaction, string matchId, int first,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var capture = await LoadDisasterCaptureAsync(connection, transaction, matchId);
        if (capture is null) return new("unknown", "legacy-unavailable", null, [], null, [], []);
        var decisions = await LoadDisasterDecisionsAsync(connection, transaction, matchId);
        ValidateDisasterReceipts(capture, decisions);
        ValidateDisasterStructure(capture, decisions, first);
        var result = new L12DisasterSetupFactsView(capture.Coverage, capture.Status, capture.Mode,
            ReadDisasterJson<string[]>(capture.PoolJson), capture.PublicCard,
            capture.CompositionJson is null ? [] : ReadDisasterJson<L12DisasterCompositionCard[]>(capture.CompositionJson), decisions,
            capture.EffectVersion);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    internal async Task<L12DisasterSetupCoverage> ReadDisasterSetupCoverageAsync(
        L12CommittedStatisticsExclusions exclusions, CancellationToken cancellationToken = default)
    {
        if (!exclusions.Available) throw new L12PlatformStorageUnavailableException("已提交统计排除集不可用");
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = DisasterCommand(connection, transaction, """
            SELECT m.match_id,m.first_player FROM matches m
            WHERE m.ended_utc IS NOT NULL AND m.winner IN (0,1) AND COALESCE(m.error,'')=''
              AND m.mode_id IN ('ranked','casual','friendly','tournament')
              AND NOT EXISTS(SELECT 1 FROM json_each($matches) e WHERE e.value=m.match_id)
              AND NOT EXISTS(SELECT 1 FROM json_each($accounts) e WHERE e.value=m.account_0 OR e.value=m.account_1);
            """, ("$matches", JsonSerializer.Serialize(exclusions.MatchIds)),
            ("$accounts", JsonSerializer.Serialize(exclusions.AccountIds)));
        var matches = new List<(string Id, int First)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                matches.Add((reader.GetString(0), reader.IsDBNull(1) ? -1 : reader.GetInt32(1)));
        long exact = 0, unknown = 0, incomplete = 0, notApplicable = 0;
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capture = await LoadDisasterCaptureAsync(connection, transaction, match.Id);
            if (capture is null) { unknown++; continue; }
            var decisions = await LoadDisasterDecisionsAsync(connection, transaction, match.Id);
            ValidateDisasterReceipts(capture, decisions);
            ValidateDisasterStructure(capture, decisions, match.First);
            if (capture.Status != "complete") incomplete++;
            else if (capture.Mode is "none" or "random") notApplicable++;
            else if (capture.Coverage == "exact") exact++;
            else throw DisasterConflict();
        }
        transaction.Commit();
        return new(matches.Count, exact, unknown, incomplete, notApplicable);
    }
}
