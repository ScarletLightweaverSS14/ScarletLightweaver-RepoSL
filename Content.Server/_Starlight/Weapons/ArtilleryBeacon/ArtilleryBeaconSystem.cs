using Content.Server.AlertLevel;
using Content.Server.Chat.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Starlight.Weapons.ArtilleryBeacon;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Weapons.ArtilleryBeacon;

/// <summary>
/// Drives the NEMESIS-IV Orbital Strike Designator.
///
/// Arming sequence:
///   1. Player uses item in hand.
///   2. Station alert is forced to Red (plays siren).
///   3. A station-wide announcement warns of incoming fire in 30 s.
///   4. After <see cref="ArtilleryBeaconComponent.CountdownSeconds"/>, each "impactor" fires
///      at a random offset within <see cref="ArtilleryBeaconComponent.StrikeRadius"/> tiles,
///      one per <see cref="ArtilleryBeaconComponent.StrikeInterval"/> seconds.
/// </summary>
public sealed class ArtilleryBeaconSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly AlertLevelSystem _alertLevel = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    private static readonly SoundSpecifier ActivationSound =
        new SoundPathSpecifier("/Audio/Items/beep.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArtilleryBeaconComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ArtilleryBeaconComponent, ArtilleryBeaconArmDoAfterEvent>(OnArmDoAfter);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Arming
    // ──────────────────────────────────────────────────────────────────────────

    private void OnUseInHand(Entity<ArtilleryBeaconComponent> ent, ref UseInHandEvent args)
    {
        if (ent.Comp.IsArmed)
        {
            _popup.PopupEntity(
                Loc.GetString("artillery-beacon-already-armed"),
                ent.Owner, args.User,
                PopupType.MediumCaution);
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("artillery-beacon-arming-self"),
            ent.Owner, args.User,
            PopupType.MediumCaution);

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            args.User,
            5f,
            new ArtilleryBeaconArmDoAfterEvent(),
            ent.Owner,
            used: ent.Owner)
        {
            BreakOnMove = true,
            BreakOnHandChange = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
        args.Handled = true;
    }

    private void OnArmDoAfter(Entity<ArtilleryBeaconComponent> ent, ref ArtilleryBeaconArmDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        ent.Comp.IsArmed = true;
        ent.Comp.StrikesRemaining = ent.Comp.NumberOfStrikes;
        ent.Comp.FirstStrikeTime = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.CountdownSeconds);
        ent.Comp.NextStrikeTime = ent.Comp.FirstStrikeTime;

        _audio.PlayEntity(ActivationSound, args.User, ent.Owner);

        _popup.PopupEntity(
            Loc.GetString("artillery-beacon-armed-self"),
            ent.Owner, args.User,
            PopupType.LargeCaution);

        // Station-wide red alert and announcement.
        var station = _stationSystem.GetOwningStation(ent.Owner);
        if (station != null)
        {
            // Force red alert — this plays the siren automatically.
            _alertLevel.SetLevel(station.Value, "red",
                playSound: true,
                announce: false,
                force: true,
                locked: true);

            // Custom announcement over intercom.
            _chat.DispatchStationAnnouncement(
                station.Value,
                Loc.GetString("artillery-beacon-announcement",
                    ("seconds", (int)ent.Comp.CountdownSeconds)),
                Loc.GetString("artillery-beacon-sender"),
                playDefaultSound: false,
                colorOverride: Color.OrangeRed);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Impact loop
    // ──────────────────────────────────────────────────────────────────────────

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ArtilleryBeaconComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsArmed || now < comp.NextStrikeTime)
                continue;

            if (comp.StrikesRemaining <= 0)
            {
                comp.IsArmed = false;
                // Beacon is a one-use device — delete it after all strikes land.
                QueueDel(uid);
                continue;
            }

            FireStrike(uid, comp);
            comp.StrikesRemaining--;
            comp.NextStrikeTime = now + TimeSpan.FromSeconds(comp.StrikeInterval);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private void FireStrike(EntityUid uid, ArtilleryBeaconComponent comp)
    {
        var xform = Transform(uid);

        // Scatter the impact point randomly within StrikeRadius tiles of the beacon.
        var offset = _random.NextVector2(-comp.StrikeRadius, comp.StrikeRadius);
        var epicenter = new MapCoordinates(
            xform.WorldPosition + offset,
            xform.MapID);

        _explosion.QueueExplosion(
            epicenter,
            comp.ExplosionType,
            comp.TotalIntensity,
            comp.Slope,
            comp.MaxTileIntensity,
            cause: uid,
            canCreateVacuum: false,
            addLog: false);
    }
}
