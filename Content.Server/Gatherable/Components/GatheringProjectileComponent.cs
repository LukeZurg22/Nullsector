namespace Content.Server.Gatherable.Components;

/// <summary>
/// Destroys a gatherable entity when colliding with it.
/// </summary>
[RegisterComponent]
public sealed partial class GatheringProjectileComponent : Component
{
    /// <summary>
    /// How many more times we can gather.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("amount")]
    public int Amount = 1;

    /// <summary>
    /// Whether we destroy ores in a gatherable when overpenning
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("nonDestructiveOverpen")]
    public bool NonDestructiveOverpen = false;
}
