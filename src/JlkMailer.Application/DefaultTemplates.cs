using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Core.Text;

namespace JlkMailer.Application;

/// <summary>
/// 설계 §08 세그먼트별 제목·본문 슬롯 초안.
/// 실제 문구는 영업 담당자가 편집 화면에서 확정한다 — 여기 값은 시작점이다.
/// </summary>
public static class DefaultTemplates
{
    private const string GreetingCommon =
        $"{TokenRenderer.Hospital} {TokenRenderer.Dept} {TokenRenderer.Name} {TokenRenderer.Honorific}, 안녕하십니까.<br>" +
        $"제이엘케이 <span class=\"greeting-name\">{TokenRenderer.SenderName}</span>입니다.";

    private const string ClosingCommon =
        "한번 찾아뵙고 설명 드릴 수 있도록 시간 허락 부탁드립니다.";

    public static IReadOnlyList<MailTemplate> All =>
    [
        new MailTemplate
        {
            Segment = SegmentCatalog.S1,
            Subject = $"[{TokenRenderer.Hospital} {TokenRenderer.Dept}] CT 관류영상 자동분석 JLK-CTP, 평가유예 신의료기술 확정",
            Greeting = GreetingCommon,
            Intro =
                "뇌 CT 관류영상을 자동으로 분석해 초급성기 뇌경색 치료 방침 결정을 돕는 JLK-CTP 가 " +
                "평가유예 신의료기술로 확정되어, 판독 워크플로우에 어떻게 붙는지 간략히 안내드리고자 메일 드립니다.",
            BenefitLead = "저희 솔루션이 가진 3가지 장점에 대하여 말씀드립니다.",
            Closing = ClosingCommon,
        },
        new MailTemplate
        {
            Segment = SegmentCatalog.S2,
            Subject = $"[{TokenRenderer.Hospital}] 초급성기 뇌경색 EVT 판단 보조 AI, 비급여 처방 가능해졌습니다",
            Greeting = GreetingCommon,
            Intro =
                "초급성기 뇌경색 환자의 치료 방침 결정에 도움이 될 수 있는 JLK-CTP 가 " +
                "평가유예 신의료기술로 확정되어 설명을 드리고자 간략하게 메일을 드리게 되었습니다.",
            BenefitLead = "저희 솔루션이 가진 3가지 장점에 대하여 말씀드립니다.",
            Closing = ClosingCommon,
        },
        new MailTemplate
        {
            Segment = SegmentCatalog.S3,
            Subject = $"[{TokenRenderer.Hospital}] 혈전제거술 대상 선별을 돕는 JLK-CTP 소개드립니다",
            Greeting = GreetingCommon,
            Intro =
                "혈전제거술(EVT) 대상 선별 판단에 활용할 수 있는 뇌 CT 관류영상 자동분석 솔루션 JLK-CTP 가 " +
                "평가유예 신의료기술로 확정되어 소개드리고자 메일 드립니다.",
            BenefitLead = "저희 솔루션이 가진 3가지 장점에 대하여 말씀드립니다.",
            Closing = ClosingCommon,
        },
        new MailTemplate
        {
            Segment = SegmentCatalog.S4,
            Subject = $"[{TokenRenderer.Hospital}] 증상발생 24시간 내 뇌졸중 의심환자 판독 보조 AI 안내",
            Greeting = GreetingCommon,
            Intro =
                "응급실에 내원한 뇌졸중 의심환자의 초기 판단 시간을 줄이는 데 도움이 될 수 있는 JLK-CTP 를 안내드립니다. " +
                "증상 발생 24시간 이내 환자를 대상으로 하며, 최근 평가유예 신의료기술로 확정되었습니다.",
            BenefitLead = "저희 솔루션이 가진 3가지 장점에 대하여 말씀드립니다.",
            Closing = ClosingCommon,
        },
        new MailTemplate
        {
            Segment = SegmentCatalog.S5,
            Subject = $"[{TokenRenderer.Hospital}] 비급여 수가 적용 가능한 뇌졸중 AI 솔루션 도입 안내",
            Greeting = GreetingCommon,
            Intro =
                "별도의 재무 부담 없이 비급여 처방으로 운영 가능한 뇌졸중 AI 솔루션 JLK-CTP 의 도입 방식을 안내드리고자 메일 드립니다. " +
                "평가유예 신의료기술로 확정되어 병원에서 금액을 책정해 비급여 처방이 가능합니다.",
            BenefitLead = "도입 검토에 필요한 3가지 사항을 정리했습니다.",
            Closing = ClosingCommon,
        },
        new MailTemplate
        {
            Segment = SegmentCatalog.S6,
            Subject = $"[{TokenRenderer.Hospital}] PACS 연동형 뇌관류 분석 솔루션 JLK-CTP 기술 안내",
            Greeting = GreetingCommon,
            Intro =
                "원내 PACS 와 연동해 뇌 CT 관류영상을 자동 분석하는 JLK-CTP 의 기술 사양과 연동 방식을 안내드리고자 메일 드립니다.",
            BenefitLead = "연동 검토에 필요한 3가지 사항을 정리했습니다.",
            Closing = ClosingCommon,
        },
        new MailTemplate
        {
            // S7 은 기본 발송 대상이 아니지만, 사용자가 세그먼트를 지정하기 전 미리보기에 쓰인다.
            // 진료과를 언급하지 않는 범용 문안이다.
            Segment = SegmentCatalog.S7,
            Subject = $"[{TokenRenderer.Hospital}] 뇌졸중 AI 솔루션 JLK-CTP, 평가유예 신의료기술 확정 안내",
            Greeting =
                $"{TokenRenderer.Hospital} {TokenRenderer.Name} {TokenRenderer.Honorific}, 안녕하십니까.<br>" +
                $"제이엘케이 <span class=\"greeting-name\">{TokenRenderer.SenderName}</span>입니다.",
            Intro =
                "뇌 CT 관류영상을 자동으로 분석하는 JLK-CTP 가 평가유예 신의료기술로 확정되어 " +
                "간략하게 안내드리고자 메일 드립니다.",
            BenefitLead = "저희 솔루션이 가진 3가지 장점에 대하여 말씀드립니다.",
            Closing = ClosingCommon,
        },
    ];

    public static MailTemplate For(string segment) =>
        All.FirstOrDefault(t => t.Segment == segment)?.Clone()
        ?? All.First(t => t.Segment == SegmentCatalog.S7).Clone();
}
