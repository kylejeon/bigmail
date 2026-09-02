using ClosedXML.Excel;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Infrastructure.Excel;
using Xunit;

namespace JlkMailer.Tests;

public sealed class ResultExporterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"jlk-export-{Guid.NewGuid():N}.xlsx");

    private static Recipient Make(int row, string email, RecipientStatus status) => new()
    {
        RowNo = row,
        Hospital = "테스트병원",
        Name = $"수신자{row}",
        DeptRaw = "신경과",
        Segment = SegmentCatalog.S2,
        EmailRaw = email,
        EmailNorm = email,
        Status = status,
    };

    /// <summary>
    /// 중복 행은 승자와 주소가 같다. 로그를 주소만으로 붙이면 남의 발송 결과를 자기 것처럼 표시하게 된다.
    /// 영업 담당자가 이 엑셀로 발송 건수를 세므로 그대로 두면 안 된다.
    /// </summary>
    [Fact]
    public void 중복_행은_승자의_발송상태를_가져오지_않는다()
    {
        var winner = Make(2, "a@b.com", RecipientStatus.Ready);
        var loser = Make(3, "a@b.com", RecipientStatus.Duplicate);

        var log = new List<SendLogEntry>
        {
            new() { Id = 1, EmailNorm = "a@b.com", State = SendState.Sent, SentAt = DateTimeOffset.Now, SmtpMessage = "250 OK" },
        };

        ResultExporter.Export(_path, [winner, loser], log);

        using var wb = new XLWorkbook(_path);
        var ws = wb.Worksheet(1);

        Assert.Equal("발송상태", ws.Cell(1, 9).GetString());
        Assert.Equal("발송완료", ws.Cell(2, 9).GetString());   // 승자
        Assert.Equal("미발송", ws.Cell(3, 9).GetString());     // 중복 행
        Assert.Equal("중복", ws.Cell(3, 8).GetString());       // 사유는 검토상태에 남는다
        Assert.Equal("", ws.Cell(3, 11).GetString());          // 남의 SMTP 응답을 가져오지 않는다
    }

    [Fact]
    public void 원본_컬럼_순서를_유지하고_결과_컬럼_3개를_덧붙인다()
    {
        ResultExporter.Export(_path, [Make(2, "a@b.com", RecipientStatus.Ready)], []);

        using var wb = new XLWorkbook(_path);
        var ws = wb.Worksheet(1);

        string[] expected = ["NO", "병원 및 업체명", "성함", "진료과", "연락처", "이메일",
                             "세그먼트", "검토상태", "발송상태", "발송시각", "SMTP응답"];

        for (var i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], ws.Cell(1, i + 1).GetString());

        Assert.Equal("미발송", ws.Cell(2, 9).GetString());
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
