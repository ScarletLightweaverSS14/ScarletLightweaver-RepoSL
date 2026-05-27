using Content.Shared._Starlight.SCP;
using Robust.Server.GameObjects;
using Robust.Shared.Random;

namespace Content.Server._Starlight.SCP;

/// <summary>
/// Server-side system that scrambles the entity name of any entity with
/// <see cref="RedactedComponent"/> and refreshes the scramble every 30 seconds.
/// The original name is restored when the component is removed.
/// </summary>
public sealed class RedactedNameSystem : EntitySystem
{
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly char[] _scrambleChars =
        "=\"¤#%)?!@$&*+-~^|\\/><{}[]§°▓█▒░".ToCharArray();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RedactedComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<RedactedComponent, ComponentRemove>(OnRemove);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<RedactedComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.NameTimer -= frameTime;
            if (comp.NameTimer > 0f) continue;
            comp.NameTimer = 30f;
            ApplyScrambledName(uid);
        }
    }

    private void OnInit(Entity<RedactedComponent> ent, ref ComponentInit args)
    {
        // Store the real name before we overwrite it.
        ent.Comp.OriginalName = MetaData(ent.Owner).EntityName;
        ent.Comp.NameTimer = 0f; // scramble on the very next Update tick
    }

    private void OnRemove(Entity<RedactedComponent> ent, ref ComponentRemove args)
    {
        if (!string.IsNullOrEmpty(ent.Comp.OriginalName))
            _meta.SetEntityName(ent.Owner, ent.Comp.OriginalName);
    }

    private void ApplyScrambledName(EntityUid uid)
    {
        var rng = new System.Random(uid.Id ^ (int)(_random.NextDouble() * int.MaxValue));
        var len = 6 + rng.Next(6);
        var buf = new char[len];
        for (var i = 0; i < len; i++)
            buf[i] = _scrambleChars[rng.Next(_scrambleChars.Length)];
        _meta.SetEntityName(uid, new string(buf));
    }
}
