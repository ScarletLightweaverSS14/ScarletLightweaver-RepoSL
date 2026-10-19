using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._Starlight.RTS;

[Serializable, NetSerializable]
public enum RtsOrderType : byte
{
    Move,
    Stop,
}

/// <summary>Unit requests contain no trusted ownership or map information.</summary>
[Serializable, NetSerializable]
public sealed class RtsOrderRequestEvent(RtsOrderType order, NetEntity[] units, Vector2 target) : EntityEventArgs
{
    public const int MaxUnits = 32;
    public readonly RtsOrderType Order = order;
    public readonly NetEntity[] Units = units;
    public readonly Vector2 Target = target;
}

[Serializable, NetSerializable]
public sealed class RtsOrderFeedbackEvent(int accepted, string message) : EntityEventArgs
{
    public readonly int Accepted = accepted;
    public readonly string Message = message;
}

[Serializable, NetSerializable]
public sealed class RtsLeaveRequestEvent : EntityEventArgs;
