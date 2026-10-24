# Third-party notices

This project builds against the official xPilot plugin SDK by Justin Shannon, pinned at commit `3f3472f45c51717b90d91adb80114874bad7e8d1`:

https://github.com/xpilot-project/plugin-sdk

Its source is included under `sdk/` with the original MIT license at `sdk/LICENSE`. The source is used for compilation and mock tests. The SDK DLL is not redistributed in the plugin ZIP; xPilot supplies it at runtime. The release includes a copy of that license as `xPilot-SDK-LICENSE.txt`.

The Flight Companion implementation was independently written. xPilot, vPilot, VATSIM and Telegram names identify their respective products and interfaces; this companion is not endorsed by those projects.
