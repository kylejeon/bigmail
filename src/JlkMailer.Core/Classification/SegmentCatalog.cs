using JlkMailer.Core.Models;

namespace JlkMailer.Core.Classification;

/// <summary>
/// 설계 §07 의 세그먼트 정의와 기본 분류 규칙.
/// 규칙은 UI 에서 편집 가능해야 하므로(§06) 여기 값은 '초기값'일 뿐 하드코딩된 진실이 아니다.
/// </summary>
public static class SegmentCatalog
{
    public const string S1 = "S1";
    public const string S2 = "S2";
    public const string S3 = "S3";
    public const string S4 = "S4";
    public const string S5 = "S5";
    public const string S6 = "S6";
    public const string S7 = "S7";

    public const string HonorificClinical = "선생님";
    public const string HonorificStaff = "담당자님";

    public static readonly IReadOnlyList<SegmentDef> All =
    [
        //           code  name           deptLabel     honorific           clinical dedupe sendByDefault
        new SegmentDef(S1, "영상의학",     "영상의학과",  HonorificClinical, true,  4, true),
        new SegmentDef(S2, "신경과",       "신경과",      HonorificClinical, true,  1, true),
        new SegmentDef(S3, "신경외과",     "신경외과",    HonorificClinical, true,  2, true),
        new SegmentDef(S4, "응급의학과",   "응급의학과",  HonorificClinical, true,  3, true),
        new SegmentDef(S5, "행정·구매",    "",            HonorificStaff,    false, 6, true),
        new SegmentDef(S6, "IT·의공",      "",            HonorificStaff,    false, 5, true),
        // S7 은 기본 발송 제외. 사용자가 검토 화면에서 세그먼트를 직접 지정해야만 큐에 들어간다(§07).
        new SegmentDef(S7, "기타·확인필요", "",           HonorificStaff,    false, 99, false),
    ];

    private static readonly Dictionary<string, SegmentDef> ByCode =
        All.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

    public static SegmentDef Get(string code) =>
        ByCode.TryGetValue(code ?? "", out var s) ? s : ByCode[S7];

    public static bool Exists(string code) => ByCode.ContainsKey(code ?? "");

    /// <summary>
    /// 설계 §07 표의 기본 규칙. Priority 순서가 결과를 바꾼다 — 임상(S1~S4)이 반드시 먼저.
    /// S5 의 '부장$'·'실장$' 이 '영상의학과 부장' 을 가로채는 것을 막기 위한 순서다.
    /// </summary>
    public static IReadOnlyList<SegmentRule> DefaultRules =>
    [
        new SegmentRule(1, S1, "영상의학|영상검사|MRI|PACS|판독"),
        new SegmentRule(2, S2, "신경과|신경과학|뇌졸중"),
        new SegmentRule(3, S3, "신경외과|뇌내시경"),
        new SegmentRule(4, S4, "응급"),
        new SegmentRule(5, S6, "의료정보|전산|정보전략|정보통신|의공|스마트헬스"),
        new SegmentRule(6, S5,
            "구매|자재|물류|총무|원무|심사|기획|재단|경영|행정|사무국|관리이사|본부장|국장|실장$|부장$|" +
            "대외협력|홍보|마케팅|고객지원|결과관리|관리팀|영업"),
    ];
}
