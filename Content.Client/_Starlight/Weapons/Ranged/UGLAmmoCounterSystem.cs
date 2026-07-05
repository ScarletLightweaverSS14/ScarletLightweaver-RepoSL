using Content.Client.Weapons.Ranged.Systems;
using Content.Shared._Starlight.ItemSwitch.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using AmmoControlEvent = Content.Client.Weapons.Ranged.Systems.GunSystem.AmmoCounterControlEvent;
using AmmoUpdateEvent = Content.Client.Weapons.Ranged.Systems.GunSystem.UpdateAmmoCounterEvent;

namespace Content.Client._Starlight.Weapons.Ranged;

/// <summary>
/// Shows a "[GL] n/1" label in the ammo status panel when the M90 GL is in grenade
/// launcher mode, giving players a clear visual indicator of mode and loaded state.
/// </summary>
public sealed class UGLAmmoCounterSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ItemSwitchComponent, AmmoControlEvent>(OnControl);
        SubscribeLocalEvent<ItemSwitchComponent, AmmoUpdateEvent>(OnUpdate);
    }

    private void OnControl(Entity<ItemSwitchComponent> ent, ref AmmoControlEvent args)
    {
        if (ent.Comp.State != "grenade" || args.Control != null)
            return;

        var control = new GLModeStatusControl();

        // Initialise the count immediately so the label isn't empty on first display.
        if (TryComp<BallisticAmmoProviderComponent>(ent, out var ballistic))
            control.Update(ballistic.Count, ballistic.Capacity);

        args.Control = control;
    }

    private void OnUpdate(Entity<ItemSwitchComponent> ent, ref AmmoUpdateEvent args)
    {
        if (args.Control is not GLModeStatusControl glControl) return;
        if (!TryComp<BallisticAmmoProviderComponent>(ent, out var ballistic)) return;
        glControl.Update(ballistic.Count, ballistic.Capacity);
    }

    // ---- UI control ----

    private sealed class GLModeStatusControl : Label
    {
        public GLModeStatusControl()
        {
            MinHeight = 15;
            HorizontalExpand = true;
            HorizontalAlignment = HAlignment.Right;
            VerticalAlignment = VAlignment.Center;
            FontColorOverride = Color.Yellow;
            Text = "[GL] -/-";
        }

        public void Update(int count, int capacity)
        {
            Text = $"[GL] {count}/{capacity}";
            FontColorOverride = count > 0 ? Color.LimeGreen : Color.Red;
        }
    }
}

