using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._Starlight.Weapons.Engraving;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.Weapons.Engraving;

public sealed class EngravableWeaponSystem : SharedEngravableWeaponSystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly QuickDialogSystem _dialog = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;

    private static readonly ProtoId<ToolQualityPrototype> WeldingQuality = "Welding";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EngravableWeaponComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<EngravableWeaponComponent, GetVerbsEvent<ActivationVerb>>(AddEngraveVerb);
        SubscribeLocalEvent<EngravableWeaponComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<EngravableWeaponComponent, EngravingRemoveWithSolderDoAfterEvent>(OnSolderDoAfter);
    }

    private void OnMapInit(Entity<EngravableWeaponComponent> ent, ref MapInitEvent args)
    {
        // Store the original name so we can restore it later.
        if (string.IsNullOrEmpty(ent.Comp.BaseName))
            ent.Comp.BaseName = MetaData(ent).EntityName;
    }

    private void AddEngraveVerb(Entity<EngravableWeaponComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        // Can only engrave when there is no existing engraving. Must weld it off first to re-engrave.
        if (!string.IsNullOrEmpty(ent.Comp.Nickname))
            return;

        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        var label = Loc.GetString("engravable-weapon-verb-engrave");

        args.Verbs.Add(new ActivationVerb
        {
            Text = label,
            Act = () =>
            {
                _dialog.OpenDialog(actor.PlayerSession,
                    label,
                    Loc.GetString("engravable-weapon-dialog-prompt"),
                    (string nickname) =>
                    {
                        if (actor.PlayerSession.AttachedEntity == null || !HasComp<EngravableWeaponComponent>(ent))
                            return;

                        nickname = nickname.Trim();

                        // Enforce a 10-word limit.
                        var wordCount = nickname.Split((char[]) null!, StringSplitOptions.RemoveEmptyEntries).Length;
                        if (string.IsNullOrWhiteSpace(nickname) || wordCount > 10)
                        {
                            _popup.PopupEntity(Loc.GetString("engravable-weapon-popup-invalid"), ent, actor.PlayerSession);
                            return;
                        }

                        ApplyNickname(ent, nickname);

                        _popup.PopupEntity(
                            Loc.GetString("engravable-weapon-popup-success", ("nickname", nickname)),
                            actor.PlayerSession.AttachedEntity.Value,
                            actor.PlayerSession,
                            PopupType.Medium);

                        _adminLogger.Add(LogType.Action, LogImpact.Low,
                            $"{ToPrettyString(actor.PlayerSession.AttachedEntity):player} engraved {ToPrettyString(ent):weapon} with nickname: {nickname}");
                    });
            },
            Impact = LogImpact.Low,
        });
    }

    /// <summary>
    /// When a welder is used on an engraved weapon, start a DoAfter to remove the engraving.
    /// </summary>
    private void OnInteractUsing(Entity<EngravableWeaponComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (string.IsNullOrEmpty(ent.Comp.Nickname))
            return;

        args.Handled = _tool.UseTool(
            args.Used,
            args.User,
            ent,
            ent.Comp.SolderTime,
            WeldingQuality,
            new EngravingRemoveWithSolderDoAfterEvent(),
            fuel: 1f);

        if (args.Handled)
        {
            _popup.PopupEntity(
                Loc.GetString("engravable-weapon-popup-soldering"),
                ent,
                args.User,
                PopupType.Small);
        }
    }

    private void OnSolderDoAfter(Entity<EngravableWeaponComponent> ent, ref EngravingRemoveWithSolderDoAfterEvent args)
    {
        if (args.Cancelled || args.Used == null)
            return;

        var user = args.User;
        RemoveNickname(ent);

        _popup.PopupEntity(
            Loc.GetString("engravable-weapon-popup-removed"),
            user,
            user,
            PopupType.Medium);

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} removed engraving from {ToPrettyString(ent):weapon} using a welder");
    }

    private void ApplyNickname(Entity<EngravableWeaponComponent> ent, string nickname)
    {
        ent.Comp.Nickname = nickname;
        Dirty(ent);
        _metaData.SetEntityName(ent, $"{ent.Comp.BaseName} '{nickname}'");
    }

    private void RemoveNickname(Entity<EngravableWeaponComponent> ent)
    {
        ent.Comp.Nickname = string.Empty;
        Dirty(ent);
        _metaData.SetEntityName(ent, ent.Comp.BaseName);
    }
}
