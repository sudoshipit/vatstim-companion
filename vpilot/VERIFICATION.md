# Verification

Release: **0.2.2**. Compiled against the installed **vPilot 3.12.1.0** plugin SDK using the Windows .NET Framework compiler, AnyCPU, with compiler warnings treated as errors.

## Reproduce

```powershell
.\Test.cmd
.\Test-Setup.cmd
.\Package.ps1
```

Pass a custom vPilot directory to `Test.cmd` if required. Build/test scripts do not install the plugin or open a network connection.

## Automated checks

**46 plugin tests, 0 failures.** The harness references the compiled release DLL and real SDK interface, with a fake broker and Telegram transport. It verifies:

- Disabled/missing/invalid configuration, positive chat/user allowlists, JSON token loading and precedence, environment fallback, redacted validation errors, and malformed explicit tokens failing closed.
- Private-message routing, five-minute reply expiry, connection/session invalidation, single-line length limits, SELCAL alerts, opt-in callsign-filtered radio forwarding, and rejection of arbitrary recipients/radio/flight-control commands.
- Denial of wrong actors/chats, groups, bots, forwarded, stale, future, pre-start, repeated and malformed updates; safe startup backlog handling.
- Plain `/confirm` after `/disconnect`, no confirmation without a pending request, rejection of confirmation predating a replacement request, 60-second expiry, cancellation, actor/session binding, duplicate suppression, and the ability to make a new request after a new connection.
- Actual disconnection-event confirmation, synchronous-event handling, uncertain broker outcomes without retries, failed acknowledgements without replay, queue bounds, expiry, rate limiting and delayed UI callbacks.
- Receive-only operation when no UI synchronization context is supplied, plus event unsubscription and cancellation at session end.

**15 setup-helper tests, 0 failures.** The harness references the compiled helper and uses a fake HTTP handler and synthetic temporary files. It checks random single-use pairing, exact private identity capture, denial of unsafe/stale/replayed updates, token and bot validation, existing-webhook rejection, prohibition of message sending/webhook changes, sanitized errors, child-only token environment, and preservation of unrelated files/backups.

## Release hygiene

Only source, build/test/package scripts, generic documentation and a disabled credential-free example configuration are committed. Build outputs, personal configuration, local scratch scripts, status files and vendor binaries are excluded. Release ZIP entries come from an explicit allowlist. SHA-256 checksums are generated beside the ZIP.

## Runtime evidence and limits

A local vPilot 3.12.1.0 session loaded the plugin and answered a user-issued Telegram `/status` with healthy polling and command capabilities enabled. This establishes that the basic Telegram/vPilot integration can initialize in that environment; it is not a certification of every runtime path.

No real ATC/private reply or VATSIM disconnect was sent during automated testing. Disconnect behavior, including the plain `/confirm` flow, is mock-tested. Recipient delivery cannot be confirmed by the SDK. Later vPilot versions, alternate installation paths and actual network failure conditions may need additional verification.
