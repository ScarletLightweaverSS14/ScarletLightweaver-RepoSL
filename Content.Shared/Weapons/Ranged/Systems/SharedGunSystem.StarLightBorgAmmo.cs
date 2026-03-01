// 🌟Starlight🌟
using Content.Shared._Starlight.Weapons.AmmoCycler;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    [MustCallBase]
    protected virtual void InitializeBorgAmmo()
    {
        SubscribeLocalEvent<BallisticAmmoProviderComponent, BorgAmmoSwapEvent>(OnBorgAmmoSwap);
    }

    private void OnBorgAmmoSwap(EntityUid uid, BallisticAmmoProviderComponent comp, BorgAmmoSwapEvent args)
    {
        // Delete any physically-spawned ammo entities inside the container.
        foreach (var contained in new List<EntityUid>(comp.Container.ContainedEntities))
            Del(contained);

        comp.Entities.Clear();
        comp.Proto = args.NewProto;
        comp.UnspawnedCount = comp.Capacity;

        Dirty(uid, comp);
    }
}
