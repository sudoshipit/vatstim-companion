# Flight Companion for vPilot

A standalone **vPilot to Telegram** plugin for private-message notifications, replies, SELCAL alerts, and confirmed remote disconnect. Version **0.2.3**.

There is no web dashboard, Node.js service, SimConnect connector, inbound port, or automatic VATSIM reconnection. Telegram uses outbound HTTPS polling while vPilot is running.

## Requirements

- Windows with .NET Framework 4.8 and **vPilot 3.12.1**. This is the SDK version used for compilation and mock tests; other versions require verification.
- A Telegram account and a dedicated bot created with [@BotFather](https://t.me/BotFather).
- A private chat with your bot. Group and channel commands are rejected.

This is an independent, experimental plugin, not an official vPilot or VATSIM product. Telegram does not replace VATSIM attendance and prompt-response requirements.

## Quick setup — one EXE

1. Download [FlightCompanion.Setup.exe](https://github.com/sudoshipit/vatstim-companion/releases/download/v0.2.3/FlightCompanion.Setup.exe). The plugin is included; no ZIP extraction or other files are required. **Close vPilot when convenient**, then run setup under the Windows account you use for vPilot.
2. Check the vPilot location. If installed on D: or another custom path, click **Browse** and select `vPilot.exe`, or paste its installation folder.
3. Paste your bot token and click **Connect Telegram**. To create a bot first, use **Create a bot** to open [@BotFather](https://t.me/BotFather), send `/newbot`, and follow its prompts.
4. Click **Open Telegram** and press **Start** in your bot's private chat. The helper verifies the one-time link and discovers your chat/user IDs. If the link is unavailable, send the displayed pairing code instead. Pairing expires after five minutes.
5. Choose the optional reply/disconnect controls and click **Install Flight Companion**, then **Open vPilot** or use your usual shortcut. Setup remembers your token, encrypted for your Windows account. **No JSON editing or repeat pairing is needed for normal launches.**

After vPilot starts, send a fresh `/status` to your bot. Connect to VATSIM manually when ready. Setup never sends VATSIM messages or disconnect commands. A running vPilot blocks pairing and installation to avoid interfering with its Telegram connection.

The helper starts with `%LOCALAPPDATA%\vPilot` as a suggestion and supports installations on any drive through **Browse**. Both installation and launch use the selected location. A missing `Plugins` folder is created, and the selected path is locked after a successful installation. The version check remains 3.12.1.0. Setup never closes a running vPilot. Windows may identify this independently built helper as an unsigned application; review the repository source/build process if your system blocks it.

## Advanced token configuration (optional)

The setup EXE handles configuration automatically. It saves the token in `ProtectedBotToken` using Windows DPAPI for the current user, leaving plaintext `BotToken` empty. If moving to a different Windows account or computer, run setup there again. The configuration is in `Plugins\FlightCompanion.Plugin.json` under the selected vPilot folder (for example, `D:\Apps\vPilot\Plugins\FlightCompanion.Plugin.json`).

Manual plaintext/environment configuration remains supported. Use the installed **`FlightCompanion.Plugin.json`**, not the example file in your download:

```json
{
  "Enabled": true,
  "BotToken": "PASTE_YOUR_BOT_TOKEN_HERE",
  "ProtectedBotToken": "",
  "BotTokenEnvironmentVariable": "FLIGHT_COMPANION_TELEGRAM_TOKEN",
  "AllowedChatId": "YOUR_PRIVATE_CHAT_ID",
  "AllowedUserId": "YOUR_USER_ID",
  "AllowReplies": true,
  "AllowDisconnect": true,
  "ForwardPrivateMessages": true,
  "ForwardSelcal": true,
  "ForwardRadioMessages": false,
  "RadioOnlyWhenCallsignMentioned": true
}
```

For manual configuration, replace the placeholders and keep IDs in quotes. **Do not put the actual token in `BotTokenEnvironmentVariable`**: that field contains a variable *name*. Precedence is plaintext `BotToken`, then `ProtectedBotToken`, then the environment variable. Invalid configured credentials fail closed instead of falling back to another source. Do not manually edit the encrypted value.

Configuration is read once at startup. Restart vPilot at a suitable time after changing it. Load the plugin before manually connecting to VATSIM.

## Telegram commands

| Action | What to send |
| --- | --- |
| Check connection and polling health | `/status` |
| Request disconnect from VATSIM | `/disconnect` |
| Confirm the pending disconnect within 60 seconds | `/confirm` — no code needed |
| Cancel an unsubmitted disconnect request | `/cancel` |
| Show available commands | `/help` |
| Reply to an incoming private vPilot message | Use Telegram's **Reply** action on that particular notification |

**Disconnect example:** send `/disconnect`, review the bot's prompt, then send `/confirm`. Each request can be confirmed once. Repeated confirmation cannot resend the disconnect; after reconnecting manually, you can make a new request normally. The confirmation must come from the same allowed chat/user, after the request, during the same vPilot connection. It expires after 60 seconds. `/cancel` cannot recall a command that has already been dispatched.

A disconnect is reported as complete only after vPilot emits its actual disconnection event. A submitted request without that event becomes **uncertain** and is not retried. MSFS stays running. Reconnect manually in vPilot.

Private replies must target a private notification from the current connection within five minutes, contain at most 220 characters, and use one line. The original sender is the only reply recipient. “Submitted” means vPilot accepted the call; it does not prove delivery to the recipient. SELCAL and optional radio notifications do not support replies. Arbitrary-recipient sends, radio sends and flight controls are not provided.

## Configuration options

| Field | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` | Enables the plugin only with valid configuration. |
| `BotToken` | empty | Optional token saved in JSON; takes precedence over the environment source. |
| `ProtectedBotToken` | empty | Windows-encrypted token written by setup; used when `BotToken` is empty. Requires the same Windows account. |
| `BotTokenEnvironmentVariable` | `FLIGHT_COMPANION_TELEGRAM_TOKEN` | Process environment variable name used when `BotToken` is empty. |
| `AllowedChatId` | empty | One explicit positive private chat ID. |
| `AllowedUserId` | empty | One explicit positive user ID; both IDs must match. |
| `AllowReplies` | `false` | Permits replies to mapped recent private notifications. |
| `AllowDisconnect` | `false` | Permits `/disconnect` followed by `/confirm`. |
| `ForwardPrivateMessages` | `true` | Sends incoming vPilot private messages to Telegram. |
| `ForwardSelcal` | `true` | Sends SELCAL alerts to Telegram. |
| `ForwardRadioMessages` | `false` | Optional receive-only radio notifications. |
| `RadioOnlyWhenCallsignMentioned` | `true` | Requires an exact callsign token in radio text when forwarding is enabled. |

## Manual installation / updates

1. Close vPilot at a suitable time.
2. Copy `release\FlightCompanion.Plugin.dll` into your vPilot `Plugins` directory.
3. For a **new** installation only, copy `release\FlightCompanion.Plugin.example.json` into that directory and rename it `FlightCompanion.Plugin.json`. Fill in your token/IDs and desired settings. For an **update**, preserve your existing configuration.
4. Start vPilot, send a fresh `/status`, then connect manually.

Do not copy the source repository's test binaries or vendor SDK files into the Plugins folder. The ZIP contains only the plugin, example configuration, optional helper and documentation.

For environment-only mode, leave both `BotToken` and `ProtectedBotToken` blank and launch from **PowerShell 7**:

```powershell
$env:FLIGHT_COMPANION_TELEGRAM_TOKEN = Read-Host 'Telegram bot token' -MaskInput
Start-Process "$env:LOCALAPPDATA\vPilot\vPilot.exe"
Remove-Item Env:\FLIGHT_COMPANION_TELEGRAM_TOKEN
```

The prompt avoids putting the token in shell history. This does not save a permanent environment variable. `-MaskInput` requires PowerShell 7. Most users should use the setup EXE instead.

For a custom installation, replace the executable path in `Start-Process` with your selected `vPilot.exe` path.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Setup cannot find vPilot, or vPilot is installed on D: | Use setup 0.2.3 or later. Click **Browse** at the top and select the installed `vPilot.exe`. |
| No replies after restarting vPilot with an older setup | Run setup 0.2.3 once to save an encrypted token that works with normal launches. |
| Saved token cannot be read after changing PCs or Windows accounts | Run setup again under the account that runs vPilot. |
| Token was pasted into JSON but the bot is silent | Use plugin 0.2.1 or later, put the token in **`BotToken`**, keep `BotTokenEnvironmentVariable` as a name, and restart. |
| No response to old `/status` messages | Startup backlog is deliberately skipped. Wait a few seconds after startup and send a fresh command. |
| Still no response | Check `Enabled`, valid JSON, the actual installed config filename, both allowlists, a valid token and internet connectivity. vPilot's debug log contains redacted initialization errors. |
| “Disconnected / no connection event observed” | Connect vPilot manually after plugin startup. The SDK provides events rather than a current-state query. |
| Replies or disconnect show disabled | Enable their separate settings and restart. If `/status` reports no UI synchronization context, this vPilot runtime can only receive notifications; commands fail closed. |
| Setup reports another poller/webhook | Use a dedicated bot. Stop the other poller; only the bot owner should remove a pre-existing webhook. The helper never changes webhook settings. |
| Confirmation expired or invalid | Send `/disconnect` again, then `/confirm` within 60 seconds in the same private chat and connection. |
| A request is uncertain | Check vPilot itself. Commands are not automatically repeated. |
| Messages are missing | Queues are bounded; old/failed notifications can be dropped. `/status` reports drops and transport errors. |

## Build, test and package from source

Run these commands from the repository's `vpilot` folder. The installed Windows .NET Framework C# compiler is sufficient; no npm, Node.js, package restore or SDK redistribution is required. Build against the SDK from **your installed vPilot**.

```powershell
.\Build.cmd
.\Build-Setup.cmd
.\Test.cmd
.\Test-Setup.cmd
.\Package.ps1
```

For a non-default vPilot path, pass it to `Build.cmd` and `Test.cmd`:

```powershell
.\Test.cmd 'D:\Apps\vPilot'
```

Build outputs go to `release` and `setup`; the standalone setup EXE, optional manual-install ZIP and their checksums go to `dist`. `Build-Setup.cmd` builds and embeds the plugin. The package script includes an explicit file list, so runtime configuration, tokens, scratch scripts, SDK binaries and test outputs cannot be swept into the release accidentally.

Current validation: **51 plugin tests + 29 helper tests**. All command tests use a mocked broker and fake Telegram transport. See [VERIFICATION.md](VERIFICATION.md) for what these checks establish and their limits.

## Privacy and behavior limits

- The installed `FlightCompanion.Plugin.json` is ignored by Git. Setup encrypts the token with Windows DPAPI; manually entered `BotToken` values are plaintext. Keep the configuration and backups private.
- Token values, message contents and token-bearing request URLs are not logged. Setup saves only the Windows-encrypted token, not its plaintext value.
- Enabling forwarding shares selected message content with Telegram. Bot messages are not end-to-end encrypted.
- Groups, bots, forwarded commands, stale updates, replayed updates and unsupported commands are rejected. Session changes invalidate reply targets and disconnect requests.
- No automatic reconnect or unattended-flight behavior is provided. Follow the [VATSIM Code of Conduct](https://vatsim.net/docs/policy/code-of-conduct/).

## Interface references

- [vPilot documentation](https://vpilot.rosscarlson.dev/Documentation) and the [published plugin SDK XML reference](https://github.com/blt950/vPilot-Pushover/blob/main/RossCarlson.Vatsim.Vpilot.Plugins.xml). Builds use the DLL from the user's installation.
- [Telegram Bot API](https://core.telegram.org/bots/api) and [BotFather setup guide](https://core.telegram.org/bots/tutorial).

The implementation was independently written. Third-party relay source and vendor SDK binaries are not distributed in this repository or release. Compatibility with future major vPilot versions is unverified.
