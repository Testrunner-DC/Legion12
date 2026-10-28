using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    private static bool IsVerifiedBugDisposition(string? disposition)
        => disposition is "fixed_verified" or "data_corrected_verified";

    private static L12BugClosureValidationException DataCorrectionError(string message)
        => new("bug_data_correction_invalid", message);

    private static void RequireDataKeys(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw DataCorrectionError("数据修正证据必须是对象。");
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != keys.Length || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length
            || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(keys))
            throw DataCorrectionError("数据修正证据字段缺失、重复或包含未知声明。");
    }

    private static string DataText(JsonElement value, string name, int max = 100)
    {
        var property = value.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
            throw DataCorrectionError("数据修正证据字段必须为文字。");
        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || text.Length > max)
            throw DataCorrectionError("数据修正证据字段为空或需要隐式归一化。");
        return text;
    }

    private static void RequireDataCorrectionInputShape(params string?[] values)
    {
        if (values.Any(value => value is not null && (value.Length > 500 || value != value.Trim())))
            throw DataCorrectionError("数据修正字段不能被隐式修剪或截断。");
    }

    // Normalize only known DTO date metadata, never arbitrary text or actual values.
    private static JsonNode NormalizeDataCorrectionDates(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Value is null) continue;
                if (property.Key is "CreatedAt" or "UpdatedAt" or "GrantedAt")
                {
                    var text = property.Value.GetValue<string>();
                    if (!(text.EndsWith("Z", StringComparison.Ordinal)
                            || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:\d{2}$"))
                        || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
                        throw DataCorrectionError("原审计时间必须包含明确时区。");
                    obj[property.Key] = instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                }
                else NormalizeDataCorrectionDates(property.Value);
            }
        }
        else if (node is JsonArray array)
            foreach (var child in array)
                if (child is not null) NormalizeDataCorrectionDates(child);
        return node;
    }

    private void ValidateDataCorrectionClosure(BugRow row, string targetStatus, string? fixCommit,
        string? regressionTest, string? deployedVersion, string? duplicateOf, string? comment,
        bool enteringClosure)
    {
        if (targetStatus != "resolved" || !string.IsNullOrEmpty(fixCommit) || !string.IsNullOrEmpty(duplicateOf))
            throw DataCorrectionError("数据修正必须已解决，且不能冒充代码修复或重复反馈。");
        if (!enteringClosure)
        {
            // A later priority/assignment edit is not a new verification and cannot replace its evidence.
            if (regressionTest != row.RegressionTest || deployedVersion != row.DeployedVersion)
                throw DataCorrectionError("已核验的数据终态证据不能被静默改写；请先重新打开反馈。");
            return;
        }
        if (row.FixCommit is not null || row.DuplicateOf is not null)
            throw DataCorrectionError("不能在数据关单时隐式清除既有代码修复或重复关联。");
        if (string.IsNullOrWhiteSpace(regressionTest) || regressionTest != regressionTest.Trim()
            || regressionTest.Length > 500 || deployedVersion != L12RuntimeBuildVersion.Capture().ServerRelease
            || string.IsNullOrWhiteSpace(comment) || comment != comment.Trim() || comment.Length > 2000)
            throw DataCorrectionError("需要真实具名数据核验、当前版本和完整且无需截断的原始证据。");
        try
        {
            using var document = JsonDocument.Parse(comment);
            var proof = document.RootElement;
            RequireDataKeys(proof, "schema", "bugId", "auditId", "alternateArtId", "artCode", "before", "after", "localEvidence");
            if (!proof.GetProperty("schema").TryGetInt32(out var schema) || schema != 1)
                throw DataCorrectionError("不支持的数据修正证据版本。");
            if (DataText(proof, "bugId", 32) != row.Id)
                throw DataCorrectionError("原数据修正证据未绑定本条反馈。");
            var auditId = DataText(proof, "auditId", 32);
            var artId = DataText(proof, "alternateArtId", 32);
            if (!Guid.TryParseExact(auditId, "N", out _) || !Guid.TryParseExact(artId, "N", out _))
                throw DataCorrectionError("需要确切的原审计及异画标识。");
            var artCode = DataText(proof, "artCode", 80);
            var before = proof.GetProperty("before");
            var after = proof.GetProperty("after");
            RequireDataKeys(before, "baseCardId", "displayName");
            RequireDataKeys(after, "baseCardId", "displayName");
            var beforeBase = DataText(before, "baseCardId");
            var beforeName = DataText(before, "displayName");
            var afterBase = DataText(after, "baseCardId");
            var afterName = DataText(after, "displayName");
            if (beforeBase == afterBase || beforeName == afterName)
                throw DataCorrectionError("本分支只接受已实际改变基础卡及对应名称的同ID异画纠正。");
            var local = proof.GetProperty("localEvidence");
            RequireDataKeys(local, "operationReceiptSha256", "readbackReceiptSha256", "auditResponseSha256");
            foreach (var name in new[] { "operationReceiptSha256", "readbackReceiptSha256", "auditResponseSha256" })
            {
                var hash = DataText(local, name, 64);
                if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
                    throw DataCorrectionError("本地证据摘要格式无效。");
            }
            // These three hashes are operator evidence references, NOT server verification of local files.
            // Reuse the existing checksum-validating ledger reader; never trust an audit body in the request.
            var audit = AdminAudit("alternate-art", 1000, "succeeded")
                .SingleOrDefault(candidate => candidate.Id == auditId)
                ?? throw DataCorrectionError("在现有有界审计窗口内未找到原操作；保持待验，不扩大扫描。");
            if (audit.Target != artId || audit.Category != "alternate-art" || audit.Action != "update"
                || audit.Outcome != "succeeded" || audit.DryRun || string.IsNullOrWhiteSpace(audit.ActorId)
                || audit.Permission != L12Authorization.Key(L12Permission.AdminContentDraft)
                || audit.RequestMethod != "PUT" || audit.RequestPath != "/api/admin/alternate-arts"
                || string.IsNullOrWhiteSpace(audit.FromValue) || string.IsNullOrWhiteSpace(audit.ToValue))
                throw DataCorrectionError("原审计不是该异画的真实已授权更新。");
            using var fromDocument = JsonDocument.Parse(audit.FromValue);
            using var toDocument = JsonDocument.Parse(audit.ToValue);
            var from = fromDocument.RootElement;
            var to = toDocument.RootElement;
            if (DataText(from, "Id", 32) != artId || DataText(to, "Id", 32) != artId
                || DataText(from, "ArtCode", 80) != artCode || DataText(to, "ArtCode", 80) != artCode
                || DataText(from, "BaseCardId") != beforeBase || DataText(from, "DisplayName") != beforeName
                || DataText(to, "BaseCardId") != afterBase || DataText(to, "DisplayName") != afterName
                || DataText(from, "BaseCardName") != beforeName || DataText(to, "BaseCardName") != afterName)
                throw DataCorrectionError("证据前后值没有精确绑定原审计。");
            var oldTime = from.GetProperty("UpdatedAt").GetDateTimeOffset();
            var newTime = to.GetProperty("UpdatedAt").GetDateTimeOffset();
            if (newTime <= oldTime || newTime > audit.CreatedAt)
                throw DataCorrectionError("原修改时间与审计不一致。");
            var fromNode = NormalizeDataCorrectionDates(JsonNode.Parse(audit.FromValue)!).AsObject();
            var toNode = NormalizeDataCorrectionDates(JsonNode.Parse(audit.ToValue)!).AsObject();
            if (!fromNode.Select(property => property.Key).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(toNode.Select(property => property.Key)))
                throw DataCorrectionError("原审计前后字段集合发生改变。");
            foreach (var name in new[] { "BaseCardId", "DisplayName", "BaseCardName", "UpdatedAt" })
            {
                fromNode.Remove(name);
                toNode.Remove(name);
            }
            if (!JsonNode.DeepEquals(fromNode, toNode))
                throw DataCorrectionError("原操作同时修改了批准范围外的异画字段。");
            var currentRow = _data.AlternateArts.SingleOrDefault(candidate => candidate.Id == artId)
                ?? throw DataCorrectionError("当前本地权威异画不存在；不能用共享缓存替代。");
            var current = ToAlternateArtView(currentRow, true);
            if (current.Kind != "alternate-art" || current.BuiltIn
                || !_officialCards.TryGetValue(afterBase, out var baseCard)
                || baseCard.NameZh != afterName || current.BaseCardName != afterName
                || !JsonNode.DeepEquals(NormalizeDataCorrectionDates(JsonSerializer.SerializeToNode(current)!),
                    NormalizeDataCorrectionDates(JsonNode.Parse(audit.ToValue)!)))
                throw DataCorrectionError("当前权威条目或基础卡身份已变化；保持待验。");
        }
        catch (L12BugClosureValidationException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or ArgumentException)
        {
            throw DataCorrectionError("数据修正证据无法按严格合同读取。");
        }
    }
}
