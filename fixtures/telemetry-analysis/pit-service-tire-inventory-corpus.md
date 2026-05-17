# Pit Service Tire Inventory Corpus

Compact redacted evidence from the May 2026 PCup and NASCAR captures.

Raw `telemetry.bin`, full session YAML, driver names, user IDs, and team names are intentionally not stored here.

## Sources

| Capture | Category | Session | Frames | Notes |
| --- | --- | --- | ---: | --- |
| capture-20260515-210810-124 | ai-nascar-limited-tire-race | AI NASCAR race at Charlotte Motor Speedway Oval | 55944 | Proves a positive dry tire set limit and meaningful limited inventory counters around pit/service context. |
| capture-20260516-204700-385 | pcup-open-practice-pit-service | Porsche 911 Cup open practice at Spa | 29363 | Proves non-zero available/used counters can appear while `PlayerCarDryTireSetLimit` is zero. |

## Decisions

- Render visible tire remaining only when `PlayerCarDryTireSetLimit` is positive.
- Treat the visible remaining value as one shared inventory count. Prefer `TireSetsAvailable`; only use corner/side counters when they agree.
- Do not render remaining tire cells in unlimited/open-practice contexts even when `*TiresAvailable` or `*TireSetsAvailable` is non-zero.
- Keep NASCAR `dpWeightJackerLeft` and `dpWeightJackerRight` as future setup-service evidence. They are wedge/weight-jacker request fields, not ARB or wing fields.
- No data-contract snapshot is needed. These captures changed telemetry evidence and overlay interpretation, not durable app-data schemas.

## Key Evidence

NASCAR limited inventory:

- `PlayerCarDryTireSetLimit` stayed at `13`.
- `TireSetsAvailable` showed `0`, `13`, `12`, then `11`.
- `TireSetsUsed` showed `0`, `1`, then `2`.
- During the real service stop, right-side/shared counters dropped at session time `755.167s`, left rear at `761.833s`, and left front at `762.167s`.
- Early/on-track `0` values are not enough by themselves to prove exhausted tires; they can mean the counter is not populated in that context.

PCup open-practice evidence:

- `PlayerCarDryTireSetLimit` stayed at `0`.
- `TireSetsAvailable`, `TireSetsUsed`, and corner available/used counters still showed `0` and `1`.
- The main service window shows `PitSvFuel`/`dpFuelAddKg` stepping from `110` down to `76`, `dpFuelFill` clearing, and tire request fields clearing during service.
- The non-zero counters are diagnostic/service-menu evidence, not valid visible limited-inventory evidence.

See `pit-service-tire-inventory-corpus.json` for the compact timeline and field groups.
