using TmrOverlay.Core.History;
using Xunit;

namespace TmrOverlay.App.Tests.History;

public sealed class SessionInfoSummaryParserTests
{
    [Fact]
    public void Parse_SelectsCurrentSessionResultsPositions()
    {
        var context = SessionInfoSummaryParser.Parse("""
SessionInfo:
 CurrentSessionNum: 1
 Sessions:
 - SessionNum: 0
   SessionType: Practice
   ResultsPositions:
   - Position: 1
     ClassPosition: 0
     CarIdx: 40
     LapsComplete: 4
 - SessionNum: 1
   SessionType: Race
   SessionName: RACE
   ResultsPositions:
   - Position: 1
     ClassPosition: 0
     CarIdx: 10
     Lap: 7
     Time: 0.0000
     FastestLap: 4
     FastestTime: 90.1234
     LastTime: 91.2345
     LapsLed: 2
     LapsComplete: 7
     LapsDriven: 7.420
     ReasonOutStr: Running
   - Position: 2
     ClassPosition: 1
     CarIdx: 12
     Lap: 7
     Time: 4.5000
     FastestLap: 3
     FastestTime: 90.9000
     LastTime: 92.0000
     LapsLed: 0
     LapsComplete: 7
     LapsDriven: 7.100
     ReasonOutStr: Running
DriverInfo:
 DriverCarIdx: 10
 Drivers:
 - CarIdx: 10
   UserName: Driver One
   CarClassID: 4098
   CarClassShortName: GT3
   CarClassRelSpeed: 50
   CarClassEstLapTime: 90.1234
   IRating: 2875
 - CarIdx: 12
   UserName: Driver Two
""");

        var driver = Assert.Single(context.Drivers, driver => driver.CarIdx == 10);
        Assert.Equal(4098, driver.CarClassId);
        Assert.Equal("GT3", driver.CarClassShortName);
        Assert.Equal(50, driver.CarClassRelSpeed);
        Assert.Equal(90.1234d, driver.CarClassEstLapTimeSeconds);
        Assert.Equal(2875, driver.IRating);
        Assert.Equal("Race", context.Session.SessionType);
        Assert.Equal("RACE", context.Session.SessionName);
        Assert.Collection(
            context.ResultPositions,
            row =>
            {
                Assert.Equal(1, row.Position);
                Assert.Equal(0, row.ClassPosition);
                Assert.Equal(10, row.CarIdx);
                Assert.Equal(7, row.LapsComplete);
                Assert.Equal(90.1234d, row.FastestTimeSeconds!.Value);
            },
            row =>
            {
                Assert.Equal(2, row.Position);
                Assert.Equal(1, row.ClassPosition);
                Assert.Equal(12, row.CarIdx);
                Assert.Equal(4.5d, row.TimeSeconds!.Value);
            });
    }

    [Fact]
    public void Parse_ReadsDriverTireCompoundDefinitions()
    {
        var context = SessionInfoSummaryParser.Parse("""
DriverInfo:
 DriverCarIdx: 10
 DriverTires:
 - TireIndex: 0
   TireCompoundType: "Hard"
 - TireIndex: 1
   TireCompoundType: "Wet"
 Drivers:
 - CarIdx: 10
   UserName: Driver One
""");

        Assert.Collection(
            context.TireCompounds,
            tire =>
            {
                Assert.Equal(0, tire.TireIndex);
                Assert.Equal("Hard", tire.TireCompoundType);
            },
            tire =>
            {
                Assert.Equal(1, tire.TireIndex);
                Assert.Equal("Wet", tire.TireCompoundType);
            });
    }
}
