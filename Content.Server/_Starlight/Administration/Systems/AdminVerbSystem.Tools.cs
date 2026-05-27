using Content.Server._Starlight.Antags;
using Content.Server.Administration.Systems;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Content.Shared.Administration.Managers;
using Content.Shared.Database;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using static Content.Server.Administration.Systems.AdminVerbSystem;

namespace Content.Server.Starlight.Administration.Systems;
public sealed partial class AdminVerbSystem : EntitySystem
{
    [Dependency] private readonly AdminTestArenaSystem _adminTestArenaSystem = default!;
    [Dependency] private readonly ISharedAdminManager _adminManager = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(AddVerbs);
    }
    private void AddVerbs(GetVerbsEvent<Verb> args)
    {
        if (!EntityManager.TryGetComponent(args.User, out ActorComponent? actor))
            return;

        var player = actor.PlayerSession;

        if (!_adminManager.HasAdminFlag(player, AdminFlags.Admin))
            return;

        if (_adminManager.HasAdminFlag(player, AdminFlags.Admin))
        {
            Verb sendToTestArena = new()
            {
                Text = "Reset test arena",
                Category = VerbCategory.Tricks,
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/refresh.svg.192dpi.png")),

                Act = () =>
                {
                    //we technically load the map here, but it doesnt matter, this is safer since the behaviour of this function is garunteed
                    //and reimplementing it would be stupid and unsafe
                    var data = _adminTestArenaSystem.AssertArenaLoaded(player);

                    var _mapManager = _entities.System<SharedMapSystem>();

                    //we need to get the actual map ID, so first get the transform
                    if (!_entities.TryGetComponent(data.Map, out TransformComponent? transform))
                        return;
                    
                    //then get the map ID from the transform
                    MapId mapId = transform.MapID;

                    //call remove map on it
                    _mapManager.DeleteMap(mapId);
                    //_transformSystem.SetCoordinates(args.Target, new EntityCoordinates(data.gridUid ?? data.mapUid, Vector2.One));
                },
                Impact = LogImpact.Medium,
                Message = Loc.GetString("admin-trick-reset-test-arena-description"),
                Priority = (int)TricksVerbPriorities.SendToTestArena,
            };
            args.Verbs.Add(sendToTestArena);

            Verb preventObjectiveTargeting = new()
            {
                Text = "Prevent objective targeting",
                Category = VerbCategory.Tricks,
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/sentient.svg.192dpi.png")),
                Act = () =>
                {
                    EnsureComp<NoObjectiveTargetComponent>(args.Target);
                    _chat.SendAdminAnnouncementMessage(player, $"Added NoObjectiveTarget component to the entity! ({args.Target})");
                },
                Impact = LogImpact.Low,
                Message = "Prevents this entity from being targeted by other player's objectives. Will also prevent paraclones of this player.",
                Priority = (int)TricksVerbPriorities.BlockObjectiveTargeting
            };
            if (HasComp<ActorComponent>(args.Target)) args.Verbs.Add(preventObjectiveTargeting);

            // ── Redact (head) / Un-redact ─────────────────────────────────────
            var isRedacted    = HasComp<Content.Shared._Starlight.SCP.RedactedComponent>(args.Target);
            var isHeadRedact  = isRedacted && !Comp<Content.Shared._Starlight.SCP.RedactedComponent>(args.Target).FullBody;
            var isBodyRedact  = isRedacted && Comp<Content.Shared._Starlight.SCP.RedactedComponent>(args.Target).FullBody;

            Verb redactHeadVerb = new()
            {
                Text = isHeadRedact ? "Un-Redact (Head)" : "Redact Head",
                Category = VerbCategory.Tricks,
                Icon = new SpriteSpecifier.Rsi(new("/Textures/_Starlight/Clothing/Head/Hardsuits/Decimus_Helm.rsi"), "icon"),
                Act = () =>
                {
                    if (isHeadRedact)
                    {
                        RemComp<Content.Shared._Starlight.SCP.RedactedComponent>(args.Target);
                        _chat.SendAdminAnnouncementMessage(player, $"Removed head redaction from {args.Target}.");
                    }
                    else
                    {
                        var c = EnsureComp<Content.Shared._Starlight.SCP.RedactedComponent>(args.Target);
                        c.FullBody = false;
                        Dirty(args.Target, c);
                        _chat.SendAdminAnnouncementMessage(player, $"Applied head redaction to {args.Target}.");
                    }
                },
                Impact = LogImpact.Low,
                Message = isHeadRedact
                    ? "Removes the black-box head overlay and name scramble from this entity."
                    : "Adds a SCP-style black box over the entity's head and scrambles its name.",
                Priority = (int)TricksVerbPriorities.BlockObjectiveTargeting - 1,
            };
            args.Verbs.Add(redactHeadVerb);

            Verb redactBodyVerb = new()
            {
                Text = isBodyRedact ? "Un-Redact (Body)" : "Redact Full Body",
                Category = VerbCategory.Tricks,
                Icon = new SpriteSpecifier.Rsi(new("/Textures/_Starlight/Clothing/Head/Hardsuits/Decimus_Helm.rsi"), "icon"),
                Act = () =>
                {
                    if (isBodyRedact)
                    {
                        RemComp<Content.Shared._Starlight.SCP.RedactedComponent>(args.Target);
                        _chat.SendAdminAnnouncementMessage(player, $"Removed full-body redaction from {args.Target}.");
                    }
                    else
                    {
                        var c = EnsureComp<Content.Shared._Starlight.SCP.RedactedComponent>(args.Target);
                        c.FullBody = true;
                        Dirty(args.Target, c);
                        _chat.SendAdminAnnouncementMessage(player, $"Applied full-body redaction to {args.Target}.");
                    }
                },
                Impact = LogImpact.Low,
                Message = isBodyRedact
                    ? "Removes the full-body black-box overlay and name scramble from this entity."
                    : "Covers the entire entity in a black box and scrambles its name.",
                Priority = (int)TricksVerbPriorities.BlockObjectiveTargeting - 2,
            };
            args.Verbs.Add(redactBodyVerb);
        }
    }
}
