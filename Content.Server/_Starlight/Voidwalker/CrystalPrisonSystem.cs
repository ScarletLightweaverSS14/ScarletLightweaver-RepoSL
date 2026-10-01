using Content.Server.Chat;
using Content.Server.Chat.Systems;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Chat;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._Starlight.Voidwalker;

public sealed partial class CrystalPrisonSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrystalPrisonComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CrystalPrisonComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CrystalPrisonComponent, CrystalPrisonEvent>(OnCast);
    }

    private void OnStartup(Entity<CrystalPrisonComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<CrystalPrisonComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnCast(Entity<CrystalPrisonComponent> ent, ref CrystalPrisonEvent args)
    {
        if (args.Handled || args.Action.Owner != ent.Comp.ActionEntity || !_mobState.IsAlive(ent.Owner) ||
            _containers.IsEntityOrParentInContainer(ent.Owner) || !args.Target.IsValid(EntityManager))
            return;

        var action = (args.Action.Owner, Comp<WorldTargetActionComponent>(args.Action));
        if (!_actions.ValidateWorldTarget(ent.Owner, args.Target, action))
            return;

        var tile = _turf.GetTileRef(args.Target);
        if (tile is not { } center || _turf.IsSpace(center) ||
            _turf.IsTileBlocked(center, CollisionGroup.Impassable))
        {
            ShowBlocked(ent);
            return;
        }

        // Anchor to the floor, not to a target entity: moving the target cannot drag the cage.
        var centerCoords = _turf.GetTileCenter(center);
        if (!_actions.ValidateWorldTarget(ent.Owner, centerCoords, action))
            return;

        var placements = new List<EntityCoordinates>();
        var tiles = new HashSet<Vector2i>();
        foreach (var offset in ent.Comp.Offsets)
        {
            if (offset == Vector2i.Zero || !tiles.Add(center.GridIndices + offset))
                continue;

            var coords = _map.ToCenterCoordinates(center.GridUid, center.GridIndices + offset);
            var edge = _turf.GetTileRef(coords);
            if (edge is not { } perimeter || perimeter.GridUid != center.GridUid || _turf.IsSpace(perimeter))
            {
                ShowBlocked(ent);
                return;
            }

            // Existing walls can complete the cage. Occupants on its edge leave an escape opening.
            if (_turf.IsTileBlocked(perimeter, ent.Comp.PlacementMask, minIntersectionArea: 0.01f))
                continue;

            placements.Add(coords);
        }

        if (placements.Count == 0)
        {
            ShowBlocked(ent);
            return;
        }

        // Validate the whole footprint before spawning, so a rejected cast creates no partial cage.
        foreach (var coords in placements)
        {
            Spawn(ent.Comp.CrystalPrototype, coords);
            if (ent.Comp.GrowthEffect is { } effect)
                Spawn(effect, coords);
        }

        _audio.PlayPvs(ent.Comp.GrowthSound, centerCoords);
        _chat.TrySendInGameICMessage(ent.Owner, Loc.GetString(ent.Comp.Callout),
            InGameICChatType.Speak, ChatTransmitRange.Normal);
        args.Handled = true;
    }

    private void ShowBlocked(EntityUid caster)
    {
        _popup.PopupEntity(Loc.GetString("voidwalker-crystal-prison-blocked"), caster, caster);
    }
}
