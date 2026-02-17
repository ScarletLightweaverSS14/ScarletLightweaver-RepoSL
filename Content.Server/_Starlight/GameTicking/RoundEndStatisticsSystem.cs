using Content.Server.Cargo.Systems;
using Content.Server.GameTicking.Events;
using Content.Server.Power.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Power.Components;
using Content.Shared.Research.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Components;
using Robust.Shared.Player;

namespace Content.Server.GameTicking;

/// <summary>
/// Tracks various statistics during the round for display at round end.
/// </summary>
public sealed class RoundEndStatisticsSystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _role = default!;

    private readonly Dictionary<EntityUid, int> _lastKnownCargoBalances = new();
    private readonly HashSet<EntityUid> _clownsCurrentlyBeaten = new();
    private EntityUid? _statisticsEntity;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEnd);
        
        // Track cargo balance changes
        SubscribeLocalEvent<StationBankAccountComponent, BankBalanceUpdatedEvent>(OnBankBalanceUpdated);
        
        // Track healing
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>(OnDamageChanged);
        
        // Track research points
        SubscribeLocalEvent<ResearchServerComponent, ResearchServerPointsChangedEvent>(OnResearchPointsChanged);
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        // Create the statistics tracking entity
        _statisticsEntity = Spawn(null);
        var comp = AddComp<RoundEndStatisticsComponent>(_statisticsEntity.Value);
        
        // Reset tracking dictionaries
        _lastKnownCargoBalances.Clear();
        _clownsCurrentlyBeaten.Clear();
        
        Log.Info("Round end statistics tracking started");
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent ev)
    {
        _lastKnownCargoBalances.Clear();
        _clownsCurrentlyBeaten.Clear();
        _statisticsEntity = null;
    }

    private void OnBankBalanceUpdated(EntityUid uid, StationBankAccountComponent component, ref BankBalanceUpdatedEvent args)
    {
        if (_statisticsEntity == null || !TryComp<RoundEndStatisticsComponent>(_statisticsEntity.Value, out var stats))
            return;

        // Calculate the total money across all accounts
        int newTotal = 0;
        foreach (var (_, balance) in component.Accounts)
        {
            newTotal += balance;
        }

        // Update the total delta
        if (_lastKnownCargoBalances.TryGetValue(uid, out var lastBalance))
        {
            var delta = newTotal - lastBalance;
            stats.TotalCargoMoney += delta;
        }
        
        _lastKnownCargoBalances[uid] = newTotal;
    }

    private void OnDamageChanged(EntityUid uid, DamageableComponent component, DamageChangedEvent args)
    {
        if (_statisticsEntity == null || !TryComp<RoundEndStatisticsComponent>(_statisticsEntity.Value, out var stats))
            return;

        // If damage was healed (negative damage change)
        if (args.DamageDelta != null)
        {
            var totalDelta = args.DamageDelta.GetTotal();
            if (totalDelta < 0)
            {
                stats.TotalDamageHealed += (int)Math.Abs(totalDelta.Float());
            }
            
            // Check if this entity is a clown
            if (_mind.TryGetMind(uid, out var mindId, out var mind))
            {
                if (_role.MindHasRole<JobRoleComponent>((mindId, mind), out var jobRole) &&
                    jobRole.Value.Comp1.JobPrototype == "Clown")
                {
                    // If clown now has 50+ damage and wasn't tracked before, count it
                    if (component.TotalDamage >= 50 && !_clownsCurrentlyBeaten.Contains(uid))
                    {
                        _clownsCurrentlyBeaten.Add(uid);
                        stats.ClownBeatenCount++;
                    }
                    // If clown healed below 50, remove from tracking so they can be counted again
                    else if (component.TotalDamage < 50 && _clownsCurrentlyBeaten.Contains(uid))
                    {
                        _clownsCurrentlyBeaten.Remove(uid);
                    }
                }
            }
        }
    }

    private void OnResearchPointsChanged(EntityUid uid, ResearchServerComponent component, ref ResearchServerPointsChangedEvent args)
    {
        if (_statisticsEntity == null || !TryComp<RoundEndStatisticsComponent>(_statisticsEntity.Value, out var stats))
            return;

        // Track positive point changes
        if (args.Delta > 0)
        {
            stats.TotalSciencePoints += args.Delta;
        }
    }





    private void OnRoundEnd(RoundEndTextAppendEvent ev)
    {
        var stats = GetStatistics();
        if (stats == null)
            return;

        ev.AddLine("");
        ev.AddLine(Loc.GetString("round-end-statistics-header"));
        ev.AddLine("");

        // Cargo statistics
        if (stats.TotalCargoMoney != 0)
        {
            ev.AddLine(Loc.GetString("round-end-statistics-cargo-money", 
                ("amount", stats.TotalCargoMoney)));
        }

        // Medical healing
        if (stats.TotalDamageHealed > 0)
        {
            ev.AddLine(Loc.GetString("round-end-statistics-damage-healed", 
                ("amount", stats.TotalDamageHealed)));
        }

        // Science points
        if (stats.TotalSciencePoints > 0)
        {
            ev.AddLine(Loc.GetString("round-end-statistics-science-points", 
                ("points", stats.TotalSciencePoints)));
        }

        // SMES power
        if (stats.HighestSMESPowerOutput > 0)
        {
            ev.AddLine(Loc.GetString("round-end-statistics-highest-smes-power", 
                ("power", $"{stats.HighestSMESPowerOutput:F1}")));
        }

        // Clown beatings
        if (stats.ClownBeatenCount > 0)
        {
            ev.AddLine(Loc.GetString("round-end-statistics-clown-beaten", 
                ("count", stats.ClownBeatenCount)));
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_statisticsEntity == null || !TryComp<RoundEndStatisticsComponent>(_statisticsEntity.Value, out var stats))
            return;

        // Track highest SMES power output
        var query = EntityQueryEnumerator<PowerNetworkBatteryComponent, BatteryComponent>();
        while (query.MoveNext(out var uid, out var netBattery, out var battery))
        {
            // Check if this is an SMES (has high capacity)
            if (battery.MaxCharge >= 1000000) // 1 MJ or more
            {
                var currentOutput = netBattery.CurrentSupply;
                if (currentOutput > stats.HighestSMESPowerOutput)
                {
                    stats.HighestSMESPowerOutput = currentOutput;
                }
            }
        }
    }

    /// <summary>
    /// Gets the round end statistics component if it exists
    /// </summary>
    public RoundEndStatisticsComponent? GetStatistics()
    {
        if (_statisticsEntity == null)
            return null;
        
        return CompOrNull<RoundEndStatisticsComponent>(_statisticsEntity.Value);
    }
}
