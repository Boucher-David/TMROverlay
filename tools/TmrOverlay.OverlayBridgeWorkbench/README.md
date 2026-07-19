# Overlay Bridge developer loopback workbench

This is an isolated developer tool, not a shipping app component. It binds **only** to
`127.0.0.1`, serves a synthetic producer and receiver, and invokes the current Core path:

```text
synthetic fixture → virtual circuit → ephemeral mutual TLS → protected Hello
                  → Core frame/CBOR decode → receiver admission → atomic composition
                  → source-neutral Fuel input
```

It does not read iRacing, application settings, captures, history, or live telemetry. Beyond its
own loopback-only HTTP listener, it has no room listener, relay, pairing, invite, Oracle
dependency, persistence, credential/key store, or secret input. It creates an ephemeral signed
synthetic policy only to bind the local protected Hello; it does not model enrollment or retain
the policy. The short-lived certificates exist in memory only. It is not a `LocalhostOverlays` or
OBS route, and it is not evidence for a production transport.

The Core bridge helpers intentionally remain `internal`. The Core assembly grants this one
nonshipping executable friend-assembly access so the workbench can call the same frame and
receiver-pipeline seam; it does not widen a public transport API or duplicate frame, CBOR,
admission, freshness, or priority logic in browser code.

## Run

Run the no-listener readiness check first:

```bash
dotnet run --project tools/TmrOverlay.OverlayBridgeWorkbench/TmrOverlay.OverlayBridgeWorkbench.csproj -- --self-test
```

Then start the loopback-only host:

```bash
dotnet run --project tools/TmrOverlay.OverlayBridgeWorkbench/TmrOverlay.OverlayBridgeWorkbench.csproj -- --port 51931
```

Open these two browser documents:

- `http://127.0.0.1:51931/developer/bridge/producer`
- `http://127.0.0.1:51931/developer/bridge/receiver`

Use **Publish synthetic sector** on the producer page. The receiver page will show the real Core
admission/composition result. After five seconds without a new publication, the receiver moves to
the Core held state and facts are withheld; after twenty seconds it becomes unavailable. The
receiver page can also simulate direct-local precedence, proving that the retained remote group
does not expose current facts or Fuel input while local in-car telemetry is authoritative.

## Limits

This tool deliberately does not implement a remote transport or a production host/session layer.
It has no user pairing/approval workflow, Oracle relay, persisted identity, monotonic-clock
source, real publisher telemetry adapter, multi-viewer fan-out, diagnostics, or app-overlay
consumption. Its one synthetic signed policy and protected Hello prove only the Core binding shape;
they are not an authenticated room service. The product Settings tab remains inert and truthful
while this workbench exists.

The process clears ambient application configuration, blocks hosting-startup injection, and
accepts no host/source command-line option. Its two pages view one synthetic server-side receiver;
they are not independent app sessions or a browser-to-browser transport test.
