# EncounterDecisionCheck

Runs the compiled production BMR module factories, state-machine callbacks, world-state event subscriptions and component AI hints. It does not copy production decisions into fakes or start the Dalamud plugin.

Build BMR separately into an isolated output directory, then run from the repository root:

```powershell
dotnet run --project tests/EncounterDecisionCheck -c Release -p:BmrDll=C:/absolute/isolated/BossModReborn.dll
```

The check prints the loaded DLL's SHA256. `BMR_DEPENDENCIES` overrides the default local Dalamud directory; `BMR_SQPACK` overrides the local read-only game data directory. The latter defaults to the path verified in this workstation's launcher configuration. No user BMR configuration is read or written.

`Fixture.EnterState` seeks through private native transition methods only as test setup. Subsequent transitions run production callbacks from emitted real world-state operations. `Advance` updates the state machine; `Tick` also executes the module/component frame update. Casts finish only through an explicit `FinishCast` operation; advancing time does not fabricate cast events or elapsed-time updates. Reflection is used to activate internal production component types through the generated factory.

These are deterministic synthetic event traces. They do not verify live navigation, collision/pathfinding in the game, player input, or the correctness of the strategy sources themselves.

M12S phase two now has separate native decision groups for clone casts/replays, Doom, sparse parties, clockspot assignment, meteor ordering, existing positioning goals and their cleanup. DMU P5 coverage includes both Orchestra/Holy/Flare sequences, all three tower waves, reused tower actors with interleaved glow/explosion events, and all observed exaflare lines. The Portent and meteor fixtures seed native recorded-soak/tower-construction entry points; they do not replay an entire pull.

`TIMELINE_STRUCTURE` lines fingerprint state IDs, durations, links and forecast flags for comparison across DLLs. They do not imply identical callbacks or live behavior. M9/M10's pre-existing authored ID collisions are explicitly checked against their historical lists so this pass does not silently change saved plan IDs.
