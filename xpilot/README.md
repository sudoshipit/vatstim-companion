# Flight Companion for xPilot

A standalone **xPilot to Telegram** plugin. Version **0.3.0** supports private-message notifications, private replies, SELCAL alerts, optional radio notifications and confirmed remote VATSIM disconnect.

**Requires xPilot 4 with plugin API 0.1.0.0. It does not work with xPilot 3.** The official SDK is beta; this build refuses a different API version. It has been compiled against the official SDK and tested with mocks, but has **not yet been loaded into a real xPilot client**. This is an independent experimental plugin, not an official xPilot or VATSIM product.

## 1. Download and place the plugin

1. Download `vatstim-companion-xpilot-v0.3.0.zip` from [Releases](https://github.com/sudoshipit/vatstim-companion/releases) and extract it.
2. In the **xPilot client**, enter `.plugins` to open its Plugins directory. This is **not** X-Plane's `Resources/plugins` folder.
3. Close xPilot at a suitable time. Copy the extracted **`FlightCompanion.XPilot` folder** into the client Plugins directory. Keep that folder name unchanged: the main DLL must match it.

The result should be:

```text
Plugins/
  FlightCompanion.XPilot/
    FlightCompanion.XPilot.dll
    FlightCompanion.XPilot.deps.json
    FlightCompanion.XPilot.runtimeconfig.json
    FlightCompanion.XPilot.example.json
    FlightCompanion.XPilot.json         <- your private config, created next
```

Default client Plugins paths, from the [official installation guide](https://xpilot.app/docs/client/installing-plugins):

| OS | Folder |
| --- | --- |
| Windows | `%LOCALAPPDATA%\org.vatsim.xpilot\Plugins` |
| macOS | `~/Library/Application Support/org.vatsim.xpilot/Plugins` |
| Linux | `~/.local/share/org.vatsim.xpilot/Plugins` |

The plugin is portable managed code. Build and mock tests were run on Windows; macOS/Linux and actual xPilot loading still require runtime verification. Do not copy `xPilot.PluginSdk.dll` into this folder: xPilot supplies its SDK. Keep the setup helper outside the Plugins directory.

## 2. Create a dedicated Telegram bot

1. Open the verified [@BotFather](https://t.me/BotFather) account in Telegram.
2. Send `/newbot`, choose its name and username, then keep the returned **bot token** private.
3. Open your new bot's private chat and tap **Start**.

Use a separate bot from your vPilot version if both clients may run. Only one poller can use a bot at a time. Keep xPilot and any other poller using this token closed during pairing. If the bot already has a webhook, use a dedicated bot or resolve it yourself; the helper does not change webhook settings.

## 3. Pair and save configuration

The optional console helper discovers your exact private chat/user IDs. It needs an installed **.NET 10 runtime**; building from source needs the .NET 10 SDK. The plugin itself uses the runtime supplied by xPilot 4.

From the extracted download folder, run:

**Windows PowerShell**

```powershell
dotnet .\setup\FlightCompanion.XPilot.Setup.dll "$env:LOCALAPPDATA\org.vatsim.xpilot\Plugins\FlightCompanion.XPilot"
```

**macOS**

```sh
dotnet ./setup/FlightCompanion.XPilot.Setup.dll "$HOME/Library/Application Support/org.vatsim.xpilot/Plugins/FlightCompanion.XPilot"
```

**Linux**

```sh
dotnet ./setup/FlightCompanion.XPilot.Setup.dll "$HOME/.local/share/org.vatsim.xpilot/Plugins/FlightCompanion.XPilot"
```

Use your actual folder if `.plugins` shows a different location. The helper:

1. Asks before contacting Telegram and prompts for your token without echoing it.
2. Verifies the bot and checks that it has no webhook.
3. Shows random `pair ...` text. Send that exact text to the bot in a private chat within five minutes.
4. Captures that chat and user's IDs, then asks separately whether to enable private replies and remote disconnect.
5. Asks you to type `SAVE` before writing `FlightCompanion.XPilot.json`. **The token is stored in plain text**, as in the JSON option for the vPilot version. Keep the file private and out of GitHub.

It refuses to overwrite an existing configuration, does not launch xPilot, and does not send Telegram messages or change webhooks. For later changes, edit the existing JSON. To pair a different account, back up and remove that configuration first, then run the helper again. On macOS/Linux a new config is created with owner-only read/write permissions; Windows inherits the folder's permissions.

### Manual configuration

If you already know your private Telegram chat/user IDs, copy `FlightCompanion.XPilot.example.json` beside the plugin DLL, rename it **`FlightCompanion.XPilot.json`**, and edit it:

```json
{
  "Enabled": true,
  "BotToken": "PASTE_YOUR_BOT_TOKEN_HERE",
  "BotTokenEnvironmentVariable": "FLIGHT_COMPANION_XPILOT_TELEGRAM_TOKEN",
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

Both IDs must be explicit positive numbers in quotes. `BotToken` takes precedence. For environment-only mode, leave `BotToken` blank and supply the token in the named environment variable **to the xPilot process** when you launch it. `BotTokenEnvironmentVariable` contains the variable's name, never the actual token. A malformed explicit token fails closed.

## 4. Start and verify

Start xPilot normally, wait a few seconds, then send a **new `/status`** to your bot. Startup backlog is deliberately skipped. Connect to VATSIM manually when appropriate and check `/status` again. Configuration is read once at startup; restart after editing it.

The xPilot `.logs` command opens the log folder. A successful plugin load should identify **Flight Companion for xPilot 0.3.0**. Redacted initialization errors describe disabled configuration or an incompatible SDK. No token or message contents are logged by the plugin.

## Commands and behavior

| Action | Telegram command |
| --- | --- |
| Check connection and transport health | `/status` |
| Request remote VATSIM disconnect | `/disconnect` |
| Confirm that request within 60 seconds | `/confirm` |
| Cancel a request that has not been submitted | `/cancel` |
| Show commands | `/help` |
| Reply privately to a message's original sender | Use Telegram **Reply** on that private notification |

Plain `/confirm` requires a pending request from the same allowed private chat/user and current xPilot connection. It is single-use; repeated confirmation cannot resend the disconnect. A new connection requires a new request. Expired, replayed, forwarded, group and bot commands are rejected. `/cancel` cannot recall an already dispatched command.

A disconnect is confirmed only when xPilot emits its disconnection event. Without that event the request becomes **uncertain** after 15 seconds and is never automatically repeated. X-Plane keeps running. Reconnect manually in xPilot.

Private replies must target a notification from the current connection within five minutes, use one line and contain at most 220 characters. They go only to the mapped original sender. Submission does not prove recipient delivery. SELCAL and radio notifications have no reply target; arbitrary-recipient sends, radio sends and flight controls are not provided.

`ForwardPrivateMessages` and `ForwardSelcal` default to true. `ForwardRadioMessages` defaults to false; when enabled, `RadioOnlyWhenCallsignMentioned` defaults to true and requires an exact callsign token. `AllowReplies`, `AllowDisconnect` and the overall `Enabled` switch default to false. The example contains no credentials and is disabled.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Plugin does not load | Confirm xPilot **4**, API **0.1.0.0**, the client Plugins folder, matching folder/DLL name and `.logs`. xPilot 3 cannot load this plugin. |
| Incompatible API message | This SDK is beta. An adapter update and tests are required; do not simply remove the compatibility check. |
| Bot silent after restart | Check the installed JSON filename, `Enabled`, token, both IDs and outbound connectivity. Send a fresh command after startup. |
| Telegram poll conflicts | Stop the other poller or create a separate bot. Do not run both companions with the same token. |
| Commands disabled but notifications work | Enable their separate JSON settings. If no UI synchronization context was supplied by xPilot, broker commands intentionally remain unavailable. |
| Confirmation invalid or expired | Send `/disconnect` again, then `/confirm` within 60 seconds in the same private chat and connection. |
| Disconnect uncertain | Check xPilot itself. The plugin never retries a possibly executed command. |
| Missing notifications | Queues are bounded; old/failed sends are dropped. `/status` reports drops and transport errors. |
| Setup says config already exists | Edit that file or back it up and remove it before pairing again. The helper never overwrites it. |

For plugin updates, close xPilot and replace the DLL/dependency files while preserving your private JSON configuration. No hot reload is supported.

## Build and test

From this `xpilot` directory, with the .NET 10 SDK installed:

```powershell
.\Test.cmd
.\Test-Setup.cmd
.\Build-Setup.cmd
.\Package.ps1
```

On other platforms, the same projects can be built and tested with `dotnet` directly:

```sh
dotnet build src/FlightCompanion.XPilot.csproj -c Release
dotnet run --project tests/FlightCompanion.XPilot.Tests.csproj -c Release
dotnet run --project setup-tests/FlightCompanion.XPilot.Setup.Tests.csproj -c Release
dotnet publish setup/FlightCompanion.XPilot.Setup.csproj -c Release --no-self-contained -o release/setup
```

`Build.cmd` also copies the example JSON to `release`. The PowerShell packager requires that example and the published helper, uses an explicit allowlist, and writes the ZIP plus SHA-256 checksum to `dist`. It ships no SDK DLL. The pinned SDK source and license are in [sdk/](sdk/UPSTREAM.md). No NuGet packages are required; `NuGet.Config` disables external sources.

Validation: **50 plugin tests + 18 setup/transport tests**, all mocked. See [VERIFICATION.md](VERIFICATION.md) for evidence and remaining runtime limits.

## Privacy and references

Forwarding shares selected messages with Telegram; bots are not end-to-end encrypted. Never commit a live JSON config, token or token screenshot. Revoke exposed tokens through BotFather. There is no web server, inbound port or automatic VATSIM reconnection. Stay present and available to ATC under the [VATSIM Code of Conduct](https://vatsim.net/docs/policy/code-of-conduct/).

- [Official xPilot plugin developer guide](https://xpilot.app/docs/plugins)
- [Official SDK and license](https://github.com/xpilot-project/plugin-sdk)
- [Telegram Bot API](https://core.telegram.org/bots/api)

The companion implementation is independent. The vendored SDK source is MIT-licensed by Justin Shannon; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
