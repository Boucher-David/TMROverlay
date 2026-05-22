using TmrOverlay.App.Overlays.Abstractions;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class OverlayHeaderTimeFormatterTests
{
    [Fact]
    public void FormatTimeRemaining_HidesUnlimitedClockForLapLimitedRace()
    {
        var snapshot = RaceSnapshot(
            sessionState: 4,
            sessionTimeRemainSeconds: 604_800d,
            sessionTimeTotalSeconds: 604_800d,
            sessionLapsRemain: 3,
            sessionLapsTotal: 3);

        Assert.Equal(string.Empty, OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot));
        Assert.Equal(string.Empty, OverlayHeaderTimeFormatter.FormatCompactTimeRemaining(snapshot));
    }

    [Fact]
    public void FormatTimeRemaining_HidesReportedClockForLapLimitedRace()
    {
        var snapshot = RaceSnapshot(
            sessionState: 4,
            sessionTimeRemainSeconds: 7_440d,
            sessionTimeTotalSeconds: 7_440d,
            sessionLapsRemain: 3,
            sessionLapsTotal: 3,
            sessionTimeLabel: null);

        Assert.Equal(string.Empty, OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot));
        Assert.Equal(string.Empty, OverlayHeaderTimeFormatter.FormatCompactTimeRemaining(snapshot));
    }

    [Fact]
    public void FormatTimeRemaining_StillShowsRacePreGreenCountdownForLapLimitedRace()
    {
        var snapshot = RaceSnapshot(
            sessionState: 3,
            sessionTimeRemainSeconds: 238d,
            sessionTimeTotalSeconds: 604_800d,
            sessionLapsRemain: 3,
            sessionLapsTotal: 3);

        Assert.Equal("00:03:58", OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot));
        Assert.Equal("03:58", OverlayHeaderTimeFormatter.FormatCompactTimeRemaining(snapshot));
    }

    [Fact]
    public void FormatTimeRemaining_HidesLongRaceClockDuringLapLimitedPreGreen()
    {
        var snapshot = RaceSnapshot(
            sessionState: 3,
            sessionTimeRemainSeconds: 7_440d,
            sessionTimeTotalSeconds: 7_440d,
            sessionLapsRemain: 3,
            sessionLapsTotal: 3);

        Assert.Equal(string.Empty, OverlayHeaderTimeFormatter.FormatTimeRemaining(snapshot));
        Assert.Equal(string.Empty, OverlayHeaderTimeFormatter.FormatCompactTimeRemaining(snapshot));
    }

    private static LiveTelemetrySnapshot RaceSnapshot(
        int sessionState,
        double sessionTimeRemainSeconds,
        double sessionTimeTotalSeconds,
        int sessionLapsRemain,
        int sessionLapsTotal,
        string? sessionTimeLabel = "unlimited")
    {
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity(),
            Track = new HistoricalTrackIdentity(),
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                SessionName = "RACE",
                SessionTime = sessionTimeLabel,
                SessionLaps = sessionLapsTotal.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            Conditions = new HistoricalSessionInfoConditions()
        };

        return LiveTelemetrySnapshot.Empty with
        {
            Context = context,
            Combo = HistoricalComboIdentity.From(context),
            Models = LiveRaceModels.Empty with
            {
                Session = LiveSessionModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    SessionType = "Race",
                    SessionName = "RACE",
                    SessionState = sessionState,
                    SessionTimeRemainSeconds = sessionTimeRemainSeconds,
                    SessionTimeTotalSeconds = sessionTimeTotalSeconds,
                    SessionLapsRemain = sessionLapsRemain,
                    SessionLapsTotal = sessionLapsTotal
                }
            }
        };
    }
}
