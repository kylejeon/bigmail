using JlkMailer.Application;
using JlkMailer.Core.Classification;
using JlkMailer.Infrastructure.Excel;
using Xunit;

namespace JlkMailer.Tests;

public class SegmentClassifierTests
{
    private readonly SegmentClassifier _classifier = new();

    [Theory]
    [InlineData("영상의학과", SegmentCatalog.S1)]
    [InlineData("영상의학팀 팀장", SegmentCatalog.S1)]
    [InlineData("영상의학과 mri", SegmentCatalog.S1)]
    [InlineData("MRI실", SegmentCatalog.S1)]
    [InlineData("PACS담당", SegmentCatalog.S1)]
    [InlineData("신경과", SegmentCatalog.S2)]
    [InlineData("신경과학교실 교수", SegmentCatalog.S2)]
    [InlineData("뇌졸중센터장", SegmentCatalog.S2)]
    [InlineData("신경외과", SegmentCatalog.S3)]
    [InlineData("뇌내시경", SegmentCatalog.S3)]
    [InlineData("응급의학과교수", SegmentCatalog.S4)]
    [InlineData("의료정보팀", SegmentCatalog.S6)]
    [InlineData("의공관리팀", SegmentCatalog.S6)]
    [InlineData("구매계약팀", SegmentCatalog.S5)]
    [InlineData("기획조정실", SegmentCatalog.S5)]
    [InlineData("", SegmentCatalog.S7)]
    [InlineData("임동수", SegmentCatalog.S7)]
    [InlineData("호흡기내과", SegmentCatalog.S7)]
    public void 진료과_원본값을_세그먼트로_분류한다(string deptRaw, string expected) =>
        Assert.Equal(expected, _classifier.Classify(deptRaw));

    /// <summary>
    /// 설계 §07 의 핵심 경고. S5 의 '부장$'·'실장$' 이 임상 진료과를 가로채면 안 된다.
    /// 규칙 순서가 뒤바뀌면 이 테스트가 가장 먼저 깨진다.
    /// </summary>
    [Theory]
    [InlineData("영상의학과 부장", SegmentCatalog.S1)]
    [InlineData("영상의학실장", SegmentCatalog.S1)]
    [InlineData("신경외과 부장", SegmentCatalog.S3)]
    [InlineData("신경과 부장", SegmentCatalog.S2)]
    public void 직함_패턴이_임상_세그먼트를_가로채지_않는다(string deptRaw, string expected) =>
        Assert.Equal(expected, _classifier.Classify(deptRaw));

    /// <summary>'신경외과' 안에 '신경과' 는 부분문자열로 들어있지 않다. 그래도 순서 변경 시 회귀를 잡기 위해 고정한다.</summary>
    [Fact]
    public void 신경외과는_신경과로_분류되지_않는다() =>
        Assert.Equal(SegmentCatalog.S3, _classifier.Classify("신경외과"));

    [Fact]
    public void 규칙_순서를_뒤집으면_분류가_바뀐다()
    {
        var reversed = SegmentCatalog.DefaultRules
            .Select(r => r with { Priority = 100 - r.Priority })
            .ToList();

        Assert.Equal(SegmentCatalog.S1, new SegmentClassifier().Classify("영상의학과 부장"));
        Assert.Equal(SegmentCatalog.S5, new SegmentClassifier(reversed).Classify("영상의학과 부장"));
    }

    /// <summary>설계 §07 표의 수치를 실제 납품 엑셀로 고정한다. 규칙을 손대면 여기서 차이가 드러난다.</summary>
    [Fact]
    public void 실제_엑셀에서_설계_문서의_세그먼트_분포가_재현된다()
    {
        var (_, summary) = ImportRealExcel();

        Assert.Equal(1872, summary.TotalRows);
        Assert.Equal(187, summary.Hospitals);
        Assert.Equal(112, summary.DistinctDeptRaw);

        Assert.Equal(676, summary.BySegment[SegmentCatalog.S1]);
        Assert.Equal(363, summary.BySegment[SegmentCatalog.S2]);
        Assert.Equal(301, summary.BySegment[SegmentCatalog.S3]);
        Assert.Equal(344, summary.BySegment[SegmentCatalog.S4]);
        Assert.Equal(91, summary.BySegment[SegmentCatalog.S5]);
        Assert.Equal(24, summary.BySegment[SegmentCatalog.S6]);
        Assert.Equal(73, summary.BySegment[SegmentCatalog.S7]);

        // 자동 분류율 96.1%
        var auto = summary.TotalRows - summary.BySegment[SegmentCatalog.S7];
        Assert.Equal(96.1, Math.Round(100.0 * auto / summary.TotalRows, 1));
    }

    internal static (List<Core.Models.Recipient>, ImportSummary) ImportRealExcel()
    {
        var reader = new ClosedXmlRecipientReader();
        var sheet = reader.ListSheets(TestData.Excel)[0];
        var map = reader.GuessColumns(TestData.Excel, sheet, 1);
        var rows = reader.Read(TestData.Excel, sheet, 1, map);
        return new ImportService(new SegmentClassifier()).Build(rows);
    }
}
