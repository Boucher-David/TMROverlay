# iRacing Fuel Burn Telemetry Notes

This is a research note for learning how iRacing reports fuel-flow and engine
state telemetry. It is intentionally separate from `fuel-calculator-v2.md`: this
file records observed behavior, useful tests, and interpretation rules. Product
strategy decisions should be promoted into Fuel Calculator docs only after the
behavior is validated.

## Working Interpretation

`FuelUsePerHour` is an instantaneous engine fuel-flow signal in `kg/h`. It is not
fuel per lap, not fuel to finish, and not a standalone efficiency metric.

For combustion captures, convert it to liters with `DriverCarFuelKgPerLtr`:

```text
litersPerHour = FuelUsePerHour / DriverCarFuelKgPerLtr
liters += litersPerHour * deltaSeconds / 3600
```

The useful race metric is distance-integrated burn:

```text
litersPerKm = integratedLiters / distanceKm
litersPerLap = litersPerKm * trackLengthKm
```

`FuelUsePerHour` needs vehicle-state context before it means anything useful:

- `Throttle` / `ThrottleRaw`
- `Clutch` / `ClutchRaw`
- `Gear`
- `RPM` / `Engine0_RPM`
- `Speed`
- `Brake`
- `LapDistPct`, `LapCompleted`, `LapDist`
- `IsOnTrack`, `OnPitRoad`, `PlayerTrackSurface`
- `FuelLevel`

The current pattern is:

- Moving, clutch-engaged samples can make `FuelUsePerHour` useful for flow
  integration.
- Stationary or clutch-disengaged samples can make RPM/throttle look misleading.
- Raw `kg/h` is not fuel efficiency because speed and distance matter.
- Gear is usually not an independent fuel-flow signal. It mostly changes RPM,
  speed, load, and distance covered per unit time.

## Previous Burn Investigation

The first frame-level correlation pass used Dallara P217 and Toyota GR86 race
captures from Nurburgring.

Dallara P217, `capture-20260522-204847-774`, 36,689 usable sampled clean
on-track frames:

| Signal | Corr With `FuelUsePerHour` |
| --- | ---: |
| `Throttle` | 0.990 |
| `RPM` | 0.569 |
| `Speed` | 0.513 |
| `Gear` | 0.474 |
| `RPM * throttle` | 0.996 |

Toyota GR86, `capture-20260523-200213-824`, 32,965 usable sampled clean on-track
frames:

| Signal | Corr With `FuelUsePerHour` |
| --- | ---: |
| `Throttle` | 0.975 |
| `RPM` | 0.516 |
| `Speed` | 0.365 |
| `Gear` | 0.254 |
| `RPM * throttle` | 0.994 |

At near-100% throttle, fuel flow still rose with RPM inside the same gear:

| Capture | Gear | Approx kg/h Per 1000 RPM |
| --- | ---: | ---: |
| Dallara P217 | 1 | 15.7 |
| Dallara P217 | 2 | 15.1 |
| Dallara P217 | 3 | 9.3 |
| Dallara P217 | 4 | 8.1 |
| Dallara P217 | 5 | 7.6 |
| Dallara P217 | 6 | 6.4 |
| Toyota GR86 | 1 | 6.0 |
| Toyota GR86 | 2 | 4.6 |
| Toyota GR86 | 3 | 4.0 |
| Toyota GR86 | 4 | 3.8 |
| Toyota GR86 | 5 | 3.6 |
| Toyota GR86 | 6 | 4.9 |

Same-RPM-bin comparisons reduced the apparent gear effect. For the Dallara P217,
raw WOT gear means ranged from about `94.8` to `106.4 kg/h`, but same-RPM-bin
gear spread was only about `0.7 kg/h` in the 7000-7499 RPM bin and `0.2 kg/h` in
the 7500-7999 RPM bin. For the GR86, matched 500 RPM bins with multiple gears
spread only about `0.3` to `0.7 kg/h`.

The 4-hour GT3 Nurburgring capture gave a cleaner long-straight comparison. In
track-position windows where gear 5 and gear 6 appeared at similar WOT fuel flow,
both gears averaged about `106 kg/h`, but gear 6 was around `265 kph` while gear
5 was around `234-236 kph`. That means gear 6 did not greatly reduce raw fuel
flow, but it did improve fuel per distance because the car covered more distance
per hour.

Race-speed fuel-per-distance examples from clean WOT frames:

| Capture | Gear | L/km | kg/h | Speed |
| --- | ---: | ---: | ---: | ---: |
| Dallara P217 | 1 | 1.202 | 98.7 | 109 kph |
| Dallara P217 | 6 | 0.519 | 106.4 | 274 kph |
| Toyota GR86 | 2 | 0.473 | 37.3 | 105 kph |
| Toyota GR86 | 6 | 0.234 | 38.5 | 219 kph |
| 4h GT3 | 2 | 1.145 | 104.6 | 123 kph |
| 4h GT3 | 6 | 0.537 | 106.0 | 263 kph |

All-throttle race-speed buckets showed the same broad shape:

- Dallara P217: about `0.811-0.902 L/km` around `80-120 kph`, falling to
  `0.501 L/km` at `280-300 kph`.
- Toyota GR86: about `0.357 L/km` around `80-120 kph`, falling to about
  `0.208-0.218 L/km` at `200-240 kph`.
- 4h GT3: about `0.915-0.940 L/km` around `80-120 kph`, falling to
  `0.525 L/km` at `260-280 kph`.

## Daytona Dallara Oval Capture

Source:

```text
v1.2.1-capture/captures/capture-20260525-131952-975
```

Version context: this capture came from the v1.2.1 capture/package folder. The
active product context has since moved to v1.2.2 because issues were noticed in
v1.2.1 by the user and team. Treat the folder name as source provenance for this
fuel-flow sample, not as the current release target.

Context:

- Dallara P217 LMP2
- Daytona International Speedway, 2011 Oval
- Offline Testing
- Track length: `3.9927 km`
- `DriverCarFuelKgPerLtr = 0.750`
- 45,597 frames at 60 Hz
- Capture duration: about 760 seconds

The test had two deliberate phases:

1. Stationary / near-stationary full clutch, 100% throttle, held in each gear for
   around 10 seconds.
2. Full lap in each gear at about 50% throttle.

### Stationary WOT Clutch Holds

Filter used:

```text
Throttle >= 0.95
Clutch <= 0.10
Speed < 15 kph
Gear >= 1
```

Result:

| Gear | Duration | RPM Mean | RPM p10-p90 | FuelUsePerHour | Liters / 10s |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 13.0s | 7977 | 7917-8035 | 3.88 kg/h | 0.0144 |
| 2 | 10.7s | 7977 | 7919-8034 | 3.79 kg/h | 0.0140 |
| 3 | 11.6s | 7976 | 7919-8034 | 3.79 kg/h | 0.0140 |
| 4 | 12.8s | 7977 | 7919-8034 | 3.79 kg/h | 0.0140 |
| 5 | 13.1s | 7976 | 7918-8034 | 3.79 kg/h | 0.0140 |
| 6 | 11.1s | 8476 | 8419-8533 | 3.79 kg/h | 0.0140 |

This is the weird but useful result: with the clutch disengaged, WOT and high RPM
do not produce race-load fuel flow. The selected gear also does not matter much
because the engine is not doing meaningful work through the drivetrain.

Interpretation:

- `RPM * throttle` is not a universal fuel model.
- Clutch/load/moving context is required before interpreting fuel flow.
- Stationary clutch-in free-revving should be excluded from race-burn and
  formation-burn learning.
- This state is still useful as diagnostics because it proves the telemetry can
  distinguish unloaded engine burn from loaded driving burn.

### 50% Throttle Gear Laps

Filter used:

```text
0.43 <= Throttle <= 0.57
Speed > 20 kph
Gear >= 1
IsOnTrack
not OnPitRoad
```

Flow integration matched tank-level deltas closely.

| Gear | Duration | Laps | Speed | RPM | FuelUsePerHour | L/lap Flow | L/lap Tank |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 156.7s | 1.40 | 128.1 kph | 7938 | 65.9 kg/h | 2.740 | 2.717 |
| 2 | 86.9s | 1.00 | 165.3 kph | 7924 | 70.1 kg/h | 2.261 | 2.229 |
| 3 | 73.9s | 1.00 | 194.1 kph | 7938 | 71.1 kg/h | 1.954 | 1.922 |
| 4 | 64.3s | 1.00 | 223.2 kph | 7918 | 71.6 kg/h | 1.710 | 1.679 |
| 5 | 59.0s | 1.00 | 242.4 kph | 7525 | 72.9 kg/h | 1.603 | 1.571 |
| 6 | 63.9s | 1.05 | 235.8 kph | 6556 | 64.6 kg/h | 1.459 | 1.432 |

Notes:

- Gear 1 included extra approach/partial-lap distance, but the flow/tank
  agreement is still close.
- Raw `kg/h` is broadly similar in gears 2-5, but `L/lap` drops sharply as speed
  rises and distance covered per second improves.
- Gear 6 used lower RPM and lower flow than gear 5 in this test, but it was also
  slightly slower than gear 5. It still had the lowest `L/lap`.
- The tank-delta and flow-integrated results were within roughly `0.03 L/lap` in
  each segment, which is strong evidence that `FuelUsePerHour` can become useful
  for sector/lap estimation after calibration.

Moving, clutch-engaged correlation in this capture:

| Signal | Corr With `FuelUsePerHour` |
| --- | ---: |
| `Throttle` | 0.877 |
| `RPM` | 0.739 |
| `RPM * throttle` | 0.986 |
| `Speed` | 0.484 |
| `Gear` | 0.175 |
| `ManifoldPress` | 0.808 |

The same capture's stationary clutch-in WOT phase collapsed to about `3.8 kg/h`,
so those moving correlations should only be used in a loaded, clutch-engaged,
distance-valid context.

## Product-Relevant Lessons

- `FuelUsePerHour` should be a flow-integration input, not a direct strategy
  output.
- Valid flow-derived sector/lap burn should require moving, clutch-engaged,
  on-track, non-pit context with usable time and distance.
- Flow integration should be calibrated against `FuelLevel` deltas before it can
  influence fuel strategy.
- Tank deltas remain the strongest completed-lap truth source.
- Gear/RPM/speed can explain fuel behavior, but they should not become strategy
  commands until repeated captures prove the advice is stable and useful.
- Formation/caution burn and race-speed burn should remain separate families.
  Fixed-speed pacing can care about target speed and gear; race-speed strategy
  cares about integrated sector/lap burn under real driving conditions.

## Next Oval Tests

The next useful tests should isolate load, speed, gear, and coasting more
carefully.

### Same Speed, Different Gear

Hold steady target speeds and repeat across usable gears:

| Target Speed | Candidate Gears |
| ---: | --- |
| 100 kph | 1 / 2 / 3 |
| 150 kph | 2 / 3 / 4 |
| 200 kph | 3 / 4 / 5 / 6 |
| 240 kph | 4 / 5 / 6 |

This is the most valuable next test because it asks whether gear/RPM changes
fuel burn when the car is doing roughly the same distance work.

### Same Gear, Different Throttle

For each chosen gear, hold long segments at:

- 25% throttle
- 50% throttle
- 75% throttle
- 100% throttle

The Daytona capture already has the 50% case. Additional throttle bands would
give a cleaner load curve.

### WOT Acceleration Pulls

In gears 3-6, start from lower RPM and go WOT to near shift light / limiter.
These samples should show loaded WOT fuel flow across RPM, unlike the clutch-in
test.

### Coast Tests

At high speed, compare:

- lift completely while staying in gear;
- clutch in, no throttle;
- neutral, no throttle if practical;
- tiny maintenance throttle, such as 5-10%.

This would show whether lift-and-coast fuel saving is visible in telemetry and
whether clutch-in coasting burns more or less than in-gear overrun.

### Formation / Pace-Speed Tests

Hold low-speed targets in different gears:

| Target Speed | Candidate Gears |
| ---: | --- |
| 80 kph | 1 / 2 / 3 |
| 100 kph | 1 / 2 / 3 |
| 120 kph | 2 / 3 / 4 |

This directly supports formation-lap and caution-lap fuel learning.

### Repeat A-B-A

Repeat important comparisons in an A-B-A shape:

```text
gear 5 at 240 kph
gear 6 at 240 kph
gear 5 at 240 kph again
```

This helps separate real gear effects from tire temp, fuel load, wind, line, or
driver variation.

## Capture Guidance

To make future captures easier to segment:

- Hold each segment for at least 15-20 seconds; full laps are better when safe.
- Add a clear separator between tests, such as 2-3 seconds of zero throttle.
- Use `DriverMarker` if bound.
- Keep the car on the same line when comparing gears at the same speed.
- Avoid pit road, apron transitions, wall contact, and major steering changes
  during measurement segments.
- Use repeated segments when the result is surprising.
