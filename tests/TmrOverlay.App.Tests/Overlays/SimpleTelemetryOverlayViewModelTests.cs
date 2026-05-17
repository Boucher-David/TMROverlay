using TmrOverlay.App.Overlays.CarRadar;
using TmrOverlay.App.Overlays.Flags;
using TmrOverlay.App.Overlays.Content;
using TmrOverlay.App.Overlays.InputState;
using TmrOverlay.App.Overlays.PitService;
using TmrOverlay.App.Overlays.SessionWeather;
using TmrOverlay.App.Overlays.SimpleTelemetry;
using TmrOverlay.Core.History;
using TmrOverlay.Core.Overlays;
using TmrOverlay.Core.Settings;
using TmrOverlay.Core.Telemetry.Live;
using Xunit;

namespace TmrOverlay.App.Tests.Overlays;

public sealed class SimpleTelemetryOverlayViewModelTests
{
    [Fact]
    public void Flags_FromTelemetry_LabelsActiveFlagState()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionState = 4,
                SessionFlags = 0x00000004,
                SessionTimeRemainSeconds = 125d,
                SessionLapsRemain = 12,
                RaceLaps = 40
            }
        });

        var viewModel = FlagsOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("green", viewModel.Status);
        Assert.Contains(viewModel.Rows, row => row.Label == "Flags" && row.Value == "green");
        Assert.Contains(viewModel.Rows, row => row.Label == "Raw" && row.Value == "0x00000004");
    }

    [Fact]
    public void Flags_FromTelemetry_LabelsRacePreGreenCountdown()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race",
                SessionState = 2,
                SessionFlags = 0,
                SessionTimeRemainSeconds = 92d
            }
        });

        var viewModel = FlagsOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("none", viewModel.Status);
        Assert.Contains(viewModel.Rows, row => row.Label == "State" && row.Value == "grid countdown (2)");
        Assert.Contains(viewModel.Rows, row => row.Label == "Countdown" && row.Value == "1:32");
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Time left");
    }

    [Fact]
    public void Flags_ForDisplay_IgnoresBackgroundOnlyFlags()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionState = 4,
                SessionFlags = 0x00040000 | 0x10000000
            }
        });

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        Assert.False(viewModel.HasDisplayFlags);
        Assert.Equal("none", viewModel.Status);
    }

    [Fact]
    public void Flags_ForDisplay_IgnoresSteadyGreenRunning()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionState = 4,
                SessionFlags = 0x00000004
            }
        });

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        Assert.False(viewModel.HasDisplayFlags);
        Assert.Equal("none", viewModel.Status);
    }

    [Fact]
    public void Flags_ForDisplay_ShowsExplicitGreenStartBits()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionState = 4,
                SessionFlags = unchecked((int)0x80000000)
            }
        });

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        Assert.Single(viewModel.Flags);
        Assert.Equal(FlagDisplayKind.Green, viewModel.Flags[0].Kind);
        Assert.Equal("Start", viewModel.Status);
    }

    [Fact]
    public void Flags_ForDisplay_ReturnsMultipleActiveDisplayFlags()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionState = 4,
                SessionFlags = 0x00100000 | 0x00010000 | 0x00000020 | 0x00000002
            }
        });

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        Assert.Equal(
            new[]
            {
                FlagDisplayKind.Meatball,
                FlagDisplayKind.Black,
                FlagDisplayKind.Blue,
                FlagDisplayKind.White
            },
            viewModel.Flags.Select(flag => flag.Kind).ToArray());
        Assert.Equal("Repair + Black + Blue + White", viewModel.Status);
    }

    [Fact]
    public void Flags_ForDisplay_DeduplicatesYellowFamily()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionState = 4,
                SessionFlags = 0x00000008 | 0x00000040 | 0x00004000
            }
        });

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        Assert.Single(viewModel.Flags);
        Assert.Equal(FlagDisplayKind.Caution, viewModel.Flags[0].Kind);
        Assert.Equal("Caution", viewModel.Status);
    }

    [Fact]
    public void Flags_ForDisplay_UsesCheckeredWhenSessionStateCompletes()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionState = 5,
                SessionFlags = 0
            }
        });

        var viewModel = FlagsOverlayViewModel.ForDisplay(snapshot, now);

        Assert.Single(viewModel.Flags);
        Assert.Equal(FlagDisplayKind.Checkered, viewModel.Flags[0].Kind);
        Assert.Equal("Checkered", viewModel.Status);
    }

    [Fact]
    public void SessionWeather_FromTelemetry_FormatsSessionAndWeatherRows()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race",
                SessionName = "Endurance",
                EventType = "Race",
                TeamRacing = true,
                SessionTimeSeconds = 60d,
                SessionTimeRemainSeconds = 3600d,
                SessionTimeTotalSeconds = 14400d,
                SessionLapsRemain = 20,
                RaceLaps = 50,
                SessionState = 4,
                SessionFlags = 4,
                TrackDisplayName = "Road Atlanta",
                TrackLengthKm = 4.088d,
                CarDisplayName = "Mercedes-AMG GT3"
            },
            Reference = LiveReferenceModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = 10,
                FocusCarIdx = 10,
                FocusIsPlayer = true,
                PlayerYawNorthRadians = Math.PI
            },
            Weather = LiveWeatherModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                AirTempC = 19d,
                TrackTempCrewC = 44d,
                TrackWetness = 0,
                TrackWetnessLabel = "dry",
                WeatherDeclaredWet = false,
                WeatherType = "constant",
                SkiesLabel = "partly cloudy",
                PrecipitationPercent = 0.12d,
                WindVelocityMetersPerSecond = 4.2d,
                WindDirectionRadians = Math.PI,
                RelativeHumidityPercent = 0.67d,
                FogLevelPercent = 0.02d,
                AirPressurePa = 101325d,
                SolarAltitudeRadians = 0.5d,
                SolarAzimuthRadians = 2.2d,
                RubberState = "moderate usage"
            }
        });

        var viewModel = SessionWeatherOverlayViewModel.From(snapshot, now, "Imperial");

        Assert.Equal("Race", viewModel.Status);
        Assert.Contains(viewModel.Rows, row => row.Label == "Session" && row.Value.Contains("team", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Temps" && row.Value.Contains("66 F", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Surface" && row.Value.Contains("Dry", StringComparison.Ordinal) && row.Value.Contains("Rubber Moderate Usage", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Sky" && row.Value.Contains("Partly Cloudy", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Sky" && row.Value.Contains("rain:12%", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Wind" && row.Value.Contains("S", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Atmosphere" && row.Value.Contains("hum 67%", StringComparison.Ordinal) && row.Value.Contains("29.92 inHg", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "State");
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Sun");
        Assert.Contains(viewModel.Rows, row => row.Label == "Wind" && row.Segments.Any(segment => segment.Label == "Facing" && segment.Value == "Head"));
        Assert.Collection(
            viewModel.MetricSections,
            section =>
            {
                Assert.Equal("Session", section.Title);
                Assert.Contains(section.Rows, row => row.Label == "Event" && row.Segments.Select(segment => segment.Label).SequenceEqual(new[] { "Event", "Car" }));
                Assert.Contains(section.Rows, row => row.Label == "Clock" && row.Segments.Select(segment => segment.Label).SequenceEqual(new[] { "Elapsed", "Left", "Total" }));
                Assert.Contains(section.Rows, row => row.Label == "Laps" && row.Segments.Select(segment => segment.Label).SequenceEqual(new[] { "Remaining", "Total" }));
                Assert.DoesNotContain(section.Rows, row => row.Label == "State");
            },
            section =>
            {
                Assert.Equal("Weather", section.Title);
                var temps = Assert.Single(section.Rows, row => row.Label == "Temps");
                Assert.Equal(new[] { "Air", "Track" }, temps.Segments.Select(segment => segment.Label));
                Assert.Contains(temps.Segments, segment => segment.Label == "Air" && segment.AccentHex == "#33CEFF");
                Assert.Contains(temps.Segments, segment => segment.Label == "Track" && segment.AccentHex == "#FF7D49");
                Assert.Contains(section.Rows, row => row.Label == "Surface" && row.Segments.Select(segment => segment.Label).SequenceEqual(new[] { "Wetness", "Declared", "Rubber" }));
                Assert.Contains(section.Rows, row => row.Label == "Surface" && row.Segments.Any(segment => segment.Label == "Wetness" && segment.Value == "Dry"));
                Assert.Contains(section.Rows, row => row.Label == "Surface" && row.Segments.Any(segment => segment.Label == "Rubber" && segment.Value == "Moderate Usage"));
                Assert.Contains(section.Rows, row => row.Label == "Sky" && row.Segments.Any(segment => segment.Label == "Skies" && segment.Value == "Partly Cloudy"));
                Assert.Contains(section.Rows, row => row.Label == "Atmosphere" && row.Segments.Any(segment => segment.Label == "Fog" && segment.Value == "2%"));
                Assert.Contains(section.Rows, row => row.Label == "Wind" && row.Segments.Any(segment => segment.Label == "Facing" && segment.RotationDegrees == 0d && segment.AccentHex is null));
            });
    }

    [Fact]
    public void SessionWeather_FromTelemetry_HidesLocalWindOutsideLocalInCarContext()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race"
            },
            RaceEvents = LiveRaceEventModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                IsOnTrack = false,
                IsInGarage = true
            },
            Reference = LiveReferenceModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarIdx = 10,
                FocusCarIdx = 10,
                FocusIsPlayer = true,
                PlayerYawNorthRadians = Math.PI
            },
            Weather = LiveWeatherModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                WindVelocityMetersPerSecond = 4.2d,
                WindDirectionRadians = Math.PI
            }
        });

        var viewModel = SessionWeatherOverlayViewModel.From(snapshot, now, "Metric");

        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Local wind");
        Assert.Contains(viewModel.Rows, row => row.Label == "Wind" && row.Segments.All(segment => segment.Label != "Facing"));
    }

    [Fact]
    public void SessionWeather_FromTelemetry_HonorsContentCellToggles()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race"
            },
            Weather = LiveWeatherModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                AirTempC = 21d,
                TrackTempCrewC = 38d,
                SkiesLabel = "partly cloudy",
                WeatherType = "dynamic",
                PrecipitationPercent = 0.2d
            }
        });
        var settings = new OverlaySettings { Id = "session-weather" };
        settings.SetBooleanOption($"{OverlayContentColumnSettings.SessionWeatherSkySkiesBlockId}.enabled", false);
        settings.SetBooleanOption($"{OverlayContentColumnSettings.SessionWeatherSkyWeatherBlockId}.enabled", false);
        settings.SetBooleanOption($"{OverlayContentColumnSettings.SessionWeatherSkyRainBlockId}.enabled", false);
        settings.SetBooleanOption($"{OverlayContentColumnSettings.SessionWeatherTempsAirBlockId}.enabled", false);

        var viewModel = SessionWeatherOverlayViewModel.From(snapshot, now, "Metric", settings);

        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Sky");
        Assert.Contains(
            viewModel.Rows,
            row => row.Label == "Temps"
                && row.Value == "38 C"
                && row.Segments.Select(segment => segment.Label).SequenceEqual(new[] { "Track" }));
    }

    [Fact]
    public void SessionWeather_FromTelemetry_FormatsRacePreGreenClockAsCountdown()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race",
                SessionState = 3,
                SessionTimeSeconds = 120d,
                SessionTimeRemainSeconds = 88d
            },
            Weather = LiveWeatherModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable
            }
        });

        var viewModel = SessionWeatherOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Contains(viewModel.Rows, row => row.Label == "Clock" && row.Value.Contains("countdown", StringComparison.Ordinal));
    }

    [Fact]
    public void SessionWeather_FromTelemetry_TreatsUnknownWetFlagAsNormalTelemetry()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Practice"
            },
            Weather = LiveWeatherModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                WeatherDeclaredWet = null,
                TrackWetness = null
            }
        });

        var viewModel = SessionWeatherOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("Practice", viewModel.Status);
        Assert.Equal(SimpleTelemetryTone.Normal, viewModel.Tone);
        Assert.Contains(viewModel.Rows, row => row.Label == "Surface" && row.Value == "--");
    }

    [Fact]
    public void SessionWeather_CreateBuilder_HighlightsWeatherRowsAfterChange()
    {
        var now = DateTimeOffset.UtcNow;
        var builder = SessionWeatherOverlayViewModel.CreateBuilder();
        var initial = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race"
            },
            Weather = LiveWeatherModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                AirTempC = 20d,
                TrackWetness = 1,
                TrackWetnessLabel = "dry",
                WeatherDeclaredWet = false
            }
        });
        var changed = initial with
        {
            Models = initial.Models with
            {
                Weather = initial.Models.Weather with
                {
                    AirTempC = 22d
                }
            }
        };

        var first = builder(initial, now, "Metric");
        var second = builder(changed, now.AddSeconds(1), "Metric");

        Assert.Contains(first.Rows, row => row.Label == "Surface" && row.Tone == SimpleTelemetryTone.Normal);
        Assert.Contains(second.Rows, row => row.Label == "Surface" && row.Tone == SimpleTelemetryTone.Normal);
        Assert.Contains(second.Rows, row => row.Label == "Temps" && row.Tone == SimpleTelemetryTone.Info);
    }

    [Fact]
    public void PitService_FromTelemetry_FormatsRequestedService()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                OnPitRoad = true,
                PitstopActive = true,
                PitServiceStatus = PitServiceStatusFormatter.InProgress,
                PitServiceFlags = 0x3f,
                PitServiceFuelLiters = 45.5d,
                PitRepairLeftSeconds = 12.2d,
                TireSetsUsed = 2,
                FastRepairUsed = 0,
                TeamFastRepairsUsed = 1
            },
            RaceEvents = LiveRaceEventModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                IsOnTrack = false,
                IsInGarage = false,
                OnPitRoad = true
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("hold", viewModel.Status);
        Assert.Equal("source: player/team pit service telemetry", viewModel.Source);
        Assert.Contains(viewModel.Rows, row => row.Label == "Release" && row.Value == "RED - service active");
        Assert.Contains(viewModel.Rows, row => row.Label == "Pit status" && row.Value == "in progress");
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Location");
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Service");
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Tires");
        Assert.Contains(viewModel.Rows, row => row.Label == "Fuel request" && row.Value == "requested | 45.5 L");
        Assert.Contains(viewModel.Rows, row => row.Label == "Fuel request" && row.Segments.Select(segment => segment.Label).SequenceEqual(new[] { "Requested", "Selected" }));
        Assert.Contains(viewModel.Rows, row => row.Label == "Tearoff" && row.Value == "requested" && row.Segments.Count == 1);
        Assert.Contains(viewModel.Rows, row => row.Label == "Repair" && row.Value.Contains("12s required", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Fast repair" && row.Value == "--" && row.Segments.Count == 2);
    }

    [Fact]
    public void SimpleV2NativeViewModels_CompleteModelsFromLegacySample()
    {
        var now = DateTimeOffset.UtcNow;
        var drivingSnapshot = LegacySampleSnapshot(now, LegacySample(now, carLeftRight: 2));
        var pitSnapshot = LegacySampleSnapshot(now, LegacySample(now, onPitRoad: true, pitstopActive: true));

        var input = InputStateOverlayViewModel.From(drivingSnapshot, now, "Metric");
        var weather = SessionWeatherOverlayViewModel.From(drivingSnapshot, now, "Metric");
        var radar = CarRadarOverlayViewModel.From(
            drivingSnapshot,
            now,
            previewVisible: false,
            showMulticlassWarning: true);
        var pit = PitServiceOverlayViewModel.From(pitSnapshot, now, "Metric");

        Assert.Contains(input.Rows, row => row.Label == "Gear / RPM" && row.Value.Contains("4", StringComparison.Ordinal));
        Assert.Contains(weather.Rows, row => row.Label == "Temps" && row.Value.Contains("30", StringComparison.Ordinal));
        Assert.True(radar.IsAvailable);
        Assert.True(radar.HasCarLeft);
        Assert.Contains(pit.Rows, row => row.Label == "Fuel request" && row.Value.Contains("45.5 L", StringComparison.Ordinal));
    }

    [Fact]
    public void PitService_FromTelemetry_HonorsMetricCellToggles()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                OnPitRoad = true,
                PitServiceStatus = PitServiceStatusFormatter.InProgress,
                PitServiceFlags = 0x10,
                PitServiceFuelLiters = 45.5d,
                PitRepairLeftSeconds = 12.2d,
                PitOptRepairLeftSeconds = 18.4d
            }
        });
        var settings = new OverlaySettings { Id = "pit-service" };
        settings.SetBooleanOption($"{OverlayContentColumnSettings.PitServiceReleaseBlockId}.enabled", false);
        settings.SetBooleanOption($"{OverlayContentColumnSettings.PitServiceFuelSelectedBlockId}.enabled", false);
        settings.SetBooleanOption($"{OverlayContentColumnSettings.PitServiceRepairRequiredBlockId}.enabled", false);
        settings.SetBooleanOption($"{OverlayContentColumnSettings.PitServiceRepairOptionalBlockId}.enabled", false);

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric", settings);

        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Release");
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Repair");
        Assert.Contains(
            viewModel.Rows,
            row => row.Label == "Fuel request"
                && row.Value == "Yes"
                && Assert.Single(row.Segments).Label == "Requested");
    }

    [Fact]
    public void PitService_FromTelemetry_GroupsRowsAndShowsTimeWithFiniteRaceLaps()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Session = LiveSessionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SessionType = "Race",
                SessionTimeRemainSeconds = 86_258.266667d,
                SessionLapsRemain = 4,
                SessionLapsTotal = 5
            },
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarInPitStall = true,
                PitstopActive = false,
                PitServiceFlags = 0
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("release ready", viewModel.Status);
        Assert.Contains(viewModel.Rows, row => row.Label == "Release" && row.Value == "GREEN - go (inferred)");
        Assert.Contains(viewModel.Rows, row => row.Label == "Time / Laps" && row.Value == "23:58 | 4/5 laps");

        Assert.Collection(
            viewModel.MetricSections,
            section =>
            {
                Assert.Equal("Session", section.Title);
                Assert.Contains(section.Rows, row => row.Label == "Time / Laps");
            },
            section =>
            {
                Assert.Equal("Pit Signal", section.Title);
                Assert.Contains(section.Rows, row => row.Label == "Release");
                Assert.Contains(section.Rows, row => row.Label == "Pit status");
                Assert.DoesNotContain(section.Rows, row => row.Label == "Location");
            },
            section =>
            {
                Assert.Equal("Service Request", section.Title);
                Assert.DoesNotContain(section.Rows, row => row.Label == "Service");
                Assert.Contains(section.Rows, row => row.Label == "Tearoff");
                Assert.Contains(section.Rows, row => row.Label == "Fast repair");
            });
    }

    [Fact]
    public void PitService_FromTelemetry_WaitsWhenFocusMovesToAnotherCar()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            DriverDirectory = LiveDriverDirectoryModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Partial,
                PlayerCarIdx = 10,
                FocusCarIdx = 42
            },
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PitServiceFlags = 0x10,
                PitServiceFuelLiters = 45.5d
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("waiting for local pit-service context", viewModel.Status);
        Assert.Equal("source: waiting", viewModel.Source);
        Assert.Empty(viewModel.Rows);
    }

    [Fact]
    public void PitService_FromTelemetry_ShowsGreenReleaseWhenServiceComplete()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarInPitStall = true,
                PitstopActive = false,
                PitServiceStatus = PitServiceStatusFormatter.Complete,
                PitServiceFlags = 0x1f,
                PitServiceFuelLiters = 0d
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("release ready", viewModel.Status);
        Assert.Equal(SimpleTelemetryTone.Success, viewModel.Tone);
        Assert.Contains(viewModel.Rows, row => row.Label == "Release" && row.Value == "GREEN - go");
        Assert.Contains(viewModel.Rows, row => row.Label == "Pit status" && row.Value == "complete");
    }

    [Fact]
    public void PitService_FromTelemetry_TreatsOptionalRepairAsAdvisory()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarInPitStall = true,
                PitstopActive = false,
                PitServiceStatus = PitServiceStatusFormatter.None,
                PitOptRepairLeftSeconds = 18.4d,
                PitServiceFlags = 0
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("optional repair", viewModel.Status);
        Assert.Equal(SimpleTelemetryTone.Warning, viewModel.Tone);
        Assert.Contains(viewModel.Rows, row => row.Label == "Release" && row.Value == "YELLOW - optional repair");
        Assert.Contains(viewModel.Rows, row => row.Label == "Repair" && row.Value == "18s optional" && row.Tone == SimpleTelemetryTone.Warning);
    }

    [Fact]
    public void PitService_FromTelemetry_HoldsForRequiredRepair()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarInPitStall = true,
                PitstopActive = false,
                PitServiceStatus = PitServiceStatusFormatter.None,
                PitRepairLeftSeconds = 9.6d,
                PitOptRepairLeftSeconds = 18.4d,
                PitServiceFlags = 0
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("hold", viewModel.Status);
        Assert.Equal(SimpleTelemetryTone.Error, viewModel.Tone);
        Assert.Contains(viewModel.Rows, row => row.Label == "Release" && row.Value == "RED - repair active");
        Assert.Contains(viewModel.Rows, row => row.Label == "Repair" && row.Value.Contains("10s required", StringComparison.Ordinal) && row.Tone == SimpleTelemetryTone.Error);
    }

    [Fact]
    public void PitService_FromTelemetry_ShowsFastRepairSelected()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PitServiceFlags = 0x40,
                FastRepairUsed = 0,
                FastRepairAvailable = 1,
                TeamFastRepairsUsed = 1
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("service requested", viewModel.Status);
        Assert.Contains(viewModel.Rows, row => row.Label == "Release" && row.Value == "armed");
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Service");
        Assert.Contains(viewModel.Rows, row => row.Label == "Fast repair" && row.Value == "selected | available 1");
        Assert.Contains(viewModel.Rows, row => row.Label == "Fast repair" && row.Segments.Any(segment => segment.Label == "Available" && segment.Value == "1"));
    }

    [Fact]
    public void PitService_CreateBuilder_HighlightsChangedRequestValues()
    {
        var now = DateTimeOffset.UtcNow;
        var builder = PitServiceOverlayViewModel.CreateBuilder();
        var initial = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PitServiceFlags = 0x10,
                PitServiceFuelLiters = 25d,
                FastRepairUsed = 0,
                TeamFastRepairsUsed = 0
            }
        });
        var changed = Snapshot(now.AddSeconds(1), LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PitServiceFlags = 0x1f,
                PitServiceFuelLiters = 45d,
                FastRepairUsed = 0,
                TeamFastRepairsUsed = 0
            }
        });

        var first = builder(initial, now, "Metric");
        var second = builder(changed, now.AddSeconds(1), "Metric");

        Assert.Contains(first.Rows, row => row.Label == "Fuel request" && row.Tone == SimpleTelemetryTone.Normal);
        Assert.Contains(second.Rows, row => row.Label == "Fuel request" && row.Value == "requested | 45.0 L" && row.Tone == SimpleTelemetryTone.Info);
        Assert.DoesNotContain(second.Rows, row => row.Label == "Tires");
        Assert.Contains(second.Sections.SelectMany(section => section.Rows), row => row.Label == "Change" && row.Cells.All(cell => cell.Value == "Change"));
    }

    [Fact]
    public void PitService_FromTelemetry_HidesTireAnalysisWhenCountersAreNotRepresentative()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarDryTireSetLimit = 0,
                TireSetsAvailable = 0,
                TireSetsUsed = 0,
                LeftFrontTiresAvailable = 0,
                RightFrontTiresAvailable = 0,
                LeftRearTiresAvailable = 0,
                RightRearTiresAvailable = 0
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Empty(viewModel.Sections);
        Assert.DoesNotContain(viewModel.Rows, row => row.Label == "Tires");
    }

    [Fact]
    public void PitService_FromTelemetry_HidesAvailableTiresWhenDryTireLimitIsUnlimited()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarDryTireSetLimit = 0,
                TireSetsAvailable = 1,
                LeftFrontTiresAvailable = 1,
                RightFrontTiresAvailable = 1,
                LeftRearTiresAvailable = 1,
                RightRearTiresAvailable = 1,
                TireSetsUsed = 1,
                LeftFrontTiresUsed = 1,
                RightFrontTiresUsed = 1,
                LeftRearTiresUsed = 1,
                RightRearTiresUsed = 1
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        var section = Assert.Single(viewModel.Sections);
        Assert.DoesNotContain(section.Rows, row => row.Label == "Set limit");
        Assert.DoesNotContain(section.Rows, row => row.Label == "Available");
        Assert.Contains(section.Rows, row => row.Label == "Used" && row.Cells.All(cell => cell.Value == "1"));
    }

    [Fact]
    public void PitService_FromTelemetry_ShowsRepresentativeTireAnalysisRows()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = TireAnalysisSnapshot(now);

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Imperial");

        var section = Assert.Single(viewModel.Sections);
        Assert.Equal("Tire Analysis", section.Title);
        Assert.Collection(
            section.Headers,
            header => Assert.Equal("Info", header),
            header => Assert.Equal("FL", header),
            header => Assert.Equal("FR", header),
            header => Assert.Equal("RL", header),
            header => Assert.Equal("RR", header));
        Assert.Contains(section.Rows, row => row.Label == "Set limit" && row.Cells.All(cell => cell.Value == "4 sets"));
        Assert.Contains(section.Rows, row => row.Label == "Compound" && row.Cells.All(cell => cell.Value == "Dry" && cell.Tone == SimpleTelemetryTone.Info));
        Assert.Contains(section.Rows, row => row.Label == "Change" && row.Cells[0].Value == "Change" && row.Cells[0].Tone == SimpleTelemetryTone.Success);
        Assert.Contains(section.Rows, row => row.Label == "Change" && row.Cells[1].Value == "Keep" && row.Cells[1].Tone == SimpleTelemetryTone.Info);
        Assert.Contains(section.Rows, row => row.Label == "Available" && row.Cells.All(cell => cell.Value == "2"));
        Assert.Contains(section.Rows, row => row.Label == "Used" && row.Cells[0].Value == "1");
        Assert.Contains(section.Rows, row => row.Label == "Wear" && row.Cells[0].Value == "92/91/90%");
        Assert.Contains(section.Rows, row => row.Label == "Temp" && row.Cells[0].Value == "176/178/180 F");
        Assert.Contains(section.Rows, row => row.Label == "Pressure" && row.Cells[0].Value == "30 psi");
        Assert.Contains(section.Rows, row => row.Label == "Distance" && row.Cells[0].Value == "12.4 mi");
    }

    [Fact]
    public void PitService_FromTelemetry_ShowsZeroAvailableWhenSetLimitIsKnown()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarDryTireSetLimit = 4,
                TireSetsAvailable = 0,
                TireSetsUsed = 4,
                LeftFrontTiresAvailable = 0,
                RightFrontTiresAvailable = 0,
                LeftRearTiresAvailable = 0,
                RightRearTiresAvailable = 0
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        var section = Assert.Single(viewModel.Sections);
        Assert.Contains(section.Rows, row => row.Label == "Set limit" && row.Cells.All(cell => cell.Value == "4 sets"));
        Assert.Contains(section.Rows, row => row.Label == "Available" && row.Cells.All(cell => cell.Value == "0" && cell.Tone == SimpleTelemetryTone.Error));
    }

    [Fact]
    public void PitService_FromTelemetry_CollapsesAvailableTiresToSharedLimitedInventory()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PlayerCarDryTireSetLimit = 13,
                TireSetsAvailable = 11,
                LeftFrontTiresAvailable = 12,
                RightFrontTiresAvailable = 11,
                LeftRearTiresAvailable = 12,
                RightRearTiresAvailable = 11
            }
        });

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric");

        var section = Assert.Single(viewModel.Sections);
        Assert.Contains(section.Rows, row => row.Label == "Available" && row.Cells.All(cell => cell.Value == "11"));
    }

    [Fact]
    public void PitService_FromTelemetry_HonorsTireAnalysisContentToggles()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = TireAnalysisSnapshot(now);
        var settings = new OverlaySettings { Id = "pit-service" };
        settings.SetBooleanOption(OverlayOptionKeys.PitServiceShowTireSetsAvailable, false);
        settings.SetBooleanOption(OverlayOptionKeys.PitServiceShowTireWear, false);
        settings.SetBooleanOption(OverlayOptionKeys.PitServiceShowTireTemperature, false);

        var viewModel = PitServiceOverlayViewModel.From(snapshot, now, "Metric", settings);

        var section = Assert.Single(viewModel.Sections);
        Assert.DoesNotContain(section.Rows, row => row.Label == "Available");
        Assert.DoesNotContain(section.Rows, row => row.Label == "Wear");
        Assert.DoesNotContain(section.Rows, row => row.Label == "Temp");
        Assert.Contains(section.Rows, row => row.Label == "Set limit");
    }

    [Fact]
    public void InputState_FromTelemetry_FormatsLocalCarState()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            Inputs = LiveInputTelemetryModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SpeedMetersPerSecond = 44.7d,
                Gear = 4,
                Rpm = 7_250d,
                Throttle = 0.75d,
                Brake = 0.1d,
                Clutch = 0d,
                HasPedalInputs = true,
                SteeringWheelAngle = Math.PI / 6d,
                HasSteeringInput = true,
                BrakeAbsActive = true,
                EngineWarnings = 0,
                Voltage = 13.8d,
                WaterTempC = 88d,
                OilTempC = 96d,
                OilPressureBar = 5.4d,
                FuelPressureBar = 4.1d
            }
        });

        var viewModel = InputStateOverlayViewModel.From(snapshot, now, "Imperial");

        Assert.Equal("4 | 7250 rpm | ABS", viewModel.Status);
        Assert.Contains(viewModel.Rows, row => row.Label == "Speed" && row.Value == "100 mph");
        Assert.Contains(viewModel.Rows, row => row.Label == "Pedals" && row.Value.Contains("T 75%", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Pedals" && row.Value.Contains("B 10% ABS", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Steering" && row.Value == "+30 deg");
    }

    [Fact]
    public void InputState_FromTelemetry_WaitsWhenPlayerIsNotInCar()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            RaceEvents = LiveRaceEventModel.Empty with
            {
                HasData = true,
                IsOnTrack = false,
                IsInGarage = true
            },
            Inputs = LiveInputTelemetryModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                Throttle = 0.75d,
                Brake = 0.1d,
                HasPedalInputs = true
            }
        });

        var viewModel = InputStateOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("waiting for player in car", viewModel.Status);
    }

    [Fact]
    public void InputState_FromTelemetry_RendersWhenPlayerIsInPitRoadButNotGarage()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = Snapshot(now, LiveRaceModels.Empty with
        {
            RaceEvents = LiveRaceEventModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                IsOnTrack = true,
                IsInGarage = false,
                IsGarageVisible = false,
                OnPitRoad = true
            },
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                OnPitRoad = true,
                PlayerCarInPitStall = true,
                PitstopActive = true
            },
            Inputs = LiveInputTelemetryModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                SpeedMetersPerSecond = 0.01d,
                Gear = 0,
                Rpm = 2_015d,
                Throttle = 0d,
                Brake = 1d,
                Clutch = 0d,
                HasPedalInputs = true,
                SteeringWheelAngle = 0.003d,
                HasSteeringInput = true
            }
        });

        var viewModel = InputStateOverlayViewModel.From(snapshot, now, "Metric");

        Assert.Equal("N | 2015 rpm", viewModel.Status);
        Assert.Contains(viewModel.Rows, row => row.Label == "Pedals" && row.Value.Contains("B 100%", StringComparison.Ordinal));
        Assert.Contains(viewModel.Rows, row => row.Label == "Steering" && row.Value == "0 deg");
    }

    private static LiveTelemetrySnapshot Snapshot(DateTimeOffset now, LiveRaceModels models)
    {
        var normalizedModels = models with
        {
            DriverDirectory = models.DriverDirectory.HasData
                ? models.DriverDirectory
                : LiveDriverDirectoryModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    PlayerCarIdx = 10,
                    FocusCarIdx = 10
                },
            RaceEvents = models.RaceEvents.HasData
                ? models.RaceEvents
                : LiveRaceEventModel.Empty with
                {
                    HasData = true,
                    Quality = LiveModelQuality.Reliable,
                    IsOnTrack = true
                },
            PitService = models.PitService.HasData || !models.FuelPit.HasData
                ? models.PitService
                : LivePitServiceModel.FromFuelPit(models.FuelPit, models.TireCompounds)
        };

        return LiveTelemetrySnapshot.Empty with
        {
            IsConnected = true,
            IsCollecting = true,
            LastUpdatedAtUtc = now,
            Sequence = 1,
            Models = normalizedModels
        };
    }

    private static LiveTelemetrySnapshot LegacySampleSnapshot(DateTimeOffset now, HistoricalTelemetrySample sample)
    {
        var context = new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                DriverCarFuelMaxLiters = 100d,
                DriverCarFuelKgPerLiter = 0.75d,
                DriverCarEstLapTimeSeconds = 92d
            },
            Track = new HistoricalTrackIdentity { TrackLengthKm = 5.1d },
            Session = new HistoricalSessionIdentity
            {
                SessionType = "Race",
                SessionName = "Race Preview",
                SessionTime = "3600 sec",
                SessionLaps = "unlimited"
            },
            Conditions = new HistoricalSessionInfoConditions()
        };

        return new LiveTelemetrySnapshot(
            IsConnected: true,
            IsCollecting: true,
            SourceId: "test",
            StartedAtUtc: now.AddMinutes(-2),
            LastUpdatedAtUtc: now,
            Sequence: 1,
            Context: context,
            Combo: HistoricalComboIdentity.From(context),
            LatestSample: sample,
            Fuel: LiveFuelSnapshot.From(context, sample),
            Proximity: LiveProximitySnapshot.From(context, sample),
            LeaderGap: LiveLeaderGapSnapshot.From(sample));
    }

    private static HistoricalTelemetrySample LegacySample(
        DateTimeOffset now,
        bool onPitRoad = false,
        bool pitstopActive = false,
        int? carLeftRight = null)
    {
        return new HistoricalTelemetrySample(
            CapturedAtUtc: now,
            SessionTime: 120d,
            SessionTick: 100,
            SessionInfoUpdate: 1,
            IsOnTrack: !onPitRoad,
            IsInGarage: false,
            OnPitRoad: onPitRoad,
            PitstopActive: pitstopActive,
            PlayerCarInPitStall: pitstopActive,
            FuelLevelLiters: 52.4d,
            FuelLevelPercent: 0.52d,
            FuelUsePerHourKg: 22d,
            SpeedMetersPerSecond: onPitRoad ? 12d : 64d,
            Lap: 4,
            LapCompleted: 4,
            LapDistPct: 0.42d,
            LapLastLapTimeSeconds: 92.4d,
            LapBestLapTimeSeconds: 91.8d,
            AirTempC: 21d,
            TrackTempCrewC: 30d,
            TrackWetness: 1,
            WeatherDeclaredWet: false,
            PlayerTireCompound: 0,
            SessionTimeRemain: 600d,
            SessionTimeTotal: 3600d,
            SessionLapsRemainEx: 32_767,
            SessionLapsTotal: 32_767,
            SessionState: 4,
            RaceLaps: 4,
            PlayerCarIdx: 10,
            FocusCarIdx: 10,
            FocusLapCompleted: 4,
            FocusLapDistPct: 0.42d,
            FocusPosition: 5,
            FocusClassPosition: 3,
            FocusOnPitRoad: onPitRoad,
            PlayerTrackSurface: onPitRoad ? 1 : 3,
            CarLeftRight: carLeftRight,
            PitServiceStatus: pitstopActive ? PitServiceStatusFormatter.InProgress : null,
            PitServiceFlags: pitstopActive ? 0x3f : null,
            PitServiceFuelLiters: pitstopActive ? 45.5d : null,
            PitRepairLeftSeconds: pitstopActive ? 12.2d : null,
            Gear: 4,
            Rpm: 6800d,
            Throttle: 0.78d,
            Brake: 0.16d,
            Clutch: 0d,
            SteeringWheelAngle: -0.18d,
            PlayerYawNorthRadians: 0.35d,
            EngineWarnings: 0,
            Voltage: 14.1d,
            WaterTempC: 91d,
            OilTempC: 96d,
            BrakeAbsActive: true);
    }

    private static LiveTelemetrySnapshot TireAnalysisSnapshot(DateTimeOffset now)
    {
        return Snapshot(now, LiveRaceModels.Empty with
        {
            TireCompounds = LiveTireCompoundModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                Definitions =
                [
                    new LiveTireCompoundDefinition(0, "Dry", "Dry", IsWet: false)
                ],
                PlayerCar = new LiveCarTireCompound(
                    CarIdx: 10,
                    CompoundIndex: 0,
                    Label: "Dry",
                    ShortLabel: "Dry",
                    IsWet: false,
                    IsPlayer: true,
                    IsFocus: true,
                    Evidence: LiveSignalEvidence.Reliable("CarIdxTireCompound"))
            },
            FuelPit = LiveFuelPitModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Reliable,
                PitServiceFlags = 0x01,
                PlayerCarDryTireSetLimit = 4,
                TireSetsAvailable = 2,
                TireSetsUsed = 1,
                LeftFrontTiresAvailable = 2,
                RightFrontTiresAvailable = 2,
                LeftRearTiresAvailable = 2,
                RightRearTiresAvailable = 2,
                LeftFrontTiresUsed = 1,
                RightFrontTiresUsed = 1,
                LeftRearTiresUsed = 1,
                RightRearTiresUsed = 1,
                RequestedTireCompound = 0
            },
            TireCondition = LiveTireConditionModel.Empty with
            {
                HasData = true,
                Quality = LiveModelQuality.Partial,
                LeftFront = TireCorner("LF", 0.92d, 0.91d, 0.90d, 80d, 81d, 82d, 206.8d, 20_000d),
                RightFront = TireCorner("RF", 0.93d, 0.92d, 0.91d, 79d, 80d, 81d, 203.4d, 19_750d),
                LeftRear = TireCorner("LR", 0.96d, 0.95d, 0.94d, 72d, 73d, 74d, 196.5d, 20_000d),
                RightRear = TireCorner("RR", 0.97d, 0.96d, 0.95d, 73d, 74d, 75d, 198.6d, 19_750d)
            }
        });
    }

    private static LiveTireCornerCondition TireCorner(
        string corner,
        double wearLeft,
        double wearMiddle,
        double wearRight,
        double tempLeft,
        double tempMiddle,
        double tempRight,
        double coldPressureKpa,
        double odometerMeters)
    {
        return new LiveTireCornerCondition(
            Corner: corner,
            Wear: new LiveTireAcrossTreadValues(wearLeft, wearMiddle, wearRight),
            TemperatureC: new LiveTireAcrossTreadValues(tempLeft, tempMiddle, tempRight),
            ColdPressureKpa: coldPressureKpa,
            OdometerMeters: odometerMeters,
            PitServicePressureKpa: null,
            BlackBoxColdPressurePa: null,
            ChangeRequested: null);
    }
}
