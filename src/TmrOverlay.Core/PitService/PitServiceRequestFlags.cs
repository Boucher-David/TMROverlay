namespace TmrOverlay.Core.PitService;

// iRacing's PitSvFlags bit layout is shared raw telemetry semantics. Keep the
// decode outside an overlay or fuel calculator so live Pit Service, collection,
// and future strategy composition cannot drift apart.
internal static class PitServiceRequestFlags
{
    private const int LeftFrontTire = 0x01;
    private const int RightFrontTire = 0x02;
    private const int LeftRearTire = 0x04;
    private const int RightRearTire = 0x08;
    private const int FuelService = 0x10;
    private const int TearoffService = 0x20;
    private const int FastRepairService = 0x40;

    public static PitServiceRequestFlagSelection Decode(int? flags)
    {
        var value = flags.GetValueOrDefault();
        return new PitServiceRequestFlagSelection(
            LeftFrontTire: (value & LeftFrontTire) != 0,
            RightFrontTire: (value & RightFrontTire) != 0,
            LeftRearTire: (value & LeftRearTire) != 0,
            RightRearTire: (value & RightRearTire) != 0,
            Fuel: (value & FuelService) != 0,
            Tearoff: (value & TearoffService) != 0,
            FastRepair: (value & FastRepairService) != 0);
    }
}

internal sealed record PitServiceRequestFlagSelection(
    bool LeftFrontTire,
    bool RightFrontTire,
    bool LeftRearTire,
    bool RightRearTire,
    bool Fuel,
    bool Tearoff,
    bool FastRepair)
{
    public int RequestedTireCount =>
        (LeftFrontTire ? 1 : 0)
        + (RightFrontTire ? 1 : 0)
        + (LeftRearTire ? 1 : 0)
        + (RightRearTire ? 1 : 0);
}
