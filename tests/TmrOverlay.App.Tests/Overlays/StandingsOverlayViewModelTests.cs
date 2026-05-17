using TmrOverlay.App.Overlays.Standings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class StandingsOverlayViewModelTests
{
    [Fact]
    public void From_WhenTelemetryIsUnavailable_ShowsWaitingState()
    {
        var viewModel = StandingsOverlayViewModel.From(
            LiveTelemetrySnapshot.Empty,
            DateTimeOffset.UtcNow);

        Assert.Equal("waiting for iRacing", viewModel.Status);
        Assert.Equal("source: waiting", viewModel.Source);
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public void From_WhenFocusIsUnavailable_DoesNotFallBackToPlayerRow()
    {
        var now = DateTimeOffset.UtcNow;
        var player = TimingRow(
            carIdx: 10,
            driverName: "Player Driver",
            carNumber: "10",
            classPosition: 3,
            gapSeconds: 12.4d,
            deltaSeconds: 0d,
            isFocus: false);
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Timing = LiveTimingModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = 10,
                PlayerRow = player,
                ClassRows = [player]
            },
            DriverDirectory = LiveDriverDirectoryModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Partial,
                PlayerCarIdx = 10
            }
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now);

        Assert.Equal("waiting for focus car", viewModel.Status);
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public void From_FormatsClassTimingRows()
    {
        var now = DateTimeOffset.UtcNow;
        var reference = TimingRow(
            carIdx: 10,
            driverName: "Reference Driver",
            carNumber: "44",
            classPosition: 3,
            gapSeconds: 12.4d,
            deltaSeconds: 12.4d,
            isFocus: true,
            onPitRoad: true);
        var leader = TimingRow(
            carIdx: 1,
            driverName: "Class Leader",
            carNumber: "1",
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: null,
            isLeader: true);
        var trailing = TimingRow(
            carIdx: 12,
            driverName: "Trailing Driver",
            carNumber: "12",
            classPosition: 4,
            gapSeconds: 18.2d,
            deltaSeconds: 5.8d);

        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    FocusCarIdx = 10,
                    FocusRow = reference,
                    ClassRows = [trailing, reference, leader]
                }
            }
        };

        var viewModel = StandingsOverlayViewModel.From(snapshot, now);

        Assert.Equal("P3 | 3 shown", viewModel.Status);
        Assert.Equal("source: live timing telemetry", viewModel.Source);
        Assert.Collection(
            viewModel.Rows,
            row =>
            {
                Assert.True(row.IsLeader);
                Assert.Equal("1", row.ClassPosition);
                Assert.Equal("Leader", row.Gap);
                Assert.Equal("0.0", row.Interval);
            },
            row =>
            {
                Assert.True(row.IsReference);
                Assert.Equal("#44", row.CarNumber);
                Assert.Equal("+12.4", row.Interval);
                Assert.Equal("IN", row.Pit);
            },
            row =>
            {
                Assert.Equal("Trailing Driver", row.Driver);
                Assert.Equal("+18.2", row.Gap);
                Assert.Equal("+5.8", row.Interval);
            });
    }

    [Fact]
    public void From_DoesNotShowLapDistanceFallbackForStandingsGap()
    {
        var now = DateTimeOffset.UtcNow;
        var leader = TimingRow(
            carIdx: 11,
            driverName: "Class Leader",
            carNumber: "11",
            classPosition: 1,
            gapSeconds: null,
            deltaSeconds: null,
            isLeader: true);
        var reference = TimingRow(
            carIdx: 10,
            driverName: "Reference Driver",
            carNumber: "10",
            classPosition: 2,
            gapSeconds: null,
            deltaSeconds: null,
            gapLaps: 0.03d,
            isFocus: true);
        var chase = TimingRow(
            carIdx: 12,
            driverName: "Chase Driver",
            carNumber: "12",
            classPosition: 3,
            gapSeconds: null,
            deltaSeconds: null,
            gapLaps: 0.05d);
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Timing = LiveTimingModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                FocusCarIdx = 10,
                FocusRow = reference,
                ClassRows = [leader, reference, chase]
            }
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now, maximumRows: 3);

        Assert.Equal(new[] { "Leader", "--", "--" }, viewModel.Rows.Select(row => row.Gap));
        Assert.Equal(new[] { "0.0", "--", "--" }, viewModel.Rows.Select(row => row.Interval));
    }

    [Fact]
    public void From_DoesNotShowAnonymousTimingRowsInSoloSessions()
    {
        var now = DateTimeOffset.UtcNow;
        var reference = TimingRow(
            carIdx: 10,
            driverName: "Reference Driver",
            carNumber: "44",
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: 0d,
            isFocus: true,
            isLeader: true);
        var anonymousRow = TimingRow(
            carIdx: 63,
            driverName: string.Empty,
            carNumber: string.Empty,
            classPosition: 2,
            gapSeconds: 8.2d,
            deltaSeconds: 8.2d);

        var snapshot = LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = LiveRaceModels.Empty with
            {
                Timing = LiveTimingModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Inferred,
                    FocusCarIdx = 10,
                    FocusRow = reference,
                    ClassRows = [reference, anonymousRow]
                }
            }
        };

        var viewModel = StandingsOverlayViewModel.From(snapshot, now);

        var row = Assert.Single(viewModel.Rows);
        Assert.Equal("#44", row.CarNumber);
        Assert.Equal("Reference Driver", row.Driver);
    }

    [Fact]
    public void From_UsesScoringRowsWhenLiveTimingIsPartial()
    {
        var now = DateTimeOffset.UtcNow;
        var scoringRows = new[]
        {
            ScoringRow(1, overallPosition: 1, classPosition: 1, carNumber: "1", driverName: "Leader"),
            ScoringRow(2, overallPosition: 2, classPosition: 2, carNumber: "2", driverName: "Missing Live"),
            ScoringRow(3, overallPosition: 3, classPosition: 3, carNumber: "3", driverName: "Live Row", isFocus: true)
        };
        var misleadingLiveRow = TimingRow(
            carIdx: 3,
            driverName: "Live Row",
            carNumber: "3",
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: 0d,
            isLeader: true,
            isFocus: true);
        var extraLiveRow = TimingRow(
            carIdx: 99,
            driverName: "Rendered Only",
            carNumber: "99",
            classPosition: 2,
            gapSeconds: 4d,
            deltaSeconds: 4d);
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Coverage = new LiveCoverageModel(
                RosterCount: 3,
                ResultRowCount: 3,
                LiveScoringRowCount: 1,
                LiveTimingRowCount: 1,
                LiveSpatialRowCount: 1,
                LiveProximityRowCount: 1),
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 3,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: scoringRows.Length,
                        Rows: scoringRows)
                ],
                Rows: scoringRows),
            Timing = LiveTimingModel.Empty with
            {
                HasData = true,
                FocusCarIdx = 3,
                FocusRow = misleadingLiveRow,
                ClassRows = [extraLiveRow, misleadingLiveRow]
            }
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now, maximumRows: 3);

        Assert.Equal("P3 | 3/3 shown", viewModel.Status);
        Assert.Equal("source: scoring snapshot (partial live)", viewModel.Source);
        Assert.Collection(
            viewModel.Rows,
            row =>
            {
                Assert.Equal("#1", row.CarNumber);
                Assert.Equal("Leader", row.Driver);
                Assert.Equal("1", row.ClassPosition);
            },
            row =>
            {
                Assert.Equal("#2", row.CarNumber);
                Assert.True(row.IsPartial);
            },
            row =>
            {
                Assert.Equal("#3", row.CarNumber);
                Assert.True(row.IsReference);
                Assert.Equal("3", row.ClassPosition);
            });
    }

    [Fact]
    public void From_KeepsOfficialMulticlassGroupOrderWithConfiguredOtherClassRows()
    {
        var now = DateTimeOffset.UtcNow;
        var gt3Rows = new[]
        {
            ScoringRow(10, overallPosition: 2, classPosition: 1, carNumber: "10", driverName: "Reference", carClass: 4098, className: "GT3", isFocus: true),
            ScoringRow(11, overallPosition: 4, classPosition: 2, carNumber: "11", driverName: "GT3 Chase", carClass: 4098, className: "GT3")
        };
        var prototypeRows = new[]
        {
            ScoringRow(21, overallPosition: 1, classPosition: 1, carNumber: "21", driverName: "Proto Leader", carClass: 4000, className: "GTP"),
            ScoringRow(22, overallPosition: 3, classPosition: 2, carNumber: "22", driverName: "Proto Chase", carClass: 4000, className: "GTP")
        };
        var allRows = prototypeRows.Concat(gt3Rows)
            .OrderBy(row => row.OverallPosition)
            .ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Coverage = new LiveCoverageModel(
                RosterCount: 4,
                ResultRowCount: 4,
                LiveScoringRowCount: 4,
                LiveTimingRowCount: 4,
                LiveSpatialRowCount: 4,
                LiveProximityRowCount: 4),
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 10,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: gt3Rows.Length,
                        Rows: gt3Rows),
                    new LiveScoringClassGroup(
                        CarClass: 4000,
                        ClassName: "GTP",
                        CarClassColorHex: "#33CEFF",
                        IsReferenceClass: false,
                        RowCount: prototypeRows.Length,
                        Rows: prototypeRows)
                ],
                Rows: allRows)
        });

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: 5,
            otherClassRowsPerClass: 1);

        Assert.Collection(
            viewModel.Rows,
            row =>
            {
                Assert.True(row.IsClassHeader);
                Assert.Equal("GTP", row.Driver);
            },
            row => Assert.Equal("#21", row.CarNumber),
            row =>
            {
                Assert.True(row.IsClassHeader);
                Assert.Equal("GT3", row.Driver);
            },
            row => Assert.Equal("#10", row.CarNumber),
            row => Assert.Equal("#11", row.CarNumber));
    }

    [Fact]
    public void From_UsesLeaderLapProgressInGapColumnWhenAvailable()
    {
        var now = DateTimeOffset.UtcNow;
        var scoringRows = new[]
        {
            ScoringRow(1, overallPosition: 1, classPosition: 1, carNumber: "1", driverName: "Leader"),
            ScoringRow(2, overallPosition: 2, classPosition: 2, carNumber: "2", driverName: "Chase", isFocus: true)
        };
        var leader = TimingRow(
            carIdx: 1,
            driverName: "Leader",
            carNumber: "1",
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: null,
            isLeader: true,
            lapCompleted: 12,
            lapDistPct: 0.35d);
        var chase = TimingRow(
            carIdx: 2,
            driverName: "Chase",
            carNumber: "2",
            classPosition: 2,
            gapSeconds: 4d,
            deltaSeconds: 4d,
            isFocus: true);
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 2,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: scoringRows.Length,
                        Rows: scoringRows)
                ],
                Rows: scoringRows),
            Timing = LiveTimingModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                FocusCarIdx = 2,
                FocusRow = chase,
                OverallRows = [leader, chase],
                ClassRows = [leader, chase]
            }
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now, maximumRows: 2);

        Assert.Equal(new[] { "Lap 13", "+4.0" }, viewModel.Rows.Select(row => row.Gap));
    }

    [Fact]
    public void From_UsesClassLeaderWhenOtherClassLimitIsOne()
    {
        var now = DateTimeOffset.UtcNow;
        var referenceClassRows = new[]
        {
            ScoringRow(10, overallPosition: 4, classPosition: 1, carNumber: "10", driverName: "Reference", carClass: 4098, className: "GT3", isFocus: true),
            ScoringRow(11, overallPosition: 6, classPosition: 2, carNumber: "11", driverName: "GT3 Chase", carClass: 4098, className: "GT3")
        };
        var unsortedPrototypeRows = new[]
        {
            ScoringRow(22, overallPosition: 3, classPosition: 2, carNumber: "22", driverName: "Proto Chase", carClass: 4000, className: "GTP"),
            ScoringRow(21, overallPosition: 1, classPosition: 1, carNumber: "21", driverName: "Proto Leader", carClass: 4000, className: "GTP"),
            ScoringRow(23, overallPosition: 5, classPosition: 3, carNumber: "23", driverName: "Proto Third", carClass: 4000, className: "GTP")
        };
        var allRows = unsortedPrototypeRows.Concat(referenceClassRows)
            .OrderBy(row => row.OverallPosition)
            .ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 10,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4000,
                        ClassName: "GTP",
                        CarClassColorHex: "#33CEFF",
                        IsReferenceClass: false,
                        RowCount: unsortedPrototypeRows.Length,
                        Rows: unsortedPrototypeRows),
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: referenceClassRows.Length,
                        Rows: referenceClassRows)
                ],
                Rows: allRows)
        });

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: 5,
            otherClassRowsPerClass: 1);

        Assert.Contains(viewModel.Rows, row => row.IsClassHeader && row.Driver == "GTP");
        Assert.Contains(viewModel.Rows, row => row.CarNumber == "#21" && row.Driver == "Proto Leader");
        Assert.DoesNotContain(viewModel.Rows, row => row.CarNumber == "#22" || row.CarNumber == "#23");
    }

    [Fact]
    public void From_DoesNotLetClassHeadersExpandPrimaryClassBeyondMaximumRows()
    {
        var now = DateTimeOffset.UtcNow;
        var lmp2Rows = Enumerable.Range(0, 15)
            .Select(index => ScoringRow(
                100 + index,
                overallPosition: 3 + index,
                classPosition: index + 1,
                carNumber: $"L{index + 1}",
                driverName: $"LMP2 Driver {index + 1}",
                carClass: 4100,
                className: "LMP2",
                isFocus: index == 14))
            .ToArray();
        var prototypeRows = new[]
        {
            ScoringRow(21, overallPosition: 1, classPosition: 1, carNumber: "P1", driverName: "Proto Leader", carClass: 4000, className: "GTP"),
            ScoringRow(22, overallPosition: 2, classPosition: 2, carNumber: "P2", driverName: "Proto Chase", carClass: 4000, className: "GTP")
        };
        var gt3Rows = new[]
        {
            ScoringRow(31, overallPosition: 18, classPosition: 1, carNumber: "G1", driverName: "GT3 Leader", carClass: 4098, className: "GT3"),
            ScoringRow(32, overallPosition: 19, classPosition: 2, carNumber: "G2", driverName: "GT3 Chase", carClass: 4098, className: "GT3")
        };
        var allRows = prototypeRows.Concat(lmp2Rows).Concat(gt3Rows)
            .OrderBy(row => row.OverallPosition)
            .ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 114,
                ReferenceCarClass: 4100,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4000,
                        ClassName: "GTP",
                        CarClassColorHex: "#33CEFF",
                        IsReferenceClass: false,
                        RowCount: prototypeRows.Length,
                        Rows: prototypeRows),
                    new LiveScoringClassGroup(
                        CarClass: 4100,
                        ClassName: "LMP2",
                        CarClassColorHex: "#FFFFFF",
                        IsReferenceClass: true,
                        RowCount: lmp2Rows.Length,
                        Rows: lmp2Rows),
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: false,
                        RowCount: gt3Rows.Length,
                        Rows: gt3Rows)
                ],
                Rows: allRows)
        });

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: 14,
            otherClassRowsPerClass: 2);

        var lmp2CarRows = viewModel.Rows
            .Where(row => !row.IsClassHeader && row.Driver.StartsWith("LMP2 Driver ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(14, lmp2CarRows.Length);
        Assert.Contains(lmp2CarRows, row => row.IsLeader && row.Driver == "LMP2 Driver 1");
        Assert.Contains(lmp2CarRows, row => row.IsReference && row.Driver == "LMP2 Driver 15");
        Assert.DoesNotContain(lmp2CarRows, row => row.Driver == "LMP2 Driver 2");
        Assert.Equal("P15 | 18/19 shown", viewModel.Status);
    }

    [Fact]
    public void From_HidesOtherClassSectionsWhenConfiguredOtherClassRowsIsZero()
    {
        var now = DateTimeOffset.UtcNow;
        var referenceClassRows = new[]
        {
            ScoringRow(10, overallPosition: 2, classPosition: 1, carNumber: "10", driverName: "Reference", carClass: 4098, className: "GT3", isFocus: true),
            ScoringRow(11, overallPosition: 4, classPosition: 2, carNumber: "11", driverName: "GT3 Chase", carClass: 4098, className: "GT3")
        };
        var otherClassRows = new[]
        {
            ScoringRow(21, overallPosition: 1, classPosition: 1, carNumber: "21", driverName: "Proto Leader", carClass: 4000, className: "GTP"),
            ScoringRow(22, overallPosition: 3, classPosition: 2, carNumber: "22", driverName: "Proto Chase", carClass: 4000, className: "GTP")
        };
        var allRows = referenceClassRows.Concat(otherClassRows)
            .OrderBy(row => row.OverallPosition)
            .ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 10,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4000,
                        ClassName: "GTP",
                        CarClassColorHex: "#33CEFF",
                        IsReferenceClass: false,
                        RowCount: otherClassRows.Length,
                        Rows: otherClassRows),
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: referenceClassRows.Length,
                        Rows: referenceClassRows)
                ],
                Rows: allRows)
        });

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: 5,
            otherClassRowsPerClass: 0);

        Assert.DoesNotContain(viewModel.Rows, row => row.IsClassHeader);
        Assert.Equal(new[] { "#10", "#11" }, viewModel.Rows.Select(row => row.CarNumber));
    }

    [Fact]
    public void From_ExpandsScoringRowsToShowAllClassHeadersAndConfiguredOtherClassRows()
    {
        var now = DateTimeOffset.UtcNow;
        var classGroups = new[]
        {
            ScoringGroup(5000, "GT3", 10, firstOverallPosition: 1, isReferenceClass: true),
            ScoringGroup(5001, "Cup", 20, firstOverallPosition: 4),
            ScoringGroup(5002, "GT4", 30, firstOverallPosition: 7),
            ScoringGroup(5003, "TCR", 40, firstOverallPosition: 10),
            ScoringGroup(5004, "M2", 50, firstOverallPosition: 13)
        };
        var allRows = classGroups
            .SelectMany(group => group.Rows)
            .OrderBy(row => row.OverallPosition)
            .ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 10,
                ReferenceCarClass: 5000,
                ClassGroups: classGroups,
                Rows: allRows)
        });

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: 8,
            otherClassRowsPerClass: 1);

        Assert.Equal(12, viewModel.Rows.Count);
        Assert.Equal(
            new[] { "GT3", "Cup", "GT4", "TCR", "M2" },
            viewModel.Rows.Where(row => row.IsClassHeader).Select(row => row.Driver));
        Assert.Equal(7, viewModel.Rows.Count(row => !row.IsClassHeader));
        Assert.Contains(viewModel.Rows, row => row.CarNumber == "#11");
        Assert.Contains(viewModel.Rows, row => row.CarNumber == "#12");
        Assert.True(viewModel.Rows.Single(row => row.CarNumber == "#10").IsReference);
    }

    [Fact]
    public void From_OrdersClassHeadersByIracingOverallOrderNotReferenceClassFirst()
    {
        var now = DateTimeOffset.UtcNow;
        var referenceGt3 = ScoringGroup(5000, "GT3", 10, firstOverallPosition: 4, isReferenceClass: true);
        var leadingPrototype = ScoringGroup(5001, "GTP", 20, firstOverallPosition: 1);
        var allRows = referenceGt3.Rows.Concat(leadingPrototype.Rows)
            .OrderBy(row => row.OverallPosition)
            .ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 10,
                ReferenceCarClass: 5000,
                ClassGroups: [referenceGt3, leadingPrototype],
                Rows: allRows)
        });

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: 8,
            otherClassRowsPerClass: 1);

        Assert.Equal(
            new[] { "GTP", "GT3" },
            viewModel.Rows.Where(row => row.IsClassHeader).Select(row => row.Driver));
        Assert.True(viewModel.Rows.Single(row => row.CarNumber == "#10").IsReference);
    }

    [Fact]
    public void From_UsesCurrentReferenceCarClassForPrimaryMulticlassWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var oldFocusClassRows = new[]
        {
            ScoringRow(10, overallPosition: 2, classPosition: 1, carNumber: "10", driverName: "Old Class Leader", carClass: 4098, className: "GT3"),
            ScoringRow(11, overallPosition: 4, classPosition: 2, carNumber: "11", driverName: "Old Class Chase", carClass: 4098, className: "GT3"),
            ScoringRow(12, overallPosition: 6, classPosition: 3, carNumber: "12", driverName: "Old Class Third", carClass: 4098, className: "GT3")
        };
        var currentFocusClassRows = new[]
        {
            ScoringRow(21, overallPosition: 1, classPosition: 1, carNumber: "21", driverName: "Current Leader", carClass: 4000, className: "GTP", isFocus: true),
            ScoringRow(22, overallPosition: 3, classPosition: 2, carNumber: "22", driverName: "Current Chase", carClass: 4000, className: "GTP"),
            ScoringRow(23, overallPosition: 5, classPosition: 3, carNumber: "23", driverName: "Current Third", carClass: 4000, className: "GTP")
        };
        var allRows = currentFocusClassRows.Concat(oldFocusClassRows)
            .OrderBy(row => row.OverallPosition)
            .ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 21,
                ReferenceCarClass: 4000,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: oldFocusClassRows.Length,
                        Rows: oldFocusClassRows),
                    new LiveScoringClassGroup(
                        CarClass: 4000,
                        ClassName: "GTP",
                        CarClassColorHex: "#33CEFF",
                        IsReferenceClass: false,
                        RowCount: currentFocusClassRows.Length,
                        Rows: currentFocusClassRows)
                ],
                Rows: allRows)
        });

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: 7,
            otherClassRowsPerClass: 1);

        Assert.Equal(3, viewModel.Rows.Count(row => !row.IsClassHeader && row.CarClassColorHex == "#33CEFF"));
        Assert.Equal(1, viewModel.Rows.Count(row => !row.IsClassHeader && row.CarClassColorHex == "#FFDA59"));
        Assert.True(viewModel.Rows.Single(row => row.CarNumber == "#21").IsReference);
        Assert.DoesNotContain(viewModel.Rows, row => row.CarNumber == "#11" || row.CarNumber == "#12");
    }

    [Fact]
    public void From_WindowScoringRowsAroundFocusedCar()
    {
        var now = DateTimeOffset.UtcNow;
        var scoringRows = Enumerable.Range(1, 10)
            .Select(position => ScoringRow(
                carIdx: position,
                overallPosition: position,
                classPosition: position,
                carNumber: $"{position}",
                driverName: $"Driver {position}",
                isFocus: position == 7))
            .ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 7,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: scoringRows.Length,
                        Rows: scoringRows)
                ],
                Rows: scoringRows)
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now, maximumRows: 5);

        Assert.Equal(new[] { "#1", "#5", "#6", "#7", "#8" }, viewModel.Rows.Select(row => row.CarNumber));
        Assert.True(viewModel.Rows.Single(row => row.CarNumber == "#1").IsLeader);
        Assert.True(viewModel.Rows.Single(row => row.CarNumber == "#7").IsReference);
        Assert.DoesNotContain(viewModel.Rows, row => row.CarNumber == "#9");
    }

    [Fact]
    public void From_RaceScoringRowsDoNotRequireValidLaps()
    {
        var now = DateTimeOffset.UtcNow;
        var scoringRows = new[]
        {
            ScoringRow(1, overallPosition: 1, classPosition: 1, carNumber: "1", driverName: "Race Leader", isFocus: true),
            ScoringRow(2, overallPosition: 2, classPosition: 2, carNumber: "2", driverName: "Race Chase")
        };
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race"
            },
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 1,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: scoringRows.Length,
                        Rows: scoringRows)
                ],
                Rows: scoringRows)
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now);

        Assert.Equal(new[] { "#1", "#2" }, viewModel.Rows.Select(row => row.CarNumber));
    }

    [Fact]
    public void From_RaceLapColumnsHighlightClassFastestAndRecentCarBest()
    {
        var now = DateTimeOffset.UtcNow;
        var scoringRows = new[]
        {
            ScoringRow(1, overallPosition: 1, classPosition: 1, carNumber: "1", driverName: "Class Fastest", bestLapTimeSeconds: 82.123d, lastLapTimeSeconds: 83.4d),
            ScoringRow(2, overallPosition: 2, classPosition: 2, carNumber: "2", driverName: "Recent Team Best", isFocus: true, bestLapTimeSeconds: 83.456d, lastLapTimeSeconds: 83.456d),
            ScoringRow(3, overallPosition: 3, classPosition: 3, carNumber: "3", driverName: "Normal", bestLapTimeSeconds: 84.1d, lastLapTimeSeconds: 84.7d)
        };
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race"
            },
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 2,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: scoringRows.Length,
                        Rows: scoringRows)
                ],
                Rows: scoringRows)
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now, maximumRows: 3);

        var classFastest = viewModel.Rows.Single(row => row.CarNumber == "#1");
        Assert.Equal("1:22.123", classFastest.FastestLap);
        Assert.True(classFastest.IsClassFastestLap);
        Assert.False(classFastest.IsRecentCarBestLap);

        var recentTeamBest = viewModel.Rows.Single(row => row.CarNumber == "#2");
        Assert.Equal("1:23.456", recentTeamBest.FastestLap);
        Assert.Equal("1:23.456", recentTeamBest.LastLap);
        Assert.False(recentTeamBest.IsClassFastestLap);
        Assert.True(recentTeamBest.IsRecentCarBestLap);
        Assert.True(recentTeamBest.IsRecentCarBestLastLap);
    }

    [Fact]
    public void From_PracticeLapColumnsDoNotUseRaceOnlyClassFastestHighlight()
    {
        var now = DateTimeOffset.UtcNow;
        var scoringRows = new[]
        {
            ScoringRow(1, overallPosition: 1, classPosition: 1, carNumber: "1", driverName: "Practice Fastest", isFocus: true, bestLapTimeSeconds: 82.123d, lastLapTimeSeconds: 82.123d),
            ScoringRow(2, overallPosition: 2, classPosition: 2, carNumber: "2", driverName: "Practice Chase", bestLapTimeSeconds: 83.456d, lastLapTimeSeconds: 83.9d)
        };
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Practice"
            },
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 1,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: scoringRows.Length,
                        Rows: scoringRows)
                ],
                Rows: scoringRows)
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now, maximumRows: 2);

        var row = viewModel.Rows.Single(row => row.CarNumber == "#1");
        Assert.Equal("1:22.123", row.FastestLap);
        Assert.Equal("1:22.123", row.LastLap);
        Assert.False(row.IsClassFastestLap);
        Assert.False(row.IsClassFastestLastLap);
        Assert.False(row.IsRecentCarBestLap);
        Assert.False(row.IsRecentCarBestLastLap);
    }

    [Fact]
    public void From_RaceStartingGridMarksCarsThatHaveNotTakenGrid()
    {
        var now = DateTimeOffset.UtcNow;
        var scoringRows = new[]
        {
            ScoringRow(1, overallPosition: 1, classPosition: 1, carNumber: "1", driverName: "Gridded", isFocus: true, hasTakenGrid: true),
            ScoringRow(2, overallPosition: 2, classPosition: 2, carNumber: "2", driverName: "Not Gridded", hasTakenGrid: false)
        };
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race",
                SessionState = 1
            },
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.StartingGrid,
                ReferenceCarIdx: 1,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: scoringRows.Length,
                        Rows: scoringRows)
                ],
                Rows: scoringRows)
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now);

        Assert.False(viewModel.Rows.Single(row => row.CarNumber == "#1").IsPendingGrid);
        Assert.True(viewModel.Rows.Single(row => row.CarNumber == "#2").IsPendingGrid);
    }

    [Fact]
    public void From_NonRaceScoringRowsRequireValidLapsAndHideEmptyClassHeaders()
    {
        var now = DateTimeOffset.UtcNow;
        var gt3Rows = new[]
        {
            ScoringRow(10, overallPosition: 1, classPosition: 1, carNumber: "10", driverName: "Valid Focus", isFocus: true, bestLapTimeSeconds: 82.4d),
            ScoringRow(11, overallPosition: 2, classPosition: 2, carNumber: "11", driverName: "No Lap")
        };
        var gtpRows = new[]
        {
            ScoringRow(21, overallPosition: 3, classPosition: 1, carNumber: "21", driverName: "No Proto Lap", carClass: 4000, className: "GTP")
        };
        var allRows = gt3Rows.Concat(gtpRows).ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Practice"
            },
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 10,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: gt3Rows.Length,
                        Rows: gt3Rows),
                    new LiveScoringClassGroup(
                        CarClass: 4000,
                        ClassName: "GTP",
                        CarClassColorHex: "#33CEFF",
                        IsReferenceClass: false,
                        RowCount: gtpRows.Length,
                        Rows: gtpRows)
                ],
                Rows: allRows)
        });

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: 5,
            otherClassRowsPerClass: 1);

        var row = Assert.Single(viewModel.Rows);
        Assert.False(row.IsClassHeader);
        Assert.Equal("#10", row.CarNumber);
        Assert.Equal("P1 | 1/1 shown", viewModel.Status);
    }

    [Fact]
    public void From_NonRaceScoringRowsShowWaitingWhenNoValidLapsExist()
    {
        var now = DateTimeOffset.UtcNow;
        var scoringRows = new[]
        {
            ScoringRow(10, overallPosition: 1, classPosition: 1, carNumber: "10", driverName: "No Lap Focus", isFocus: true),
            ScoringRow(11, overallPosition: 2, classPosition: 2, carNumber: "11", driverName: "No Lap Chase")
        };
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Offline Testing"
            },
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 10,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: scoringRows.Length,
                        Rows: scoringRows)
                ],
                Rows: scoringRows)
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now);

        Assert.Equal("waiting for valid laps", viewModel.Status);
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public void From_NonRaceTimingFallbackRequiresValidLaps()
    {
        var now = DateTimeOffset.UtcNow;
        var focus = TimingRow(
            carIdx: 10,
            driverName: "Valid Focus",
            carNumber: "10",
            classPosition: 2,
            gapSeconds: 4.2d,
            deltaSeconds: 0d,
            isFocus: true,
            bestLapTimeSeconds: 82.4d);
        var noLap = TimingRow(
            carIdx: 11,
            driverName: "No Lap",
            carNumber: "11",
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: -4.2d,
            isLeader: true);
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Qualifying"
            },
            Timing = LiveTimingModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                FocusCarIdx = 10,
                FocusRow = focus,
                ClassRows = [noLap, focus]
            }
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now);

        var row = Assert.Single(viewModel.Rows);
        Assert.Equal("#10", row.CarNumber);
        Assert.True(row.IsReference);
    }

    [Fact]
    public void From_DoesNotRenderOtherClassSectionsWhenClassSeparatorsAreDisabled()
    {
        var now = DateTimeOffset.UtcNow;
        var gt3Rows = new[]
        {
            ScoringRow(10, overallPosition: 2, classPosition: 1, carNumber: "10", driverName: "Reference", isFocus: true),
            ScoringRow(11, overallPosition: 4, classPosition: 2, carNumber: "11", driverName: "GT3 Chase")
        };
        var prototypeRows = new[]
        {
            ScoringRow(21, overallPosition: 1, classPosition: 1, carNumber: "21", driverName: "Proto Leader", carClass: 4000, className: "GTP"),
            ScoringRow(22, overallPosition: 3, classPosition: 2, carNumber: "22", driverName: "Proto Chase", carClass: 4000, className: "GTP")
        };
        var allRows = prototypeRows.Concat(gt3Rows)
            .OrderBy(row => row.OverallPosition)
            .ToArray();
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 10,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: gt3Rows.Length,
                        Rows: gt3Rows),
                    new LiveScoringClassGroup(
                        CarClass: 4000,
                        ClassName: "GTP",
                        CarClassColorHex: "#33CEFF",
                        IsReferenceClass: false,
                        RowCount: prototypeRows.Length,
                        Rows: prototypeRows)
                ],
                Rows: allRows)
        });

        var viewModel = StandingsOverlayViewModel.From(
            snapshot,
            now,
            maximumRows: 5,
            otherClassRowsPerClass: 2,
            showClassSeparators: false);

        Assert.Equal(new[] { "#10", "#11" }, viewModel.Rows.Select(row => row.CarNumber));
        Assert.DoesNotContain(viewModel.Rows, row => row.IsClassHeader);
    }

    [Fact]
    public void From_RaceGreenScoringRowsKeepScoringOrderWhileUsingLiveGapAndInterval()
    {
        var now = DateTimeOffset.UtcNow;
        var scoringRows = new[]
        {
            ScoringRow(1, overallPosition: 1, classPosition: 1, carNumber: "1", driverName: "Leader"),
            ScoringRow(2, overallPosition: 2, classPosition: 2, carNumber: "2", driverName: "Second On Scoring"),
            ScoringRow(3, overallPosition: 3, classPosition: 3, carNumber: "3", driverName: "Live Second", isFocus: true)
        };
        var leader = TimingRow(
            carIdx: 1,
            driverName: "Leader",
            carNumber: "1",
            classPosition: 1,
            gapSeconds: 0d,
            deltaSeconds: null,
            isLeader: true);
        var staleSecond = TimingRow(
            carIdx: 2,
            driverName: "Second On Scoring",
            carNumber: "2",
            classPosition: 2,
            gapSeconds: 4d,
            deltaSeconds: 4d);
        var liveSecond = TimingRow(
            carIdx: 3,
            driverName: "Live Second",
            carNumber: "3",
            classPosition: 3,
            gapSeconds: 1d,
            deltaSeconds: 1d,
            isFocus: true);
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race",
                SessionState = 4
            },
            Timing = LiveTimingModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                FocusCarIdx = 3,
                FocusRow = liveSecond,
                OverallRows = [leader, staleSecond, liveSecond],
                ClassRows = [leader, staleSecond, liveSecond]
            },
            Scoring = new LiveScoringModel(
                HasData: true,
                Quality: LiveModelQuality.Reliable,
                Source: LiveScoringSource.SessionResults,
                ReferenceCarIdx: 3,
                ReferenceCarClass: 4098,
                ClassGroups:
                [
                    new LiveScoringClassGroup(
                        CarClass: 4098,
                        ClassName: "GT3",
                        CarClassColorHex: "#FFDA59",
                        IsReferenceClass: true,
                        RowCount: scoringRows.Length,
                        Rows: scoringRows)
                ],
                Rows: scoringRows)
        });

        var viewModel = StandingsOverlayViewModel.From(snapshot, now, maximumRows: 3);

        Assert.Equal("P3 | 3/3 shown", viewModel.Status);
        Assert.Equal(new[] { "#1", "#2", "#3" }, viewModel.Rows.Select(row => row.CarNumber));
        Assert.Equal(new[] { "1", "2", "3" }, viewModel.Rows.Select(row => row.ClassPosition));
        Assert.Equal(new[] { "Leader", "+4.0", "+1.0" }, viewModel.Rows.Select(row => row.Gap));
        Assert.Equal(new[] { "0.0", "+4.0", "+1.0" }, viewModel.Rows.Select(row => row.Interval));
    }

    private static LiveTimingRow TimingRow(
        int carIdx,
        string driverName,
        string carNumber,
        int classPosition,
        double? gapSeconds,
        double? deltaSeconds,
        bool isLeader = false,
        bool isFocus = false,
        bool onPitRoad = false,
        double? gapLaps = null,
        double? bestLapTimeSeconds = null,
        double? lastLapTimeSeconds = null,
        int? lapCompleted = null,
        double? lapDistPct = null)
    {
        return new LiveTimingRow(
            CarIdx: carIdx,
            Quality: LiveModelQuality.Reliable,
            Source: "test",
            IsPlayer: isFocus,
            IsFocus: isFocus,
            IsOverallLeader: isLeader,
            IsClassLeader: isLeader,
            HasTiming: true,
            HasSpatialProgress: true,
            CanUseForRadarPlacement: true,
            TimingEvidence: LiveSignalEvidence.Reliable("test"),
            SpatialEvidence: LiveSignalEvidence.Reliable("test"),
            RadarPlacementEvidence: LiveSignalEvidence.Reliable("test"),
            GapEvidence: LiveSignalEvidence.Reliable("test"),
            DriverName: driverName,
            TeamName: null,
            CarNumber: carNumber,
            CarClassName: "GT3",
            CarClassColorHex: "#FFDA59",
            OverallPosition: classPosition + 2,
            ClassPosition: classPosition,
            CarClass: 4098,
            LapCompleted: lapCompleted,
            LapDistPct: lapDistPct,
            ProgressLaps: lapCompleted is { } completed && lapDistPct is { } progress
                ? completed + progress
                : null,
            F2TimeSeconds: null,
            EstimatedTimeSeconds: null,
            LastLapTimeSeconds: lastLapTimeSeconds,
            BestLapTimeSeconds: bestLapTimeSeconds,
            GapSecondsToClassLeader: gapSeconds,
            GapLapsToClassLeader: gapLaps,
            IntervalSecondsToPreviousClassRow: deltaSeconds,
            IntervalLapsToPreviousClassRow: null,
            DeltaSecondsToFocus: deltaSeconds,
            TrackSurface: null,
            OnPitRoad: onPitRoad);
    }

    private static LiveTelemetrySnapshot Snapshot(DateTimeOffset now, LiveRaceModels models)
    {
        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = models
        };
    }

    private static LiveScoringRow ScoringRow(
        int carIdx,
        int overallPosition,
        int classPosition,
        string carNumber,
        string driverName,
        int carClass = 4098,
        string className = "GT3",
        bool isFocus = false,
        double? bestLapTimeSeconds = null,
        double? lastLapTimeSeconds = null,
        bool hasTakenGrid = false)
    {
        return new LiveScoringRow(
            CarIdx: carIdx,
            OverallPositionRaw: overallPosition,
            ClassPositionRaw: classPosition - 1,
            OverallPosition: overallPosition,
            ClassPosition: classPosition,
            CarClass: carClass,
            DriverName: driverName,
            TeamName: null,
            CarNumber: carNumber,
            CarClassName: className,
            CarClassColorHex: carClass == 4098 ? "#FFDA59" : "#33CEFF",
            IsPlayer: isFocus,
            IsFocus: isFocus,
            IsReferenceClass: isFocus || carClass == 4098,
            Lap: null,
            LapsComplete: null,
            LastLapTimeSeconds: lastLapTimeSeconds,
            BestLapTimeSeconds: bestLapTimeSeconds,
            ReasonOut: null,
            HasTakenGrid: hasTakenGrid);
    }

    private static LiveScoringClassGroup ScoringGroup(
        int carClass,
        string className,
        int firstCarIdx,
        int firstOverallPosition,
        bool isReferenceClass = false)
    {
        var rows = Enumerable.Range(0, 3)
            .Select(index => ScoringRow(
                firstCarIdx + index,
                overallPosition: firstOverallPosition + index,
                classPosition: index + 1,
                carNumber: $"{firstCarIdx + index}",
                driverName: $"{className} Driver {index + 1}",
                carClass: carClass,
                className: className,
                isFocus: isReferenceClass && index == 0))
            .ToArray();
        return new LiveScoringClassGroup(
            CarClass: carClass,
            ClassName: className,
            CarClassColorHex: "#33CEFF",
            IsReferenceClass: isReferenceClass,
            RowCount: rows.Length,
            Rows: rows);
    }
}
