using Content.Server._Null.Systems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;

namespace Content.Server._Null.Components;

/// <summary>
/// Designates this entity as a device that acts upon a grid for Emancipation.
/// </summary>
[RegisterComponent, Access(typeof(EmancipationGridSystem))/*, NetworkedComponent*/]
public sealed partial class EmancipationGridComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)] public long ItemsCleaned { get; set; }

    [ViewVariables(VVAccess.ReadOnly), DataField]
    public EntityWhitelist? Whitelist { get; set; } = new() { Components = ["SpaceGarbage"], Tags = ["Trash"], };
    [ViewVariables(VVAccess.ReadOnly)] public EntityUid? EmancipatedGrid { get; set; }
    public bool IsPowered = false;

    /// <summary>
    ///  If not null, this sound will be played when an item is deleted both on the item and the machine.
    /// </summary>
    [DataField("sound")] public SoundSpecifier? EmancipateSound;
}
