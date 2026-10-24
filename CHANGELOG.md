# Changelog

## 0.2.2

- Confirm a pending remote disconnect using `/confirm`, with no copied code.
- Retain the 60-second expiry, authorized private chat/user, current connection binding, single-use confirmation and duplicate suppression.
- Reject a confirmation that predates a replacement request. Allow a fresh request after reconnecting.
- Add portable setup/build/troubleshooting documentation and an explicit-file-list release packager.
- Validate with 46 plugin tests and 15 setup-helper tests.

## 0.2.1

- Support a saved JSON `BotToken`, with explicit-token precedence and environment-variable fallback when blank.
- Reject malformed explicit tokens without exposing their contents.

## 0.2.0

- Standalone vPilot plugin with Telegram long polling, private notifications/replies, SELCAL and optional radio notifications.
- Add confirmed remote disconnect, session/actor protections, bounded queues and mock tests.
- Add optional local setup helper with masked token entry and private-chat pairing.
