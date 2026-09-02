using JlkMailer.Core.Sending;
using Xunit;

namespace JlkMailer.Tests;

public class ThrottlePolicyTests
{
    private static readonly ThrottlePolicy Default = new();

    // 2026-09-02 는 수요일, 09-05 는 토요일, 09-07 은 월요일
    [Theory]
    [InlineData("2026-09-02 09:30", true)]
    [InlineData("2026-09-02 10:59", true)]
    [InlineData("2026-09-02 11:00", false)]  // 창 끝은 배타적
    [InlineData("2026-09-02 12:00", false)]  // 점심
    [InlineData("2026-09-02 14:30", true)]
    [InlineData("2026-09-02 16:00", false)]
    [InlineData("2026-09-02 08:59", false)]
    [InlineData("2026-09-05 10:00", false)]  // 토요일
    [InlineData("2026-09-06 10:00", false)]  // 일요일
    public void 허용_시간대만_열려_있다(string when, bool expected) =>
        Assert.Equal(expected, Default.IsOpen(DateTime.Parse(when)));

    /// <summary>설계 §12: 21시~익일 08시는 사용자가 시간대를 어떻게 설정하든 항상 막는다.</summary>
    [Fact]
    public void 야간_금지구간은_설정과_무관하게_닫힌다()
    {
        var allDay = new ThrottlePolicy
        {
            Windows = [new TimeWindow(new TimeOnly(0, 0), new TimeOnly(23, 59))],
            SkipWeekends = false,
        };

        Assert.False(allDay.IsOpen(DateTime.Parse("2026-09-02 22:00")));
        Assert.False(allDay.IsOpen(DateTime.Parse("2026-09-02 03:00")));
        Assert.False(allDay.IsOpen(DateTime.Parse("2026-09-02 07:59")));
        Assert.True(allDay.IsOpen(DateTime.Parse("2026-09-02 08:00")));
        Assert.True(allDay.IsOpen(DateTime.Parse("2026-09-02 20:59")));
    }

    [Fact]
    public void 닫혀_있으면_다음_열리는_시각을_알려준다()
    {
        Assert.Equal(DateTime.Parse("2026-09-02 14:00"), Default.NextOpening(DateTime.Parse("2026-09-02 12:30")));
        Assert.Equal(DateTime.Parse("2026-09-03 09:00"), Default.NextOpening(DateTime.Parse("2026-09-02 16:30")));
        // 금요일 저녁 → 월요일 오전
        Assert.Equal(DateTime.Parse("2026-09-07 09:00"), Default.NextOpening(DateTime.Parse("2026-09-04 17:00")));
    }

    [Fact]
    public void 열려_있으면_입력값을_그대로_돌려준다()
    {
        var open = DateTime.Parse("2026-09-02 10:00");
        Assert.Equal(open, Default.NextOpening(open));
    }

    [Fact]
    public void 열리지_않는_정책은_예외를_던진다()
    {
        var never = new ThrottlePolicy { Windows = [new TimeWindow(new TimeOnly(23, 0), new TimeOnly(23, 30))] };
        Assert.Throws<InvalidOperationException>(() => never.NextOpening(DateTime.Parse("2026-09-02 10:00")));
    }

    [Fact]
    public void 간격은_지터_범위_안에_들어온다()
    {
        var policy = new ThrottlePolicy { IntervalSeconds = 30, JitterSeconds = 10 };
        var rng = new Random(42);

        for (var i = 0; i < 500; i++)
        {
            var seconds = policy.NextInterval(rng).TotalSeconds;
            Assert.InRange(seconds, 20, 40);
        }
    }

    /// <summary>설계 §10: 30초 → 2분 → 10분, 최대 3회.</summary>
    [Fact]
    public void 백오프는_지수적으로_증가한다()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), ThrottlePolicy.BackoffFor(1));
        Assert.Equal(TimeSpan.FromMinutes(2), ThrottlePolicy.BackoffFor(2));
        Assert.Equal(TimeSpan.FromMinutes(10), ThrottlePolicy.BackoffFor(3));
        Assert.Equal(TimeSpan.FromMinutes(10), ThrottlePolicy.BackoffFor(9));  // 범위를 넘으면 마지막 값
        Assert.Equal(3, ThrottlePolicy.MaxAttempts);
    }
}
