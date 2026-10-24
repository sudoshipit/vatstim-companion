# Verification - xPilot 0.3.0

Built on Windows using .NET SDK **10.0.103**, targeting **net10.0**, with compiler warnings treated as errors. The plugin references the actual official SDK source at commit `3f3472f45c51717b90d91adb80114874bad7e8d1`, API assembly **0.1.0.0**. Runtime compatibility checks reject any other API version before transport setup.

## Automated checks

**50 plugin tests, 0 failures.** The harness references the compiled plugin and official SDK interface. A DispatchProxy broker and fake Telegram transport test actual event handlers and command dispatch. Coverage includes:

- Incompatible SDK fail-closed behavior; connected/disconnected startup snapshots; idempotent initialization and session cleanup.
- JSON token loading/precedence, environment fallback, explicit allowlists and redacted configuration failures.
- Private notifications and mapped replies, SELCAL and opt-in callsign-filtered receive-only radio messages.
- Session-bound reply expiry, wrong-user/chat, group/bot/forwarded/stale/future/replayed updates, malformed input and skipped startup backlog.
- Plain `/confirm`, pending-request requirement, actor/session binding, expiry, request replacement, cancellation and duplicate suppression.
- Synchronous/asynchronous disconnection events, uncertain outcomes, no automatic retry, failed acknowledgements without replay, delayed UI callbacks and missing-context denial.
- Queue bounds, rate limiting, stale notification drops and transport failure handling.

**18 setup/transport tests, 0 failures.** Fake HTTP responses and synthetic temporary files verify:

- Random 128-bit pairing, five-minute expiry, private identity checks, single use and replay rejection.
- Explicit JSON-token persistence, validation before file creation and refusal to overwrite existing configuration.
- Bot identity checks and refusal of existing webhooks without mutation.
- Correct Telegram request serialization, parsing, response validation, redacted network errors and denial/conflict handling.

Run `Test.cmd`, `Test-Setup.cmd` and `Build-Setup.cmd` in this folder. Builds require the .NET 10 SDK but no external package restore. Tests do not use credentials, a live bot, xPilot, X-Plane or VATSIM.

## Packaging and provenance

`Package.ps1` explicitly lists release entries and checks the example is disabled and credential-free. Packages include the companion DLL, dependency/runtime metadata, example, console helper, documentation and MIT notice. They exclude the SDK binary, live Telegram configuration, test outputs and source build directories. SHA-256 checksums accompany each ZIP. The SDK's original source and license are pinned under `sdk`; upstream provenance is recorded in `sdk/UPSTREAM.md`.

## Remaining runtime verification

**No real xPilot client was available for an end-to-end loading test.** Compilation and mocks establish interface compatibility with the pinned SDK, not guaranteed host compatibility. Windows xPilot loading, its actual UI synchronization context, real event timing and macOS/Linux execution still need verification. If the host provides no synchronization context, notifications remain enabled but replies/disconnect fail closed.

No real message or disconnect was transmitted. A submission does not prove recipient delivery. Disconnect succeeds only after an SDK connection-state event; otherwise it is uncertain and never retried. The SDK remains beta; xPilot 3 and different API versions are unsupported.
