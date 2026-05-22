using TmrOverlay.Core.Telemetry.Live;

namespace TmrOverlay.App.Overlays.CarRadar;

internal sealed record CarRadarRenderModel(
    bool ShouldRender,
    double Width,
    double Height,
    int FadeInMilliseconds,
    int FadeOutMilliseconds,
    double MinimumVisibleAlpha,
    CarRadarRenderCircle Background,
    IReadOnlyList<CarRadarRenderCircle> Rings,
    IReadOnlyList<CarRadarRenderRectangle> Cars,
    IReadOnlyList<CarRadarRenderText> Labels,
    CarRadarRenderArc? MulticlassArc)
{
    public const int RefreshIntervalMilliseconds = 100;
    public const int FadeInMillisecondsDefault = 250;
    public const int FadeOutMillisecondsDefault = 850;
    public const double MinimumVisibleAlphaDefault = 0.02d;
    public const double DesignWidth = 300d;
    public const double DesignHeight = 300d;

    private const double RadarInset = 4d;
    private const double RadarDiameter = 292d;
    private const double RadarCenter = 150d;
    private const double RadarRadius = RadarDiameter / 2d;
    private const double ProximityWarningGapMeters = 2.0d;
    private const double ProximityRedStart = 0.74d;
    private const double TimingAwareEdgeOpacity = 0.2d;
    private const int MaxWideRowRadarCars = 18;
    private const double FocusedCarWidth = 20d;
    private const double FocusedCarHeight = 36d;
    private const double RadarCarWidth = 20d;
    private const double RadarCarHeight = 36d;
    private const double CarCornerRadius = 4d;
    private const double SeparatedCarPaddingPixels = 2d;
    private const double RadarEdgeCenterPaddingPixels = 2d;
    private const double GridRowReferenceMeters = 8d;
    private const double MinimumDistinctRowGapPixels = 48d;
    private const double WideRowBucketPixels = 30d;
    private const double WideRowLongitudinalBucketMeters = 2d;
    private const double WideRowSlotPitchPixels = 36d;
    private const double MulticlassWarningArcStartDegrees = 62.5d;
    private const double MulticlassWarningArcSweepDegrees = 55d;
    private static readonly CarRadarRenderColor OuterBorderStroke = new(0, 232, 255, 88);
    private static readonly CarRadarRenderColor InnerRingStroke = new(255, 255, 255, 40);
    private const double UsableRadarRadius = RadarRadius - RadarEdgeCenterPaddingPixels;
    private const double DistinctRowPixelsPerMeter = MinimumDistinctRowGapPixels / GridRowReferenceMeters;

    public static CarRadarRenderModel Empty { get; } = new(
        ShouldRender: false,
        Width: DesignWidth,
        Height: DesignHeight,
        FadeInMilliseconds: FadeInMillisecondsDefault,
        FadeOutMilliseconds: FadeOutMillisecondsDefault,
        MinimumVisibleAlpha: MinimumVisibleAlphaDefault,
        Background: BackgroundCircle(),
        Rings: [],
        Cars: [],
        Labels: [],
        MulticlassArc: null);

    public static CarRadarRenderModel FromViewModel(
        CarRadarOverlayViewModel viewModel,
        CarRadarCalibrationProfile? calibrationProfile = null)
    {
        return FromState(
            viewModel.IsAvailable,
            viewModel.HasCarLeft,
            viewModel.HasCarRight,
            viewModel.Cars,
            viewModel.StrongestMulticlassApproach,
            viewModel.ShowMulticlassWarning,
            viewModel.PreviewVisible,
            viewModel.HasCurrentSignal,
            viewModel.Spatial.ReferenceCarClassColorHex,
            calibrationProfile,
            viewModel.RadarVisibilitySeconds);
    }

    public static CarRadarRenderModel FromState(
        bool isAvailable,
        bool hasCarLeft,
        bool hasCarRight,
        IReadOnlyList<LiveSpatialCar> cars,
        LiveMulticlassApproach? strongestMulticlassApproach,
        bool showMulticlassWarning,
        bool previewVisible,
        bool hasCurrentSignal,
        string? referenceCarClassColorHex = null,
        CarRadarCalibrationProfile? calibrationProfile = null,
        int radarVisibilitySeconds = CarRadarOverlayViewModel.DefaultRadarVisibilitySeconds)
    {
        var shouldRender = (isAvailable && hasCurrentSignal) || previewVisible;
        if (!shouldRender)
        {
            return Empty;
        }

        var geometry = CarRadarGeometry.From(calibrationProfile, radarVisibilitySeconds);
        var background = BackgroundCircle();
        var rings = DistanceRings(geometry);
        var labels = new List<CarRadarRenderText>(rings.Count + 1);
        labels.AddRange(rings.Select(ring => ring.Label).OfType<CarRadarRenderText>());

        var currentCars = cars
            .Where(car => IsInRadarRange(car, geometry))
            .GroupBy(car => car.CarIdx)
            .Select(group => group.MinBy(car => Math.Abs(RangeRatio(car, geometry)))!)
            .ToArray();
        var sideAttachments = SideWarningAttachmentsFor(hasCarLeft, hasCarRight, currentCars, geometry);
        var renderCars = new List<CarRadarRenderRectangle>();

        foreach (var placement in RadarCarPlacements(currentCars, sideAttachments, geometry))
        {
            renderCars.Add(NearbyCarRectangle(placement, geometry));
        }

        renderCars.AddRange(SideWarningRectangles(hasCarLeft, hasCarRight, sideAttachments, geometry));
        renderCars.Add(PlayerCarRectangle(referenceCarClassColorHex));

        var multiclassArc = showMulticlassWarning && strongestMulticlassApproach is not null
            ? MulticlassApproachArc(strongestMulticlassApproach)
            : null;
        if (multiclassArc?.Label is { } label)
        {
            labels.Add(label);
        }

        return new CarRadarRenderModel(
            ShouldRender: true,
            Width: DesignWidth,
            Height: DesignHeight,
            FadeInMilliseconds: FadeInMillisecondsDefault,
            FadeOutMilliseconds: FadeOutMillisecondsDefault,
            MinimumVisibleAlpha: MinimumVisibleAlphaDefault,
            Background: background,
            Rings: rings,
            Cars: renderCars,
            Labels: labels,
            MulticlassArc: multiclassArc);
    }

    private static CarRadarRenderCircle BackgroundCircle()
    {
        return new CarRadarRenderCircle(
            X: RadarInset,
            Y: RadarInset,
            Width: RadarDiameter,
            Height: RadarDiameter,
            Fill: new CarRadarRenderColor(12, 18, 22, 82),
            Stroke: OuterBorderStroke,
            StrokeWidth: 1.2d,
            Label: null);
    }

    private static IReadOnlyList<CarRadarRenderCircle> DistanceRings(CarRadarGeometry geometry)
    {
        var rings = new List<CarRadarRenderCircle>(2);
        for (var index = 1; index <= 2; index++)
        {
            var inset = RadarDiameter * index / 6d;
            var radius = RadarDiameter / 2d - inset;
            var x = RadarInset + inset;
            var y = RadarInset + inset;
            rings.Add(new CarRadarRenderCircle(
                X: x,
                Y: y,
                Width: RadarDiameter - inset * 2d,
                Height: RadarDiameter - inset * 2d,
                Fill: null,
                Stroke: InnerRingStroke,
                StrokeWidth: 1d,
                Label: new CarRadarRenderText(
                    FormatRingDistance(radius, geometry),
                    X: RadarCenter + radius * 0.35d,
                    Y: RadarCenter - radius - 8d,
                    Width: 58d,
                    Height: 16d,
                    FontSize: 7.5d,
                    Bold: false,
                    Alignment: "near",
                    Color: new CarRadarRenderColor(220, 230, 236, 118))));
        }

        return rings;
    }

    private static SideWarningAttachments SideWarningAttachmentsFor(
        bool hasCarLeft,
        bool hasCarRight,
        IReadOnlyList<LiveSpatialCar> cars,
        CarRadarGeometry geometry)
    {
        var usedCarIdxs = new HashSet<int>();
        var left = hasCarLeft ? SelectSideAttachment(cars, usedCarIdxs, geometry) : null;
        if (left is not null)
        {
            usedCarIdxs.Add(left.CarIdx);
        }

        var right = hasCarRight ? SelectSideAttachment(cars, usedCarIdxs, geometry) : null;
        return new SideWarningAttachments(left, right);
    }

    private static LiveSpatialCar? SelectSideAttachment(
        IReadOnlyList<LiveSpatialCar> cars,
        ISet<int> excludedCarIdxs,
        CarRadarGeometry geometry)
    {
        return cars
            .Where(car => !excludedCarIdxs.Contains(car.CarIdx))
            .Where(car => IsSideAttachmentCandidate(car, geometry))
            .OrderBy(car => Math.Abs(RangeRatio(car, geometry)))
            .ThenBy(car => car.CarIdx)
            .FirstOrDefault();
    }

    private static bool IsSideAttachmentCandidate(LiveSpatialCar car, CarRadarGeometry geometry)
    {
        return IsInRadarRange(car, geometry)
            && CarRadarOverlayViewModel.ReliableRelativeMeters(car) is { } meters
            && Math.Abs(meters) <= geometry.SideAttachmentWindowMeters;
    }

    private static IEnumerable<RadarCarPlacement> RadarCarPlacements(
        IReadOnlyList<LiveSpatialCar> cars,
        SideWarningAttachments sideAttachments,
        CarRadarGeometry geometry)
    {
        var usableRadius = UsableRadarRadius;
        var visibleCars = cars
            .Where(car => !sideAttachments.Contains(car.CarIdx))
            .OrderBy(car => Math.Abs(RangeRatio(car, geometry)))
            .Take(MaxWideRowRadarCars)
            .ToArray();
        var candidates = visibleCars
            .Select((car, index) =>
            {
                var offset = LongitudinalOffset(car, usableRadius, geometry);
                return new WideRowCandidate(
                    car,
                    index,
                    offset,
                    CarRadarOverlayViewModel.ReliableRelativeMeters(car),
                    PlacementDirection(car, index, offset));
            })
            .ToArray();
        var rows = new List<WideRadarRow>();

        foreach (var candidate in candidates.OrderBy(candidate => candidate.IdealOffset))
        {
            var row = rows.FirstOrDefault(row => IsSameWideRadarRow(row, candidate));
            if (row is null)
            {
                rows.Add(new WideRadarRow(
                    candidate.IdealOffset,
                    candidate.LongitudinalMeters,
                    candidate.Direction,
                    [candidate]));
                continue;
            }

            row.Candidates.Add(candidate);
        }

        return rows.SelectMany(row => PlacementsForRow(row, usableRadius));
    }

    private static bool IsSameWideRadarRow(WideRadarRow row, WideRowCandidate candidate)
    {
        if (Math.Abs(row.Direction - candidate.Direction) > 0.001d)
        {
            return false;
        }

        if (row.AnchorMeters is { } rowMeters && candidate.LongitudinalMeters is { } candidateMeters)
        {
            return Math.Abs(rowMeters - candidateMeters) <= WideRowLongitudinalBucketMeters;
        }

        return Math.Abs(row.AnchorOffset - candidate.IdealOffset) <= WideRowBucketPixels;
    }

    private static IReadOnlyList<RadarCarPlacement> PlacementsForRow(WideRadarRow row, double usableRadius)
    {
        if (row.Candidates.Count == 0)
        {
            return [];
        }

        var rowOffset = row.Candidates.Sum(candidate => candidate.IdealOffset) / row.Candidates.Count;
        var clampedRowMagnitude = Math.Min(Math.Abs(rowOffset), usableRadius);
        var availableHalfWidth = Math.Sqrt(Math.Max(
            0d,
            usableRadius * usableRadius - clampedRowMagnitude * clampedRowMagnitude));
        var maxCenterOffset = Math.Max(0d, availableHalfWidth - RadarCarWidth / 2d - 4d);
        var minimumSlots = row.Candidates.Count > 1 ? 2 : 1;
        var maxSlots = Math.Max(minimumSlots, (int)Math.Floor(maxCenterOffset * 2d / WideRowSlotPitchPixels) + 1);
        var visibleCandidates = row.Candidates
            .OrderBy(candidate => candidate.SourceIndex)
            .ThenBy(candidate => candidate.Car.CarIdx)
            .Take(maxSlots)
            .ToArray();
        var xOffsets = FocusOverlappingRow(rowOffset)
            ? FocusAvoidingXOffsets(visibleCandidates.Length, maxCenterOffset)
            : CenteredXOffsets(visibleCandidates.Length);
        var placements = new List<RadarCarPlacement>(Math.Min(visibleCandidates.Length, xOffsets.Length));
        for (var slotIndex = 0; slotIndex < visibleCandidates.Length && slotIndex < xOffsets.Length; slotIndex++)
        {
            var candidate = visibleCandidates[slotIndex];
            placements.Add(new RadarCarPlacement(
                candidate.Car,
                X: RadarCenter + xOffsets[slotIndex] - RadarCarWidth / 2d,
                Y: RadarCenter - rowOffset - RadarCarHeight / 2d,
                Offset: rowOffset));
        }

        return placements;
    }

    private static double[] CenteredXOffsets(int count)
    {
        var lineWidth = WideRowSlotPitchPixels * Math.Max(0, count - 1);
        return Enumerable
            .Range(0, count)
            .Select(index => index * WideRowSlotPitchPixels - lineWidth / 2d)
            .ToArray();
    }

    private static bool FocusOverlappingRow(double rowOffset)
    {
        var verticalSeparation = FocusedCarHeight / 2d + RadarCarHeight / 2d + 4d;
        return Math.Abs(rowOffset) < verticalSeparation;
    }

    private static double[] FocusAvoidingXOffsets(int count, double maxCenterOffset)
    {
        var minimumOffset = FocusedCarWidth / 2d + RadarCarWidth / 2d + 22d;
        if (maxCenterOffset < minimumOffset)
        {
            return CenteredXOffsets(count);
        }

        var offsets = new List<double>(count);
        var signs = count == 1 ? new[] { 1d } : new[] { -1d, 1d };
        for (var lane = 0; offsets.Count < count; lane++)
        {
            foreach (var sign in signs)
            {
                var offset = sign * (minimumOffset + lane * WideRowSlotPitchPixels);
                if (Math.Abs(offset) <= maxCenterOffset)
                {
                    offsets.Add(offset);
                }

                if (offsets.Count >= count)
                {
                    break;
                }
            }

            if (minimumOffset + lane * WideRowSlotPitchPixels > maxCenterOffset + WideRowSlotPitchPixels)
            {
                break;
            }
        }

        return offsets.Count > 0 ? offsets.ToArray() : CenteredXOffsets(count);
    }

    private static CarRadarRenderRectangle NearbyCarRectangle(RadarCarPlacement placement, CarRadarGeometry geometry)
    {
        var car = placement.Car;
        var visualAlpha = RadarEntryOpacity(car, geometry);
        return new CarRadarRenderRectangle(
            Kind: "nearby",
            CarIdx: car.CarIdx,
            X: placement.X,
            Y: placement.Y,
            Width: RadarCarWidth,
            Height: RadarCarHeight,
            Radius: CarCornerRadius,
            Fill: ProximityColor(ProximityTint(car, geometry), visualAlpha),
            Stroke: ClassBorderColor(car.CarClassColorHex, visualAlpha),
            StrokeWidth: 2d);
    }

    private static IReadOnlyList<CarRadarRenderRectangle> SideWarningRectangles(
        bool hasCarLeft,
        bool hasCarRight,
        SideWarningAttachments sideAttachments,
        CarRadarGeometry geometry)
    {
        var rectangles = new List<CarRadarRenderRectangle>(2);
        var usableRadius = UsableRadarRadius;
        if (hasCarLeft)
        {
            rectangles.Add(SideWarningRectangle(
                side: "left",
                centerX: RadarCenter - 42d,
                centerY: SideWarningCenterY(usableRadius, sideAttachments.Left, geometry),
                attachment: sideAttachments.Left));
        }

        if (hasCarRight)
        {
            rectangles.Add(SideWarningRectangle(
                side: "right",
                centerX: RadarCenter + 42d,
                centerY: SideWarningCenterY(usableRadius, sideAttachments.Right, geometry),
                attachment: sideAttachments.Right));
        }

        return rectangles;
    }

    private static CarRadarRenderRectangle SideWarningRectangle(
        string side,
        double centerX,
        double centerY,
        LiveSpatialCar? attachment)
    {
        var mappedToTimedCar = attachment is not null;
        var fillAlpha = mappedToTimedCar ? 245 : 238;
        return new CarRadarRenderRectangle(
            Kind: $"side-{side}",
            CarIdx: attachment?.CarIdx,
            X: centerX - RadarCarWidth / 2d,
            Y: centerY - RadarCarHeight / 2d,
            Width: RadarCarWidth,
            Height: RadarCarHeight,
            Radius: CarCornerRadius,
            Fill: new CarRadarRenderColor(236, 112, 99, fillAlpha),
            Stroke: ClassBorderColor(attachment?.CarClassColorHex, fillAlpha / 255d),
            StrokeWidth: 2d);
    }

    private static double SideWarningCenterY(double usableRadius, LiveSpatialCar? car, CarRadarGeometry geometry)
    {
        if (car is null)
        {
            return RadarCenter;
        }

        var maximumBias = FocusedCarHeight * 0.55d;
        var offset = Math.Clamp(LongitudinalOffset(car, usableRadius, geometry), -maximumBias, maximumBias);
        return RadarCenter - offset;
    }

    private static CarRadarRenderRectangle PlayerCarRectangle(string? referenceCarClassColorHex)
    {
        return new CarRadarRenderRectangle(
            Kind: "focus",
            CarIdx: null,
            X: RadarCenter - FocusedCarWidth / 2d,
            Y: RadarCenter - FocusedCarHeight / 2d,
            Width: FocusedCarWidth,
            Height: FocusedCarHeight,
            Radius: CarCornerRadius,
            Fill: new CarRadarRenderColor(255, 255, 255, 240),
            Stroke: ClassBorderColor(referenceCarClassColorHex, 1d),
            StrokeWidth: 2d);
    }

    private static CarRadarRenderArc MulticlassApproachArc(LiveMulticlassApproach approach)
    {
        var urgency = Math.Clamp(approach.Urgency, 0d, 1d);
        var alpha = (int)Math.Round(120d + urgency * 110d);
        var color = new CarRadarRenderColor(236, 112, 99, alpha);
        return new CarRadarRenderArc(
            X: RadarInset + 4d,
            Y: RadarInset + 4d,
            Width: RadarDiameter - 8d,
            Height: RadarDiameter - 8d,
            StartDegrees: MulticlassWarningArcStartDegrees,
            SweepDegrees: MulticlassWarningArcSweepDegrees,
            StrokeWidth: 5d,
            Stroke: color,
            Label: new CarRadarRenderText(
                FormatMulticlassWarning(approach),
                X: RadarInset + 28d,
                Y: RadarInset + RadarDiameter - 48d,
                Width: RadarDiameter - 56d,
                Height: 18d,
                FontSize: 9d,
                Bold: true,
                Alignment: "center",
                Color: new CarRadarRenderColor(255, 225, 220, alpha)));
    }

    private static double LongitudinalOffset(
        LiveSpatialCar car,
        double usableRadius,
        CarRadarGeometry geometry)
    {
        if (CarRadarOverlayViewModel.ReliableRelativeMeters(car) is { } meters)
        {
            return LongitudinalOffsetFromDistance(PlacementMeters(meters, geometry), usableRadius, geometry);
        }

        return Math.Sign(car.RelativeLaps) * usableRadius;
    }

    private static double LongitudinalOffsetFromDistance(
        double meters,
        double usableRadius,
        CarRadarGeometry geometry)
    {
        var sign = Math.Sign(meters);
        if (sign == 0)
        {
            return 0d;
        }

        var absMeters = Math.Abs(meters);
        var separatedCenterOffset = SeparatedCenterOffset(usableRadius);

        if (absMeters <= geometry.ContactWindowMeters)
        {
            return sign * (absMeters / geometry.ContactWindowMeters) * separatedCenterOffset;
        }

        var rowAwareOffset = separatedCenterOffset
            + (absMeters - geometry.ContactWindowMeters) * DistinctRowPixelsPerMeter;
        return sign * rowAwareOffset;
    }

    private static double PlacementDirection(LiveSpatialCar car, int index, double idealOffset)
    {
        if (idealOffset < 0d)
        {
            return -1d;
        }

        if (idealOffset > 0d)
        {
            return 1d;
        }

        if (Math.Abs(car.RelativeLaps) > 0.0001d)
        {
            return car.RelativeLaps < 0d ? -1d : 1d;
        }

        return index % 2 == 0 ? 1d : -1d;
    }

    private static double RangeRatio(LiveSpatialCar car, CarRadarGeometry geometry)
    {
        if (CarRadarOverlayViewModel.ReliableRelativeMeters(car) is { } meters)
        {
            return Math.Clamp(meters / VisualRadarRangeMeters(car, geometry), -1d, 1d);
        }

        return Math.Sign(car.RelativeLaps);
    }

    private static CarRadarRenderColor ProximityColor(double proximityTint, double visualAlpha)
    {
        var normalized = Math.Clamp(proximityTint, 0d, 1d);
        var alpha = ScaleAlpha(238, visualAlpha);
        var baseColor = (Red: 255, Green: 255, Blue: 255);
        var yellow = (Red: 255, Green: 220, Blue: 66);
        var alertRed = (Red: 255, Green: 24, Blue: 16);

        if (normalized <= 0d)
        {
            return new CarRadarRenderColor(baseColor.Red, baseColor.Green, baseColor.Blue, alpha);
        }

        if (normalized < ProximityRedStart)
        {
            var yellowMix = SmoothStep(0d, ProximityRedStart, normalized);
            return new CarRadarRenderColor(
                Lerp(baseColor.Red, yellow.Red, yellowMix),
                Lerp(baseColor.Green, yellow.Green, yellowMix),
                Lerp(baseColor.Blue, yellow.Blue, yellowMix),
                alpha);
        }

        var redMix = SmoothStep(ProximityRedStart, 1d, normalized);
        return new CarRadarRenderColor(
            Lerp(yellow.Red, alertRed.Red, redMix),
            Lerp(yellow.Green, alertRed.Green, redMix),
            Lerp(yellow.Blue, alertRed.Blue, redMix),
            alpha);
    }

    private static double ProximityTint(LiveSpatialCar car, CarRadarGeometry geometry)
    {
        return CarRadarOverlayViewModel.ReliableRelativeMeters(car) is { } meters
            ? BumperGapProximity(Math.Abs(meters), geometry)
            : 0d;
    }

    private static double RadarEntryOpacity(LiveSpatialCar car, CarRadarGeometry geometry)
    {
        if (CarRadarOverlayViewModel.ReliableRelativeMeters(car) is { } meters)
        {
            var warningStartMeters = geometry.ContactWindowMeters + ProximityWarningGapMeters;
            var physicalOpacity = OpacityBetweenRangeEdgeAndWarningStart(
                Math.Abs(meters),
                warningStartMeters,
                geometry.PhysicalRadarRangeMeters);
            var timingAwareOpacity = TimingAwareEntryOpacity(
                Math.Abs(meters),
                VisualRadarRangeMeters(car, geometry),
                geometry);
            return Math.Max(physicalOpacity, timingAwareOpacity);
        }

        return 0d;
    }

    private static double TimingAwareEntryOpacity(
        double absoluteMeters,
        double visualRangeMeters,
        CarRadarGeometry geometry)
    {
        if (visualRangeMeters <= geometry.PhysicalRadarRangeMeters || absoluteMeters >= visualRangeMeters)
        {
            return 0d;
        }

        if (absoluteMeters <= geometry.PhysicalRadarRangeMeters)
        {
            return TimingAwareEdgeOpacity;
        }

        var normalized = 1d - Math.Clamp(
            (absoluteMeters - geometry.PhysicalRadarRangeMeters)
                / Math.Max(0.001d, visualRangeMeters - geometry.PhysicalRadarRangeMeters),
            0d,
            1d);
        return TimingAwareEdgeOpacity * SmoothStep(0d, 1d, normalized);
    }

    private static double PlacementMeters(double meters, CarRadarGeometry geometry)
    {
        return Math.Sign(meters) * Math.Min(Math.Abs(meters), geometry.PhysicalRadarRangeMeters);
    }

    private static double OpacityBetweenRangeEdgeAndWarningStart(
        double absoluteValue,
        double warningStart,
        double radarRange)
    {
        if (absoluteValue <= warningStart)
        {
            return 1d;
        }

        if (radarRange <= warningStart)
        {
            return absoluteValue <= radarRange ? 1d : 0d;
        }

        var normalized = 1d - Math.Clamp((absoluteValue - warningStart) / (radarRange - warningStart), 0d, 1d);
        return SmoothStep(0d, 1d, normalized);
    }

    private static double BumperGapProximity(double centerDistanceMeters, CarRadarGeometry geometry)
    {
        var bumperGapMeters = centerDistanceMeters - geometry.BodyLengthMeters;
        return 1d - Math.Clamp(bumperGapMeters / ProximityWarningGapMeters, 0d, 1d);
    }

    private static int ScaleAlpha(int alpha, double multiplier)
    {
        return (int)Math.Round(Math.Clamp(alpha * multiplier, 0d, 255d));
    }

    private static CarRadarRenderColor ClassBorderColor(string? colorHex, double visualAlpha)
    {
        return TryParseHexColor(colorHex, out var color)
            ? color with { Alpha = ScaleAlpha(245, visualAlpha) }
            : new CarRadarRenderColor(255, 255, 255, ScaleAlpha(245, visualAlpha));
    }

    private static bool TryParseHexColor(string? value, out CarRadarRenderColor color)
    {
        color = new CarRadarRenderColor(255, 255, 255, 255);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var token = value.Trim();
        if (token.StartsWith('#'))
        {
            token = token[1..];
        }

        if (token.Length != 6 || token.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        color = new CarRadarRenderColor(
            Convert.ToInt32(token[..2], 16),
            Convert.ToInt32(token[2..4], 16),
            Convert.ToInt32(token[4..6], 16),
            255);
        return true;
    }

    private static int Lerp(int start, int end, double ratio)
    {
        return (int)Math.Round(start + (end - start) * ratio);
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        var ratio = Math.Clamp((value - edge0) / (edge1 - edge0), 0d, 1d);
        return ratio * ratio * (3d - 2d * ratio);
    }

    private static double SeparatedCenterOffset(double usableRadius)
    {
        return Math.Min(
            usableRadius,
            FocusedCarHeight / 2d + RadarCarHeight / 2d + SeparatedCarPaddingPixels);
    }

    private static bool IsInRadarRange(LiveSpatialCar car, CarRadarGeometry geometry)
    {
        return CarRadarOverlayViewModel.ReliableRelativeMeters(car) is { } meters
            && Math.Abs(meters) <= VisualRadarRangeMeters(car, geometry);
    }

    private static double VisualRadarRangeMeters(LiveSpatialCar car, CarRadarGeometry geometry)
    {
        var range = geometry.PhysicalRadarRangeMeters;
        if (CarRadarOverlayViewModel.ReliableRelativeMeters(car) is not { } meters
            || car.RelativeSeconds is not { } seconds
            || double.IsNaN(seconds)
            || double.IsInfinity(seconds))
        {
            return range;
        }

        var absMeters = Math.Abs(meters);
        var absSeconds = Math.Abs(seconds);
        if (absMeters <= range || absSeconds <= 0.05d)
        {
            return range;
        }

        var inferredMetersPerSecond = absMeters / absSeconds;
        if (double.IsNaN(inferredMetersPerSecond)
            || double.IsInfinity(inferredMetersPerSecond)
            || inferredMetersPerSecond <= 0d)
        {
            return range;
        }

        var timingAwareRange = inferredMetersPerSecond * geometry.TimingAwareVisibilitySeconds;
        return Math.Clamp(
            Math.Max(range, timingAwareRange),
            range,
            geometry.MaximumTimingAwareRangeMeters);
    }

    private static string FormatRingDistance(double offsetPixels, CarRadarGeometry geometry)
    {
        var meters = DistanceForLongitudinalOffset(offsetPixels, geometry);
        return FormattableString.Invariant($"{meters:0}m");
    }

    private static double DistanceForLongitudinalOffset(double offsetPixels, CarRadarGeometry geometry)
    {
        var separatedCenterOffset = SeparatedCenterOffset(UsableRadarRadius);
        var absOffset = Math.Clamp(Math.Abs(offsetPixels), 0d, UsableRadarRadius);
        if (absOffset <= separatedCenterOffset)
        {
            return geometry.ContactWindowMeters * absOffset / Math.Max(0.001d, separatedCenterOffset);
        }

        return geometry.ContactWindowMeters + (absOffset - separatedCenterOffset) / DistinctRowPixelsPerMeter;
    }

    private static string FormatMulticlassWarning(LiveMulticlassApproach approach)
    {
        return approach.RelativeSeconds is { } seconds
            ? FormattableString.Invariant($"Faster class approaching {Math.Abs(seconds):0.0}s")
            : "Faster class approaching";
    }

    private sealed record RadarCarPlacement(LiveSpatialCar Car, double X, double Y, double Offset);

    private sealed record SideWarningAttachments(LiveSpatialCar? Left, LiveSpatialCar? Right)
    {
        public bool Contains(int carIdx)
        {
            return Left?.CarIdx == carIdx || Right?.CarIdx == carIdx;
        }
    }

    private sealed record WideRowCandidate(
        LiveSpatialCar Car,
        int SourceIndex,
        double IdealOffset,
        double? LongitudinalMeters,
        double Direction);

    private sealed record WideRadarRow(
        double AnchorOffset,
        double? AnchorMeters,
        double Direction,
        List<WideRowCandidate> Candidates);

    private sealed record CarRadarGeometry(
        double BodyLengthMeters,
        double PhysicalRadarRangeMeters,
        double ContactWindowMeters,
        double SideAttachmentWindowMeters,
        double MaximumTimingAwareRangeMeters,
        double TimingAwareVisibilitySeconds)
    {
        public static CarRadarGeometry From(
            CarRadarCalibrationProfile? calibrationProfile,
            int radarVisibilitySeconds = CarRadarOverlayViewModel.DefaultRadarVisibilitySeconds)
        {
            var bodyLengthMeters = calibrationProfile?.BodyLengthMeters ?? CarRadarCalibrationProfile.DefaultBodyLengthMeters;
            if (double.IsNaN(bodyLengthMeters) || double.IsInfinity(bodyLengthMeters) || bodyLengthMeters <= 0d)
            {
                bodyLengthMeters = CarRadarCalibrationProfile.DefaultBodyLengthMeters;
            }

            return new CarRadarGeometry(
                BodyLengthMeters: bodyLengthMeters,
                PhysicalRadarRangeMeters: bodyLengthMeters * 6d,
                ContactWindowMeters: bodyLengthMeters,
                SideAttachmentWindowMeters: bodyLengthMeters * 2d,
                MaximumTimingAwareRangeMeters: bodyLengthMeters * 15d,
                TimingAwareVisibilitySeconds: CarRadarOverlayViewModel.ClampRadarVisibilitySeconds(radarVisibilitySeconds));
        }
    }
}

internal sealed record CarRadarRenderCircle(
    double X,
    double Y,
    double Width,
    double Height,
    CarRadarRenderColor? Fill,
    CarRadarRenderColor? Stroke,
    double StrokeWidth,
    CarRadarRenderText? Label);

internal sealed record CarRadarRenderRectangle(
    string Kind,
    int? CarIdx,
    double X,
    double Y,
    double Width,
    double Height,
    double Radius,
    CarRadarRenderColor Fill,
    CarRadarRenderColor Stroke,
    double StrokeWidth);

internal sealed record CarRadarRenderArc(
    double X,
    double Y,
    double Width,
    double Height,
    double StartDegrees,
    double SweepDegrees,
    double StrokeWidth,
    CarRadarRenderColor Stroke,
    CarRadarRenderText? Label);

internal sealed record CarRadarRenderText(
    string Text,
    double X,
    double Y,
    double Width,
    double Height,
    double FontSize,
    bool Bold,
    string Alignment,
    CarRadarRenderColor Color);

internal sealed record CarRadarRenderColor(int Red, int Green, int Blue, int Alpha);
