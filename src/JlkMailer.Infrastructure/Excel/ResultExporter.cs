using ClosedXML.Excel;
using JlkMailer.Core.Models;

namespace JlkMailer.Infrastructure.Excel;

/// <summary>
/// 설계 §11: 원본 엑셀 구조 + 발송상태 / 발송시각 / SMTP응답 3개 컬럼.
/// 영업 담당자가 후속 팔로업을 엑셀에서 이어가기 때문에 원본 컬럼 순서를 유지한다.
/// </summary>
public static class ResultExporter
{
    public static void Export(string path, IEnumerable<Recipient> recipients, IEnumerable<SendLogEntry> log)
    {
        var byEmail = log
            .GroupBy(e => e.EmailNorm, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Id).First(), StringComparer.OrdinalIgnoreCase);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("발송결과");

        string[] headers =
        [
            "NO", "병원 및 업체명", "성함", "진료과", "연락처", "이메일",
            "세그먼트", "검토상태", "발송상태", "발송시각", "SMTP응답",
        ];

        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAEFF5");
        }

        var row = 2;
        foreach (var r in recipients)
        {
            // 발송 대상인 행에만 로그를 붙인다.
            // 중복 행은 승자와 주소가 같아서 그냥 매칭하면 남의 발송 결과를 자기 것처럼 표시한다.
            // (348통을 큐에 넣었는데 360행이 '대기'로 보이는 식)
            SendLogEntry? entry = null;
            if (r.IsSendable) byEmail.TryGetValue(r.EffectiveEmail, out entry);

            ws.Cell(row, 1).Value = r.RowNo - 1;
            ws.Cell(row, 2).Value = r.Hospital;
            ws.Cell(row, 3).Value = r.Name;
            ws.Cell(row, 4).Value = r.DeptRaw;
            ws.Cell(row, 5).Value = r.Phone;
            ws.Cell(row, 6).Value = r.EffectiveEmail;
            ws.Cell(row, 7).Value = r.Segment;
            ws.Cell(row, 8).Value = Describe(r.Status);
            ws.Cell(row, 9).Value = entry is null ? "미발송" : Describe(entry.State);
            ws.Cell(row, 10).Value = entry?.SentAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "";
            ws.Cell(row, 11).Value = entry?.SmtpMessage ?? "";

            if (entry?.State is SendState.Failed or SendState.Bounced)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F7DFDC");

            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(1, 200, 8, 42);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        wb.SaveAs(path);
    }

    private static string Describe(RecipientStatus s) => s switch
    {
        RecipientStatus.Ready => "정상",
        RecipientStatus.NoEmail => "이메일 없음",
        RecipientStatus.Invalid => "형식 오류",
        RecipientStatus.NeedsFix => "교정 필요",
        RecipientStatus.Duplicate => "중복",
        RecipientStatus.NeedsReview => "확인 필요",
        RecipientStatus.Excluded => "제외",
        RecipientStatus.Suppressed => "수신거부",
        _ => s.ToString(),
    };

    private static string Describe(SendState s) => s switch
    {
        SendState.Queued => "대기",
        SendState.Sending => "발송중",
        SendState.Sent => "발송완료",
        SendState.Retrying => "재시도 대기",
        SendState.Failed => "실패",
        SendState.Skipped => "건너뜀",
        SendState.Bounced => "반송",
        _ => s.ToString(),
    };
}
