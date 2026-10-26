# Changelog

## 0.2.3

- Let setup browse for vPilot.exe or accept an installation folder on any drive. Validate the chosen executable, SDK and supported version before installation.
- Install and launch from the same selected location; create the Plugins folder when needed. Preserve existing plugin backups and refuse installation or launch while vPilot is running.
- Use generic setup prompts and documentation without references to an individual user's setup history.
- Provide a standalone setup EXE with the plugin embedded, Telegram Start-link pairing and Windows-encrypted token storage. Normal vPilot launches need no JSON editing or repeat setup.
- Refuse pairing while vPilot is running so setup cannot compete with the active bot poller.
- Validate with 51 plugin tests and 29 setup-helper tests.

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
