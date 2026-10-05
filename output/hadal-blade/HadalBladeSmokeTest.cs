using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Starlight.Weapons.Melee;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Projectiles;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._Starlight.Actions;

// Temporary verification for the admeme weapon; no runtime test systems are registered.
public sealed class HadalBladeSmokeTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task WieldedBladeReleasesPressureAndRetainsCooldown()
    {
        await Server.WaitAssertion(() =>
        {
            var blade = SEntMan.SpawnEntity("ScarletHadalBlade", Transform.GetMapCoordinates(SPlayer));
            Assert.That(SEntMan.HasComponent<ItemToggleComponent>(blade), Is.False);
            Assert.That(HandSys.TryPickup(SPlayer, blade), Is.True);

            var ability = SEntMan.GetComponent<HadalBladeComponent>(blade);
            var actions = Server.System<SharedActionsSystem>();
            var action = actions.GetAction(ability.ActionEntity).Value;
            Assert.That(actions.GetActions(SPlayer).Any(a => a.Owner == action.Owner), Is.True);

            var attempt = new ActionAttemptEvent(SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
            Assert.That(attempt.Cancelled, Is.True, "One hand must not release the pressure wave.");

            var wieldable = SEntMan.GetComponent<WieldableComponent>(blade);
            Assert.That(Server.System<SharedWieldableSystem>().TryWield(blade, wieldable, SPlayer), Is.True);
            attempt = new ActionAttemptEvent(SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
            Assert.That(attempt.Cancelled, Is.False);

            var release = new HadalCrushEvent { Target = new EntityCoordinates(SPlayer, new Vector2(4, 0)) };
            actions.PerformAction(SPlayer, action, release);
            Assert.That(release.Handled, Is.True);
            Assert.That(action.Comp.Cooldown, Is.Not.Null);
            var cooldown = action.Comp.Cooldown;

            var shots = SEntMan.EntityQuery<ProjectileComponent>().Where(p => p.Weapon == blade).ToArray();
            Assert.That(shots, Has.Length.EqualTo(3));
            foreach (var shot in shots)
            {
                Assert.That(shot.Shooter, Is.EqualTo(SPlayer));
                Assert.That(shot.Damage.GetTotal().Float(), Is.EqualTo(11), "BaseBullet damage must not leak into the ability.");
                var velocity = SEntMan.GetComponent<PhysicsComponent>(shot.Owner).LinearVelocity;
                Assert.That(velocity.X, Is.GreaterThan(0), "Every crescent must travel toward the target.");
            }

            Assert.That(HandSys.TryDrop(SPlayer, blade), Is.True);
            Assert.That(actions.GetActions(SPlayer).Any(a => a.Owner == action.Owner), Is.False);
            Assert.That(HandSys.TryPickup(SPlayer, blade), Is.True);
            Assert.That(ability.ActionEntity, Is.EqualTo(action.Owner));
            Assert.That(action.Comp.Cooldown, Is.EqualTo(cooldown), "Re-equipping must not reset the cooldown.");

            attempt = new ActionAttemptEvent(SPlayer);
            SEntMan.EventBus.RaiseLocalEvent(action.Owner, ref attempt);
            Assert.That(attempt.Cancelled, Is.True, "Re-equipping does not restore the two-handed grip.");
            foreach (var shot in shots)
                SEntMan.DeleteEntity(shot.Owner);
            SEntMan.DeleteEntity(blade);
        });
    }
}
