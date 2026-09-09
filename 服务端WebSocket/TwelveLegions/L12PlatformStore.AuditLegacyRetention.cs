using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TwelveLegions.Server;

public sealed partial class L12PlatformStore
{
    // Existing manual JSONL segments are not re-encoded. They expire under the same
    // event-time upper bound only after streaming verification and current hold checks.
    private int ExpireLegacyAuditSegments(SqliteConnection connection, DateTimeOffset now,
        Func<string,bool> protects, CancellationToken token)
    {
        using var query = connection.CreateCommand();
        query.CommandText = """
            SELECT rowid,id,from_utc,until_utc,event_count,file_name,file_sha256,created_utc
            FROM audit_archive_segments WHERE rowid > CAST($cursor AS INTEGER) ORDER BY rowid LIMIT 5;
            """;
        query.Parameters.AddWithValue("$cursor",ReadMeta(connection,"audit_legacy_segment_cursor") ?? "0");
        var rows = new List<(long Cursor,AuditArchiveSegmentRow Segment)>();
        using(var reader=query.ExecuteReader())
            while(reader.Read()) rows.Add((reader.GetInt64(0),new(reader.GetString(1),DateTimeOffset.Parse(reader.GetString(2)),
                DateTimeOffset.Parse(reader.GetString(3)),reader.GetInt64(4),reader.GetString(5),reader.GetString(6),DateTimeOffset.Parse(reader.GetString(7)))));
        var expired=0;
        foreach(var entry in rows)
        {
            token.ThrowIfCancellationRequested();
            var row=entry.Segment;
            var path=LegacyAuditPath(row.FileName);
            using var intentCheck=connection.CreateCommand();
            intentCheck.CommandText="SELECT 1 FROM audit_legacy_expiration_intents WHERE segment_id=$id;";
            intentCheck.Parameters.AddWithValue("$id",row.Id);
            var intended=intentCheck.ExecuteScalar() is not null;
            if(File.Exists(path))
            {
                try { ValidateLegacyAuditFile(row,token,payload => protects(payload)
                    || JsonSerializer.Deserialize<AdminAuditRow>(payload,PlatformSnapshotJsonOptions)!.CreatedAt>=now.AddDays(-180)); }
                catch(AuditProtectedException) { Advance(entry.Cursor); continue; }
            }
            else if(!intended) throw new FileNotFoundException("旧审计归档缺失，停止淘汰",path);
            using(var intent=connection.CreateCommand())
            {
                intent.CommandText="INSERT OR IGNORE INTO audit_legacy_expiration_intents(segment_id) VALUES($id);";
                intent.Parameters.AddWithValue("$id",row.Id); intent.ExecuteNonQuery();
            }
            AuditLifecycleFailureInjector?.Invoke("before-legacy-delete");
            File.Delete(path);
            AuditLifecycleFailureInjector?.Invoke("after-legacy-delete");
            using(var transaction=connection.BeginTransaction())
            {
                using var remove=connection.CreateCommand(); remove.Transaction=transaction;
                remove.CommandText="DELETE FROM audit_archive_segments WHERE id=$id; DELETE FROM audit_legacy_expiration_intents WHERE segment_id=$id;";
                remove.Parameters.AddWithValue("$id",row.Id); remove.ExecuteNonQuery(); transaction.Commit();
            }
            expired++; Advance(entry.Cursor);
        }
        if(rows.Count<5)Advance(0);
        return expired;

        void Advance(long cursor)
        {
            using var progress=connection.CreateCommand();
            progress.CommandText="""
                INSERT INTO storage_meta(key,value) VALUES('audit_legacy_segment_cursor',$cursor)
                ON CONFLICT(key) DO UPDATE SET value=excluded.value;
                """;
            progress.Parameters.AddWithValue("$cursor",cursor.ToString(System.Globalization.CultureInfo.InvariantCulture));
            progress.ExecuteNonQuery();
        }
    }

    private string LegacyAuditPath(string file)
    {
        if(Path.GetFileName(file)!=file || !file.StartsWith("audit-",StringComparison.Ordinal)
            || !file.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("非法旧归档文件名");
        var directory=AuditArchiveDirectory();
        if(Directory.Exists(directory) && (File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("旧归档目录不能是重定向链接");
        var path=Path.Combine(directory,file);
        if(File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
            throw new InvalidDataException("旧归档文件不能是重定向链接");
        return path;
    }

    private void ValidateLegacyAuditFile(AuditArchiveSegmentRow segment,CancellationToken token,Func<string,bool>? protects=null)
    {
        var path=LegacyAuditPath(segment.FileName);
        if(!FixedEquals(segment.FileSha256,HashAuditFile(path,token))) throw new InvalidDataException("旧归档校验和不匹配");
        using var reader=new StreamReader(path,Encoding.UTF8);
        long count=0;
        while(reader.ReadLine() is {} line)
        {
            token.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(line))continue;
            var item=JsonSerializer.Deserialize<AuditArchiveLine>(line,PlatformSnapshotJsonOptions)
                ?? throw new InvalidDataException("旧归档记录无效");
            if(!FixedEquals(item.PayloadSha256,Sha256(item.PayloadJson)) || item.CreatedAt<=segment.From || item.CreatedAt>segment.Until)
                throw new InvalidDataException("旧归档事件或时间范围校验失败");
            var body=JsonSerializer.Deserialize<AdminAuditRow>(item.PayloadJson,PlatformSnapshotJsonOptions)
                ?? throw new InvalidDataException("旧归档正文无效");
            if(body.Id!=item.Id || body.CreatedAt!=item.CreatedAt)throw new InvalidDataException("旧归档身份或时间不一致");
            if(protects?.Invoke(item.PayloadJson)==true)throw new AuditProtectedException();
            if(++count>segment.EventCount)throw new InvalidDataException("旧归档事件数量不匹配");
        }
        if(count!=segment.EventCount)throw new InvalidDataException("旧归档事件数量不匹配");
    }
}
