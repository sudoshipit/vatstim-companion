# xPilot Plugin SDK

The interface assembly for writing plugins for the [xPilot](https://xpilot.app) client. Plugins are .NET class libraries that xPilot loads at startup. They can react to xPilot events, read the current connection and controller state, and request a few actions such as sending messages or requesting a METAR.

This repository contains only the interfaces and event types that plugins compile against. xPilot provides the implementation at runtime.

For the full developer guide, see [Creating xPilot Plugins](https://xpilot.app/docs/plugins).

## Requirements

* .NET 10 SDK (`net10.0`)
* An xPilot 4 client to run the plugin in

## Getting started

Clone this repository next to your plugin project and reference it. Set `Private` to `false` so your build does not copy `xPilot.PluginSdk.dll` into your output. xPilot already provides that assembly and always uses its own copy.

```xml
<ItemGroup>
  <ProjectReference Include="../xPilot.PluginSdk/xPilot.PluginSdk.csproj">
    <Private>false</Private>
  </ProjectReference>
</ItemGroup>
```

Implement `IPlugin` in a public class with a public parameterless constructor:

```csharp
using System;
using Vatsim.Xpilot.PluginSdk;
using Vatsim.Xpilot.PluginSdk.Events;

public sealed class MyPlugin : IPlugin
{
    public string Name => "My Plugin 1.0.0";

    public void Initialize(IBroker broker)
    {
        // Return before subscribing if this xPilot is older than the SDK you built against.
        if (broker.ApiVersion < new Version(0, 1))
            return;

        broker.NetworkConnected += (_, e) => broker.PostDebugMessage($"Connected as {e.Callsign}");
    }
}
```

Build the plugin, then copy its folder into the xPilot `Plugins` folder, laid out as `Plugins/MyPlugin/MyPlugin.dll` with any dependencies beside it. Restart xPilot to load it.

## What plugins get

### Events

| Event | Raised when |
| --- | --- |
| `SessionEnded` | xPilot is closing. The only cleanup callback. |
| `NetworkConnected` / `NetworkDisconnected` | The network session changed state. |
| `PrivateMessageReceived` / `PrivateMessageSent` | A private message was received or sent. |
| `ServerMessageReceived` | The network server itself sent a text message. |
| `RadioMessageReceived` / `RadioMessageSent` | A text radio message was received or sent. |
| `BroadcastMessageReceived` | A network broadcast was received. |
| `MetarReceived` | A requested METAR was received. |
| `AtisReceived` | Requested controller info or text ATIS was received. |
| `ControllerAdded` / `ControllerDeleted` | A controller appeared or disappeared. |
| `ControllerFrequencyChanged` / `ControllerLocationChanged` | A controller changed frequency or position. |
| `SelcalAlertReceived` | A SELCAL alert was received. |
| `AircraftAdded` / `AircraftUpdated` / `AircraftDeleted` | Nearby aircraft changed in the simulator session. |

### State

| Member | Use |
| --- | --- |
| `ApiVersion` | The SDK version the running xPilot was built with. |
| `IsConnected` | Whether xPilot is connected to the network. |
| `Callsign` | The current connection's callsign, or `null` when not connected. |
| `GetControllers()` | A thread-safe snapshot of the controllers xPilot currently tracks. |

### Actions

`RequestConnect`, `RequestConnectAsObserver`, `RequestConnectAsTowerView`, `RequestDisconnect`, `RequestMetar`, `RequestAtis`, `SendPrivateMessage`, `SendRadioMessage`, `PostDebugMessage`, `SetModeC`, `SquawkIdent`, and `SetPtt`. Some throw state exceptions such as `NotConnectedException`, `AlreadyConnectedException`, or `SimNotReadyException`. Handle them, because client state can change at any time.

## Things to know

* **Compatibility:** the SDK is in beta and any build may include breaking changes. The assembly version changes with them, and `IBroker.ApiVersion` reports the version xPilot was built with. xPilot does not refuse to load a plugin built against a different SDK version, so check `ApiVersion` at the top of `Initialize`.
* **Lifecycle:** plugins are initialized once at startup, before a network connection can exist, and can't be reloaded while xPilot runs. Unsubscribe and release resources in `SessionEnded`.
* **Threading:** events are raised on the UI thread, in order. Handlers must return quickly.
* **Errors:** an exception in a plugin's constructor, `Initialize`, or an event handler is caught and logged, and does not affect xPilot or other plugins. Plugins run in the xPilot process and are not sandboxed.
* **Isolation:** each plugin loads in its own assembly load context, so its dependencies don't conflict with xPilot or other plugins. Only this SDK assembly is shared.
* **Frequencies** are `int` Hz. For example, `123725000` is 123.725 MHz.

## License

[MIT](LICENSE)
