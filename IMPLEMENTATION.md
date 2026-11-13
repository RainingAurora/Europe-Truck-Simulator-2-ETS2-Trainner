# Implementation notes

## Verified build

- ETS2 Windows x64 1.62.1.0, product build `1afd6ae638406ad3953c6c8940f96d0e25a3eeae`.
- SHA-256: `A938CB388121F9B884A2E1B0F048C230A7BC19638EF6D641A70775F8C7FEA956`.
- Disk hash plus six in-memory instruction signatures are required before enabling writes.

## Address derivation

Fuel was traced from the game's native `CHEAT - new fuel amount: %f` setter at RVA `0x5BF110`. The setter clamps a float to [0,1] and stores it at the vehicle object + `0x1C0`.

Fatigue was traced from `CHEAT - new fatigue amount: %u (min)` at RVA `0x5BF220`. It writes the unsigned minute counter at driver + `0x50`.

All table entries below are module-relative RVAs or object-relative offsets, never absolute heap addresses:

| Data | Resolution |
| --- | --- |
| Application | `[module + 0x3714F70]` |
| Driving state | application + `0x594` (u32, 2 = driving world) |
| Pause count | application + `0xAC0` (u32, 0 = unpaused) |
| Vehicle | `[[[[module + 0x3714F30] + 0x31B0] + 0x18] + 0x1F8]` |
| Fuel ratio | vehicle + `0x1C0` (float32) |
| Economy | `[module + 0x30AA8A0]` |
| Driver | `[economy + 0x18]` |
| Fatigue | driver + `0x50` (u32 minutes) |
| Game clock | economy + `0x19C` (u32 minutes; diagnostics only) |

The pause counter was traced from the game's native paused/normal state reporting and functions that increment/decrement the pause reference count. Its changes were checked against live user-operated pause/menu transitions.

Every object in each chain is validated against its exact build-specific vtable RVA. Active world and economy objects additionally require their native live-object flag. Fuel must be finite and in [0,1.00001]; fatigue must be within 0–10080 minutes. Unsupported object layouts fail closed independently for each feature.

## Stability and active righting

The two assistance requests are independent flag bits. Natural assistance preserves a 5 degree roll deadband and opposes excess/predicted roll along the vehicle forward axis (gain 4, rate limited to 1.5 rad/s). Strong assistance is armed separately: either primary body's absolute roll above 35 degrees triggers a pose recovery of both actor assemblies, with a 2 second cooldown. It preserves forward direction/pitch, lifts by 0.3 m (0.8 m beyond 70 degrees), and zeros linear/angular velocity. Near-vertical forward pitch is excluded because Euler roll becomes ambiguous.

The first velocity-only prototype failed actual driving. SCS overwrites native PhysX state from its vehicle history buffers every physics update. Native routine RVA `0x853620` and its fragments restore each part's placement and velocities; this is why direct canonical writes read back successfully yet produced no effective stabilization. The release updates both validated history slots and corresponding native data. This was verified by physical tilt, lift/fall, and 55/-90 degree recovery trials.

| Data | Resolution / layout |
| --- | --- |
| Primary truck actor | `[truckOwner + 0x18]`, vtable RVA `0x23322C8` |
| Attached trailer | `[truckOwner + 0xC8]`, vtable RVA `0x22D4978` |
| Physics wrapper | `[actor + 0x30]`, vtable RVA `0x249DFB0` |
| Native rigid body | `[wrapper + 0xF8]`, vtable RVA `0x255B9A8` |
| History pointer array / count | actor + `0x218` / `0x220` (1..16 records) |
| History part identity | `[historyRecord]` must be a correctly typed wrapper in the same native scene |
| Current history slot | record + `0x10`: placement (32 bytes), +`0x30`: linear velocity, +`0x3C`: angular velocity |
| Previous history slot | record + `0x48`: placement, +`0x68`: linear velocity, +`0x74`: angular velocity |
| SCS placement layout | float xyz at 0..8, original tile fields at 12..15, quaternion WXYZ at 16..28 |
| Native orientation / position | core + `0x90` (XYZW) / `0xA0` (xyz) |
| Body-to-actor quaternion | core + `0xB0`; composed when setting native orientation |
| Native linear / angular velocity | core + `0xD0` / `0xE0` |
| Buffered native pose / velocities | core + `0x140`, `0x150`, `0x15C`, `0x168` |

Every part, including the suspended cabin, is resolved through the actor's history array rather than by guessing adjacent allocator slots. Reader checks pointer alignment, exact vtables, shared scene identity, unique cores, finite positions/velocities, normalized current/previous quaternions, dynamic/in-scene state, pending buffer flags, and simulation buffering. Before applying, the entire actor/history/part identity is checked again. Standard tractor + single semi-trailer was tested; additional trailer chains and physics mods are not validated.

## Runtime

- A serialized ThreadPool timer requests one tick every 100 ms. Overlapping ticks are skipped.
- Each write resolves and validates the chain again and compares the entire target identity with the initial snapshot.
- Only aligned writes of 4, 12, 16, 24 or 32 bytes to committed writable data pages are permitted. No executable pages are modified.
- Paused, menu, loading, null, unexpected-type and invalid-value states never authorize writes.
- Process exit clears requests; read-only state inspection continues while switches are off.
- A write error disables all four switches. Loading and temporary invalid objects retain the requested state while suspended.
- There is no injection, driver, remote thread, code patch, console automation, telemetry plugin or permanent configuration edit.

`IMemory` isolates operating-system memory access from the reader and engine. Tests exercise the engine against byte-addressed memory, including invalidation between the first read and the final write check. `ProcessMemory` owns the native handle and checks both the supported file and loaded instructions.

## Tools

- `build.ps1 -Test`: compile GUI, diagnostics, restart monitor, then run checks.
- `tools/Diagnostics.cs`: exercises the same compiled engine used by the GUI.
- `tools/RestartMonitor.cs`: read-only validation across distinct game PIDs.
- `package.ps1`: creates portable binary and source ZIPs using an explicit allowlist. Test profiles, backups, private game logs and vendor libraries are excluded.

## Reference material

[SCS configuration variables](https://modding.scssoft.com/wiki/Documentation/Engine/Configuration_variables) distinguishes fatigue simulation from realistic fuel consumption. [The SCS Telemetry SDK](https://modding.scssoft.com/wiki/Documentation/Engine/SDK/Telemetry) is a telemetry/input interface; this trainer obtains write targets from the installed game's native setters instead.
