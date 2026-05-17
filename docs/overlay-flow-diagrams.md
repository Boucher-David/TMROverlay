# Overlay Flow Diagrams

These diagrams are the visual companion to [Overlay Behavior Reference](overlay-behavior-reference.md). They show the high-level decisions each overlay makes before it renders user-facing content.

They intentionally describe product behavior, not every C# helper. When behavior changes, update the matching written reference and diagram together.

## Shared Visibility Gate

```mermaid
flowchart TD
    Start["Overlay tick or settings change"] --> Enabled{"Overlay enabled?"}
    Enabled -- no --> Hidden["Keep driving/support overlay hidden"]
    Enabled -- yes --> Session{"Content/header/footer session gates allow it?"}
    Session -- no --> Hidden
    Session -- yes --> Preview{"Settings preview active?"}
    Preview -- yes --> ShowPreview["Show for configuration review only"]
    Preview -- no --> Live{"Race-data overlay and telemetry stale?"}
    Live -- yes --> Fade["Fade or suppress live-data body"]
    Live -- no --> Show["Render normal overlay state"]
```

## Settings

```mermaid
flowchart TD
    Open["App starts"] --> Window["Open fixed-size Settings window"]
    Window --> General["General: units, session preview, app controls"]
    Window --> Support["Support: diagnostics, bundle actions, status"]
    Window --> Tabs["Overlay tabs"]
    Tabs --> Common["Enabled, position, size, opacity"]
    Tabs --> Content{"Overlay supports content controls?"}
    Content -- yes --> Columns["Rows, blocks, session gates, localhost sizing"]
    Content -- no --> Specific["Overlay-specific options only"]
    Common --> Persist["Persist keyed OverlaySettings.Options"]
    Columns --> Persist
    Specific --> Persist
```

## Standings

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Data{"Scoring model has rows?"}
    Data -- yes --> Scoring["Use scoring class groups"]
    Data -- no --> Timing{"Timing rows available?"}
    Timing -- no --> Waiting["Waiting for scoring/timing"]
    Timing -- yes --> Fallback["Build fallback table from timing rows"]
    Scoring --> Focus{"Focus car known?"}
    Fallback --> Focus
    Focus -- no --> FocusWait["Waiting for focus car"]
    Focus -- yes --> Ref["Resolve reference car and reference class"]
    Ref --> RaceGate{"Race-like session with lap evidence?"}
    RaceGate -- no --> Filter["Keep usable rows only"]
    RaceGate -- yes --> LapRows["Hide pre-lap noise until valid lap evidence"]
    Filter --> Layout["Apply row budget and class separator settings"]
    LapRows --> Layout
    Layout --> Columns["Apply visible column descriptors"]
    Columns --> Render["Render class headers, reference, pit, partial rows"]
```

## Relative

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Ref{"Focus/reference car known?"}
    Ref -- no --> Waiting["Waiting for focus-relative telemetry"]
    Ref -- yes --> Rows{"Relative rows available?"}
    Rows -- no --> ReferenceOnly["Render stable reference row only"]
    Rows -- yes --> Window["Select configured cars ahead and behind"]
    ReferenceOnly --> Position
    Window --> Ahead["Ahead: nearest for selection, farthest-to-nearest for display"]
    Window --> Behind["Behind: nearest-first below reference"]
    Ahead --> Evidence["Prefer relative seconds; mark partial fallback rows"]
    Behind --> Evidence
    Evidence --> Position{"Session kind makes position meaningful?"}
    Position -- yes --> ShowPos["Show reference position"]
    Position -- no --> HidePos["Suppress misleading position"]
    ShowPos --> Render["Render table columns"]
    HidePos --> Render
```

## Fuel Calculator

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Local{"Local active driver/team context?"}
    Local -- no --> Hidden["Native window hidden; localhost/model waits"]
    Local -- yes --> Fuel{"Fuel and race context usable?"}
    Fuel -- no --> WaitingFuel["Show waiting/current fuel fallback"]
    Fuel -- yes --> History["Load user/baseline history for combo"]
    History --> Burn["Choose measured green-lap burn or exact history"]
    Burn --> Strategy{"Race laps/stints known?"}
    Strategy -- yes --> Plan["Calculate stint count and final stint target"]
    Strategy -- no --> Remaining["Estimate fuel needed from current state"]
    Plan --> Render["Render plan, stint rows, source text"]
    Remaining --> Render
```

## Track Map

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Identity["Resolve current track identity"]
    Identity --> Map{"Track map document found?"}
    Map -- yes --> Load["Load bundled or generated map"]
    Map -- no --> Placeholder["Use deterministic placeholder"]
    Load --> State["Read track-map model and sector state"]
    Placeholder --> State
    State --> Markers{"Marker telemetry usable?"}
    Markers -- yes --> Smooth["Smooth car marker progress"]
    Markers -- no --> Static["Draw map without pretending markers are exact"]
    Smooth --> Render["Draw line, pit lane, sectors, start/finish, cars"]
    Static --> Render
```

## Stream Chat

```mermaid
flowchart TD
    Start["Overlay enabled by user"] --> Provider{"Provider configured?"}
    Provider -- no --> NotConfigured["Show not-configured state"]
    Provider -- yes --> Source{"Provider type"}
    Source -- Twitch --> Connect["Connect to Twitch IRC websocket"]
    Source -- Streamlabs --> BrowserOnly["Use localhost widget route"]
    Connect --> Valid{"Connection accepted?"}
    Valid -- no --> Error["Show bounded error message"]
    Valid -- yes --> Messages["Append bounded chat messages"]
    Messages --> Render["Render chat independent of telemetry fade"]
    BrowserOnly --> Render
```

## Garage Cover

```mermaid
flowchart TD
    Start["Localhost request"] --> Telemetry{"Fresh telemetry says garage safe?"}
    Telemetry -- no --> Cover["Render cover or fallback"]
    Telemetry -- yes --> Garage{"Garage/setup visible?"}
    Garage -- yes --> Cover
    Garage -- no --> Image{"User image configured?"}
    Image -- yes --> Custom["Render configured cover image"]
    Image -- no --> Fallback["Render deterministic fallback"]
    Custom --> Output["Localhost output"]
    Fallback --> Output
    Cover --> Output
```

## Flags

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Session{"Session kind known?"}
    Session -- no --> Hidden["Hide overlay"]
    Session -- yes --> Flags["Read normalized race-control flags"]
    Flags --> Filter["Apply category settings"]
    Filter --> Any{"Any displayable flags?"}
    Any -- no --> Hidden
    Any -- yes --> Settings{"Settings overlay active?"}
    Settings -- yes --> Suppress["Suppress transparent flags while Settings is active"]
    Settings -- no --> Render["Render transparent click-through flags"]
```

## Session / Weather

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Session{"Session model usable?"}
    Session -- no --> Waiting["Waiting for session data"]
    Session -- yes --> Time["Format elapsed/remaining time"]
    Time --> Laps["Include plausible lap totals and remaining laps"]
    Laps --> Track["Show track name and length when known"]
    Track --> Weather["Convert weather units and wind"]
    Weather --> Changes["Tone changed weather/surface values"]
    Changes --> Render["Render metric rows"]
```

## Pit Service

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Local{"Local active driver/team context?"}
    Local -- no --> Hidden["Native window hidden; localhost/model waits"]
    Local -- yes --> Pit{"Pit-service model usable?"}
    Pit -- no --> Waiting["Waiting for pit service data"]
    Pit -- yes --> Status["Resolve current service status"]
    Status --> Requests["Read fuel, tires, repair, pit road, pit stall"]
    Requests --> Release{"Release state known?"}
    Release -- hold --> Hold["Warning tone"]
    Release -- go --> Go["Success tone"]
    Release -- advisory --> Advisory["Info tone"]
    Release -- unknown --> Pending["Pending/waiting tone"]
    Hold --> Render["Render pit metric rows"]
    Go --> Render
    Advisory --> Render
    Pending --> Render
```

## Input / Car State

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Local{"Focused local player in-car context?"}
    Local -- no --> Hidden["Native window hidden; localhost/model waits"]
    Local -- yes --> Player{"Car telemetry known?"}
    Player -- no --> Waiting["Waiting for car telemetry"]
    Player -- yes --> Inputs["Read pedals, steering, gear, RPM, speed"]
    Inputs --> Mechanical["Read cooling, pressure, electrical, warnings"]
    Mechanical --> Units["Convert speed, temp, pressure units"]
    Units --> Blocks["Apply enabled block settings"]
    Blocks --> Warnings["Tone engine warnings"]
    Warnings --> Render["Render graph/content-focused body"]
```

## Car Radar

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Local{"Local player context usable?"}
    Local -- no --> Suppress["Suppress normal radar display"]
    Local -- yes --> Side["Read immediate left/right pressure"]
    Side --> Nearby{"Nearby placement usable?"}
    Nearby -- yes --> Spatial["Use spatial/progress placement"]
    Nearby -- no --> Partial["Degrade without claiming exact placement"]
    Spatial --> Render["Render local car and nearby pressure"]
    Partial --> Render
```

## Gap To Leader

```mermaid
flowchart TD
    Start["Fresh live snapshot"] --> Gap{"Usable timing/leader-gap model?"}
    Gap -- no --> Waiting["Waiting for timing"]
    Gap -- yes --> Session{"Race session?"}
    Session -- yes --> RaceGap["Use race class-leader gap semantics"]
    Session -- no --> NonRace["Use non-race timing semantics carefully"]
    RaceGap --> Sanity["Reject huge lap gaps, resets, discontinuities"]
    NonRace --> Sanity
    Sanity --> Series["Append bounded recent gap point"]
    Series --> Render["Render trend graph and source/status"]
```

## Status

```mermaid
flowchart TD
    Start["Status refresh"] --> Capture["Read capture and telemetry status"]
    Capture --> Support["Read support/runtime state"]
    Support --> Health["Prioritize current issue text"]
    Health --> Render["Render app-health state, not driving data"]
```

## Browser Review Surface

```mermaid
flowchart TD
    Start["Browser review route"] --> Fixture{"Preview fixture requested?"}
    Fixture -- yes --> Mock["Use deterministic browser fixture"]
    Fixture -- no --> Localhost["Use localhost model route"]
    Mock --> Page["Render settings shell or overlay page"]
    Localhost --> Page
    Page --> Validate["Validate layout, JS behavior, native/localhost model parity"]
    Validate --> Boundary["Leave focus/topmost/click-through to Windows validation"]
```
