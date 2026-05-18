using System.Globalization;

namespace TmrOverlay.Core.History;

internal static class SessionInfoSummaryParser
{
    public static HistoricalSessionContext Parse(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return HistoricalSessionContext.Empty;
        }

        var parsed = ParseSections(yaml);
        var selectedSession = SelectSession(parsed);
        var session = selectedSession?.Values ?? EmptyDictionary;
        var driver = SelectDriver(parsed);

        return new HistoricalSessionContext
        {
            Car = new HistoricalCarIdentity
            {
                CarId = ReadInt(driver, "CarID"),
                CarPath = ReadString(driver, "CarPath"),
                CarScreenName = ReadString(driver, "CarScreenName"),
                CarScreenNameShort = ReadString(driver, "CarScreenNameShort"),
                CarClassId = ReadInt(driver, "CarClassID"),
                CarClassShortName = ReadString(driver, "CarClassShortName"),
                CarClassEstLapTimeSeconds = ReadDouble(driver, "CarClassEstLapTime"),
                DriverCarFuelMaxLiters = ReadDouble(parsed.DriverInfo, "DriverCarFuelMaxLtr"),
                DriverCarFuelKgPerLiter = ReadDouble(parsed.DriverInfo, "DriverCarFuelKgPerLtr"),
                DriverCarEstLapTimeSeconds = ReadDouble(parsed.DriverInfo, "DriverCarEstLapTime"),
                DriverCarVersion = ReadString(parsed.DriverInfo, "DriverCarVersion"),
                DriverGearboxType = ReadString(parsed.DriverInfo, "DriverGearboxType"),
                DriverSetupName = ReadString(parsed.DriverInfo, "DriverSetupName"),
                DriverSetupIsModified = ReadBool(parsed.DriverInfo, "DriverSetupIsModified")
            },
            Track = new HistoricalTrackIdentity
            {
                TrackId = ReadInt(parsed.WeekendInfo, "TrackID"),
                TrackName = ReadString(parsed.WeekendInfo, "TrackName"),
                TrackDisplayName = ReadString(parsed.WeekendInfo, "TrackDisplayName"),
                TrackConfigName = ReadString(parsed.WeekendInfo, "TrackConfigName"),
                TrackLengthKm = ReadDouble(parsed.WeekendInfo, "TrackLength"),
                TrackCity = ReadString(parsed.WeekendInfo, "TrackCity"),
                TrackCountry = ReadString(parsed.WeekendInfo, "TrackCountry"),
                TrackNumTurns = ReadInt(parsed.WeekendInfo, "TrackNumTurns"),
                TrackType = ReadString(parsed.WeekendInfo, "TrackType"),
                TrackVersion = ReadString(parsed.WeekendInfo, "TrackVersion")
            },
            Session = new HistoricalSessionIdentity
            {
                CurrentSessionNum = ReadInt(parsed.SessionInfo, "CurrentSessionNum"),
                SessionNum = ReadInt(session, "SessionNum"),
                SessionType = ReadString(session, "SessionType"),
                SessionName = ReadString(session, "SessionName"),
                SessionTime = ReadString(session, "SessionTime"),
                SessionLaps = ReadString(session, "SessionLaps"),
                EventType = ReadString(parsed.WeekendInfo, "EventType"),
                Category = ReadString(parsed.WeekendInfo, "Category"),
                Official = ReadBool(parsed.WeekendInfo, "Official"),
                TeamRacing = ReadBool(parsed.WeekendInfo, "TeamRacing"),
                SeriesId = ReadInt(parsed.WeekendInfo, "SeriesID"),
                SeasonId = ReadInt(parsed.WeekendInfo, "SeasonID"),
                SessionId = ReadInt(parsed.WeekendInfo, "SessionID"),
                SubSessionId = ReadInt(parsed.WeekendInfo, "SubSessionID"),
                BuildVersion = ReadString(parsed.WeekendInfo, "BuildVersion")
            },
            Conditions = new HistoricalSessionInfoConditions
            {
                TrackWeatherType = ReadString(parsed.WeekendInfo, "TrackWeatherType"),
                TrackSkies = ReadString(parsed.WeekendInfo, "TrackSkies"),
                TrackPrecipitationPercent = ReadDouble(parsed.WeekendInfo, "TrackPrecipitation"),
                SessionTrackRubberState = ReadString(session, "SessionTrackRubberState")
            },
            Drivers = parsed.Drivers
                .Select(ToDriver)
                .Where(driver => driver.CarIdx is not null)
                .ToArray(),
            TireCompounds = parsed.DriverTires
                .Select(ToTireCompound)
                .Where(tire => tire.TireIndex is not null)
                .ToArray(),
            Sectors = parsed.Sectors
                .Select(ToSector)
                .Where(sector => sector.SectorStartPct >= 0d && sector.SectorStartPct < 1d)
                .OrderBy(sector => sector.SectorStartPct)
                .ToArray(),
            ResultPositions = (selectedSession?.ResultPositions ?? [])
                .Select(ToResultPosition)
                .Where(position => position.CarIdx is not null)
                .ToArray(),
            StartingGridPositions = parsed.QualifyResults
                .Select(ToResultPosition)
                .Where(position => position.CarIdx is not null)
                .ToArray()
        };
    }

    private static ParsedSessionInfo ParseSections(string yaml)
    {
        var parsed = new ParsedSessionInfo();
        var section = string.Empty;
        ParsedSession? currentSession = null;
        Dictionary<string, string>? currentResultPosition = null;
        Dictionary<string, string>? currentQualifyResult = null;
        Dictionary<string, string>? currentDriver = null;
        Dictionary<string, string>? currentDriverTire = null;
        Dictionary<string, string>? currentSector = null;
        var inSessions = false;
        var inDrivers = false;
        var inDriverTires = false;
        var inSectors = false;
        var inResultPositions = false;
        var inQualifyResults = false;

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed == "---" || trimmed == "...")
            {
                continue;
            }

            var indent = CountLeadingSpaces(line);

            if (indent == 0 && trimmed.EndsWith(':') && !trimmed.StartsWith('-'))
            {
                FinishCurrentResultPosition();
                FinishCurrentQualifyResult();
                FinishCurrentSession();
                FinishCurrentDriver();
                FinishCurrentDriverTire();
                FinishCurrentSector();
                section = trimmed.TrimEnd(':');
                inSessions = false;
                inDrivers = false;
                inDriverTires = false;
                inSectors = false;
                inResultPositions = false;
                inQualifyResults = false;
                continue;
            }

            switch (section)
            {
                case "WeekendInfo":
                    if (indent == 1 && TryReadKeyValue(trimmed, out var weekendKey, out var weekendValue))
                    {
                        parsed.WeekendInfo[weekendKey] = weekendValue;
                    }
                    break;

                case "SessionInfo":
                    if (indent == 1 && trimmed == "Sessions:")
                    {
                        inSessions = true;
                        break;
                    }

                    if (!inSessions && indent == 1 && TryReadKeyValue(trimmed, out var sessionInfoKey, out var sessionInfoValue))
                    {
                        parsed.SessionInfo[sessionInfoKey] = sessionInfoValue;
                        break;
                    }

                    if (inSessions)
                    {
                        if (indent == 1 && trimmed.StartsWith("- ", StringComparison.Ordinal))
                        {
                            FinishCurrentResultPosition();
                            FinishCurrentSession();
                            currentSession = new ParsedSession();
                            inResultPositions = false;
                            if (TryReadKeyValue(trimmed, out var listKey, out var listValue))
                            {
                                currentSession.Values[listKey] = listValue;
                            }
                            break;
                        }

                        if (currentSession is not null
                            && indent >= 3
                            && string.Equals(trimmed, "ResultsPositions:", StringComparison.Ordinal))
                        {
                            FinishCurrentResultPosition();
                            inResultPositions = true;
                            break;
                        }

                        if (currentSession is not null && inResultPositions)
                        {
                            if (indent == 3 && trimmed.StartsWith("- ", StringComparison.Ordinal))
                            {
                                FinishCurrentResultPosition();
                                currentResultPosition = [];
                                if (TryReadKeyValue(trimmed, out var resultKey, out var resultValue))
                                {
                                    currentResultPosition[resultKey] = resultValue;
                                }
                                break;
                            }

                            if (currentResultPosition is not null && indent > 3)
                            {
                                if (TryReadKeyValue(trimmed, out var resultKey, out var resultValue))
                                {
                                    currentResultPosition[resultKey] = resultValue;
                                }
                                break;
                            }

                            FinishCurrentResultPosition();
                            inResultPositions = false;
                        }

                        if (currentSession is not null && TryReadKeyValue(trimmed, out var activeSessionKey, out var activeSessionValue))
                        {
                            currentSession.Values[activeSessionKey] = activeSessionValue;
                        }
                    }
                    break;

                case "DriverInfo":
                    if (indent == 1 && trimmed == "Drivers:")
                    {
                        FinishCurrentDriverTire();
                        inDrivers = true;
                        inDriverTires = false;
                        break;
                    }

                    if (indent == 1 && trimmed == "DriverTires:")
                    {
                        FinishCurrentDriver();
                        inDrivers = false;
                        inDriverTires = true;
                        break;
                    }

                    if (!inDrivers
                        && !inDriverTires
                        && indent == 1
                        && TryReadKeyValue(trimmed, out var driverInfoKey, out var driverInfoValue))
                    {
                        parsed.DriverInfo[driverInfoKey] = driverInfoValue;
                        break;
                    }

                    if (inDrivers)
                    {
                        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                        {
                            FinishCurrentDriver();
                            currentDriver = [];
                            if (TryReadKeyValue(trimmed, out var listKey, out var listValue))
                            {
                                currentDriver[listKey] = listValue;
                            }
                            break;
                        }

                        if (currentDriver is not null && TryReadKeyValue(trimmed, out var driverKey, out var driverValue))
                        {
                            currentDriver[driverKey] = driverValue;
                        }

                        break;
                    }

                    if (inDriverTires)
                    {
                        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                        {
                            FinishCurrentDriverTire();
                            currentDriverTire = [];
                            if (TryReadKeyValue(trimmed, out var listKey, out var listValue))
                            {
                                currentDriverTire[listKey] = listValue;
                            }
                            break;
                        }

                        if (currentDriverTire is not null && TryReadKeyValue(trimmed, out var tireKey, out var tireValue))
                        {
                            currentDriverTire[tireKey] = tireValue;
                        }
                    }
                    break;

                case "SplitTimeInfo":
                    if (indent == 1 && trimmed == "Sectors:")
                    {
                        inSectors = true;
                        break;
                    }

                    if (inSectors)
                    {
                        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                        {
                            FinishCurrentSector();
                            currentSector = [];
                            if (TryReadKeyValue(trimmed, out var sectorListKey, out var sectorListValue))
                            {
                                currentSector[sectorListKey] = sectorListValue;
                            }
                            break;
                        }

                        if (currentSector is not null && TryReadKeyValue(trimmed, out var sectorKey, out var sectorValue))
                        {
                            currentSector[sectorKey] = sectorValue;
                        }
                    }
                    break;

                case "QualifyResultsInfo":
                    if (indent == 1 && trimmed == "Results:")
                    {
                        inQualifyResults = true;
                        break;
                    }

                    if (inQualifyResults)
                    {
                        if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                        {
                            FinishCurrentQualifyResult();
                            currentQualifyResult = [];
                            if (TryReadKeyValue(trimmed, out var qualifyListKey, out var qualifyListValue))
                            {
                                currentQualifyResult[qualifyListKey] = qualifyListValue;
                            }
                            break;
                        }

                        if (currentQualifyResult is not null
                            && TryReadKeyValue(trimmed, out var qualifyKey, out var qualifyValue))
                        {
                            currentQualifyResult[qualifyKey] = qualifyValue;
                        }
                    }
                    break;
            }
        }

        FinishCurrentResultPosition();
        FinishCurrentQualifyResult();
        FinishCurrentSession();
        FinishCurrentDriver();
        FinishCurrentDriverTire();
        FinishCurrentSector();
        return parsed;

        void FinishCurrentSession()
        {
            if (currentSession is not null)
            {
                parsed.Sessions.Add(currentSession);
                currentSession = null;
            }
        }

        void FinishCurrentResultPosition()
        {
            if (currentSession is not null && currentResultPosition is not null)
            {
                currentSession.ResultPositions.Add(currentResultPosition);
                currentResultPosition = null;
            }
        }

        void FinishCurrentQualifyResult()
        {
            if (currentQualifyResult is not null)
            {
                parsed.QualifyResults.Add(currentQualifyResult);
                currentQualifyResult = null;
            }
        }

        void FinishCurrentDriver()
        {
            if (currentDriver is not null)
            {
                parsed.Drivers.Add(currentDriver);
                currentDriver = null;
            }
        }

        void FinishCurrentDriverTire()
        {
            if (currentDriverTire is not null)
            {
                parsed.DriverTires.Add(currentDriverTire);
                currentDriverTire = null;
            }
        }

        void FinishCurrentSector()
        {
            if (currentSector is not null)
            {
                parsed.Sectors.Add(currentSector);
                currentSector = null;
            }
        }
    }

    private static ParsedSession? SelectSession(ParsedSessionInfo parsed)
    {
        var currentSessionNum = ReadInt(parsed.SessionInfo, "CurrentSessionNum");
        if (currentSessionNum is not null)
        {
            var currentSession = parsed.Sessions.FirstOrDefault(session => ReadInt(session.Values, "SessionNum") == currentSessionNum);
            if (currentSession is not null)
            {
                return currentSession;
            }
        }

        return parsed.Sessions.FirstOrDefault();
    }

    private static IReadOnlyDictionary<string, string> SelectDriver(ParsedSessionInfo parsed)
    {
        var driverCarIdx = ReadInt(parsed.DriverInfo, "DriverCarIdx");
        if (driverCarIdx is not null)
        {
            var driver = parsed.Drivers.FirstOrDefault(candidate => ReadInt(candidate, "CarIdx") == driverCarIdx);
            if (driver is not null)
            {
                return driver;
            }
        }

        return parsed.Drivers.FirstOrDefault() ?? EmptyDictionary;
    }

    private static HistoricalSessionDriver ToDriver(IReadOnlyDictionary<string, string> values)
    {
        return new HistoricalSessionDriver
        {
            CarIdx = ReadInt(values, "CarIdx"),
            UserName = ReadString(values, "UserName"),
            AbbrevName = ReadString(values, "AbbrevName"),
            Initials = ReadString(values, "Initials"),
            UserId = ReadInt(values, "UserID"),
            TeamId = ReadInt(values, "TeamID"),
            TeamName = ReadString(values, "TeamName"),
            IRating = ReadInt(values, "IRating"),
            CarNumber = ReadString(values, "CarNumber"),
            CarPath = ReadString(values, "CarPath"),
            CarScreenName = ReadString(values, "CarScreenName"),
            CarScreenNameShort = ReadString(values, "CarScreenNameShort"),
            CarClassId = ReadInt(values, "CarClassID"),
            CarClassShortName = ReadString(values, "CarClassShortName"),
            CarClassRelSpeed = ReadInt(values, "CarClassRelSpeed"),
            CarClassEstLapTimeSeconds = ReadDouble(values, "CarClassEstLapTime"),
            CarClassColorHex = NormalizeColorHex(ReadString(values, "CarClassColor")),
            IsSpectator = ReadBool(values, "IsSpectator")
        };
    }

    private static HistoricalSessionTireCompound ToTireCompound(IReadOnlyDictionary<string, string> values)
    {
        return new HistoricalSessionTireCompound
        {
            TireIndex = ReadInt(values, "TireIndex"),
            TireCompoundType = ReadString(values, "TireCompoundType")
        };
    }

    private static HistoricalTrackSector ToSector(IReadOnlyDictionary<string, string> values)
    {
        return new HistoricalTrackSector
        {
            SectorNum = ReadInt(values, "SectorNum") ?? 0,
            SectorStartPct = ReadDouble(values, "SectorStartPct") ?? -1d
        };
    }

    private static HistoricalSessionResultPosition ToResultPosition(IReadOnlyDictionary<string, string> values)
    {
        return new HistoricalSessionResultPosition
        {
            Position = ReadInt(values, "Position"),
            ClassPosition = ReadInt(values, "ClassPosition"),
            CarIdx = ReadInt(values, "CarIdx"),
            Lap = ReadInt(values, "Lap"),
            TimeSeconds = ReadDouble(values, "Time"),
            FastestLap = ReadInt(values, "FastestLap"),
            FastestTimeSeconds = ReadDouble(values, "FastestTime"),
            LastTimeSeconds = ReadDouble(values, "LastTime"),
            LapsLed = ReadInt(values, "LapsLed"),
            LapsComplete = ReadInt(values, "LapsComplete"),
            LapsDriven = ReadDouble(values, "LapsDriven"),
            ReasonOut = ReadString(values, "ReasonOutStr")
        };
    }

    private static string? NormalizeColorHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var token = ReadLeadingToken(value).Trim();
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }
        else if (token.StartsWith('#'))
        {
            token = token[1..];
        }

        if (token.Length != 6 || token.Any(character => !Uri.IsHexDigit(character)))
        {
            return null;
        }

        return $"#{token.ToUpperInvariant()}";
    }

    private static bool TryReadKeyValue(string trimmedLine, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        if (trimmedLine.StartsWith("- ", StringComparison.Ordinal))
        {
            trimmedLine = trimmedLine[2..].TrimStart();
        }

        var separatorIndex = trimmedLine.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        key = trimmedLine[..separatorIndex].Trim();
        value = NormalizeString(trimmedLine[(separatorIndex + 1)..]);
        return value.Length > 0;
    }

    private static int CountLeadingSpaces(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static string? ReadString(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        return int.TryParse(ReadLeadingToken(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        return double.TryParse(ReadLeadingToken(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return null;
        }

        var token = ReadLeadingToken(value);
        if (bool.TryParse(token, out var parsedBool))
        {
            return parsedBool;
        }

        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
        {
            return parsedInt != 0;
        }

        return null;
    }

    private static string ReadLeadingToken(string value)
    {
        var trimmed = NormalizeString(value);
        var separator = trimmed.IndexOf(' ');
        return separator > 0 ? trimmed[..separator] : trimmed;
    }

    private static string NormalizeString(string value)
    {
        return value.Trim().Trim('"');
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyDictionary =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private sealed class ParsedSessionInfo
    {
        public Dictionary<string, string> WeekendInfo { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> SessionInfo { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> DriverInfo { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<ParsedSession> Sessions { get; } = [];

        public List<Dictionary<string, string>> Drivers { get; } = [];

        public List<Dictionary<string, string>> DriverTires { get; } = [];

        public List<Dictionary<string, string>> Sectors { get; } = [];

        public List<Dictionary<string, string>> QualifyResults { get; } = [];
    }

    private sealed class ParsedSession
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<Dictionary<string, string>> ResultPositions { get; } = [];
    }
}
