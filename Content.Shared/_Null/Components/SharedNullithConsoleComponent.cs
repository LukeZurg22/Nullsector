using Content.Shared._NF.Shipyard;
using Content.Shared.Access;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Null.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedShipyardSystem))]
public sealed partial class NullithConsoleComponent : Component
{
    /// <summary>
    /// The ID of the Card Slot the user inserts their ID card into.
    /// </summary>
    public const string TargetIdCardSlotId = "ShipyardConsole-targetId";

    [DataField("targetIdSlot")]
    public ItemSlot TargetIdSlot = new();

    [DataField("soundError")]
    public SoundSpecifier ErrorSound =
        new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField("soundConfirm")]
    public SoundSpecifier ConfirmSound =
        new SoundPathSpecifier("/Audio/Effects/Shuttle/hyperspace_end.ogg");

    /// <summary>
    /// The comms channel that announces the PoI purchase. The purchase is *always* announced
    /// on this channel.
    /// </summary>
    [DataField("shipyardChannel")]
    public ProtoId<RadioChannelPrototype> ShipyardChannel = "Traffic";

    /// <summary>
    /// A second comms channel that announces the ship purchase, with some information redacted.
    /// </summary>
    [DataField("secretShipyardChannel")]
    public ProtoId<RadioChannelPrototype>? SecretShipyardChannel = null;

    /// <summary>
    /// Access levels to be added to the owner's ID card.
    /// </summary>
    [DataField]
    public List<ProtoId<AccessLevelPrototype>> NewAccessLevels = [];

    /// <summary>
    /// Indicates that the deeds that come from this console can be copied and transferred.
    /// </summary>
    [DataField]
    public bool CanTransferDeed = true;

    /// <summary>
    /// If true, the base sale rate is ignored before calculating taxes.
    /// </summary>
    [DataField]
    public bool IgnoreBaseSaleRate;
}
