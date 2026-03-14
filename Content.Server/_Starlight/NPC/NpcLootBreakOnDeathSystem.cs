using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Starlight.NPC;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Starlight.NPC;

/// <summary>
/// When an NPC with <see cref="NpcLootBreakOnDeathComponent"/> dies, each item held in its hands
/// has a <see cref="NpcLootBreakOnDeathComponent.Chance"/> probability of being broken.
///
/// If a YAML prototype named &lt;OriginalProto&gt;Broken exists, that entity is spawned in place
/// and the original is deleted. Otherwise the item is renamed and stripped of weapon components.
/// </summary>
public sealed class NpcLootBreakOnDeathSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcLootBreakOnDeathComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(EntityUid uid, NpcLootBreakOnDeathComponent component, MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        // Collect held items first so we don't modify during iteration
        var held = new List<EntityUid>(_hands.EnumerateHeld(uid));

        foreach (var item in held)
        {
            if (!_random.Prob(component.Chance))
                continue;

            BreakItem(item);
        }
    }

    private void BreakItem(EntityUid item)
    {
        var meta = MetaData(item);
        var protoId = meta.EntityPrototype?.ID;

        // If a dedicated broken-variant prototype exists, spawn it in place and remove original.
        if (protoId != null)
        {
            var brokenProtoId = protoId + "Broken";
            if (_protoManager.HasIndex<EntityPrototype>(brokenProtoId))
            {
                var coords = _transform.GetMoverCoordinates(item);
                var brokenEntity = Spawn(brokenProtoId, coords);
                // Strip weapon components immediately so clients never see them as functional.
                RemComp<GunComponent>(brokenEntity);
                RemComp<MeleeWeaponComponent>(brokenEntity);
                QueueDel(item);
                return;
            }
        }

        // Fallback: rename and strip weapon components in-place.
        _metaData.SetEntityName(item, $"(broken) {meta.EntityName}");
        RemComp<GunComponent>(item);
        RemComp<MeleeWeaponComponent>(item);
    }
}
