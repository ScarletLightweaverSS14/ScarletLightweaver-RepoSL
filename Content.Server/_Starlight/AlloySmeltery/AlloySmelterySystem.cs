using Content.Server.Atmos.EntitySystems;
using Content.Server.Lathe;
using Content.Server.Lathe.Components;
using Content.Shared._Starlight.AlloySmeltery;
using Content.Shared.Atmos;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.AlloySmeltery;

/// <summary>
/// Handles the alloy smeltery machine:
/// - When a recipe finishes on a machine with <see cref="AlloySmelteryOutputComponent"/>,
///   the spawned output entity receives a <see cref="HotAlloyComponent"/>.
/// - <see cref="HotAlloyComponent"/> entities have <see cref="DamageOnHoldingComponent"/>
///   (burn damage) while still above safe temperature.
/// - They cool passively via atmosphere or faster with cold gas / water contact.
/// </summary>
public sealed class AlloySmelterySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageOnHoldingSystem _damageOnHolding = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        // When the lathe finishes production on a smeltery, mark the output as hot.
        SubscribeLocalEvent<AlloySmelteryOutputComponent, LatheStartPrintingEvent>(OnSmelteryStartPrinting);

        // When a hot alloy component is added, enable the DamageOnHolding component.
        SubscribeLocalEvent<HotAlloyComponent, ComponentStartup>(OnHotAlloyStartup);
        SubscribeLocalEvent<HotAlloyComponent, ComponentShutdown>(OnHotAlloyShutdown);

        // Cool via atmosphere.
        SubscribeLocalEvent<HotAlloyComponent, AtmosExposedUpdateEvent>(OnAtmosExposedUpdate);

        // Material gauge UI.
        SubscribeLocalEvent<AlloySmelteryOutputComponent, AfterActivatableUIOpenEvent>(OnUiOpened);
        SubscribeLocalEvent<AlloySmelteryOutputComponent, MaterialAmountChangedEvent>(OnMaterialChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Passive (non-atmos) cooling tick for entities not exposed to atmosphere.
        var query = EntityQueryEnumerator<HotAlloyComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime < comp.NextCoolTick)
                continue;
            comp.NextCoolTick = _timing.CurTime + TimeSpan.FromSeconds(1);

            // Passive cooling in air at default rate.
            CoolAlloy((uid, comp), comp.CoolingRatePerSecond);
        }
    }

    private void OnSmelteryStartPrinting(Entity<AlloySmelteryOutputComponent> ent, ref LatheStartPrintingEvent args)
    {
        // We cannot directly intercept the spawned entity here because printing hasn't
        // finished yet.  Instead we subscribe to the finished-production path by
        // listening for newly spawned entities.  The simplest approach here is to rely
        // on the fact that alloy sheets have HotAlloyComponent already defined in YAML
        // (they always start hot). This event just serves as a confirmation.
        // The actual hot-state is handled entirely by HotAlloyComponent logic.
    }

    private void OnHotAlloyStartup(Entity<HotAlloyComponent> ent, ref ComponentStartup args)
    {
        // Ensure DamageOnHolding is present. Damage/Interval values are set in YAML.
        // Enable or disable based on starting temperature.
        EnsureComp<DamageOnHoldingComponent>(ent);
        var shouldBurn = ent.Comp.Temperature > ent.Comp.SafeTemperature;
        _damageOnHolding.SetEnabled(ent.Owner, shouldBurn);
    }

    private void OnHotAlloyShutdown(Entity<HotAlloyComponent> ent, ref ComponentShutdown args)
    {
        // Remove burn damage when fully cooled.
        RemComp<DamageOnHoldingComponent>(ent);
    }

    private void OnAtmosExposedUpdate(Entity<HotAlloyComponent> ent, ref AtmosExposedUpdateEvent args)
    {
        var gasMix = args.GasMixture;
        var gasTemp = gasMix.Temperature;

        // Calculate heat exchange: the colder the gas, the faster the cooling.
        // Minimum gas temp we care about to avoid division issues.
        if (gasTemp < 1f)
            gasTemp = 1f;

        // Extra cooling multiplier from cold gas (e.g., CO2 fire suppression, cold room)
        var tempDelta = ent.Comp.Temperature - gasTemp;
        if (tempDelta <= 0f)
            return; // Gas is hotter than alloy, no cooling happens here.

        // Scale cooling rate by temperature difference. Formula: Fourier-ish.
        // Base rate * (1 + delta/500) so cold gas speeds cooling significantly.
        var rate = ent.Comp.CoolingRatePerSecond * (1f + tempDelta / 500f);
        CoolAlloy(ent, rate);
    }

    /// <summary>
    /// Reduces the alloy temperature by <paramref name="amount"/> degrees and
    /// updates the <see cref="DamageOnHoldingComponent"/> enabled state accordingly.
    /// Removes <see cref="HotAlloyComponent"/> once the alloy is safe to handle.
    /// </summary>
    private void CoolAlloy(Entity<HotAlloyComponent> ent, float amount)
    {
        ent.Comp.Temperature -= amount;

        if (TryComp<DamageOnHoldingComponent>(ent, out var dmg))
        {
            var shouldBurnNow = ent.Comp.Temperature > ent.Comp.SafeTemperature;
            if (dmg.Enabled != shouldBurnNow)
            {
                _damageOnHolding.SetEnabled(ent.Owner, shouldBurnNow);

                if (!shouldBurnNow)
                {
                    _popup.PopupEntity(
                        Loc.GetString("hot-alloy-cooled"),
                        ent,
                        PopupType.Medium);
                }
            }
        }

        // Safe to touch now — remove the hot component entirely.
        if (ent.Comp.Temperature <= ent.Comp.SafeTemperature)
        {
            RemComp<HotAlloyComponent>(ent);
        }
        else
        {
            Dirty(ent, ent.Comp);
        }
    }

    private void OnUiOpened(Entity<AlloySmelteryOutputComponent> ent, ref AfterActivatableUIOpenEvent args)
    {
        // Open the material gauge window alongside the lathe menu.
        _uiSystem.OpenUi(ent.Owner, AlloySmelteryUiKey.Key, args.User);
        UpdateBuiState(ent);
    }

    private void OnMaterialChanged(Entity<AlloySmelteryOutputComponent> ent, ref MaterialAmountChangedEvent args)
    {
        UpdateBuiState(ent);
    }

    private void UpdateBuiState(Entity<AlloySmelteryOutputComponent> ent)
    {
        if (!TryComp<MaterialStorageComponent>(ent, out var matComp))
            return;

        var stored = _materialStorage.GetStoredMaterials((ent.Owner, matComp));
        var entries = new List<AlloySmelteryMaterialEntry>();

        foreach (var (id, amount) in stored)
        {
            if (amount <= 0)
                continue;

            if (!_protoManager.TryIndex<MaterialPrototype>(id, out var proto))
                continue;

            entries.Add(new AlloySmelteryMaterialEntry(
                id,
                Loc.GetString(proto.Name),
                amount,
                proto.Color));
        }

        entries.Sort((a, b) => string.Compare(a.LocalizedName, b.LocalizedName, StringComparison.OrdinalIgnoreCase));

        var state = new AlloySmelteryBuiState(entries, ent.Comp.DisplayCapacity);
        _uiSystem.SetUiState(ent.Owner, AlloySmelteryUiKey.Key, state);
    }
}
