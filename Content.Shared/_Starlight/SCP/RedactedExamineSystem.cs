using Content.Shared.Examine;
using Robust.Shared.Random;

namespace Content.Shared._Starlight.SCP;

/// <summary>
/// Scrambles the examine text for any entity that has <see cref="RedactedComponent"/>.
/// </summary>
public sealed class RedactedExamineSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly char[] ScrambleChars =
        "=\"¤#%)?!@$&*+-~^|\\/><{}[]§°".ToCharArray();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RedactedComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<RedactedComponent> ent, ref ExaminedEvent args)
    {
        // Replace everything the examiner sees with redacted gibberish
        args.PushMarkup(GenerateScrambled(ent.Owner.Id));
    }

    private string GenerateScrambled(int seed)
    {
        var rng = new System.Random(seed ^ (int)(_random.NextDouble() * int.MaxValue));
        var len = 6 + rng.Next(6);
        var buf = new char[len];
        for (var i = 0; i < len; i++)
            buf[i] = ScrambleChars[rng.Next(ScrambleChars.Length)];
        return $"[color=red][bold]{new string(buf)}[/bold][/color]";
    }
}
