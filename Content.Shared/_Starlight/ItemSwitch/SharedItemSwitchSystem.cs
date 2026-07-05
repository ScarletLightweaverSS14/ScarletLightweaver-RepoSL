using System.Linq;
using Content.Shared._Starlight.ItemSwitch.Components;
using Content.Shared._Starlight.Switchable;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._Starlight.ItemSwitch;
public abstract partial class SharedItemSwitchSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedItemSystem _item = default!;
    [Dependency] private ClothingSystem _clothing = default!;

    private EntityQuery<ItemSwitchComponent> _query;

    public override void Initialize()
    {
        base.Initialize();

        _query = GetEntityQuery<ItemSwitchComponent>();

        SubscribeLocalEvent<ItemSwitchComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ItemSwitchComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ItemSwitchComponent, GetVerbsEvent<ActivationVerb>>(OnActivateVerb);
        SubscribeLocalEvent<ItemSwitchComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<ItemSwitchComponent, SwitchableActionEvent>(OnSwitchableAction); // Starlight — hotbar action toggle (e.g. M90 GL underbarrel)

        SubscribeLocalEvent<ClothingComponent, ItemSwitchedEvent>(UpdateClothingLayer);
    }

    private void OnMapInit(Entity<ItemSwitchComponent> ent, ref MapInitEvent args)
    {
        var state = ent.Comp.State;
        state ??= ent.Comp.States.Keys.FirstOrDefault();
        if (state != null)
            Switch((ent, ent.Comp), state, predicted: ent.Comp.Predictable);
    }

    private void OnUseInHand(Entity<ItemSwitchComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !ent.Comp.OnUse || ent.Comp.States.Count == 0) return;
        args.Handled = true;

        if (ent.Comp.States.TryGetValue(Next(ent), out var state) && state.Hiden)
            return;

        Switch((ent, ent.Comp), Next(ent), args.User, predicted: ent.Comp.Predictable);
    }

    private void OnActivateVerb(Entity<ItemSwitchComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !ent.Comp.OnActivate || ent.Comp.States.Count == 0) return;

        var user = args.User;
        int addedVerbs = 0;

        foreach (var state in ent.Comp.States)
        {
            if (state.Value.Hiden)
                continue;
            args.Verbs.Add(new ActivationVerb()
            {
                Text = Loc.TryGetString(state.Value.Verb, out var title) ? title : state.Value.Verb,
                Category = VerbCategory.Switch,
                Act = () => Switch((ent.Owner, ent.Comp), state.Key, user, ent.Comp.Predictable)
            });
            addedVerbs++;
        }

        if (addedVerbs > 0)
            args.ExtraCategories.Add(VerbCategory.Switch);
    }

    private void OnActivate(Entity<ItemSwitchComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !ent.Comp.OnActivate)
            return;

        args.Handled = true;

        if (ent.Comp.States.TryGetValue(Next(ent), out var state) && state.Hiden)
            return;

        Switch((ent.Owner, ent.Comp), Next(ent), args.User, predicted: ent.Comp.Predictable);
    }

    // Starlight — raised when a player uses the hotbar toggle action (e.g. M90 GL underbarrel toggle)
    private void OnSwitchableAction(Entity<ItemSwitchComponent> ent, ref SwitchableActionEvent args)
    {
        if (ent.Comp.States.Count == 0) return;
        var next = Next(ent);
        if (ent.Comp.States.TryGetValue(next, out var state) && state.Hiden) return;
        if (Switch((ent, ent.Comp), next, args.Performer, predicted: ent.Comp.Predictable))
            args.Handled = true;
    }

    private static string Next(Entity<ItemSwitchComponent> ent)
    {
        var foundCurrent = false;
        string firstState = null!;

        foreach (var state in ent.Comp.States.Keys)
        {
            firstState ??= state;

            if (foundCurrent)
                return state;

            if (state == ent.Comp.State)
                foundCurrent = true;
        }
        return firstState;
    }

    /// <summary>
    /// Used when an item is attempted to be toggled.
    /// Sets its state to the opposite of what it is.
    /// </summary>
    /// <returns>Same as <see cref="TrySetActive"/></returns>
    public bool Switch(Entity<ItemSwitchComponent?> ent, string key, EntityUid? user = null, bool predicted = true)
    {
        if (!_query.Resolve(ent, ref ent.Comp, false) || !ent.Comp.States.TryGetValue(key, out var state))
            return false;

        var uid = ent.Owner;
        var comp = ent.Comp;

        if (!comp.Predictable && _netManager.IsClient)
            return true;

        var attempt = new ItemSwitchAttemptEvent
        {
            User = user,
            State = key
        };
        RaiseLocalEvent(uid, ref attempt);

        if (ent.Comp.States.TryGetValue(ent.Comp.State, out var prevState) && prevState.RemoveComponents && prevState.Components is not null)
            EntityManager.RemoveComponents(ent, prevState.Components);

        if (state.Components is not null)
            EntityManager.AddComponents(ent, state.Components);

        if (!comp.Predictable) predicted = false;

        if (attempt.Cancelled)
        {
            if (predicted)
                _audio.PlayPredicted(state.SoundFailToActivate, uid, user);
            else
                _audio.PlayPvs(state.SoundFailToActivate, uid);

            if (attempt.Popup != null && user != null)
                if (predicted)
                    _popup.PopupClient(attempt.Popup, uid, user.Value);
                else
                    _popup.PopupEntity(attempt.Popup, uid, user.Value);

            return false;
        }

        if (predicted)
            _audio.PlayPredicted(state.SoundStateActivate, uid, user);
        else
            _audio.PlayPvs(state.SoundStateActivate, uid);

        comp.State = key;
        UpdateVisuals((uid, comp), key);
        Dirty(uid, comp);

        var switched = new ItemSwitchedEvent { Predicted = predicted, State = key, User = user };
        RaiseLocalEvent(uid, ref switched);

        return true;
    }
    public virtual void VisualsChanged(Entity<ItemSwitchComponent> ent, string key)
    {

    }
    protected virtual void UpdateVisuals(Entity<ItemSwitchComponent> ent, string key)
    {
        if (TryComp(ent, out AppearanceComponent? appearance))
            _appearance.SetData(ent, SwitchableVisuals.Switched, key, appearance);
        // Starlight — only update the held-prefix when the state defines a sprite;
        // states without sprites (e.g. M90 GL rifle/grenade modes) should not clear in-hand RSI rendering.
        if (ent.Comp.States.TryGetValue(key, out var switchState) && switchState.Sprite != null)
            _item.SetHeldPrefix(ent, key);

        VisualsChanged(ent, key);
    }
    private void UpdateClothingLayer(Entity<ClothingComponent> ent, ref ItemSwitchedEvent args)
    {
        // Only update the equipped prefix when the state defines a sprite;
        // states without sprites would look for e.g. "rifle-equipped-BACKPACK" which doesn't exist.
        if (!TryComp<ItemSwitchComponent>(ent, out var switcher) ||
            !switcher.States.TryGetValue(args.State, out var switchState) ||
            switchState.Sprite == null)
            return;
        _clothing.SetEquippedPrefix(ent, args.State, ent.Comp);
    }
}
