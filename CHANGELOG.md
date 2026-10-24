# Changelog

## 0.3.0

- Separate the existing vPilot product into `vpilot/` and the new xPilot product into `xpilot/`.
- Add an xPilot 4 adapter compiled against pinned official plugin SDK API 0.1.0.0. Reject different runtime API versions before opening Telegram transport.
- Preserve private notifications/replies, SELCAL, optional callsign-filtered receive-only radio notifications and `/disconnect` followed by plain `/confirm`.
- Add xPilot's current-connection snapshot, session/replay protection, UI-thread dispatch and fail-closed command behavior.
- Add a portable .NET 10 console helper for private Telegram pairing and explicit local JSON-token setup.
- Provide separate component ZIPs and setup guides. Existing vPilot runtime remains version 0.2.2.
- Validate 129 mocked tests: vPilot 46 + 15; xPilot 50 + 18. No live xPilot client or real outgoing message/disconnect was used.

Earlier vPilot changes are in [vpilot/CHANGELOG.md](vpilot/CHANGELOG.md).
