# Verification

Release: **0.2.3**. Compiled against the installed **vPilot 3.12.1.0** plugin SDK using the Windows .NET Framework compiler, AnyCPU, with compiler warnings treated as errors.

## Reproduce

```powershell
.\Test.cmd
.\Test-Setup.cmd
.\Package.ps1
```

Pass a custom vPilot directory to `Test.cmd` if required. Build/test scripts do not install the plugin or open a network connection.

## Automated checks

**51 plugin tests, 0 failures.** The harness references the compiled release DLL and real SDK interface, with a fake broker and Telegram transport. It verifies:

- Disabled/missing/invalid configuration, positive chat/user allowlists, JSON token loading and precedence, environment fallback, redacted validation errors, and malformed explicit tokens failing closed.
- Windows DPAPI token round trips through saved configuration, credential precedence, corrupt-ciphertext rejection and redacted recovery instructions. DPAPI tests use synthetic credentials only.
- Private-message routing, five-minute reply expiry, connection/session invalidation, single-line length limits, SELCAL alerts, opt-in callsign-filtered radio forwarding, and rejection of arbitrary recipients/radio/flight-control commands.
- Denial of wrong actors/chats, groups, bots, forwarded, stale, future, pre-start, repeated and malformed updates; safe startup backlog handling.
- Plain `/confirm` after `/disconnect`, no confirmation without a pending request, rejection of confirmation predating a replacement request, 60-second expiry, cancellation, actor/session binding, duplicate suppression, and the ability to make a new request after a new connection.
- Actual disconnection-event confirmation, synchronous-event handling, uncertain broker outcomes without retries, failed acknowledgements without replay, queue bounds, expiry, rate limiting and delayed UI callbacks.
- Receive-only operation when no UI synchronization context is supplied, plus event unsubscription and cancellation at session end.

**29 setup-helper tests, 0 failures.** The harness references the compiled helper and uses a fake HTTP handler and synthetic temporary files. It checks random single-use pairing, exact private identity capture, denial of unsafe/stale/replayed updates, token and bot validation, existing-webhook rejection, prohibition of message sending/webhook changes, sanitized errors, ordinary launch without token arguments, and preservation of unrelated files/backups. It also checks Start-link nonce validation/expiry, encrypted credential serialization and installation from the embedded plugin without a neighboring release folder.

Custom-location tests cover folder and quoted executable paths with spaces, environment-variable expansion, missing paths, wrong executables, missing SDKs, incompatible versions, creation of a missing Plugins folder, installation isolation and launching the selected executable with its own working directory. These fixtures inject version metadata and never launch a simulator or vPilot. Set `FC_SETUP_TEST_ROOT` to a writable test directory to run the location fixtures on another drive.

## Release hygiene

Only source, build/test/package scripts, generic documentation and a disabled credential-free example configuration are committed. Build outputs, personal configuration, local scratch scripts, status files and vendor binaries are excluded. Release ZIP entries come from an explicit allowlist. SHA-256 checksums are generated beside the ZIP.

## Runtime evidence and limits

The standalone 0.2.3 setup EXE was opened from a separate D: folder without a configuration file or neighboring plugin DLL. Its UI accepted a synthetic vPilot executable on D: in a path containing spaces. The setup tests also ran their custom-location fixtures on D:. No real bot was paired, no live plugin was installed, and the existing vPilot process was left running during this verification.

A local vPilot 3.12.1.0 session loaded the plugin and answered a user-issued Telegram `/status` with healthy polling and command capabilities enabled. This establishes that the basic Telegram/vPilot integration can initialize in that environment; it is not a certification of every runtime path.

No real ATC/private reply or VATSIM disconnect was sent during automated testing. Disconnect behavior, including the plain `/confirm` flow, is mock-tested. Recipient delivery cannot be confirmed by the SDK. Later vPilot versions, alternate installation paths and actual network failure conditions may need additional verification.
