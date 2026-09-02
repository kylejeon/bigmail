using System.Text;
using JlkMailer.Application;
using JlkMailer.Core.Abstractions;
using JlkMailer.Core.Classification;
using JlkMailer.Core.Models;
using JlkMailer.Core.Sending;
using JlkMailer.Infrastructure.Excel;
using JlkMailer.Infrastructure.Html;
using JlkMailer.Infrastructure.Mail;
using JlkMailer.Infrastructure.Storage;

// 설계 §13: M0~M3 만으로 UI 없이 발송할 수 있어야 한다. 그 경로가 이 CLI 다.
// WPF 앱(§11)과 같은 Application 계층을 쓰므로 동작이 갈리지 않는다.

Console.OutputEncoding = Encoding.UTF8;

// 메일 본문 검증용 파일은 BOM 없이 저장한다. BOM 이 붙으면 일부 뷰어가 첫 글자를 깨뜨린다.
var Utf8NoBom = new UTF8Encoding(false);

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var opts = ParseOptions(args);

try
{
    return command switch
    {
        "analyze" => Analyze(),
        "build" => Build(),
        "preview" => Preview(),
        "test-send" => await TestSend(),
        "send" => await Send(),
        "export" => Export(),
        _ => Help(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\n오류: {ex.Message}");
    return 1;
}

// ---------------------------------------------------------------- analyze

int Analyze()
{
    var (recipients, summary) = Import();

    Header("§03 실측 데이터");
    Row("유효 행", summary.TotalRows);
    Row("기관 수", summary.Hospitals);
    Row("진료과 고유값", summary.DistinctDeptRaw);
    Row("고유 이메일", recipients.Where(r => r.EmailNorm.Length > 0)
                                 .Select(r => r.EmailNorm)
                                 .Distinct(StringComparer.OrdinalIgnoreCase).Count());
    Console.WriteLine();
    Row("발송 가능", summary.Sendable);
    Row("이메일 없음", summary.NoEmail);
    Row("형식 오류(교정 제안 있음)", summary.NeedsFix);
    Row("형식 오류(교정 불가)", summary.Invalid);
    Row("중복", summary.Duplicate);
    Row("확인 필요(S7)", summary.NeedsReview);
    Row("수신거부", summary.Suppressed);

    Header("§03 교정 제안");
    foreach (var r in recipients.Where(r => r.SuggestedEmail is not null))
        Console.WriteLine($"  {r.RowNo,5}행  {r.EmailRaw,-28} → {r.SuggestedEmail}");

    Header("§07 세그먼트 분포");
    foreach (var def in SegmentCatalog.All)
    {
        var count = summary.BySegment.GetValueOrDefault(def.Code, 0);
        var share = summary.TotalRows == 0 ? 0 : 100.0 * count / summary.TotalRows;
        var raw = recipients.Where(r => r.Segment == def.Code)
                            .Select(r => r.DeptRaw).Distinct(StringComparer.Ordinal).Count();
        Console.WriteLine($"  {def.Code} {def.Name,-12} {count,5}건  ({share,4:F1}%)  원본표기 {raw,3}종");
    }

    Header("§07 S7 상세 — 수동 처리 대상");
    foreach (var g in recipients.Where(r => r.Segment == SegmentCatalog.S7)
                                .GroupBy(r => r.DeptRaw)
                                .OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {g.Count(),3}건  {(g.Key.Length == 0 ? "(빈 값)" : g.Key)}");

    return 0;
}

// ---------------------------------------------------------------- build

int Build()
{
    var outDir = opts.GetValueOrDefault("out", "build-output");
    Directory.CreateDirectory(outDir);

    var campaign = BuildCampaign();
    var bundle = new RenderService(BuildOptions()).BuildFromFile(HtmlPath(), campaign, DefaultTemplates.All);

    Header("§09 HTML 변환");
    var rawBytes = new FileInfo(HtmlPath()).Length;
    Console.WriteLine($"  원본 HTML         {rawBytes / 1024,6} KB");
    Console.WriteLine($"  변환 후 본문      {bundle.Plans[0].Stats.HtmlBytes / 1024,6} KB   (Gmail 클리핑 한계 102 KB)");
    Console.WriteLine($"  CID 이미지 {bundle.Assets.Images.Count}장   {bundle.Assets.Images.Sum(i => i.Bytes.Length) / 1024,6} KB");
    Console.WriteLine($"  메일 총량         {bundle.Plans[0].Stats.TotalBytes / 1024,6} KB");

    foreach (var image in bundle.Assets.Images)
    {
        File.WriteAllBytes(Path.Combine(outDir, image.FileName), image.Bytes);
        Console.WriteLine($"    → {image.FileName}  {image.Bytes.Length / 1024} KB  cid:{image.ContentId}");
    }

    Header("경고 · 정보");
    foreach (var w in bundle.AllWarnings) Console.WriteLine($"  {w}");

    Header("세그먼트별 산출물");
    var sample = SampleRecipient();
    foreach (var plan in bundle.Plans)
    {
        var def = SegmentCatalog.Get(plan.Segment);
        sample.Segment = def.Code;
        sample.DeptLabel = def.DeptLabel;
        sample.Honorific = def.Honorific;

        var mail = bundle.Composer.Compose(sample, campaign, DefaultTemplates.For(def.Code));

        // 실제 발송본. 이미지는 cid: 로 참조되므로 브라우저에서는 깨져 보인다.
        File.WriteAllText(Path.Combine(outDir, $"{def.Code}.html"), mail.Html, Utf8NoBom);
        File.WriteAllText(Path.Combine(outDir, $"{def.Code}.txt"), mail.PlainText, Utf8NoBom);

        // 브라우저 확인용. cid: 를 같은 폴더의 이미지 파일로 바꾼 사본이다.
        // 레이아웃 점검에는 쓸 수 있지만, 이것이 수신자가 받는 것은 아니다.
        var browserHtml = mail.Html;
        foreach (var image in mail.Images)
            browserHtml = browserHtml.Replace($"cid:{image.ContentId}", image.FileName);
        File.WriteAllText(Path.Combine(outDir, $"{def.Code}.preview.html"), browserHtml, Utf8NoBom);

        var length = mail.Subject.Length;
        var flag = length > 60 ? " ← 60자 초과" : "";
        Console.WriteLine($"  {def.Code} 제목({length,2}자){flag}  {mail.Subject}");
    }

    Console.WriteLine($"\n  산출물 위치: {Path.GetFullPath(outDir)}");
    Console.WriteLine("  *.html          실제 발송본 (이미지는 cid: 참조 — 브라우저에서는 안 보임)");
    Console.WriteLine("  *.preview.html  브라우저 확인용 사본");
    Console.WriteLine("  *.txt           text/plain 대체본");
    Console.WriteLine();
    Console.WriteLine("  설계 §09 검증: 브라우저 확인은 레이아웃 점검까지입니다.");
    Console.WriteLine("  실제 판정은 Gmail·Outlook·네이버·모바일 4곳에 보내 봐야 나옵니다.");
    return 0;
}

// ---------------------------------------------------------------- preview

int Preview()
{
    var (recipients, _) = Import();
    var rowNo = int.TryParse(opts.GetValueOrDefault("row", ""), out var n) ? n : 0;

    var recipient = rowNo > 0
        ? recipients.FirstOrDefault(r => r.RowNo == rowNo)
        : recipients.FirstOrDefault(r => r.IsSendable && r.Segment == opts.GetValueOrDefault("segment", SegmentCatalog.S2));

    if (recipient is null) { Console.Error.WriteLine("해당 수신자를 찾지 못했습니다."); return 1; }

    var campaign = BuildCampaign();
    var bundle = new RenderService(BuildOptions()).BuildFromFile(HtmlPath(), campaign, DefaultTemplates.All);
    var mail = bundle.Composer.Compose(recipient, campaign, DefaultTemplates.For(recipient.Segment));

    Header($"미리보기 — {recipient.RowNo}행");
    Console.WriteLine($"  받는사람  {recipient.Name} <{recipient.EffectiveEmail}>");
    Console.WriteLine($"  기관      {recipient.Hospital}");
    Console.WriteLine($"  진료과    {recipient.DeptRaw}  →  {recipient.Segment} ({SegmentCatalog.Get(recipient.Segment).Name})");
    Console.WriteLine($"  제목      {mail.Subject}");
    Console.WriteLine($"  본문      {Encoding.UTF8.GetByteCount(mail.Html) / 1024} KB + 이미지 {mail.Images.Sum(i => i.Bytes.Length) / 1024} KB");
    if (mail.ListUnsubscribe is not null) Console.WriteLine($"  수신거부  {mail.ListUnsubscribe}");

    Header("text/plain 대체본");
    Console.WriteLine(mail.PlainText);

    var outPath = opts.GetValueOrDefault("out", "preview.html");
    File.WriteAllText(outPath, mail.Html, Utf8NoBom);
    Console.WriteLine($"\n  HTML 저장: {Path.GetFullPath(outPath)}");
    return 0;
}

// ---------------------------------------------------------------- send

async Task<int> Send()
{
    var dbPath = opts.GetValueOrDefault("db", "campaign.db");
    using var store = new SqliteCampaignStore(dbPath);
    store.Initialize();

    var campaign = BuildCampaign();
    campaign.DailyCap = int.TryParse(opts.GetValueOrDefault("daily-cap", ""), out var cap) ? cap : campaign.DailyCap;
    store.UpsertCampaign(campaign);
    store.SaveRules(SegmentCatalog.DefaultRules);
    foreach (var t in DefaultTemplates.All) store.SaveTemplate(t);

    var (recipients, summary) = Import(store.GetSuppressions());

    // --segment 로 대상을 좁힌다. 워밍업 스케줄(§10)에 맞춰 나눠 보내기 위한 것.
    var wanted = opts.GetValueOrDefault("segment", "");
    var targets = recipients
        .Where(r => r.IsSendable)
        .Where(r => wanted.Length == 0 || wanted.Split(',').Contains(r.Segment))
        .Where(r => SegmentCatalog.Get(r.Segment).SendByDefault)
        .ToList();

    store.ReplaceRecipients(recipients);
    var stored = store.GetRecipients();
    var byKey = stored.ToDictionary(r => (r.RowNo, r.EmailNorm));
    foreach (var t in targets)
        if (byKey.TryGetValue((t.RowNo, t.EmailNorm), out var s)) t.Id = s.Id;

    var enqueued = store.EnqueueMissing(campaign.Id, targets);

    Header("발송 준비");
    Row("전체 행", summary.TotalRows);
    Row("발송 대상", targets.Count);
    Row("새로 큐에 넣은 건수", enqueued);
    Row("이미 처리된 건수(중복 방지)", targets.Count - enqueued);

    var policy = new ThrottlePolicy
    {
        IntervalSeconds = int.TryParse(opts.GetValueOrDefault("interval", ""), out var i) ? i : 30,
        JitterSeconds = int.TryParse(opts.GetValueOrDefault("jitter", ""), out var j) ? j : 10,
        DailyCap = campaign.DailyCap,
    };

    if (opts.ContainsKey("dry-run"))
    {
        Header("드라이런 — 실제로 보내지 않습니다");
        Console.WriteLine($"  다음 발송 가능 시각  {policy.NextOpening(DateTime.Now):yyyy-MM-dd HH:mm}");
        Console.WriteLine($"  간격                 {policy.IntervalSeconds}±{policy.JitterSeconds}초");
        Console.WriteLine($"  일 상한              {policy.DailyCap}통");
        var days = (int)Math.Ceiling((double)enqueued / Math.Max(1, policy.DailyCap));
        Console.WriteLine($"  예상 소요            약 {days}일");
        return 0;
    }

    var smtp = new SmtpOptions
    {
        Host = opts.GetValueOrDefault("host", "smtp.gmail.com"),
        Port = int.TryParse(opts.GetValueOrDefault("port", ""), out var p) ? p : 587,
        UserName = Require("user"),
        Secret = ReadSecret(),
        CheckCertificateRevocation = !opts.ContainsKey("no-crl-check"),
    };

    await using var sender = new MailKitSender(smtp);
    var bundle = new RenderService(BuildOptions()).BuildFromFile(HtmlPath(), campaign, DefaultTemplates.All);

    foreach (var w in bundle.AllWarnings.Where(w => w.StartsWith("[경고]"))) Console.WriteLine($"  {w}");

    var orchestrator = new SendOrchestrator(store, sender, bundle.Composer, policy);
    var progress = new Progress<SendProgress>(s =>
        Console.WriteLine($"  [{s.Sent,5}/{s.Sent + s.Failed + s.Remaining,-5}] {s.LastState,-8} {s.LastEmail,-38} {s.LastMessage}"));

    Header("발송 시작");
    var outcome = await orchestrator.RunAsync(
        campaign,
        store.GetRecipients().ToDictionary(r => r.Id),
        store.GetTemplates().ToDictionary(t => t.Segment),
        progress);

    Header("발송 종료");
    Console.WriteLine($"  사유    {outcome.Reason}");
    Console.WriteLine($"  성공    {outcome.Sent}");
    Console.WriteLine($"  실패    {outcome.Failed}");
    if (outcome.Detail is not null) Console.WriteLine($"  상세    {outcome.Detail}");
    return outcome.Reason is StopReason.Completed or StopReason.DailyCapReached or StopReason.OutsideWindow ? 0 : 2;
}

// ---------------------------------------------------------------- test-send

// 설계 §11 화면4 의 '테스트 발송' 에 해당한다.
// 엑셀에 없는 임의의 수신자를 만들어 실제 SMTP 로 한 통만 보낸다.
// 진료과는 실제 분류기를 통과시키므로, 본 발송과 같은 문안이 나온다.
async Task<int> TestSend()
{
    var to = Require("to");
    var hospital = opts.GetValueOrDefault("hospital", "가천대학교길병원");
    var dept = opts.GetValueOrDefault("dept", "신경과");
    var name = opts.GetValueOrDefault("name", "홍길동");

    var classifier = new SegmentClassifier();
    var recipient = new Recipient
    {
        RowNo = 0,
        Hospital = hospital,
        Name = name,
        DeptRaw = dept,
        EmailRaw = to,
        EmailNorm = to.Trim().ToLowerInvariant(),
        Status = RecipientStatus.Ready,
    };
    classifier.Apply(recipient);

    // 사용자가 세그먼트를 직접 지정하면 분류 결과를 덮어쓴다.
    var forced = opts.GetValueOrDefault("segment", "");
    if (forced.Length > 0 && SegmentCatalog.Exists(forced))
    {
        var def = SegmentCatalog.Get(forced);
        recipient.Segment = def.Code;
        recipient.DeptLabel = def.DeptLabel;
        recipient.Honorific = def.Honorific;
    }

    var campaign = BuildCampaign();
    var bundle = new RenderService(BuildOptions()).BuildFromFile(HtmlPath(), campaign, DefaultTemplates.All);
    var mail = bundle.Composer.Compose(recipient, campaign, DefaultTemplates.For(recipient.Segment));

    var segmentDef = SegmentCatalog.Get(recipient.Segment);

    Header("테스트 발송 — 보낼 내용");
    Console.WriteLine($"  보내는사람  {campaign.FromName} <{campaign.FromAddress}>");
    if (campaign.ReplyTo.Length > 0) Console.WriteLine($"  회신주소    {campaign.ReplyTo}");
    Console.WriteLine($"  받는사람    {recipient.Name} <{recipient.EffectiveEmail}>");
    Console.WriteLine($"  기관        {recipient.Hospital}");
    Console.WriteLine($"  진료과      {recipient.DeptRaw}  →  {segmentDef.Code} {segmentDef.Name}");
    Console.WriteLine($"  호칭        {recipient.Honorific}");
    Console.WriteLine($"  발신자명    {campaign.SenderDisplayName}   (본문 인사말·서명의 {{{{발신자명}}}})");
    Console.WriteLine($"  제목        {mail.Subject}   ({mail.Subject.Length}자)");
    Console.WriteLine($"  본문        {System.Text.Encoding.UTF8.GetByteCount(mail.Html) / 1024} KB + 이미지 {mail.Images.Sum(i => i.Bytes.Length) / 1024} KB");
    Console.WriteLine($"  수신거부    {mail.ListUnsubscribe ?? "(없음)"}");
    Console.WriteLine($"  광고 표기   {(campaign.AdPrefix ? "켜짐" : "꺼짐")}");

    foreach (var w in bundle.AllWarnings.Where(w => w.StartsWith("[경고]"))) Console.WriteLine($"  {w}");

    var previewPath = opts.GetValueOrDefault("out", "");
    if (previewPath.Length > 0)
    {
        File.WriteAllText(previewPath, mail.Html, Utf8NoBom);
        Console.WriteLine($"  미리보기    {Path.GetFullPath(previewPath)}");
    }

    if (opts.ContainsKey("dry-run"))
    {
        Console.WriteLine();
        Console.WriteLine("  드라이런입니다. 실제로 보내지 않았습니다.");
        return 0;
    }

    var smtp = new SmtpOptions
    {
        Host = opts.GetValueOrDefault("host", "smtp.gmail.com"),
        Port = int.TryParse(opts.GetValueOrDefault("port", ""), out var p) ? p : 587,
        UserName = opts.GetValueOrDefault("user", campaign.FromAddress),
        Secret = ReadSecret(),
        CheckCertificateRevocation = !opts.ContainsKey("no-crl-check"),
    };

    Header("발송");
    Console.WriteLine($"  SMTP  {smtp.Host}:{smtp.Port}  as {smtp.UserName}");
    if (!smtp.CheckCertificateRevocation)
        Console.WriteLine("  주의  인증서 폐기 확인을 건너뜁니다. 체인·호스트명·유효기간 검증은 유지됩니다.");

    await using var sender = new MailKitSender(smtp);
    await sender.ConnectAsync();
    Console.WriteLine("  인증 성공");

    var result = await sender.SendAsync(recipient.EffectiveEmail, recipient.Name, mail, campaign);

    Console.WriteLine($"  결과  {result.Outcome}  {result.Code}  {result.Message}");
    if (result.MessageId is not null) Console.WriteLine($"  Message-Id  {result.MessageId}");

    if (result.Outcome != SmtpOutcome.Success) return 2;

    Console.WriteLine();
    Console.WriteLine("  받은편지함을 확인하세요. 스팸함도 함께 보셔야 합니다.");
    Console.WriteLine("  설계 §09: Gmail 뿐 아니라 Outlook·네이버·모바일에서도 확인해야 판정이 끝납니다.");
    return 0;
}

// ---------------------------------------------------------------- export

int Export()
{
    var dbPath = opts.GetValueOrDefault("db", "campaign.db");
    var outPath = opts.GetValueOrDefault("out", "발송결과.xlsx");

    using var store = new SqliteCampaignStore(dbPath);
    store.Initialize();

    var campaignId = long.TryParse(opts.GetValueOrDefault("campaign", ""), out var id) ? id : 1;
    ResultExporter.Export(outPath, store.GetRecipients(), store.GetLog(campaignId));

    Console.WriteLine($"저장: {Path.GetFullPath(outPath)}");
    return 0;
}

// ---------------------------------------------------------------- helpers

(List<Recipient>, ImportSummary) Import(IReadOnlySet<string>? suppressions = null)
{
    var reader = new ClosedXmlRecipientReader();
    var path = ExcelPath();
    var sheet = opts.GetValueOrDefault("sheet", reader.ListSheets(path)[0]);
    var headerRow = int.TryParse(opts.GetValueOrDefault("header-row", ""), out var h) ? h : 1;
    var map = reader.GuessColumns(path, sheet, headerRow);
    var rows = reader.Read(path, sheet, headerRow, map);
    return new ImportService(new SegmentClassifier()).Build(rows, suppressions);
}

Campaign BuildCampaign() => new()
{
    Id = 1,
    Name = opts.GetValueOrDefault("campaign-name", "JLK-CTP 소개"),
    HtmlPath = HtmlPath(),
    FromName = opts.GetValueOrDefault("from-name", "제이엘케이"),
    FromAddress = opts.GetValueOrDefault("from", "cs@jlkgroup.com"),
    ReplyTo = opts.GetValueOrDefault("reply-to", ""),
    SenderDisplayName = opts.GetValueOrDefault("sender", "홍길동"),
    AdPrefix = opts.ContainsKey("ad-prefix"),
    IncludeUnsubscribe = !opts.ContainsKey("no-unsubscribe"),
    UnsubscribeTarget = opts.GetValueOrDefault("unsubscribe", "cs@jlkgroup.com"),
    DailyCap = 300,
};

EmailBuildOptions BuildOptions() => new()
{
    KeepPng = opts.ContainsKey("keep-png"),
    JpegQuality = int.TryParse(opts.GetValueOrDefault("quality", ""), out var q) ? q : 82,
};

Recipient SampleRecipient() => new()
{
    RowNo = 0,
    Hospital = "분당서울대학교병원",
    Name = "박○○",
    DeptRaw = "신경과",
    Segment = SegmentCatalog.S2,
    DeptLabel = "신경과",
    Honorific = SegmentCatalog.HonorificClinical,
    EmailRaw = "sample@example.com",
    EmailNorm = "sample@example.com",
};

// 앱 비밀번호는 명령줄 인자로 받지 않는다. ps 로 다른 프로세스에 노출되기 때문이다.
//   --secret-file <경로>  또는  환경변수 JLKMAIL_SECRET
string ReadSecret()
{
    var file = opts.GetValueOrDefault("secret-file", "");
    if (file.Length > 0)
    {
        if (!File.Exists(file)) throw new FileNotFoundException($"비밀번호 파일이 없습니다: {file}");
        // Gmail 앱 비밀번호는 표시할 때 4자리씩 띄어 주지만 실제 값에는 공백이 없다.
        return File.ReadAllText(file).Trim().Replace(" ", "");
    }

    var env = Environment.GetEnvironmentVariable("JLKMAIL_SECRET");
    if (!string.IsNullOrWhiteSpace(env)) return env.Trim().Replace(" ", "");

    throw new InvalidOperationException(
        "앱 비밀번호를 찾지 못했습니다. --secret-file <경로> 를 주거나 환경변수 JLKMAIL_SECRET 을 설정하세요. " +
        "명령줄 인자로는 받지 않습니다.");
}

string ExcelPath() => opts.GetValueOrDefault("excel", "260709_병원이메일.xlsx");
string HtmlPath() => opts.GetValueOrDefault("html", "JLK-CTP_소개메일.html");

string Require(string key) => opts.TryGetValue(key, out var v) && v.Length > 0
    ? v
    : throw new InvalidOperationException($"--{key} 인자가 필요합니다.");

Dictionary<string, string> ParseOptions(string[] argv)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 1; i < argv.Length; i++)
    {
        if (!argv[i].StartsWith("--")) continue;
        var key = argv[i][2..];
        var value = i + 1 < argv.Length && !argv[i + 1].StartsWith("--") ? argv[++i] : "";
        result[key] = value;
    }
    return result;
}

void Header(string title)
{
    Console.WriteLine();
    Console.WriteLine($"── {title} " + new string('─', Math.Max(0, 62 - title.Length)));
}

void Row(string label, int value) => Console.WriteLine($"  {label,-28} {value,7:N0}");

int Help()
{
    Console.WriteLine("""
        jlkmail — JLK-CTP 메일 발송기 (CLI). 설계 §13 M0~M3 경로.

          analyze                엑셀 실측 통계와 세그먼트 분포 출력 (§03, §07)
          build   --out <dir>    HTML 변환 결과를 세그먼트별 파일로 저장 (§09 검증용)
          preview --row <n>      특정 행의 메일 미리보기
          test-send --to <addr>  임의 수신자에게 한 통만 실제 발송 (§11 테스트 발송)
          send    --user <addr>  실제 발송
          export  --out <xlsx>   발송 결과 엑셀 내보내기 (§11)

        공통 인자
          --excel <path>    기본 260709_병원이메일.xlsx
          --html  <path>    기본 JLK-CTP_소개메일.html
          --db    <path>    기본 campaign.db
          --sender <이름>   {{발신자명}} 값

        연결
          --host / --port        기본 smtp.gmail.com / 587
          --no-crl-check         인증서 폐기 확인(CRL/OCSP) 건너뛰기.
                                 macOS 의 .NET 은 폐기 조회를 완결하지 못해 정상 인증서에서도
                                 연결이 끊긴다. 체인·호스트명·유효기간 검증은 그대로 유지된다.

        자격증명 (인자로 받지 않음 — ps 노출 방지)
          --secret-file <경로>   앱 비밀번호가 든 파일
          또는 환경변수 JLKMAIL_SECRET

        test-send 전용
          --to <addr>       받는 사람            --hospital <이름>  병원명
          --dept <진료과>   진료과 (분류기 통과)  --name <성함>      받는 사람 성함
          --segment S3      분류 결과를 강제로 지정
          --out <path>      보낸 HTML 을 파일로도 저장
          --dry-run         보낼 내용만 보여주고 발송하지 않음

        send 전용
          --segment S2,S3   대상 세그먼트 (워밍업 스케줄 §10 에 맞춰 나눠 보낼 때)
          --interval 30     발송 간격(초)     --jitter 10   지터(초)
          --daily-cap 150   일 상한          --dry-run     실제로 보내지 않고 계획만 출력
          --ad-prefix       제목에 '(광고)' 접두어 (§12)

        예)
          jlkmail analyze
          jlkmail build --out out
          jlkmail send --user sales@jlkgroup.com --segment S2 --daily-cap 50 --dry-run
          jlkmail test-send --to me@example.com --dept 신경외과 --name 홍길동 \\
                            --from Neurology@jlkgroup.com --secret-file ~/.jlkmail-secret
        """);
    return 0;
}
