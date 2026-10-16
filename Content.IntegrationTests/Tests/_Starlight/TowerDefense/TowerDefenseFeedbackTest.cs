#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Starlight.TowerDefense;
using Content.Shared._Starlight.TowerDefense;
using Content.Shared.Effects;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Starlight.TowerDefense;

public sealed class TowerDefenseFeedbackTest : InteractionTest
{
    [TestCase("TowerDefenseTider")]
    [TestCase("TowerDefenseSecurity")]
    public async Task EquippedTiersDeliverAttackAudioAndNativeEffectsOncePerCooldown(string id)
    {
        EntityUid map = default;
        EntityUid console = default;
        EntityUid enemy = default;
        EntityUid towerUid = default;
        TowerDefenseArenaComponent arena = default!;
        TowerDefenseTowerComponent tower = default!;
        var tile = new Vector2i(6, 5);
        var ranged = id == "TowerDefenseSecurity";
        var lunges = new List<MeleeLungeEvent>();
        var shots = new List<SharedGunSystem.HitscanEvent>();
        var muzzles = new List<MuzzleFlashEvent>();
        var flashes = new List<ColorFlashEffectEvent>();
        var nativeAnimationRan = false;
        var nativeArcSpawned = false;
        var nativeShotEffectsSpawned = false;
        var nativeMuzzleSpawned = false;
        var effectsBefore = new HashSet<EntityUid>();
        var client = (ClientEntityManager) CEntMan;

        IEnumerable<EntityUid> ClientEffects(string? prototype = null) => CEntMan.EntityQuery<MetaDataComponent>(true)
            .Where(meta => prototype == null || meta.EntityPrototype?.ID == prototype)
            .Select(meta => meta.Owner);

        // Observe the messages after normal native client handlers have processed them.
        // No test-only combat events or replacements for the native renderers are involved.
        void Observe(object? sender, object message)
        {
            switch (message)
            {
                case MeleeLungeEvent lunge:
                    lunges.Add(lunge);
                    nativeAnimationRan |= CEntMan.System<AnimationPlayerSystem>()
                        .HasRunningAnimation(CEntMan.GetEntity(lunge.Entity), "melee-lunge");
                    nativeArcSpawned |= CEntMan.EntityQuery<MetaDataComponent>(true)
                        .Any(meta => meta.EntityPrototype?.ID == lunge.Animation);
                    break;
                case SharedGunSystem.HitscanEvent shot:
                    shots.Add(shot);
                    nativeShotEffectsSpawned |= ClientEffects("HitscanEffect").Any(uid => !effectsBefore.Contains(uid));
                    break;
                case MuzzleFlashEvent muzzle:
                    muzzles.Add(muzzle);
                    nativeMuzzleSpawned |= ClientEffects(muzzle.Prototype).Any(uid => !effectsBefore.Contains(uid));
                    break;
                case ColorFlashEffectEvent flash:
                    flashes.Add(flash);
                    break;
            }
        }

        void Send(TowerDefenseAction action, int level = 0) =>
            SEntMan.EventBus.RaiseLocalEvent(console,
                new TowerDefenseMessage(action, tile, id, level) { Actor = SPlayer });

        Dictionary<EntityUid, string> Sounds() => SEntMan.EntityQuery<AudioComponent>(true)
            .ToDictionary(audio => audio.Owner, audio => audio.FileName);

        try
        {
            await Server.WaitPost(() =>
            {
                console = Server.System<TowerDefenseSystem>().CreateArena(SPlayer, out var mapId);
                map = MapSystem.GetMapOrInvalid(mapId);
                arena = SEntMan.GetComponent<TowerDefenseArenaComponent>(console);
                Transform.SetCoordinates(SPlayer, new EntityCoordinates(arena.Grid, new Vector2(6.5f, 3.5f)));
                Send(TowerDefenseAction.Place);
                towerUid = arena.Towers[tile];
                tower = SEntMan.GetComponent<TowerDefenseTowerComponent>(towerUid);
                var position = new Vector2(ranged ? 8.5f : 6.5f, 4.5f);
                enemy = SEntMan.SpawnEntity("TowerDefenseCarp", new EntityCoordinates(arena.Grid, position));
                var health = SEntMan.GetComponent<TowerDefenseEnemyComponent>(enemy);
                health.Health = health.MaxHealth = 1000;
                health.Speed = 0.001f;
                arena.Enemies.Add(enemy, 3);
            });
            await RunTicks(15); // The tower, held weapon, and target must be in the client's PVS first.
            await Client.WaitPost(() => client.ReceivedSystemMessage += Observe);

            var gunSounds = new List<string>();
            for (var level = 0; level < 3; level++)
            {
                var expectedLevel = level;
                if (level > 0)
                {
                    await Server.WaitAssertion(() =>
                    {
                        arena.Cash = 1000;
                        Send(TowerDefenseAction.Upgrade, expectedLevel);
                        Assert.That(tower.Level, Is.EqualTo(expectedLevel));
                    });
                    await RunTicks(10); // Upgrade replication includes a different held weapon entity.
                }

                await Client.WaitPost(() =>
                {
                    lunges.Clear();
                    shots.Clear();
                    muzzles.Clear();
                    flashes.Clear();
                    nativeAnimationRan = nativeArcSpawned = nativeShotEffectsSpawned = nativeMuzzleSpawned = false;
                    effectsBefore = ClientEffects().ToHashSet();
                });

                var attackSounds = new Dictionary<EntityUid, string>();
                var networkSounds = new Dictionary<NetEntity, string>();
                await Server.WaitAssertion(() =>
                {
                    var before = Sounds();
                    var health = SEntMan.GetComponent<TowerDefenseEnemyComponent>(enemy);
                    var beforeHealth = health.Health;
                    var definition = ProtoMan.Index(tower.Definition).Levels[expectedLevel];
                    tower.AttackTimer = 0;
                    arena.Phase = TowerDefensePhase.Running;
                    arena.Wave = 1;
                    var system = Server.System<TowerDefenseSystem>();
                    system.Update(0.01f);
                    Assert.That(health.Health, Is.EqualTo(beforeHealth - definition.Damage));
                    system.Update(0.01f);
                    Assert.That(health.Health, Is.EqualTo(beforeHealth - definition.Damage),
                        "A second update within the cooldown cannot deal damage or replay feedback.");
                    arena.Phase = TowerDefensePhase.Ready; // Hold the single attack while network delivery runs.
                    attackSounds = Sounds().Where(pair => !before.ContainsKey(pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value);
                    networkSounds = attackSounds.ToDictionary(pair => SEntMan.GetNetEntity(pair.Key), pair => pair.Value);
                    Assert.That(attackSounds, Has.Count.EqualTo(2),
                        "One successful attack must create exactly one weapon sound and one impact sound.");
                    Assert.That(attackSounds.Values, Has.All.EndsWith(".ogg"));
                    if (ranged)
                    {
                        var gun = SEntMan.GetComponent<GunComponent>(tower.Weapon!.Value);
                        var sound = gun.SoundGunshotModified ?? gun.SoundGunshot;
                        Assert.That(sound, Is.TypeOf<SoundPathSpecifier>());
                        var expectedSound = ((SoundPathSpecifier) sound!).Path.ToString();
                        Assert.That(attackSounds.Values.Count(path => path == expectedSound), Is.EqualTo(1),
                            "Exactly one gunshot must use the currently equipped gun's OGG.");
                        gunSounds.Add(expectedSound);
                    }
                });

                // Poll early enough to observe short OGGs before their audio entities expire.
                var heard = new HashSet<NetEntity>();
                var renderedContact = false;
                for (var tick = 0; tick < 12; tick++)
                {
                    await RunTicks(1);
                    await Client.WaitPost(() =>
                    {
                        foreach (var (netEntity, path) in networkSounds)
                        {
                            if (CEntMan.TryGetEntity(netEntity, out var audioUid) &&
                                CEntMan.TryGetComponent<AudioComponent>(audioUid, out var audio) && audio.FileName == path)
                                heard.Add(netEntity);
                        }
                        renderedContact |= ClientEffects(ranged ? "BulletImpactEffect" : "WeaponArcSmash")
                            .Any(uid => !effectsBefore.Contains(uid));
                    });
                }

                await Client.WaitAssertion(() =>
                {
                    Assert.That(heard, Is.SupersetOf(networkSounds.Keys),
                        "The connected viewer must receive the attack's actual audio players.");
                    Assert.That(flashes, Has.Count.EqualTo(1), "A surviving target receives one hit flash.");
                    Assert.That(flashes[0].Entities, Does.Contain(SEntMan.GetNetEntity(enemy)));
                    Assert.That(lunges.Count, Is.EqualTo(ranged ? 0 : 1));
                    Assert.That(shots.Count, Is.EqualTo(ranged ? 1 : 0));
                    Assert.That(muzzles.Count, Is.EqualTo(ranged ? 1 : 0));
                    Assert.That(renderedContact, Is.True, "The direct hit must create a visible native contact effect.");
                    if (ranged)
                    {
                        Assert.That(nativeShotEffectsSpawned, Is.True, "The native gun renderer must create a travel effect.");
                        Assert.That(nativeMuzzleSpawned, Is.True, "The native muzzle renderer must create a muzzle flash.");
                        Assert.That(shots[0].Traces, Has.Count.EqualTo(1));
                        Assert.That(shots[0].TravelFlash, Is.Not.Null);
                        Assert.That(shots[0].Gun, Is.EqualTo(SEntMan.GetNetEntity(tower.Weapon!.Value)));
                        Assert.That(shots[0].Traces[0].ImpactCoordinates.NetEntity,
                            Is.EqualTo(SEntMan.GetNetEntity(arena.Grid)));
                    }
                    else
                    {
                        Assert.That(nativeAnimationRan, Is.True, "The received lunge must start the native tower animation.");
                        Assert.That(nativeArcSpawned, Is.True, "The received lunge must spawn the equipped weapon's native arc.");
                        Assert.That(lunges[0].Weapon, Is.EqualTo(SEntMan.GetNetEntity(tower.Weapon!.Value)));
                    }
                });
            }

            if (ranged)
            {
                Assert.That(gunSounds[1], Is.EqualTo(gunSounds[0]), "The ammunition upgrade retains the Mk58.");
                Assert.That(gunSounds[2], Is.Not.EqualTo(gunSounds[0]), "The Lecter upgrade must sound different from the Mk58.");
                await VerifyLethalImpact();
            }
        }
        finally
        {
            await Client.WaitPost(() => client.ReceivedSystemMessage -= Observe);
            await Server.WaitPost(() =>
            {
                Transform.SetCoordinates(SPlayer, SEntMan.GetCoordinates(PlayerCoords));
                if (SEntMan.EntityExists(map))
                    SEntMan.DeleteEntity(map);
            });
            await RunTicks(5);
        }

        async Task VerifyLethalImpact()
        {
            await Client.WaitPost(() =>
            {
                shots.Clear();
                effectsBefore = ClientEffects().ToHashSet();
            });
            var impact = Vector2.Zero;
            await Server.WaitAssertion(() =>
            {
                impact = SEntMan.GetComponent<TransformComponent>(enemy).LocalPosition;
                SEntMan.GetComponent<TowerDefenseEnemyComponent>(enemy).Health = 1;
                tower.AttackTimer = 0;
                arena.Phase = TowerDefensePhase.Running;
                Server.System<TowerDefenseSystem>().Update(0.01f);
                arena.Phase = TowerDefensePhase.Ready;
                Assert.That(SEntMan.IsQueuedForDeletion(enemy), Is.True);
            });
            var renderedImpact = false;
            for (var i = 0; i < 15; i++)
            {
                await RunTicks(1);
                await Client.WaitPost(() =>
                {
                    renderedImpact |= ClientEffects("BulletImpactEffect").Where(uid => !effectsBefore.Contains(uid)).Any(uid =>
                    {
                        var transform = CEntMan.GetComponent<TransformComponent>(uid);
                        return CEntMan.GetNetEntity(transform.ParentUid) == SEntMan.GetNetEntity(arena.Grid)
                               && Vector2.Distance(transform.LocalPosition, impact) < 0.01f;
                    });
                });
            }
            await Client.WaitAssertion(() =>
            {
                Assert.That(shots, Has.Count.EqualTo(1));
                Assert.That(shots[0].Traces[0].ImpactCoordinates.NetEntity,
                    Is.EqualTo(SEntMan.GetNetEntity(arena.Grid)),
                    "Impact coordinates must survive removal of the killed target.");
                Assert.That(renderedImpact, Is.True, "The native impact flash must render even after its target is deleted.");
            });
        }
    }
}
