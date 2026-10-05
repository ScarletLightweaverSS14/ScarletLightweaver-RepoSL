using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Starlight.Weapons.Melee;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._Starlight.Actions;

// Temporary verification, archived outside the test project after the stage is checked.
public sealed class NullstarRiftCleaveTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private void PrepareMob(EntityUid mob)
    {
        // The interaction fixture has one plating tile surrounded by vacuum.
        // Isolate environmental damage while retaining ordinary combat health and damage processing.
        SEntMan.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(mob);
        SEntMan.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(mob);
        SEntMan.RemoveComponent<Content.Shared.Temperature.Components.TemperatureDamageComponent>(mob);
        // BaseMobSpeciesOrganic also heals small injuries every second; freeze that for exact hit accounting.
        SEntMan.RemoveComponent<Content.Shared.Damage.Components.PassiveDamageComponent>(mob);
        Server.System<DamageableSystem>().ClearAllDamage(mob);
    }

    private EntityUid EquipBlade()
    {
        PrepareMob(SPlayer);
        var blade = SEntMan.SpawnEntity("ScarletNullstar", Transform.GetMapCoordinates(SPlayer));
        Assert.That(HandSys.TryPickup(SPlayer, blade), Is.True);
        var comp = SEntMan.GetComponent<NullstarRiftCleaveComponent>(blade);
        var action = Server.System<SharedActionsSystem>().GetAction(comp.ActionEntity).Value;
        var attempt = new ActionAttemptEvent(SPlayer);
        SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
        Assert.That(attempt.Cancelled, Is.True, "Requires both hands.");
        Assert.That(Server.System<SharedWieldableSystem>().TryWield(blade, SEntMan.GetComponent<WieldableComponent>(blade), SPlayer), Is.True);
        return blade;
    }

    private EntityUid Cast(EntityUid blade)
    {
        var origin = Transform.GetMapCoordinates(SPlayer);
        var action = Server.System<SharedActionsSystem>().GetAction(SEntMan.GetComponent<NullstarRiftCleaveComponent>(blade).ActionEntity).Value;
        var cast = new NullstarRiftCleaveEvent
        {
            Target = Transform.ToCoordinates(Transform.GetParentUid(SPlayer), new MapCoordinates(origin.Position + Vector2.UnitY * 4, origin.MapId)),
        };
        Server.System<SharedActionsSystem>().PerformAction(SPlayer, action, cast);
        Assert.That(cast.Handled, Is.True);
        Assert.That(action.Comp.Cooldown, Is.Not.Null);
        var component = SEntMan.GetComponent<NullstarRiftCleaveComponent>(blade);
        Assert.That(component.PendingDoAfter, Is.Not.Null);
        Assert.That(component.PendingRift, Is.Not.Null);
        return component.PendingRift.Value;
    }

    [Test]
    public async Task ChargesThenDashesAndStrikesAtArrival()
    {
        EntityUid blade = default, rift = default, near = default, outside = default, entrant = default;
        MapCoordinates center = default, start = default;
        await Server.WaitAssertion(() =>
        {
            blade = EquipBlade();
            var origin = Transform.GetMapCoordinates(SPlayer);
            start = origin;
            EntityUid Mob(Vector2 offset)
            {
                var mob = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(origin.Position + offset, origin.MapId));
                PrepareMob(mob);
                return mob;
            }
            near = Mob(new Vector2(1, 4));
            outside = Mob(new Vector2(-3, 4));
            entrant = Mob(new Vector2(0, 6));
            rift = Cast(blade);
            center = Transform.GetMapCoordinates(rift);
            Assert.That(SEntMan.GetComponent<NullstarRiftComponent>(rift).Active, Is.False);
            var melee = new AttemptMeleeEvent(SPlayer, blade);
            SEntMan.EventBus.RaiseLocalEvent(blade, ref melee);
            Assert.That(melee.Cancelled, Is.True, "No ordinary swings during windup.");
        });
        await RunSeconds(0.4f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<DamageableSystem>().GetTotalDamage(near).Float(), Is.Zero);
            Transform.SetWorldRotation(SPlayer, Angle.FromDegrees(90));
        });
        await RunSeconds(0.5f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<NullstarCleaveDashComponent>(SPlayer), Is.True);
            Assert.That(Transform.GetMapCoordinates(SPlayer).Position.Y, Is.GreaterThan(start.Position.Y));
            Assert.That(Transform.GetMapCoordinates(SPlayer).Position.Y, Is.LessThan(start.Position.Y + 3.1f));
            Assert.That(SEntMan.GetComponent<NullstarRiftComponent>(rift).Active, Is.False, "No strike until arrival.");
            Assert.That(Server.System<DamageableSystem>().GetTotalDamage(near).Float(), Is.Zero);
            Assert.That(Server.System<ActionBlockerSystem>().CanMove(SPlayer), Is.False, "Cannot steer the committed rush.");
        });
        await RunSeconds(0.3f);
        await Server.WaitAssertion(() =>
        {
            var damage = Server.System<DamageableSystem>();
            Assert.That(SEntMan.GetComponent<NullstarRiftComponent>(rift).Active, Is.True);
            Assert.That(Vector2.Distance(Transform.GetMapCoordinates(rift).Position, center.Position), Is.LessThan(.08f), "Turning must not change aim.");
            Assert.That(Transform.GetMapCoordinates(SPlayer).Position.Y - start.Position.Y, Is.EqualTo(3.1f).Within(.08f));
            Assert.That(SEntMan.HasComponent<NullstarCleaveDashComponent>(SPlayer), Is.False);
            Assert.That(Server.System<ActionBlockerSystem>().CanMove(SPlayer), Is.True, "Movement restored after arrival.");
            Assert.That(damage.GetTotalDamage(near).Float(), Is.EqualTo(60));
            Assert.That(damage.GetTotalDamage(outside).Float(), Is.Zero);
            Assert.That(damage.GetTotalDamage(SPlayer).Float(), Is.Zero);
            // Enter just in front of the tear, after its direct strike.
            Transform.SetCoordinates(entrant, Transform.ToCoordinates(Transform.GetParentUid(SPlayer),
                new MapCoordinates(center.Position + Vector2.UnitY * .1f, center.MapId)));
        });
        await RunSeconds(0.15f);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<NullstarRiftComponent>(rift);
            Assert.That(Server.System<DamageableSystem>().GetTotalDamage(entrant).Float(), Is.EqualTo(5));
            Assert.That(comp.Pushed.Contains(entrant), Is.True);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(entrant).LinearVelocity.Y, Is.GreaterThan(0));
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(SPlayer).LinearVelocity.Length(), Is.EqualTo(0).Within(.001));
            var actions = Server.System<SharedActionsSystem>();
            var bladeComp = SEntMan.GetComponent<NullstarRiftCleaveComponent>(blade);
            var cooldown = actions.GetAction(bladeComp.ActionEntity).Value.Comp.Cooldown;
            Assert.That(HandSys.TryDrop(SPlayer, blade), Is.True);
            Assert.That(HandSys.TryPickup(SPlayer, blade), Is.True);
            Assert.That(actions.GetAction(bladeComp.ActionEntity).Value.Comp.Cooldown, Is.EqualTo(cooldown));
        });
        await RunSeconds(3f);
        await Server.WaitAssertion(() => Assert.That(SEntMan.EntityExists(rift), Is.False, "Hazard must despawn after closing."));
    }

    [TestCase("WallSolid", false)]
    [TestCase("Airlock", false)]
    [TestCase("WallSolid", true)]
    public async Task SolidObstaclesShortenDashAndBlockDamage(string obstacle, bool spawnDuringDash)
    {
        EntityUid blade = default, rift = default, victim = default;
        MapCoordinates start = default;
        await Server.WaitAssertion(() =>
        {
            blade = EquipBlade();
            start = Transform.GetMapCoordinates(SPlayer);
            victim = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(start.Position + Vector2.UnitY * 4, start.MapId));
            PrepareMob(victim);
            if (!spawnDuringDash)
                SEntMan.SpawnEntity(obstacle, new MapCoordinates(start.Position + Vector2.UnitY * 2, start.MapId));
            rift = Cast(blade);
        });
        if (spawnDuringDash)
        {
            await RunSeconds(.86f);
            await Server.WaitAssertion(() =>
                SEntMan.SpawnEntity(obstacle, new MapCoordinates(start.Position + Vector2.UnitY * 2, start.MapId)));
        }
        await RunSeconds(1.4f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Transform.GetMapCoordinates(SPlayer).Position.Y - start.Position.Y, Is.LessThan(1.6f), "Cannot cross solid obstacles.");
            Assert.That(SEntMan.GetComponent<NullstarRiftComponent>(rift).Active, Is.True);
            Assert.That(Server.System<DamageableSystem>().GetTotalDamage(victim).Float(), Is.Zero, "Cannot cleave through an obstruction.");
            Assert.That(Server.System<ActionBlockerSystem>().CanMove(SPlayer), Is.True);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(SPlayer).LinearVelocity.Length(), Is.LessThan(.001f));
        });
    }

    [TestCase("unwield")]
    [TestCase("delete")]
    [TestCase("removeDash")]
    public async Task LosingBladeDuringDashRestoresMovement(string interruption)
    {
        EntityUid blade = default, rift = default;
        await Server.WaitAssertion(() => { blade = EquipBlade(); rift = Cast(blade); });
        await RunSeconds(.86f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<NullstarCleaveDashComponent>(SPlayer), Is.True);
            if (interruption == "delete")
                SEntMan.DeleteEntity(blade);
            else if (interruption == "removeDash")
                SEntMan.RemoveComponent<NullstarCleaveDashComponent>(SPlayer);
            else
                Server.System<SharedWieldableSystem>().TryUnwield(blade, SEntMan.GetComponent<WieldableComponent>(blade), SPlayer);
        });
        await RunSeconds(.6f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<NullstarCleaveDashComponent>(SPlayer), Is.False);
            Assert.That(SEntMan.EntityExists(rift), Is.False);
            Assert.That(Server.System<ActionBlockerSystem>().CanMove(SPlayer), Is.True);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(SPlayer).LinearVelocity.Length(), Is.LessThan(.001f));
        });
    }

    [TestCase("move")]
    [TestCase("unwield")]
    [TestCase("damage")]
    [TestCase("delete")]
    public async Task InterruptedWindupCannotStrike(string interruption)
    {
        EntityUid blade = default, rift = default, victim = default;
        await Server.WaitAssertion(() =>
        {
            blade = EquipBlade();
            var origin = Transform.GetMapCoordinates(SPlayer);
            victim = SEntMan.SpawnEntity("MobHuman", new MapCoordinates(origin.Position + Vector2.UnitY * 1.6f, origin.MapId));
            PrepareMob(victim);
            rift = Cast(blade);
        });
        await RunSeconds(.2f);
        await Server.WaitAssertion(() =>
        {
            switch (interruption)
            {
                case "move":
                    var origin = Transform.GetMapCoordinates(SPlayer);
                    Transform.SetCoordinates(SPlayer, Transform.ToCoordinates(Transform.GetParentUid(SPlayer),
                        new MapCoordinates(origin.Position + Vector2.UnitX, origin.MapId)));
                    break;
                case "unwield":
                    Assert.That(Server.System<SharedWieldableSystem>().TryUnwield(blade, SEntMan.GetComponent<WieldableComponent>(blade), SPlayer), Is.True);
                    break;
                case "damage":
                    Server.System<DamageableSystem>().TryChangeDamage(SPlayer, new DamageSpecifier { DamageDict = { ["Blunt"] = 5 } });
                    break;
                case "delete":
                    SEntMan.DeleteEntity(blade);
                    break;
            }
        });
        await RunSeconds(1f);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(rift), Is.False);
            Assert.That(Server.System<DamageableSystem>().GetTotalDamage(victim).Float(), Is.Zero);
            if (interruption != "delete")
                Assert.That(SEntMan.GetComponent<NullstarRiftCleaveComponent>(blade).PendingDoAfter, Is.Null);
        });
    }
}
