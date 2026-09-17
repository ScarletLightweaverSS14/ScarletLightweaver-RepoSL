using Content.Shared._Starlight.Dragon;

namespace Content.Client._Starfall.Particles;

public sealed partial class FlammableParticleSystem
{
    private bool ShouldSpawnSmoke(EntityUid uid) => !HasComp<WesternDragonFirePatchComponent>(uid);
}
