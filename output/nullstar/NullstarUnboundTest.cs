using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Starlight.Weapons.Melee;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Throwing;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._Starlight.Actions;

// Temporary stage verification, archived outside the test project after the run.
public sealed class NullstarUnboundTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task ReleaseRequiresWieldAndPushesVisibleMobsOnce()
    {
        await Server.WaitAssertion(() =>
        {
            var origin = Transform.GetMapCoordinates(SPlayer);
            var normal = SEntMan.SpawnEntity("ScarletNullstar", origin);
            Assert.That(SEntMan.GetComponent<NullstarUnboundComponent>(normal).ActionEntity, Is.Null);
            SEntMan.DeleteEntity(normal);

            var blade = SEntMan.SpawnEntity("ScarletNullstarUnboundPreview", origin);
            Assert.That(HandSys.TryPickup(SPlayer, blade), Is.True);
            var comp = SEntMan.GetComponent<NullstarUnboundComponent>(blade);
            var actions = Server.System<SharedActionsSystem>();
            var action = actions.GetAction(comp.ActionEntity).Value;
            var attempt = new ActionAttemptEvent(SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
            Assert.That(attempt.Cancelled, Is.True);

            var wieldable = SEntMan.GetComponent<WieldableComponent>(blade);
            Assert.That(Server.System<SharedWieldableSystem>().TryWield(blade, wieldable, SPlayer), Is.True);
            attempt = new ActionAttemptEvent(SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
            Assert.That(attempt.Cancelled, Is.False);

            EntityUid Mob(Vector2 offset) => SEntMan.SpawnEntity("MobHuman", new MapCoordinates(origin.Position + offset, origin.MapId));
            var near = Mob(new Vector2(2, 0));
            var blocked = Mob(new Vector2(0, 3));
            var far = Mob(new Vector2(8, 0));
            var wall = SEntMan.SpawnEntity("WallSolid", new MapCoordinates(origin.Position + new Vector2(0, 1.5f), origin.MapId));
            var damage = Server.System<DamageableSystem>().GetTotalDamage(near);
            var casterVelocity = SEntMan.GetComponent<PhysicsComponent>(SPlayer).LinearVelocity;

            var release = new NullstarUnboundEvent();
            actions.PerformAction(SPlayer, action, release);
            Assert.That(release.Handled, Is.True);
            Assert.That(comp.Released, Is.True);
            Assert.That(action.Comp.Enabled, Is.False);

            var waves = SEntMan.EntityQueryEnumerator<NullstarUnboundWaveComponent>();
            Assert.That(waves.MoveNext(out var waveUid, out var wave), Is.True);
            Assert.That(waves.MoveNext(out _, out _), Is.False);
            Assert.That(wave.Caster, Is.EqualTo(SPlayer));
            var system = Server.System<NullstarUnboundSystem>();
            wave.StartTime = STiming.CurTime - TimeSpan.FromSeconds(0.2);
            system.Update(0);
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(near), Is.True);
            var velocity = SEntMan.GetComponent<PhysicsComponent>(near).LinearVelocity;
            Assert.That(velocity.X, Is.GreaterThan(0));
            Assert.That(velocity.Y, Is.EqualTo(0).Within(0.001));

            wave.StartTime = STiming.CurTime - TimeSpan.FromSeconds(0.7);
            system.Update(0);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(near).LinearVelocity, Is.EqualTo(velocity), "A mob must not receive repeated impulses.");
            Assert.That(wave.HitEntities.Count, Is.EqualTo(1), "Walls, caster exclusion, and range must be respected.");
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(blocked), Is.False);
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(far), Is.False);
            Assert.That(Server.System<DamageableSystem>().GetTotalDamage(near), Is.EqualTo(damage));
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(SPlayer).LinearVelocity, Is.EqualTo(casterVelocity));
            Assert.That(system.TryRelease((blade, comp), SPlayer), Is.False);
            Assert.That(HandSys.TryDrop(SPlayer, blade), Is.True);
            Assert.That(HandSys.TryPickup(SPlayer, blade), Is.True);
            Assert.That(comp.Released, Is.True, "Passing or re-equipping must not refresh a one-time release.");

            foreach (var uid in new[] { near, blocked, far, wall, waveUid, blade })
                SEntMan.DeleteEntity(uid);
        });
    }
}


