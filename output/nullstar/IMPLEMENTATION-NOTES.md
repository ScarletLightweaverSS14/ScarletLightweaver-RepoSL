# Nullstar implementation notes for later stages

- Use an ability-specific marker component on each action for `ActionAttemptEvent` validation. Robust rejects duplicate subscriptions to the same component/event pair even across different systems. Hadal already subscribes to `WorldTargetActionComponent`/`ActionAttemptEvent`; Unbound currently subscribes to the instant-action equivalent. Rift Cleave therefore uses `NullstarRiftCleaveActionComponent` on its action entity. Do not add another generic targeted/instant subscription for subsequent moves.
- Keep cooldowns on the action owned by the sword. `GetItemActionsEvent` grants it while held; re-equipping must not recreate it or reset cooldowns.
- Rift Cleave starts its own standard DoAfter when the action fires so other players can see a fixed lane during the windup. The cooldown begins then, including cancelled attempts. `DoAfterAttemptEvent` checks the grip every tick. Completion/cancellation owns cleanup.
- Both the visual scale and damage lane read `NullstarRiftComponent` dimensions. The exported artwork occupies 120 × 24 pixels inside each 128 × 32 state. `new Angle(tangent)` rotates its horizontal X axis correctly; `ToWorldAngle()` adds a quarter-turn for south-facing art and would point this effect the wrong way.
- Store the effect relative to the caster's grid/parent and calculate geometry from its current world transform. That retains the fixed lane while supporting a moving or rotating parent grid. Per-target hit limits use elapsed phase time; the phase timestamp uses AutoPausedField.
- `ThrowingSystem.TryThrow` needs **both** `recoil: false` and `pushbackRatio: 0` to prevent camera recoil and physical recoil respectively. Rift Cleave pushes a target at most once; Unbound applies one outward impulse per target.
- `DamageableSystem.TryChangeDamage` preserves damage modifiers and armor through DamageModifyEvent. Use the damage system's accessors in verification; DamageableComponent's TotalDamage is access-restricted.
- Set `EmitSoundOnSpawn.positional: true` when a finite audio tail should outlive its effect entity. Unbound uses this to finish the accepted 8.5-second mix after the three-second visual disappears. Rift's charge remains parented so cancellation cuts it off.
- Preserve the approved Unbound recording. The first eight WAVs are checked against `rift-existing-audio-hashes.json`; new move sounds are additional clips.
- Optional callouts belong to a later presentation stage. Prioritize functional moves, their warning/readability, cleanup and sound first.

## Charged dash revision

- `NullstarRiftCleaveSystem` is a `VirtualController`. BeforeSolve runs after walking/friction; it sets a bounded velocity, then normal physics resolves movement. AfterSolve releases the slash at the actual stop. Do not replace this with a position teleport.
- `RayCastSystem.CastShape` sweeps each actual hard body fixture, with its collision layers/mask. Sweep again every step, not just on the initial cast; doors and walls can change while charging/dashing. Leave a small separation at contact.
- `NullstarCleaveDashComponent` blocks ordinary walking through `UpdateCanMoveEvent`, with refresh on startup AND shutdown. Cancel/delete/component-removal paths must release the lock and stop the dash velocity. Do not remove a preexisting generic immobilization component, since it might belong to another system.
- `PullingSystem.TryStopPull` requires the PullableComponent argument; it cannot be called with just a UID in this fork.
- New 128×64 motion art: core occupies 48×48, streak 120×24, arc 120×48. The arc's outer edge points along the rush after applying the same tangent rotation as the rift. Its quick 0.24-second sweep replaces the slow normal weapon arc for this action.
- Never scale a visible Sprite layer below 0.005; Robust warns because its matrix can become singular. Hide fully faded layers, clamp any remaining scale to 0.01.
- Free online audio source is retained in `source/audio/swishes.zip`, with provenance and licenses in `rift-audio-sources.md`. The rebuild script reads source WAV chunks (24-bit stereo) and emits mono PCM16. Do not substitute unlicensed sounds.
- Next requested separate stage: Hadal held-right-click charge UI, severe charge slowdown, two advancing slashes, then stomp. Keep its implementation separate from Nullstar's targeted action.
