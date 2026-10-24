# Pinned official xPilot SDK source

Origin: https://github.com/xpilot-project/plugin-sdk

Commit: `3f3472f45c51717b90d91adb80114874bad7e8d1`

API assembly version: `0.1.0.0`; target: `.NET 10`; host: **xPilot 4**.

The interface, event/model/exception types and project are copied unchanged from the official SDK so builds are reproducible without downloading a moving branch. The original MIT license is included in `LICENSE`. No SDK binary is shipped in the plugin ZIP: xPilot supplies its own SDK assembly at runtime.

Review upstream changes before replacing these files. The companion rejects a different runtime API version rather than assuming beta compatibility.
