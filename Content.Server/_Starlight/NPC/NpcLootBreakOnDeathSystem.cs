using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Starlight.NPC;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Starlight.NPC;

/// <summary>
/// When an NPC with <see cref="NpcLootBreakOnDeathComponent"/> drops a weapon (because it went
/// critical or dead), the weapon has a <see cref="NpcLootBreakOnDeathComponent.Chance"/> probability
/// of being replaced by its broken prototype variant (appended "Broken").
/// Broken items have Gun, Melee, and ItemSlots components removed so they cannot be fired,
/// used as weapons, or unloaded.
/// </summary>
public sealed class NpcLootBreakOnDeathSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        // DidUnequipHandEvent fires on the NPC (uid) the moment an item leaves one of its hands.
        // This is the earliest reliable hook — the item still exists and hasn't been repositioned.
        SubscribeLocalEvent<NpcLootBreakOnDeathComponent, DidUnequipHandEvent>(OnNpcUnequippedHand);
    }

    private void OnNpcUnequippedHand(EntityUid uid, NpcLootBreakOnDeathComponent component, DidUnequipHandEvent args)
    {
        // Only process weapons
        if (!HasComp<GunComponent>(args.Unequipped) && !HasComp<MeleeWeaponComponent>(args.Unequipped))
            return;

        // Only break weapons when the NPC is crit or dead (not during normal gameplay)
        if (!TryComp<MobStateComponent>(uid, out var mobState))
            return;
        if (mobState.CurrentState != MobState.Critical && mobState.CurrentState != MobState.Dead)
            return;

        if (!_random.Prob(component.Chance))
            return;

        // Spawn at the NPC's position — the item hasn't been placed on the floor yet
        var coords = _transform.GetMoverCoordinates(uid);
        BreakItem(args.Unequipped, coords);
    }

    private void BreakItem(EntityUid item, EntityCoordinates spawnCoords)
    {
        var meta = MetaData(item);
        var protoId = meta.EntityPrototype?.ID;

        // If a dedicated broken-variant prototype exists, spawn it and remove original.
        if (protoId != null)
        {
            var brokenProtoId = protoId + "Broken";
            if (_protoManager.HasIndex<EntityPrototype>(brokenProtoId))
            {
                var brokenEntity = Spawn(brokenProtoId, spawnCoords);
                StripWeaponComponents(brokenEntity);
                QueueDel(item);
                return;
            }
        }

        // Fallback: rename and strip weapon components in-place.
        _metaData.SetEntityName(item, $"(broken) {meta.EntityName}");
        StripWeaponComponents(item);
    }

    /// <summary>Removes all components that allow the item to function as a weapon or be reloaded.</summary>
    private void StripWeaponComponents(EntityUid entity)
    {
        RemComp<GunComponent>(entity);
        RemComp<MeleeWeaponComponent>(entity);
        RemComp<ItemSlotsComponent>(entity); // prevents magazine unloading
    }
}
