using Content.Shared._Starlight.Dragon;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Robust.Server.Audio;
using Robust.Shared.Audio;

namespace Content.Server._Starlight.Dragon;

public sealed partial class DragonRoarSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly StatusEffectsSystem _status = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    private readonly HashSet<Entity<MobStateComponent>> _nearby = [];
    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _nearbyHumanoids = [];

    public override void Initialize()
    {
        SubscribeLocalEvent<DragonRoarEvent>(OnRoar);
    }

    private void OnRoar(DragonRoarEvent args)
    {
        if (args.Handled)
            return;

        var performer = args.Performer;
        if (!TryComp<DragonRoarComponent>(performer, out var comp))
            return;

        _audio.PlayPvs(comp.RoarSound, performer, AudioParams.Default.WithVolume(4f));
        _popup.PopupEntity(Loc.GetString("western-dragon-roar-emote"), performer, PopupType.LargeCaution);

        var coords = Transform(performer).Coordinates;

        _nearby.Clear();
        _lookup.GetEntitiesInRange<MobStateComponent>(coords, comp.Range, _nearby);
        foreach (var target in _nearby)
        {
            if (target.Owner == performer)
                continue;
            if (_status.TryAddStatusEffectDuration(target.Owner, comp.PredatorHuntEffect, comp.StatusDuration))
                _movement.RefreshMovementSpeedModifiers(target.Owner);
        }

        // Red glow on nearby humanoids only
        _nearbyHumanoids.Clear();
        _lookup.GetEntitiesInRange<HumanoidAppearanceComponent>(coords, comp.Range, _nearbyHumanoids);
        foreach (var humanoid in _nearbyHumanoids)
        {
            if (humanoid.Owner == performer)
                continue;
            Spawn("WesternDragonRoarGlowEffect", Transform(humanoid.Owner).Coordinates);
        }

        args.Handled = true;
    }
}
