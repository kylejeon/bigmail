using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Models;
using Microsoft.Data.Sqlite;

namespace JlkMailer.Infrastructure.Storage;

/// <summary>
/// 설계 §06 데이터 모델의 SQLite 구현.
/// 발송 로그가 이 앱의 심장이다 — 앱을 껐다 켜도, 네트워크가 끊겨도 어디까지 보냈는지 알아야 한다.
/// 재발송 방지는 UNIQUE(campaign_id, email_norm) 하나로 보장한다.
/// </summary>
public sealed class SqliteCampaignStore(string databasePath) : ICampaignStore, IDisposable
{
    private readonly SqliteConnection _connection = OpenConnection(databasePath);

    private static SqliteConnection OpenConnection(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            // 풀링을 끈다. 이 앱은 커넥션 하나를 프로세스 수명 내내 들고 있으므로 풀링의 이득이 없고,
            // 손해만 있다: Close() 가 커넥션을 풀에 반납할 뿐 파일 핸들을 놓지 않아
            // Windows 에서 campaign.db 가 잠긴 채 남는다(삭제·이동·경로 변경이 실패).
            // Linux/macOS 는 열린 파일도 unlink 되므로 이 증상이 드러나지 않는다.
            Pooling = false,
        }.ToString());

        connection.Open();
        Exec(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;");
        return connection;
    }

    public void Initialize() => Exec(_connection, Schema);

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS recipients (
          id             INTEGER PRIMARY KEY,
          row_no         INTEGER,
          hospital       TEXT NOT NULL,
          name           TEXT NOT NULL,
          dept_raw       TEXT,
          dept_label     TEXT,
          segment        TEXT,
          honorific      TEXT,
          phone          TEXT,
          email_raw      TEXT,
          email_norm     TEXT,
          suggested_email TEXT,
          fix_accepted   INTEGER DEFAULT 0,
          status         TEXT NOT NULL,
          issue          TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_recip_seg ON recipients(segment, status);
        CREATE INDEX IF NOT EXISTS ix_recip_email ON recipients(email_norm);

        CREATE TABLE IF NOT EXISTS segment_rules (
          priority   INTEGER PRIMARY KEY,
          segment    TEXT NOT NULL,
          pattern    TEXT NOT NULL,
          enabled    INTEGER DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS templates (
          segment      TEXT PRIMARY KEY,
          subject      TEXT NOT NULL,
          greeting     TEXT,
          intro        TEXT,
          benefit_lead TEXT,
          closing      TEXT
        );

        CREATE TABLE IF NOT EXISTS campaigns (
          id                  INTEGER PRIMARY KEY,
          name                TEXT,
          html_path           TEXT,
          html_hash           TEXT,
          from_name           TEXT,
          from_addr           TEXT,
          reply_to            TEXT,
          sender_display_name TEXT,
          ad_prefix           INTEGER DEFAULT 0,
          include_unsub       INTEGER DEFAULT 1,
          unsub_target        TEXT,
          daily_cap           INTEGER DEFAULT 300,
          created_at          TEXT
        );

        CREATE TABLE IF NOT EXISTS send_log (
          id              INTEGER PRIMARY KEY,
          campaign_id     INTEGER NOT NULL,
          recipient_id    INTEGER NOT NULL,
          email_norm      TEXT NOT NULL,
          state           TEXT NOT NULL,
          attempt         INTEGER DEFAULT 0,
          smtp_code       TEXT,
          smtp_message    TEXT,
          message_id      TEXT,
          sent_at         TEXT,
          next_attempt_at TEXT,
          UNIQUE(campaign_id, email_norm)
        );
        CREATE INDEX IF NOT EXISTS ix_log_state ON send_log(campaign_id, state, next_attempt_at);

        CREATE TABLE IF NOT EXISTS suppressions (
          email_norm TEXT PRIMARY KEY,
          reason     TEXT,
          added_at   TEXT
        );
        """;

    // ---------- campaigns ----------

    public long UpsertCampaign(Campaign c)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = c.Id > 0
            ? """
              UPDATE campaigns SET name=$name, html_path=$path, html_hash=$hash, from_name=$fromName,
                     from_addr=$fromAddr, reply_to=$replyTo, sender_display_name=$sender, ad_prefix=$ad,
                     include_unsub=$unsub, unsub_target=$target, daily_cap=$cap WHERE id=$id;
              SELECT $id;
              """
            : """
              INSERT INTO campaigns (name, html_path, html_hash, from_name, from_addr, reply_to,
                                     sender_display_name, ad_prefix, include_unsub, unsub_target, daily_cap, created_at)
              VALUES ($name, $path, $hash, $fromName, $fromAddr, $replyTo, $sender, $ad, $unsub, $target, $cap, $created);
              SELECT last_insert_rowid();
              """;

        cmd.Parameters.AddWithValue("$id", c.Id);
        cmd.Parameters.AddWithValue("$name", c.Name);
        cmd.Parameters.AddWithValue("$path", c.HtmlPath);
        cmd.Parameters.AddWithValue("$hash", c.HtmlHash);
        cmd.Parameters.AddWithValue("$fromName", c.FromName);
        cmd.Parameters.AddWithValue("$fromAddr", c.FromAddress);
        cmd.Parameters.AddWithValue("$replyTo", c.ReplyTo);
        cmd.Parameters.AddWithValue("$sender", c.SenderDisplayName);
        cmd.Parameters.AddWithValue("$ad", c.AdPrefix ? 1 : 0);
        cmd.Parameters.AddWithValue("$unsub", c.IncludeUnsubscribe ? 1 : 0);
        cmd.Parameters.AddWithValue("$target", c.UnsubscribeTarget);
        cmd.Parameters.AddWithValue("$cap", c.DailyCap);
        cmd.Parameters.AddWithValue("$created", c.CreatedAt.ToString("O"));

        var id = Convert.ToInt64(cmd.ExecuteScalar());
        c.Id = id;
        return id;
    }

    public Campaign? GetCampaign(long id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM campaigns WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return new Campaign
        {
            Id = r.GetInt64(r.GetOrdinal("id")),
            Name = Str(r, "name"),
            HtmlPath = Str(r, "html_path"),
            HtmlHash = Str(r, "html_hash"),
            FromName = Str(r, "from_name"),
            FromAddress = Str(r, "from_addr"),
            ReplyTo = Str(r, "reply_to"),
            SenderDisplayName = Str(r, "sender_display_name"),
            AdPrefix = r.GetInt32(r.GetOrdinal("ad_prefix")) != 0,
            IncludeUnsubscribe = r.GetInt32(r.GetOrdinal("include_unsub")) != 0,
            UnsubscribeTarget = Str(r, "unsub_target"),
            DailyCap = r.GetInt32(r.GetOrdinal("daily_cap")),
            CreatedAt = DateTimeOffset.TryParse(Str(r, "created_at"), out var t) ? t : DateTimeOffset.Now,
        };
    }

    // ---------- recipients ----------

    public void ReplaceRecipients(IEnumerable<Recipient> recipients)
    {
        using var tx = _connection.BeginTransaction();
        Exec(_connection, "DELETE FROM recipients;", tx);

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO recipients (row_no, hospital, name, dept_raw, dept_label, segment, honorific, phone,
                                    email_raw, email_norm, suggested_email, fix_accepted, status, issue)
            VALUES ($row, $hosp, $name, $deptRaw, $deptLabel, $seg, $hon, $phone,
                    $emailRaw, $emailNorm, $suggested, $fix, $status, $issue);
            """;

        var p = cmd.Parameters;
        foreach (var name in new[] { "$row", "$hosp", "$name", "$deptRaw", "$deptLabel", "$seg", "$hon", "$phone",
                                     "$emailRaw", "$emailNorm", "$suggested", "$fix", "$status", "$issue" })
            p.Add(name, SqliteType.Text);

        foreach (var r in recipients)
        {
            p["$row"].Value = r.RowNo;
            p["$hosp"].Value = r.Hospital;
            p["$name"].Value = r.Name;
            p["$deptRaw"].Value = r.DeptRaw;
            p["$deptLabel"].Value = r.DeptLabel;
            p["$seg"].Value = r.Segment;
            p["$hon"].Value = r.Honorific;
            p["$phone"].Value = r.Phone;
            p["$emailRaw"].Value = r.EmailRaw;
            p["$emailNorm"].Value = r.EmailNorm;
            p["$suggested"].Value = (object?)r.SuggestedEmail ?? DBNull.Value;
            p["$fix"].Value = r.FixAccepted ? 1 : 0;
            p["$status"].Value = r.Status.ToString();
            p["$issue"].Value = r.Issue;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public IReadOnlyList<Recipient> GetRecipients(string? segment = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = segment is null
            ? "SELECT * FROM recipients ORDER BY row_no"
            : "SELECT * FROM recipients WHERE segment=$seg ORDER BY row_no";
        if (segment is not null) cmd.Parameters.AddWithValue("$seg", segment);

        using var r = cmd.ExecuteReader();
        var list = new List<Recipient>();
        while (r.Read())
        {
            list.Add(new Recipient
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                RowNo = r.GetInt32(r.GetOrdinal("row_no")),
                Hospital = Str(r, "hospital"),
                Name = Str(r, "name"),
                DeptRaw = Str(r, "dept_raw"),
                DeptLabel = Str(r, "dept_label"),
                Segment = Str(r, "segment"),
                Honorific = Str(r, "honorific"),
                Phone = Str(r, "phone"),
                EmailRaw = Str(r, "email_raw"),
                EmailNorm = Str(r, "email_norm"),
                SuggestedEmail = r.IsDBNull(r.GetOrdinal("suggested_email")) ? null : Str(r, "suggested_email"),
                FixAccepted = r.GetInt32(r.GetOrdinal("fix_accepted")) != 0,
                Status = Enum.TryParse<RecipientStatus>(Str(r, "status"), out var s) ? s : RecipientStatus.NeedsReview,
                Issue = Str(r, "issue"),
            });
        }
        return list;
    }

    public void UpdateRecipient(Recipient r)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE recipients SET segment=$seg, dept_label=$label, honorific=$hon, suggested_email=$suggested,
                   fix_accepted=$fix, status=$status, issue=$issue WHERE id=$id;
            """;
        cmd.Parameters.AddWithValue("$seg", r.Segment);
        cmd.Parameters.AddWithValue("$label", r.DeptLabel);
        cmd.Parameters.AddWithValue("$hon", r.Honorific);
        cmd.Parameters.AddWithValue("$suggested", (object?)r.SuggestedEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fix", r.FixAccepted ? 1 : 0);
        cmd.Parameters.AddWithValue("$status", r.Status.ToString());
        cmd.Parameters.AddWithValue("$issue", r.Issue);
        cmd.Parameters.AddWithValue("$id", r.Id);
        cmd.ExecuteNonQuery();
    }

    // ---------- rules / templates ----------

    public void SaveRules(IEnumerable<SegmentRule> rules)
    {
        using var tx = _connection.BeginTransaction();
        Exec(_connection, "DELETE FROM segment_rules;", tx);
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO segment_rules (priority, segment, pattern, enabled) VALUES ($p,$s,$x,$e)";
        foreach (var rule in rules)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$p", rule.Priority);
            cmd.Parameters.AddWithValue("$s", rule.Segment);
            cmd.Parameters.AddWithValue("$x", rule.Pattern);
            cmd.Parameters.AddWithValue("$e", rule.Enabled ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<SegmentRule> GetRules()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT priority, segment, pattern, enabled FROM segment_rules ORDER BY priority";
        using var r = cmd.ExecuteReader();
        var list = new List<SegmentRule>();
        while (r.Read())
            list.Add(new SegmentRule(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3) != 0));
        return list;
    }

    public void SaveTemplate(MailTemplate t)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO templates (segment, subject, greeting, intro, benefit_lead, closing)
            VALUES ($seg,$sub,$greet,$intro,$lead,$close)
            ON CONFLICT(segment) DO UPDATE SET subject=$sub, greeting=$greet, intro=$intro,
                                               benefit_lead=$lead, closing=$close;
            """;
        cmd.Parameters.AddWithValue("$seg", t.Segment);
        cmd.Parameters.AddWithValue("$sub", t.Subject);
        cmd.Parameters.AddWithValue("$greet", t.Greeting);
        cmd.Parameters.AddWithValue("$intro", t.Intro);
        cmd.Parameters.AddWithValue("$lead", t.BenefitLead);
        cmd.Parameters.AddWithValue("$close", t.Closing);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<MailTemplate> GetTemplates()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT segment, subject, greeting, intro, benefit_lead, closing FROM templates ORDER BY segment";
        using var r = cmd.ExecuteReader();
        var list = new List<MailTemplate>();
        while (r.Read())
            list.Add(new MailTemplate
            {
                Segment = r.GetString(0),
                Subject = r.GetString(1),
                Greeting = r.IsDBNull(2) ? "" : r.GetString(2),
                Intro = r.IsDBNull(3) ? "" : r.GetString(3),
                BenefitLead = r.IsDBNull(4) ? "" : r.GetString(4),
                Closing = r.IsDBNull(5) ? "" : r.GetString(5),
            });
        return list;
    }

    // ---------- send log ----------

    /// <summary>
    /// INSERT OR IGNORE + UNIQUE(campaign_id, email_norm).
    /// 이미 발송했거나 큐에 있는 주소는 조용히 무시된다 — 이것이 중복발송 방지의 전부다.
    /// </summary>
    public int EnqueueMissing(long campaignId, IEnumerable<Recipient> recipients)
    {
        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO send_log (campaign_id, recipient_id, email_norm, state, attempt)
            VALUES ($c, $r, $e, 'Queued', 0);
            """;
        cmd.Parameters.Add("$c", SqliteType.Integer);
        cmd.Parameters.Add("$r", SqliteType.Integer);
        cmd.Parameters.Add("$e", SqliteType.Text);

        var inserted = 0;
        foreach (var recipient in recipients)
        {
            if (!recipient.IsSendable) continue;
            cmd.Parameters["$c"].Value = campaignId;
            cmd.Parameters["$r"].Value = recipient.Id;
            cmd.Parameters["$e"].Value = recipient.EffectiveEmail;
            inserted += cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return inserted;
    }

    public IReadOnlyList<SendLogEntry> TakeQueued(long campaignId, int max, DateTimeOffset now)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM send_log
            WHERE campaign_id=$c
              AND (state='Queued' OR (state='Retrying' AND (next_attempt_at IS NULL OR next_attempt_at <= $now)))
            ORDER BY id LIMIT $max;
            """;
        cmd.Parameters.AddWithValue("$c", campaignId);
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        cmd.Parameters.AddWithValue("$max", max);

        using var r = cmd.ExecuteReader();
        var list = new List<SendLogEntry>();
        while (r.Read())
            list.Add(new SendLogEntry
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                CampaignId = r.GetInt64(r.GetOrdinal("campaign_id")),
                RecipientId = r.GetInt64(r.GetOrdinal("recipient_id")),
                EmailNorm = Str(r, "email_norm"),
                State = Enum.TryParse<SendState>(Str(r, "state"), out var s) ? s : SendState.Queued,
                Attempt = r.GetInt32(r.GetOrdinal("attempt")),
                SmtpCode = r.IsDBNull(r.GetOrdinal("smtp_code")) ? null : Str(r, "smtp_code"),
                SmtpMessage = r.IsDBNull(r.GetOrdinal("smtp_message")) ? null : Str(r, "smtp_message"),
                MessageId = r.IsDBNull(r.GetOrdinal("message_id")) ? null : Str(r, "message_id"),
                SentAt = ReadTime(r, "sent_at"),
                NextAttemptAt = ReadTime(r, "next_attempt_at"),
            });
        return list;
    }

    public void UpdateLog(SendLogEntry e)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE send_log SET state=$state, attempt=$attempt, smtp_code=$code, smtp_message=$msg,
                   message_id=$mid, sent_at=$sent, next_attempt_at=$next WHERE id=$id;
            """;
        cmd.Parameters.AddWithValue("$state", e.State.ToString());
        cmd.Parameters.AddWithValue("$attempt", e.Attempt);
        cmd.Parameters.AddWithValue("$code", (object?)e.SmtpCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$msg", (object?)e.SmtpMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mid", (object?)e.MessageId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sent", (object?)e.SentAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$next", (object?)e.NextAttemptAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<SendLogEntry> GetLog(long campaignId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM send_log WHERE campaign_id=$c ORDER BY id";
        cmd.Parameters.AddWithValue("$c", campaignId);
        using var r = cmd.ExecuteReader();
        var list = new List<SendLogEntry>();
        while (r.Read())
            list.Add(new SendLogEntry
            {
                Id = r.GetInt64(r.GetOrdinal("id")),
                CampaignId = campaignId,
                RecipientId = r.GetInt64(r.GetOrdinal("recipient_id")),
                EmailNorm = Str(r, "email_norm"),
                State = Enum.TryParse<SendState>(Str(r, "state"), out var s) ? s : SendState.Queued,
                Attempt = r.GetInt32(r.GetOrdinal("attempt")),
                SmtpCode = r.IsDBNull(r.GetOrdinal("smtp_code")) ? null : Str(r, "smtp_code"),
                SmtpMessage = r.IsDBNull(r.GetOrdinal("smtp_message")) ? null : Str(r, "smtp_message"),
                MessageId = r.IsDBNull(r.GetOrdinal("message_id")) ? null : Str(r, "message_id"),
                SentAt = ReadTime(r, "sent_at"),
                NextAttemptAt = ReadTime(r, "next_attempt_at"),
            });
        return list;
    }

    /// <summary>일 상한 계산용. 로컬 날짜 기준으로 그날 실제로 나간 통수.</summary>
    public int CountSentOn(long campaignId, DateOnly localDate)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT sent_at FROM send_log WHERE campaign_id=$c AND state='Sent' AND sent_at IS NOT NULL";
        cmd.Parameters.AddWithValue("$c", campaignId);
        using var r = cmd.ExecuteReader();

        var count = 0;
        while (r.Read())
            if (DateTimeOffset.TryParse(r.GetString(0), out var t) &&
                DateOnly.FromDateTime(t.ToLocalTime().DateTime) == localDate)
                count++;
        return count;
    }

    public (int Sent, int Failed, int Queued) Counts(long campaignId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT state, COUNT(*) FROM send_log WHERE campaign_id=$c GROUP BY state";
        cmd.Parameters.AddWithValue("$c", campaignId);
        using var r = cmd.ExecuteReader();

        int sent = 0, failed = 0, queued = 0;
        while (r.Read())
        {
            var n = r.GetInt32(1);
            switch (r.GetString(0))
            {
                case nameof(SendState.Sent): sent += n; break;
                case nameof(SendState.Failed):
                case nameof(SendState.Bounced): failed += n; break;
                case nameof(SendState.Queued):
                case nameof(SendState.Retrying):
                case nameof(SendState.Sending): queued += n; break;
            }
        }
        return (sent, failed, queued);
    }

    /// <summary>
    /// 설계 §10 재개. 앱 시작 시 'Sending' 상태를 'Queued' 로 되돌린다.
    /// 실제로 나갔는지 불확실해도 UNIQUE 제약이 중복 발송을 막고, 최종 확인은 보낸편지함 대조로 한다.
    /// </summary>
    public int ResetStuckSending(long campaignId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE send_log SET state='Queued' WHERE campaign_id=$c AND state='Sending'";
        cmd.Parameters.AddWithValue("$c", campaignId);
        return cmd.ExecuteNonQuery();
    }

    // ---------- suppressions ----------

    public void AddSuppression(Suppression s)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO suppressions (email_norm, reason, added_at) VALUES ($e,$r,$a)
            ON CONFLICT(email_norm) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("$e", s.EmailNorm);
        cmd.Parameters.AddWithValue("$r", s.Reason);
        cmd.Parameters.AddWithValue("$a", s.AddedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlySet<string> GetSuppressions()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT email_norm FROM suppressions";
        using var r = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    // ---------- helpers ----------

    private static string Str(SqliteDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? "" : r.GetString(i);
    }

    private static DateTimeOffset? ReadTime(SqliteDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        if (r.IsDBNull(i)) return null;
        return DateTimeOffset.TryParse(r.GetString(i), out var t) ? t : null;
    }

    private static void Exec(SqliteConnection connection, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Close();

        // 풀링을 껐어도, 과거에 만들어진 풀이 남아 있을 수 있으므로 한 번 더 비운다.
        // 이것까지 해야 WAL 파일(-wal, -shm)이 정리되고 파일 잠금이 완전히 풀린다.
        SqliteConnection.ClearPool(_connection);

        _connection.Dispose();
    }
}
