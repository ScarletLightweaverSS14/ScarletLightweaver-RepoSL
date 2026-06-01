using Content.Shared.Examine;
using Robust.Shared.Utility;

namespace Content.Shared._Starlight.Weapons.Engraving;

public abstract class SharedEngravableWeaponSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EngravableWeaponComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<EngravableWeaponComponent> ent, ref ExaminedEvent args)
    {
        if (string.IsNullOrEmpty(ent.Comp.Nickname))
            return;

        var msg = new FormattedMessage();
        msg.AddMarkupOrThrow(Loc.GetString("engravable-weapon-examine-engraved", ("nickname", ent.Comp.Nickname)));
        args.PushMessage(msg, 1);
    }
}
