# ETS2 Trainer — Four Driving Assists

[简体中文](./README.md) | **English**

A portable Windows trainer with a Chinese-language interface for **Euro Truck Simulator 2 1.62.1.0 x64 in single-player mode**.

1. Unlimited fuel
2. No fatigue
3. Rollover assistance with two modes that can run together

The trainer verifies the game executable by its SHA-256 hash. See the [test results](TEST_RESULTS.md) for the supported hash.

## How to use

1. Download the Windows ZIP from GitHub Releases and extract it. The archive includes the checksum in `SHA256SUMS.txt`.
2. Start the game, load a single-player save, and enter the cab.
3. Double-click `ETS2Trainer.exe` and wait for `驾驶场景已连接` (Connected to the driving session).
4. Enable the features you want using the window buttons or the hotkeys below. All four switches work independently, so Roll Stabilization and Automatic Righting can run together.

| Action | Hotkey (top-row number keys) |
| --- | --- |
| Toggle Unlimited Fuel | Ctrl + Alt + 1 |
| Toggle No Fatigue | Ctrl + Alt + 2 |
| Toggle Roll Stabilization | Ctrl + Alt + 3 |
| Toggle Automatic Righting | Ctrl + Alt + 4 |
| Disable all features | Ctrl + Alt + 0 |

**Unlimited Fuel** fills the tank immediately and keeps it full with a target update interval of 100 ms. When you turn the feature off, fuel consumption resumes from the current level. **No Fatigue** resets the driver's accumulated fatigue minutes to zero and keeps them there. Fatigue starts accumulating normally again when you turn it off.

**Roll Stabilization** applies an opposing roll rate to the tractor or trailer when that body rolls beyond about 5° or is predicted to do so. It preserves steering and normal motion on slopes while allowing some roll.

**Automatic Righting** monitors the tractor and trailer. When either rolls more than 35°, it adjusts the orientation of both and the cab, briefly lifts the vehicle by 0.3–0.8 m, and resets its speed to zero. Interventions are at least 2 seconds apart. With both modes enabled, Roll Stabilization counteracts excessive roll and Automatic Righting intervenes if the roll passes its threshold.

Enabled features wait or suspend while the game is paused, a menu is open, or a save is loading. They resume automatically once you return to a valid driving session. Switch settings carry over when you change profiles within the same game process. All four switches turn off when the game exits. Enable them again after relaunching. Closing the trainer stops all modifications, and the game resumes updating fuel, fatigue, and vehicle state normally.

The window shows the tractor and trailer roll angles in real time and counts righting interventions. `监测中` (Monitoring) means the assist is enabled and the data checks have passed. `已主动扶正 N 次` shows the number of actual interventions during the current connection. The window adds scrollbars on smaller displays.

The trainer runs on 64-bit Windows with .NET Framework 4.8 or a compatible version. The portable package runs without Cheat Engine, Python, extra game plugins, or drivers.

## Status and troubleshooting

- **Waiting for the game (`等待游戏启动`).** Launch `eurotrucks2.exe`. The trainer supports one running game process at a time.
- **Game file mismatch (`游戏文件不匹配`).** The executable must match the verified hash. A different build with the same version number also requires a compatibility update before the trainer can write to it.
- **Cannot access the game (`无法访问游戏`).** Run the trainer and the game with the same privileges. If the game runs as administrator, run the trainer as administrator too.
- **Game paused or menu open (`游戏暂停 / 菜单中`).** Return to driving to resume enabled features automatically.
- **Object type mismatch or invalid data.** The affected feature stops writing until the data becomes valid again. Try reloading the save or restarting the game. Mods that alter vehicle physics structures need separate compatibility testing.
- **Hotkey already in use.** The window buttons still work if a hotkey cannot be registered.

Logs are stored in `%LOCALAPPDATA%\ETS2Trainer\logs`. Click `打开日志` (Open Logs) to open the folder. Testing covered diesel trucks in single-player mode. Compatibility with Convoy, TruckersMP, and other game versions has not been verified.

The driving assists were tested with a standard tractor and semi-trailer. Tests covered road driving with both assists enabled and stationary righting after tilts of 55° and 90° in the opposite direction. Roll correction pauses when the vehicle's pitch approaches vertical. Multi-trailer combinations, other vehicle configurations, and physics mods still need testing. See the [test results](TEST_RESULTS.md) for details.

## Source and build

Run this command in PowerShell from the project directory.

```powershell
.\build.ps1 -Test
```

The build uses the Windows Framework64 C# compiler and produces `dist\ETS2Trainer.exe`. It also builds the diagnostic tools and runs the automated checks in `tests\TrainerTests.cs`. Close the trainer and diagnostic tools before rebuilding so Windows can replace their executables.

`tools\bin\Diagnostics.exe 30 off` observes the game for 30 seconds in read-only mode. Use `fuel`, `fatigue`, or `both` to test fuel, fatigue, or both features. Use `natural`, `strong`, or `stability` to test Roll Stabilization, Automatic Righting, or both assists. Use `all` to enable all four features. The diagnostic tool stops writing when it exits.

After building, run `./package.ps1` to create the Windows ZIP and a source snapshot. See the [test results](TEST_RESULTS.md) and [implementation notes](IMPLEMENTATION.md) for more details.
