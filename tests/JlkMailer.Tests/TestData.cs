namespace JlkMailer.Tests;

/// <summary>실제 납품 데이터 파일을 찾는다. 테스트는 합성 데이터가 아닌 진짜 파일로 돌아야 한다.</summary>
public static class TestData
{
    public const string ExcelName = "260709_병원이메일.xlsx";
    public const string HtmlName = "JLK-CTP_소개메일.html";

    private static readonly Lazy<string> Root = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, ExcelName))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"{ExcelName} 을 찾지 못했습니다. 저장소 루트에서 실행하세요.");
    });

    public static string Excel => Path.Combine(Root.Value, ExcelName);
    public static string Html => Path.Combine(Root.Value, HtmlName);
}
