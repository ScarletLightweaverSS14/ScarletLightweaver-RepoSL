using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Starlight.AmmoAutoloader;

/// <summary>
/// Stationary machine that automatically loads ammo from boxes into magazines at 2x the manual speed.
/// Jams if incompatible ammo is run through it – requires a screwdriver (panel open) then a crowbar to clear.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class AmmoAutoloaderComponent : Component
{
    // ─── Slot IDs ───────────────────────────────────────────────────────────────
    public const string MagSlot1Id   = "mag_slot_1";
    public const string MagSlot2Id   = "mag_slot_2";
    public const string AmmoSlot1Id  = "ammo_slot_1";
    public const string AmmoSlot2Id  = "ammo_slot_2";

    // ─── Per-side auto-load toggle ───────────────────────────────────────────────
    [DataField, AutoNetworkedField]
    public bool Side1AutoLoad;

    [DataField, AutoNetworkedField]
    public bool Side2AutoLoad;

    // ─── Loading timers ──────────────────────────────────────────────────────────
    /// <summary>How long between loading a single round. Half the manual fill-delay (0.5 s → 0.25 s).</summary>
    [DataField]
    public TimeSpan LoadInterval = TimeSpan.FromSeconds(0.25);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextSide1Load;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextSide2Load;

    // ─── Jam state ───────────────────────────────────────────────────────────────
    [DataField, AutoNetworkedField]
    public bool IsJammed;

    /// <summary>1 = side 1 caused the jam, 2 = side 2, 0 = no jam.</summary>
    [DataField, AutoNetworkedField]
    public int JammedSide;

    // ─── UI display strings (kept networked so the BUI can be opened client-side) ─
    [DataField, AutoNetworkedField]
    public string? Side1MagName;

    [DataField, AutoNetworkedField]
    public string? Side2MagName;

    [DataField, AutoNetworkedField]
    public string? Side1AmmoName;

    [DataField, AutoNetworkedField]
    public string? Side2AmmoName;

    [DataField, AutoNetworkedField]
    public bool Side1Compatible;

    [DataField, AutoNetworkedField]
    public bool Side2Compatible;

    /// <summary>Net entity of the magazine in slot 1 – used by the client SpriteView.</summary>
    [DataField, AutoNetworkedField]
    public NetEntity? Side1MagEntity;

    [DataField, AutoNetworkedField]
    public NetEntity? Side2MagEntity;

    // ─── Sounds ──────────────────────────────────────────────────────────────────
    [DataField]
    public SoundSpecifier LoadSound = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/bullet_insert.ogg");

    [DataField]
    public SoundSpecifier JamSound = new SoundPathSpecifier("/Audio/Machines/airlock_deny.ogg");

    [DataField]
    public SoundSpecifier UnjamSound = new SoundPathSpecifier("/Audio/Items/crowbar.ogg");
}

// ─── UI Key ─────────────────────────────────────────────────────────────────────
[Serializable, NetSerializable]
public enum AmmoAutoloaderUiKey : byte
{
    Key
}

// ─── Appearance visuals ──────────────────────────────────────────────────────────
[Serializable, NetSerializable]
public enum AmmoAutoloaderVisuals : byte
{
    /// <summary>True when at least one side is actively loading.</summary>
    IsRunning,
    /// <summary>True when the machine is jammed.</summary>
    IsJammed,
}

// ─── BUI state ────────────────────────────────────────────────────────────────────
[Serializable, NetSerializable]
public sealed class AmmoAutoloaderBuiState : BoundUserInterfaceState
{
    public readonly bool IsJammed;
    public readonly int JammedSide;

    public readonly bool Side1AutoLoad;
    public readonly bool Side2AutoLoad;

    public readonly string? Side1MagName;
    public readonly string? Side2MagName;
    public readonly string? Side1AmmoName;
    public readonly string? Side2AmmoName;

    public readonly bool Side1Compatible;
    public readonly bool Side2Compatible;

    public readonly bool Side1HasMag;
    public readonly bool Side2HasMag;
    public readonly bool Side1HasAmmo;
    public readonly bool Side2HasAmmo;

    public readonly NetEntity? Side1MagEntity;
    public readonly NetEntity? Side2MagEntity;

    public AmmoAutoloaderBuiState(
        bool isJammed, int jammedSide,
        bool side1AutoLoad, bool side2AutoLoad,
        string? side1MagName, string? side2MagName,
        string? side1AmmoName, string? side2AmmoName,
        bool side1Compatible, bool side2Compatible,
        bool side1HasMag, bool side2HasMag,
        bool side1HasAmmo, bool side2HasAmmo,
        NetEntity? side1MagEntity, NetEntity? side2MagEntity)
    {
        IsJammed       = isJammed;
        JammedSide     = jammedSide;
        Side1AutoLoad  = side1AutoLoad;
        Side2AutoLoad  = side2AutoLoad;
        Side1MagName   = side1MagName;
        Side2MagName   = side2MagName;
        Side1AmmoName  = side1AmmoName;
        Side2AmmoName  = side2AmmoName;
        Side1Compatible = side1Compatible;
        Side2Compatible = side2Compatible;
        Side1HasMag    = side1HasMag;
        Side2HasMag    = side2HasMag;
        Side1HasAmmo   = side1HasAmmo;
        Side2HasAmmo   = side2HasAmmo;
        Side1MagEntity = side1MagEntity;
        Side2MagEntity = side2MagEntity;
    }
}

// ─── BUI messages ────────────────────────────────────────────────────────────────
[Serializable, NetSerializable]
public sealed class AmmoAutoloaderToggleSide1Message : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class AmmoAutoloaderToggleSide2Message : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class AmmoAutoloaderEjectMag1Message : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class AmmoAutoloaderEjectMag2Message : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class AmmoAutoloaderEjectAmmo1Message : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class AmmoAutoloaderEjectAmmo2Message : BoundUserInterfaceMessage;
