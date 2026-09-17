using Content.Shared._Starlight.Dragon;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Robust.Server.Audio;
using Robust.Shared.Audio;

namespace Content.Server._Starlight.Dragon;

public sealed partial class DragonRoarSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private readonly HashSet<Entity<MobStateComponent>> _nearby = [];

    public override void Initialize()
    {
        SubscribeLocalEvent<DragonRoarComponent, DragonRoarEvent>(OnRoar);
    }

    private void OnRoar(Entity<DragonRoarComponent> ent, ref DragonRoarEvent args)
    {
        if (args.Handled)
            return;

        var performer = ent.Owner;
        var comp = ent.Comp;

        _audio.PlayPvs(comp.RoarSound, performer, AudioParams.Default.WithVolume(4f));
        _popup.PopupEntity(Loc.GetString("western-dragon-roar-emote"), performer, PopupType.LargeCaution);

        var coords = Transform(performer).Coordinates;

        _nearby.Clear();
        _lookup.GetEntitiesInRange<MobStateComponent>(coords, comp.Range, _nearby);
        foreach (var target in _nearby)
        {
            if (target.Owner == performer)
                continue;
            _status.TryAddStatusEffectDuration(target.Owner, comp.PredatorHuntEffect, comp.StatusDuration);
        }

        args.Handled = true;
    }
}
