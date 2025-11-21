using Content.Server._Null.Systems;
using Robust.Shared.Audio;

namespace Content.Server._Null.Components;

/// <summary>
/// Allows an interactable entity to be used as a "teleporter" to a different map.
/// </summary>
[RegisterComponent, AutoGenerateComponentState(fieldDeltas: false), Access(typeof(WarperSystem))]
public sealed partial class WarperComponent : Component
{
    /// Warp destination unique identifier.
    /// This is specifically set on a Ladder / Entrance with intent to
    /// -read- this ID to travel-to. It is NOT the ID of the -current- Ladder / Entrance.
    [ViewVariables(VVAccess.ReadWrite), DataField("id")]
    public string? CurrentId { get; set; } = string.Empty;

    /// Warp destination unique identifier.
    /// This is specifically set on a Ladder / Entrance with intent to
    /// -read- this ID to travel-to. It is NOT the ID of the -current- Ladder / Entrance.
    [ViewVariables(VVAccess.ReadWrite), DataField("destination")]
    public string? DestinationId { get; set; } = string.Empty;

    [ViewVariables(VVAccess.ReadWrite), DataField("hostileFactions")]
    public List<string> HostileFactions { get; set; } =
    [
        // SS14 - 20251120
        "Dragon", "Xeno", "Zombie", "SimpleHostile", "AllHostile",
    ];

    /// Does the level need to be completed before it can be used?
    [DataField("levelClearRequired"), AutoNetworkedField, ViewVariables(VVAccess.ReadWrite)]
    public bool LevelClearRequired { get; set; } = false;

    /// <summary>
    /// Assists with determining reverse-generation order for dungeons.
    /// IE: Spawning on a lower layer and working one's way up.
    /// </summary>
    [DataField("sealed"), ViewVariables(VVAccess.ReadWrite)]
    public bool Sealed { get; set; } = false;

    /// <summary>
    /// The sound played after players are shuffled/teleported around
    /// </summary>
    [DataField("teleportSound"), ViewVariables(VVAccess.ReadWrite)]
    public SoundSpecifier TeleportSound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");
}
