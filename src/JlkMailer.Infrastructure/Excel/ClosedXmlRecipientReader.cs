using ClosedXML.Excel;
using JlkMailer.Core.Abstractions;

namespace JlkMailer.Infrastructure.Excel;

/// <summary>
/// ClosedXML 기반 엑셀 리더. 대상 PC 에 Excel 이 설치되어 있지 않아도 동작한다. 설계 §04.
/// </summary>
public sealed class ClosedXmlRecipientReader : IRecipientReader
{
    private static readonly (string Field, string[] Keywords)[] HeaderHints =
    [
        ("Hospital", ["병원", "업체", "기관", "거래처"]),
        ("Name",     ["성함", "이름", "담당자", "성명"]),
        ("Dept",     ["진료과", "부서", "과", "소속"]),
        ("Phone",    ["연락처", "전화", "휴대", "핸드폰", "tel"]),
        ("Email",    ["이메일", "메일", "email", "e-mail"]),
    ];

    public IReadOnlyList<string> ListSheets(string path)
    {
        using var wb = new XLWorkbook(path);
        return wb.Worksheets.Select(w => w.Name).ToList();
    }

    /// <summary>
    /// 헤더 행의 텍스트로 컬럼 위치를 추측한다. 설계 §11 화면1 — 자동 인식 후 사용자가 고칠 수 있다.
    /// 추측에 실패한 필드는 기본 매핑(B/C/D/E/F)을 유지한다.
    /// </summary>
    public ColumnMap GuessColumns(string path, string sheet, int headerRow)
    {
        using var wb = new XLWorkbook(path);
        var ws = GetSheet(wb, sheet);
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        var lastCol = ws.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var col = 1; col <= lastCol; col++)
        {
            var text = ws.Cell(headerRow, col).GetString().Trim().ToLowerInvariant();
            if (text.Length == 0) continue;

            foreach (var (field, keywords) in HeaderHints)
            {
                if (found.ContainsKey(field)) continue;
                if (keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    found[field] = XLHelper.GetColumnLetterFromNumber(col);
            }
        }

        var d = ColumnMap.Default;
        return new ColumnMap(
            found.GetValueOrDefault("Hospital", d.Hospital),
            found.GetValueOrDefault("Name", d.Name),
            found.GetValueOrDefault("Dept", d.Dept),
            found.GetValueOrDefault("Phone", d.Phone),
            found.GetValueOrDefault("Email", d.Email));
    }

    public IReadOnlyList<RawRow> Read(string path, string sheet, int headerRow, ColumnMap map)
    {
        using var wb = new XLWorkbook(path);
        var ws = GetSheet(wb, sheet);

        var last = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        var rows = new List<RawRow>(Math.Max(0, last - headerRow));

        for (var r = headerRow + 1; r <= last; r++)
        {
            var hospital = Cell(ws, r, map.Hospital);
            var name = Cell(ws, r, map.Name);
            var dept = Cell(ws, r, map.Dept);
            var phone = Cell(ws, r, map.Phone);
            var email = Cell(ws, r, map.Email);

            // 전부 빈 행은 건너뛴다. 엑셀 하단의 서식만 남은 행이 흔하다.
            if (hospital.Length == 0 && name.Length == 0 && dept.Length == 0 && email.Length == 0)
                continue;

            rows.Add(new RawRow(r, hospital, name, dept, phone, email));
        }

        return rows;
    }

    private static IXLWorksheet GetSheet(XLWorkbook wb, string sheet) =>
        wb.Worksheets.FirstOrDefault(w => w.Name == sheet)
        ?? wb.Worksheets.First();

    private static string Cell(IXLWorksheet ws, int row, string column)
    {
        if (string.IsNullOrWhiteSpace(column)) return "";
        return ws.Cell(row, column.Trim().ToUpperInvariant()).GetString().Trim();
    }
}
