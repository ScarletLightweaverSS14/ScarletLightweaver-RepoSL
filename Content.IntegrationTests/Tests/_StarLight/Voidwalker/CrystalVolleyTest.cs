using System.Linq;
using System.Numerics;
using Content.Server._Starlight.Voidwalker;
using Content.Shared._Starlight.Voidwalker;
using Content.Shared.Actions;
using Content.Shared.Charges.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Projectiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._Starlight.Voidwalker;

[TestFixture]
public sealed class CrystalVolleyTest
{
    [Test]
    public async Task ShotsSnapshotTargetsAndConsumeOneCrystal()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var actions = server.System<SharedActionsSystem>();
        var transform = server.System<SharedTransformSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var map = await pair.CreateTestMap();
        EntityUid owner = default;
        EntityUid firstProjectile = default;
        EntityUid fireAction = default;
        CrystalVolleyComponent volley = null;
        Vector2 firstVelocity = default;

        await server.WaitAssertion(() =>
        {
            owner = entities.SpawnEntity("MobHuman", map.GridCoords);
            volley = entities.EnsureComponent<CrystalVolleyComponent>(owner);
            volley.CrystalCount = 2;
            actions.PerformAction(owner, actions.GetAction(volley.SummonActionEntity).Value);
            fireAction = volley.FireActionEntity.Value;
            Assert.That(volley.Crystals.Count, Is.EqualTo(2));
            Assert.That(actions.GetAction(volley.SummonActionEntity).Value.Comp.Enabled, Is.False);

            var selfClick = new FireCrystalVolleyEvent { Target = map.GridCoords };
            actions.PerformAction(owner, actions.GetAction(fireAction).Value, selfClick);
            Assert.That(selfClick.Handled, Is.False);
            Assert.That(server.System<SharedChargesSystem>().GetCurrentCharges(fireAction), Is.EqualTo(2));

            // Use coordinates relative to a movable entity: moving it after firing must not steer the shot.
            var cursorAnchor = entities.SpawnEntity(null, map.GridCoords.Offset(new Vector2(10, 0)));
            var click = new FireCrystalVolleyEvent { Target = new EntityCoordinates(cursorAnchor, Vector2.Zero) };
            actions.PerformAction(owner, actions.GetAction(fireAction).Value, click);
            Assert.That(click.Handled, Is.True);
            Assert.That(volley.Crystals.Count(uid => uid.IsValid()), Is.EqualTo(1));
            Assert.That(server.System<SharedChargesSystem>().GetCurrentCharges(fireAction), Is.EqualTo(1));
            Assert.That(actions.ValidAction(actions.GetAction(fireAction).Value), Is.False, "Shot delay must be enforced.");

            var projectiles = entities.EntityQueryEnumerator<ProjectileComponent>();
            Assert.That(projectiles.MoveNext(out firstProjectile, out var projectile), Is.True);
            Assert.That(projectile.Shooter, Is.EqualTo(owner));
            firstVelocity = physics.GetMapLinearVelocity(firstProjectile);
            Assert.That(firstVelocity.X, Is.EqualTo(volley.ProjectileSpeed).Within(0.001));
            Assert.That(firstVelocity.Y, Is.EqualTo(0).Within(0.001));
            transform.SetCoordinates(cursorAnchor, map.GridCoords.Offset(new Vector2(0, 10)));

            // Applying the command's component a second time must preserve both the magazine and cooldown.
            Assert.That(entities.EnsureComponent<CrystalVolleyComponent>(owner), Is.SameAs(volley));
            Assert.That(volley.FireActionEntity, Is.EqualTo(fireAction));
        });

        await pair.RunSeconds(0.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(physics.GetMapLinearVelocity(firstProjectile), Is.EqualTo(firstVelocity));
            actions.PerformAction(owner, actions.GetAction(fireAction).Value,
                new FireCrystalVolleyEvent { Target = map.GridCoords.Offset(new Vector2(0, 10)) });
            Assert.That(volley.FireActionEntity, Is.Null);
            Assert.That(volley.Crystals, Is.Empty);
            Assert.That(actions.GetAction(volley.SummonActionEntity).Value.Comp.Enabled, Is.True);
            Assert.That(actions.ValidAction(actions.GetAction(volley.SummonActionEntity).Value), Is.False,
                "Emptying the volley must not reset the summon cooldown.");
            Assert.That(physics.GetMapLinearVelocity(firstProjectile), Is.EqualTo(firstVelocity),
                "A later click must not redirect an earlier shot.");
        });

        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.EntityExists(firstProjectile), Is.False, "Missed shots must expire.");
            Assert.That(entities.EntityExists(fireAction), Is.False, "The empty launch action must be removed.");
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ProjectileDamagesTargetsAndStopsAtWalls(bool wall)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var actions = server.System<SharedActionsSystem>();
        var map = await pair.CreateTestMap();
        EntityUid victim = default;

        await server.WaitPost(() =>
        {
            var owner = entities.SpawnEntity("MobHuman", map.GridCoords);
            victim = entities.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(4, 0)));
            if (wall)
            {
                // The first hovering shard reaches inside this wall. The launch must fall back to the owner.
                entities.SpawnEntity("WallSolid", map.GridCoords.Offset(Vector2.UnitX));
            }
            var volley = entities.EnsureComponent<CrystalVolleyComponent>(owner);
            volley.CrystalCount = 1;
            actions.PerformAction(owner, actions.GetAction(volley.SummonActionEntity).Value);
            actions.PerformAction(owner, actions.GetAction(volley.FireActionEntity).Value,
                new FireCrystalVolleyEvent { Target = map.GridCoords.Offset(new Vector2(10, 0)) });
        });

        await pair.RunSeconds(0.4f);
        await server.WaitAssertion(() =>
        {
            var damage = entities.GetComponent<DamageableComponent>(victim);
            Assert.That(damage.Damage.DamageDict["Piercing"].Float(), Is.EqualTo(wall ? 0 : 25));
            var projectiles = entities.EntityQueryEnumerator<ProjectileComponent>();
            Assert.That(projectiles.MoveNext(out _, out _), Is.False, "A collision should destroy the crystal.");
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("expire")]
    [TestCase("remove")]
    [TestCase("death")]
    [TestCase("delete")]
    public async Task UnusedCrystalsAreCleanedUp(string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var actions = server.System<SharedActionsSystem>();
        var map = await pair.CreateTestMap();
        EntityUid[] crystals = [];
        EntityUid fireAction = default;

        await server.WaitPost(() =>
        {
            var owner = entities.SpawnEntity("MobHuman", map.GridCoords);
            var volley = entities.EnsureComponent<CrystalVolleyComponent>(owner);
            volley.VolleyLifetime = TimeSpan.FromSeconds(reason == "expire" ? 0.1 : 12);
            actions.PerformAction(owner, actions.GetAction(volley.SummonActionEntity).Value);
            crystals = volley.Crystals.ToArray();
            fireAction = volley.FireActionEntity.Value;
            switch (reason)
            {
                case "remove":
                    entities.RemoveComponent<CrystalVolleyComponent>(owner);
                    break;
                case "death":
                    server.System<MobStateSystem>().ChangeMobState(owner, MobState.Dead);
                    break;
                case "delete":
                    entities.DeleteEntity(owner);
                    break;
            }
        });

        await pair.RunSeconds(0.2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(crystals.All(uid => !entities.EntityExists(uid)), Is.True);
            Assert.That(entities.EntityExists(fireAction), Is.False);
            entities.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }
}
