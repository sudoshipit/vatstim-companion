# vatstim-companion

Telegram companions for **vPilot** and **xPilot**, in separate folders with separate builds, configuration and release downloads. Choose the version for your pilot client.

| Version | Folder and setup guide | Compatibility | Verification |
| --- | --- | --- | --- |
| vPilot 0.2.3 | [vpilot/](vpilot/README.md) | Windows; vPilot 3.12.1; .NET Framework 4.8 | 51 plugin + 29 setup tests; one-EXE setup and custom installation paths |
| xPilot 0.3.0 | [xpilot/](xpilot/README.md) | **xPilot 4**, plugin API **0.1.0.0**, .NET 10 | 50 plugin + 18 setup tests; **not yet tested inside a live xPilot client** |

Both versions provide private-message notifications, replies to recent private notifications, SELCAL alerts, optional receive-only radio notifications and confirmed remote disconnect. Send `/disconnect`, then plain `/confirm` within 60 seconds. A confirmation is single-use for that pending request; after reconnecting manually, you can request again. No code needs copying.

For vPilot, download the standalone [FlightCompanion.Setup.exe](https://github.com/sudoshipit/vatstim-companion/releases/download/v0.2.3/FlightCompanion.Setup.exe). It includes the plugin and remembers your token securely for normal launches. A manual-install ZIP is also available. For xPilot, use the **xpilot** ZIP from [Releases](https://github.com/sudoshipit/vatstim-companion/releases). Each folder's README covers setup, commands, troubleshooting and local builds. The vPilot installer does not install the xPilot version.

Use a **different Telegram bot for each client if both will run**. Telegram permits only one polling application per bot. Copying a token into two active clients will cause conflicts. The configuration files and environment-variable names are separate.

There is no web dashboard or flight-control feature. They use outbound Telegram HTTPS polling; no inbound internet port is needed.

Only explicit allowed private chat/user IDs can issue commands. Reply mappings and disconnect confirmations expire and are tied to the current client connection. Submitted or uncertain commands are never automatically retried, and neither plugin reconnects automatically. Telegram does not replace [VATSIM attendance and prompt-response obligations](https://vatsim.net/docs/policy/code-of-conduct/).

## Repository layout

```text
vpilot/                 vPilot plugin, Windows setup helper, tests and documentation
xpilot/                 xPilot plugin, portable console setup helper, tests and documentation
  sdk/                  Pinned official MIT-licensed xPilot SDK source for compilation
```

Generated `release`, `dist`, `bin` and `obj` folders are ignored. Live configurations and credentials must remain local. Release packagers include an explicit file list and produce SHA-256 checksums; they never include personal configuration or test binaries.

See [CHANGELOG.md](CHANGELOG.md) for repository releases. xPilot 3 and future incompatible SDK versions are unsupported. macOS/Linux runtime compatibility has not been tested here; the xPilot build contains portable .NET assemblies and uses the official cross-platform SDK.
