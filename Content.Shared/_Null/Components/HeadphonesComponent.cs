using Robust.Shared.GameStates;

namespace Content.Shared._Null.Components;

/// <summary>
/// Designates this entity as capable headphones.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class HeadphonesComponent : Component
{
    // This, for now, only exists for data work.
    // I do not know how to compare prototypes, so this is the next best thing.
}
