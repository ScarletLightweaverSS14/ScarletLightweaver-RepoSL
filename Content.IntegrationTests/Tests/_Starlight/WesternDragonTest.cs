using System;
using System.Linq;
using Content.Server.NPC.HTN;
using Content.Shared.Actions.Components;
using Content.Shared.Gravity;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Gravity;
using Content.Server._Starlight.Dragon;
using Content.Shared._Starlight.Dragon;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Explosion;
using Content.Shared.Explosion.Components;
using Content.Shared.Trigger.Components.Triggers;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Prototypes;
using Content.Shared.Movement.Components;
using Content.Shared.StatusEffectNew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Spawners;
using Content.Client._Starfall.Particles;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Robust.Shared.Configuration;
using System.Numerics;

namespace Content.IntegrationTests.Tests._Starlight;

[TestFixture]
public sealed class WesternDragonTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task WingSurgeMovesTowardMouseAndRestoresControl(bool gravity)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var testMap = await pair.CreateTestMap();
        EntityUid dragon = default;
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var x = -2; x <= 2; x++)
            for (var y = -2; y <= 10; y++)
                map.SetTile(testMap.Grid, new Vector2i(x, y), testMap.Tile.Tile);
            if (gravity)
                server.System<GravitySystem>().EnableGravity(testMap.Grid,
                    em.EnsureComponent<GravityComponent>(testMap.Grid));

            dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));

            server.System<HTNSystem>().SetHTNEnabled((dragon, em.GetComponent<HTNComponent>(dragon)), false);
            var action = em.SpawnEntity("ActionWingDash", testMap.GridCoords);
            var dash = new WingDashEvent
            {
                Performer = dragon,
                Action = (action, em.GetComponent<ActionComponent>(action)),
                Target = new EntityCoordinates(testMap.Grid, 0.5f, 7.5f),
            };
            em.EventBus.RaiseLocalEvent(dragon, dash);
            Assert.That(dash.Handled, Is.True);
            var velocity = em.GetComponent<PhysicsComponent>(dragon).LinearVelocity;
            Assert.That(velocity.Y, Is.GreaterThan(0));
            Assert.That(Math.Abs(velocity.X), Is.LessThan(0.01f));
            Assert.That(velocity.Length(), Is.EqualTo(6).Within(0.01f), "The heavy dash should use half its former speed.");
            Assert.That(em.GetComponent<ThrownItemComponent>(dragon).Animate, Is.True);
            Assert.That(em.GetComponent<InputMoverComponent>(dragon).CanMove, Is.False);
        });

        await pair.RunSeconds(0.4f);
        await server.WaitAssertion(() =>
        {
            var position = em.GetComponent<TransformComponent>(dragon).Coordinates.Position;
            Assert.That(position.Y, Is.InRange(2.5f, 3.1f), "The slower dash must still move toward the cursor.");
            Assert.That(position.X, Is.EqualTo(0.5f).Within(0.1f));
        });
        await pair.RunSeconds(0.7f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WingDashComponent>(dragon).EndTime, Is.Null);
            Assert.That(em.GetComponent<InputMoverComponent>(dragon).CanMove, Is.True,
                "Movement must recover even in zero gravity, where a throw may never land.");
            var position = em.GetComponent<TransformComponent>(dragon).Coordinates.Position;
            // Throw landing and movement run on discrete ticks; allow the same half-tile
            // early-stop tolerance as the original dash test, while requiring the longer travel.
            Assert.That(position.Y, Is.InRange(5.5f, 6.15f), "The dash must stop after approximately 5.5 tiles.");
        });
        await pair.CleanReturnAsync();
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task TailSlamWavesAndExtinguishing(bool wall)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        var em = server.EntMan;
        var map = server.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            for (var x = 0; x <= 6; x++)
            for (var y = -2; y <= 2; y++)
                map.SetTile(testMap.Grid, new Vector2i(x, y), testMap.Tile.Tile);

            if (wall)
                em.SpawnEntity("WallSolid", new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));

            var dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(testMap.Grid, 2.25f, 0.25f));

            server.System<HTNSystem>().SetHTNEnabled((dragon, em.GetComponent<HTNComponent>(dragon)), false);
            var action = em.SpawnEntity("ActionWesternTailSlam", testMap.GridCoords);
            Assert.That(em.HasComponent<InstantActionComponent>(action), Is.True);
            Assert.That(em.HasComponent<WorldTargetActionComponent>(action), Is.False);
            var slam = new WesternDragonTailSlamEvent
            {
                Performer = dragon,
                Action = (action, em.GetComponent<ActionComponent>(action)),
            };
            em.EventBus.RaiseLocalEvent(dragon, slam);
            Assert.That(slam.Handled, Is.True);
            var position = em.GetComponent<TransformComponent>(dragon).Coordinates.Position;
            Assert.That(position.X, Is.EqualTo(2.25f), "Tail Slam must not teleport or snap the dragon to tile center.");
            Assert.That(position.Y, Is.EqualTo(0.25f));
            Assert.That(em.EntityQuery<WesternDragonFirePatchComponent>().Any(), Is.False);

            // Overlapping waves must not double the amount of fire.
            slam.Handled = false;
            em.EventBus.RaiseLocalEvent(dragon, slam);
            Assert.That(slam.Handled, Is.True);
        });

        await pair.RunSeconds(0.1f);
        await server.WaitAssertion(() =>
            Assert.That(em.EntityQuery<WesternDragonFirePatchComponent>().Count(), Is.EqualTo(1),
                "The slam should begin at its center before expanding."));
        await pair.RunSeconds(1.3f);
        await server.WaitAssertion(() =>
        {
            var patches = em.EntityQuery<WesternDragonFirePatchComponent>().ToArray();
            if (wall)
                Assert.That(patches.Length, Is.InRange(1, 24));
            else
                Assert.That(patches, Has.Length.EqualTo(25));

            foreach (var patch in patches)
            {
                var position = em.GetComponent<TransformComponent>(patch.Owner).Coordinates.Position;
                Assert.That(position.X, Is.InRange(0.5f, 4.5f));
                Assert.That(position.Y, Is.InRange(-1.5f, 2.5f));
                if (wall && position.Y == 0.5f)
                    Assert.That(position.X, Is.LessThan(3.5f), "Fire must not pass through the wall.");
                Assert.That(em.GetComponent<FlammableComponent>(patch.Owner).OnFire, Is.True);
                server.System<FlammableSystem>().Extinguish(patch.Owner);
            }
        });

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
            Assert.That(em.EntityQuery<WesternDragonFirePatchComponent>().Any(), Is.False));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FireBreathOutlastsTailSlam()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var testMap = await pair.CreateTestMap();
        EntityUid victim = default;
        await server.WaitAssertion(() =>
        {
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            moles[(int) Gas.Oxygen] = 21.824779f;
            moles[(int) Gas.Nitrogen] = 82.10312f;
            server.System<AtmosphereSystem>().SetMapAtmosphere(testMap.MapUid, false,
                new GasMixture(moles, Atmospherics.T20C));
            var map = server.System<SharedMapSystem>();
            for (var x = 0; x <= 5; x++)
            for (var y = -1; y <= 1; y++)
                map.SetTile(testMap.Grid, new Vector2i(x, y), testMap.Tile.Tile);

            var dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));

            server.System<HTNSystem>().SetHTNEnabled((dragon, em.GetComponent<HTNComponent>(dragon)), false);
            victim = em.SpawnEntity("MobHuman", new EntityCoordinates(testMap.Grid, 3.5f, 0.5f));
            em.SpawnEntity("WesternDragonTailSlamWarning", new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            var breath = new WesternDragonFireBreathEvent
            {
                Performer = dragon,
                Target = new EntityCoordinates(testMap.Grid, 5.5f, 0.5f),
            };
            em.EventBus.RaiseLocalEvent(dragon, breath);
            Assert.That(breath.Handled, Is.True);
            // Casting on an existing lane must not stack more lingering fires.
            breath.Handled = false;
            em.EventBus.RaiseLocalEvent(dragon, breath);
            Assert.That(em.EntityQuery<WesternDragonFirePatchComponent>().Count(), Is.EqualTo(1),
                "Only the preexisting Tail Slam warning should exist before breath propagation starts.");
        });
        await pair.RunSeconds(0.8f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityQuery<WesternDragonFirePatchComponent>().Count(), Is.EqualTo(12),
                "The brief Tail Slam fire and longer breath must coexist on the same tile.");
            Assert.That(em.GetComponent<FlammableComponent>(victim).OnFire, Is.True,
                "The chemical-fire visual must actually ignite humanoids standing in it.");
        });
        await pair.RunSeconds(1.7f);
        await server.WaitAssertion(() =>
        {
            var patches = em.EntityQuery<WesternDragonFirePatchComponent>().ToArray();
            Assert.That(patches, Has.Length.EqualTo(11), "Only the longer Fire Breath flames should remain.");
            Assert.That(patches.All(patch => em.GetComponent<MetaDataComponent>(patch.Owner).EntityPrototype?.ID ==
                "WesternDragonFirePatch"), Is.True);
            foreach (var patch in patches)
                server.System<FlammableSystem>().Extinguish(patch.Owner);
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
            Assert.That(em.EntityQuery<WesternDragonFirePatchComponent>().Any(), Is.False));
        await pair.CleanReturnAsync();
    }

    [TestCase(0, 1, false)]
    [TestCase(1, 1, false)]
    [TestCase(1, 0, false)]
    [TestCase(1, -1, false)]
    [TestCase(0, -1, false)]
    [TestCase(-1, -1, false)]
    [TestCase(-1, 0, false)]
    [TestCase(-1, 1, false)]
    [TestCase(1, 0, true)]
    public async Task FireBreathAdvancesInEightDirections(int x, int y, bool wall)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var testMap = await pair.CreateTestMap();
        var direction = Vector2.Normalize(new Vector2(x, y));
        var firstCount = 0;
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var tx = -5; tx <= 5; tx++)
            for (var ty = -5; ty <= 5; ty++)
                map.SetTile(testMap.Grid, new Vector2i(tx, ty), testMap.Tile.Tile);
            if (wall)
            {
                for (var ty = -2; ty <= 2; ty++)
                    em.SpawnEntity("WallSolid", new EntityCoordinates(testMap.Grid, 3.5f, ty + 0.5f));
            }
            var dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            server.System<HTNSystem>().SetHTNEnabled((dragon, em.GetComponent<HTNComponent>(dragon)), false);
            var breath = new WesternDragonFireBreathEvent
            {
                Performer = dragon,
                Target = new EntityCoordinates(testMap.Grid, 0.5f + x * 5, 0.5f + y * 5),
            };
            em.EventBus.RaiseLocalEvent(dragon, breath);
            Assert.That(breath.Handled, Is.True);
            Assert.That(em.EntityQuery<WesternDragonFirePatchComponent>().Any(), Is.False);
        });
        await pair.RunSeconds(0.05f);
        await server.WaitAssertion(() =>
        {
            var patches = em.EntityQuery<WesternDragonFirePatchComponent>().ToArray();
            firstCount = patches.Length;
            Assert.That(firstCount, Is.InRange(1, 3), "Only the mouth of the plume should ignite first.");
            foreach (var patch in patches)
            {
                var offset = em.GetComponent<TransformComponent>(patch.Owner).Coordinates.Position - new Vector2(0.5f);
                Assert.That(Vector2.Dot(offset, direction), Is.LessThan(1.5f));
            }
        });
        await pair.RunSeconds(0.55f);
        await server.WaitAssertion(() =>
        {
            var offsets = em.EntityQuery<WesternDragonFirePatchComponent>().Select(patch =>
                em.GetComponent<TransformComponent>(patch.Owner).Coordinates.Position - new Vector2(0.5f)).ToArray();
            Assert.That(offsets.Length, Is.GreaterThan(firstCount));
            Assert.That(offsets.Length, Is.LessThanOrEqualTo(16), "Keep the footprint and spawn cost small.");
            Assert.That(offsets.All(offset => Vector2.Dot(offset, direction) > 0), Is.True);
            Assert.That(offsets.All(offset => offset.LengthSquared() <= 25.25f), Is.True);
            if (wall)
                Assert.That(offsets.All(offset => offset.X < 3), Is.True, "The advancing front must stop at walls.");
            else
                Assert.That(offsets.Max(offset => Vector2.Dot(offset, direction)), Is.GreaterThan(4),
                    "Diagonal casts must advance toward the corner, not along a cardinal axis.");
            Assert.That(em.EntityQuery<WesternDragonFireBreathWaveComponent>().Any(), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AncientFlameLeavesFloorFireAsItMoves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var testMap = await pair.CreateTestMap();
        EntityUid projectile = default;
        await server.WaitAssertion(() =>
        {
            var map = server.System<SharedMapSystem>();
            for (var x = 0; x <= 8; x++)
                map.SetTile(testMap.Grid, new Vector2i(x, 0), testMap.Tile.Tile);
            var dragon = em.SpawnEntity("DragonWesternDefault", new EntityCoordinates(testMap.Grid, 0.5f, 0.5f));
            server.System<HTNSystem>().SetHTNEnabled((dragon, em.GetComponent<HTNComponent>(dragon)), false);
            var gun = em.GetComponent<ActionGunComponent>(dragon);
            var shoot = new ActionGunShootEvent
            {
                Performer = dragon,
                Action = (gun.ActionEntity.Value, em.GetComponent<ActionComponent>(gun.ActionEntity.Value)),
                Target = new EntityCoordinates(testMap.Grid, 8.5f, 0.5f),
            };
            em.EventBus.RaiseLocalEvent(dragon, shoot);
            projectile = em.AllEntities<WesternDragonFireTrailComponent>().Single().Owner;
        });
        await pair.RunSeconds(0.3f);
        await server.WaitAssertion(() =>
        {
            var patches = em.EntityQuery<WesternDragonFirePatchComponent>().ToArray();
            Assert.That(patches.Length, Is.GreaterThanOrEqualTo(2), "A flying fireball must leave a visible trail.");
            server.System<SharedTransformSystem>().SetCoordinates(projectile,
                new EntityCoordinates(testMap.Grid, 20.5f, 0.5f));
        });
        await pair.RunSeconds(0.1f);
        await server.WaitAssertion(() =>
        {
            var positions = em.EntityQuery<WesternDragonFirePatchComponent>().Select(patch =>
                em.GetComponent<TransformComponent>(patch.Owner).Coordinates.Position).ToArray();
            Assert.That(positions.Distinct().Count(), Is.EqualTo(positions.Length));
            Assert.That(positions.All(position => position.Y == 0.5f && position.X <= 8.5f), Is.True,
                "The fireball must not spawn floor fires in space.");
            em.DeleteEntity(projectile);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AncientFlameUsesStrongerDragonProjectile()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var dragon = em.SpawnEntity("DragonWesternDefault", testMap.GridCoords);
            server.System<HTNSystem>().SetHTNEnabled((dragon, em.GetComponent<HTNComponent>(dragon)), false);
            var gun = em.GetComponent<ActionGunComponent>(dragon);
            var gunAction = gun.Action;
            Assert.That(gunAction.ToString(), Is.EqualTo("ActionWesternDragonsBreath"));
            Assert.That(em.GetComponent<BasicEntityAmmoProviderComponent>(gun.Gun.Value).Proto,
                Is.EqualTo("ProjectileWesternDragonsBreath"));
            Assert.That(em.HasComponent<WesternDragonTailSlamComponent>(dragon), Is.True);

            // Exercise the actual ability, including the spawned lung and its ammunition.
            var shoot = new ActionGunShootEvent
            {
                Performer = dragon,
                Action = (gun.ActionEntity.Value, em.GetComponent<ActionComponent>(gun.ActionEntity.Value)),
                Target = new EntityCoordinates(testMap.Grid, 5.5f, 0.5f),
            };
            em.EventBus.RaiseLocalEvent(dragon, shoot);
            var ancient = em.AllEntities<ProjectileComponent>().Single(entity =>
                em.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == "ProjectileWesternDragonsBreath").Owner;
            Assert.That(em.GetComponent<BasicEntityAmmoProviderComponent>(gun.Gun.Value).Count, Is.EqualTo(0));
            var ordinary = em.SpawnEntity("ProjectileDragonsBreath", testMap.GridCoords);
            Assert.That(em.HasComponent<RepeatingTriggerComponent>(ancient), Is.True);
            var stronger = em.GetComponent<ExplosiveComponent>(ancient);
            var weaker = em.GetComponent<ExplosiveComponent>(ordinary);
            Assert.That(stronger.TotalIntensity, Is.GreaterThan(weaker.TotalIntensity));
            var explosions = server.System<Content.Server.Explosion.EntitySystems.ExplosionSystem>();
            var normalRadius = explosions.IntensityToRadius(weaker.TotalIntensity, weaker.IntensitySlope, weaker.MaxIntensity);
            var ancientRadius = explosions.IntensityToRadius(stronger.TotalIntensity, stronger.IntensitySlope, stronger.MaxIntensity);
            Assert.That(ancientRadius, Is.EqualTo(normalRadius * 1.5f).Within(0.001f));
            Assert.That(em.HasComponent<TriggerOnCollideComponent>(ancient), Is.True,
                "Impact and repeating travel triggers must use the same larger explosion.");
            Assert.That(stronger.Repeatable, Is.True);
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            Assert.That(prototypes.Index<ExplosionPrototype>(stronger.ExplosionType).FireStacks,
                Is.GreaterThan(prototypes.Index<ExplosionPrototype>(weaker.ExplosionType).FireStacks));
            em.DeleteEntity(ancient);
            em.DeleteEntity(ordinary);
        });
        await pair.CleanReturnAsync();
    }
    [Test]
    public async Task DragonIsImmuneToIgnitionAndHeat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var em = server.EntMan;
            var dragon = em.SpawnEntity("DragonWesternDefault", testMap.GridCoords);
            server.System<HTNSystem>().SetHTNEnabled((dragon, em.GetComponent<HTNComponent>(dragon)), false);
            var flammable = server.System<FlammableSystem>();
            flammable.AdjustFireStacks(dragon, 6, ignite: true);
            Assert.That(em.GetComponent<FlammableComponent>(dragon).OnFire, Is.False);
            Assert.That(em.GetComponent<FlammableComponent>(dragon).FireStacks, Is.Zero);
            // Collision ignition adds stacks directly before calling Ignite rather than SetFireStacks.
            var fire = em.GetComponent<FlammableComponent>(dragon);
            fire.FireStacks = 6;
            flammable.Ignite(dragon, dragon, fire);
            Assert.That(fire.OnFire, Is.False);
            Assert.That(fire.FireStacks, Is.Zero);
            var damage = server.System<DamageableSystem>();
            var before = em.GetComponent<DamageableComponent>(dragon).TotalDamage;
            damage.TryChangeDamage(dragon, new DamageSpecifier { DamageDict = { ["Heat"] = 100 } });
            Assert.That(em.GetComponent<DamageableComponent>(dragon).TotalDamage, Is.EqualTo(before));
            damage.TryChangeDamage(dragon, new DamageSpecifier { DamageDict = { ["Slash"] = 10 } });
            Assert.That(em.GetComponent<DamageableComponent>(dragon).TotalDamage, Is.GreaterThan(before),
                "Fire immunity must not make the dragon immune to physical damage.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DragonFireKeepsFlamesWithoutSmoke()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var em = client.EntMan;
            var map = client.System<SharedMapSystem>().CreateMap();
            var cfg = client.ResolveDependency<IConfigurationManager>();
            var previousQuality = cfg.GetCVar(CCVars.ParticleQuality);
            cfg.SetCVar(CCVars.ParticleQuality, 3);
            var appearance = client.System<Robust.Client.GameObjects.AppearanceSystem>();
            var particles = client.System<ParticleSystem>();
            var patch = em.SpawnEntity("WesternDragonFirePatch", new EntityCoordinates(map, 0, 0));
            var ordinary = em.SpawnEntity(null, new EntityCoordinates(map, 2, 0));
            em.EnsureComponent<FlammableComponent>(ordinary);
            em.EnsureComponent<AppearanceComponent>(ordinary);
            foreach (var uid in new[] { patch, ordinary })
            {
                appearance.SetData(uid, FireVisuals.OnFire, true);
                appearance.SetData(uid, FireVisuals.FireStacks, 8f);
                var change = new Robust.Client.GameObjects.AppearanceChangeEvent
                {
                    Component = em.GetComponent<AppearanceComponent>(uid),
                };
                em.EventBus.RaiseLocalEvent(uid, ref change);
            }
            var dragonEmitters = particles.GetEmitters().Where(e => e.AttachedEntity == patch).ToArray();
            Assert.That(dragonEmitters.Any(e => e.Proto.ID == "SfFireContinuous"), Is.True);
            Assert.That(dragonEmitters.Any(e => e.Proto.ID == "SfFireSmoke"), Is.False);
            Assert.That(particles.GetEmitters().Any(e => e.AttachedEntity == ordinary && e.Proto.ID == "SfFireSmoke"), Is.True,
                "Ordinary burning entities must retain their smoke.");
            em.DeleteEntity(map);
            cfg.SetCVar(CCVars.ParticleQuality, previousQuality);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PredatorHuntRestoresSpeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var testMap = await pair.CreateTestMap();
        EntityUid mob = default;
        EntityUid mark = default;
        var originalSpeed = 0f;

        await server.WaitAssertion(() =>
        {
            mob = server.EntMan.SpawnEntity("MobHuman", testMap.GridCoords);
            var movement = server.EntMan.GetComponent<MovementSpeedModifierComponent>(mob);
            originalSpeed = movement.CurrentSprintSpeed;
            var status = server.System<StatusEffectsSystem>();
            var dragon = server.EntMan.SpawnEntity("DragonWesternDefault",
                new EntityCoordinates(testMap.Grid, 2.5f, 0.5f));
            server.System<HTNSystem>().SetHTNEnabled((dragon, server.EntMan.GetComponent<HTNComponent>(dragon)), false);
            var roar = new DragonRoarEvent { Performer = dragon };
            server.EntMan.EventBus.RaiseLocalEvent(dragon, roar);
            Assert.That(roar.Handled, Is.True);
            Assert.That(status.TryGetStatusEffect(mob, "StatusEffectPredatorHunt", out var effect), Is.True);
            mark = server.EntMan.GetComponent<PredatorHuntVisualComponent>(effect.Value).Mark.Value;
            Assert.That(server.EntMan.GetComponent<TransformComponent>(mark).ParentUid, Is.EqualTo(mob));
            var transform = server.System<SharedTransformSystem>();
            transform.SetCoordinates(mob, new EntityCoordinates(testMap.Grid, 1.5f, 1.5f));
            Assert.That(transform.GetWorldPosition(mark), Is.EqualTo(transform.GetWorldPosition(mob)),
                "The visible hunt mark must follow the affected humanoid.");
            Assert.That(movement.CurrentSprintSpeed, Is.EqualTo(originalSpeed * 0.65f).Within(0.001f));
            status.TryRemoveStatusEffect(mob, "StatusEffectPredatorHunt");
        });

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.Deleted(mark), Is.True, "Removing the debuff must remove its visible mark.");
            Assert.That(server.EntMan.GetComponent<MovementSpeedModifierComponent>(mob).CurrentSprintSpeed,
                Is.EqualTo(originalSpeed).Within(0.001f));
        });

        await pair.CleanReturnAsync();
    }
}
