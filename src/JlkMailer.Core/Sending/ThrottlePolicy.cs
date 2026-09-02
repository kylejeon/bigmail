namespace JlkMailer.Core.Sending;

/// <summary>발송 허용 시간대. 설계 §10 — 평일 09~11, 14~16 기본.</summary>
public readonly record struct TimeWindow(TimeOnly Start, TimeOnly End)
{
    public bool Contains(TimeOnly t) => t >= Start && t < End;
    public override string ToString() => $"{Start:HH\\:mm}–{End:HH\\:mm}";
}

/// <summary>
/// 발송 속도·시간대 정책. 순수 함수라 시계를 주입해 테스트한다. 설계 §10, §12.
/// </summary>
public sealed class ThrottlePolicy
{
    /// <summary>설계 §12: 21시~익일 08시 광고성 정보 전송은 별도 사전동의가 필요하다.
    /// 사용자가 시간대를 어떻게 설정하든 이 구간은 항상 막는다.</summary>
    public static readonly TimeOnly NightBanStart = new(21, 0);
    public static readonly TimeOnly NightBanEnd = new(8, 0);

    public int IntervalSeconds { get; init; } = 30;
    public int JitterSeconds { get; init; } = 10;
    public int DailyCap { get; init; } = 300;
    public bool SkipWeekends { get; init; } = true;

    public IReadOnlyList<TimeWindow> Windows { get; init; } =
    [
        new TimeWindow(new TimeOnly(9, 0), new TimeOnly(11, 0)),
        new TimeWindow(new TimeOnly(14, 0), new TimeOnly(16, 0)),
    ];

    /// <summary>연속 실패가 이 횟수에 도달하면 발송을 자동 중단한다. 설계 §10 차단기.</summary>
    public int ConsecutiveFailureLimit { get; init; } = 10;

    /// <summary>지수 백오프 단계. 설계 §10 — 30초 → 2분 → 10분, 최대 3회.</summary>
    public static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    public static int MaxAttempts => Backoff.Length;

    public bool IsBusinessDay(DateTime local) =>
        !SkipWeekends || local.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>야간 금지 구간(21:00–08:00)에 걸리는지.</summary>
    public static bool IsNightBanned(TimeOnly t) => t >= NightBanStart || t < NightBanEnd;

    public bool IsOpen(DateTime local)
    {
        if (!IsBusinessDay(local)) return false;
        var t = TimeOnly.FromDateTime(local);
        if (IsNightBanned(t)) return false;
        return Windows.Any(w => w.Contains(t));
    }

    /// <summary>
    /// 지금 닫혀 있으면 다음으로 열리는 시각. 열려 있으면 입력값 그대로.
    /// 최대 14일까지만 탐색하고, 그 안에 열리지 않으면 정책이 잘못된 것이므로 예외를 던진다.
    /// </summary>
    public DateTime NextOpening(DateTime local)
    {
        if (IsOpen(local)) return local;

        for (var day = 0; day <= 14; day++)
        {
            var date = local.Date.AddDays(day);
            foreach (var w in Windows.OrderBy(w => w.Start))
            {
                var candidate = date + w.Start.ToTimeSpan();
                if (candidate <= local) continue;
                if (IsOpen(candidate)) return candidate;
            }
        }

        throw new InvalidOperationException(
            "설정된 발송 시간대가 14일 안에 한 번도 열리지 않습니다. 시간대 설정을 확인하세요.");
    }

    /// <summary>다음 발송까지의 간격. 간격 ± 지터.</summary>
    public TimeSpan NextInterval(Random rng)
    {
        var jitter = JitterSeconds <= 0 ? 0 : rng.Next(-JitterSeconds, JitterSeconds + 1);
        var seconds = Math.Max(1, IntervalSeconds + jitter);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>재시도 대기 시간. attempt 는 1부터.</summary>
    public static TimeSpan BackoffFor(int attempt) =>
        Backoff[Math.Clamp(attempt - 1, 0, Backoff.Length - 1)];
}
