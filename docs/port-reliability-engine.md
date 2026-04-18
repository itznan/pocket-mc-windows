# Port Reliability Engine

## Architecture

The Port Reliability Engine lives inside `PocketMC.Desktop/Features/Networking` and is integrated into the existing lifecycle, tunnel, diagnostics, and dashboard flows instead of introducing a parallel subsystem.

The runtime pipeline is:

1. `PortPreflightService` builds normalized `PortCheckRequest` values from instance metadata and config files.
2. `PortProbeService` performs fast OS-level bind probes for the requested protocol and IP mode.
3. `PortLeaseRegistry` reserves validated bindings before process launch to prevent PocketMC-to-PocketMC startup races.
4. `ServerLifecycleService` owns the startup decision, retry handling, lease reservation, and lease cleanup.
5. `PortRecoveryService` converts failed checks into bounded retry or fallback recommendations.
6. `PortFailureMessageService` converts structured failures into UI-facing messages.
7. `PortDiagnosticsSnapshotBuilder` exports redacted port state into support bundles.

## Key Services

- `PortPreflightService`: config-level validation, protocol selection, IP mode selection, internal PocketMC conflict detection.
- `PortProbeService`: TCP/UDP and IPv4/IPv6 bind probing with structured failure classification.
- `PortLeaseRegistry`: in-memory, thread-safe lease coordination for startup, stop, crash, and restart.
- `PortRecoveryService`: transient vs persistent failure handling, exponential backoff, next-free-port recommendations, recovery history.
- `PortFailureMessageService`: user-facing titles, explanations, badge text, and remediation copy.
- `ServerLifecycleService`: authoritative startup pipeline and cleanup owner.
- `TunnelService` and `InstanceTunnelOrchestrator`: protocol-aware Playit tunnel matching and structured tunnel failure mapping.
- `PortDiagnosticsSnapshotBuilder`: support-bundle snapshot of mappings, leases, failures, tunnel state, and dependency health.

## Failure Codes

`PortFailureCode` covers:

- invalid range and reserved/privileged ports
- internal PocketMC conflicts and external process conflicts
- access denied, TCP conflict, UDP conflict
- IPv4 bind failure, IPv6 bind failure, unsupported protocol/address family
- tunnel limit reached, Playit agent offline, token invalid, claim required
- public reachability failure and unknown socket/port failures

These codes are intended to be stable integration points for lifecycle logic, diagnostics, and UI messaging.

## Recovery Flow

- Preflight or probe failures are wrapped as `PortCheckResult`.
- `ServerLifecycleService` asks `PortRecoveryService` for a recommendation before surfacing a `PortReliabilityException`.
- Transient failures can retry with bounded exponential backoff.
- Persistent conflicts can recommend a next free port without silently rewriting configuration.
- UI surfaces `PortFailureMessageService` output while detailed structured logs remain in the lifecycle layer.

## Lifecycle Cleanup

- `StopAsync`, `Kill`, `KillAll`, crash handling, failed startup, and retry-abort paths all release leases through `ServerLifecycleService`.
- Shell shutdown now routes through `IServerLifecycleService.KillAll()` so app shutdown clears leases and cached tunnel addresses instead of only killing processes.
- `ServerLifecycleService.Dispose()` unsubscribes event handlers and releases any residual networking state left during host teardown.

## Test Coverage

Current tests cover:

- preflight validation for invalid ports, malformed config fallback, and internal conflicts
- probe classification for TCP/UDP conflicts and IPv4/IPv6 bind failures
- lease acquisition, duplicate rejection, release, and concurrent reservation safety
- recovery recommendations for retry, fallback, and hard-fail paths
- tunnel failure classification without live external dependencies
- diagnostics snapshot generation
- lifecycle cleanup when launch fails after lease acquisition and when residual networking state is cleared via `KillAll()`

Manual QA is still required for real Windows permission failures, machine-specific IPv6 behavior, and live Playit claim/token/tunnel flows.
