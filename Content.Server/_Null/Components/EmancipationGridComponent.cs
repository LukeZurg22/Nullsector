using Content.Server._Null.Systems;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server._Null.Components;

/// <summary>
/// Designates this entity as a device that acts upon a grid for Emancipation.
/// <br/><br/>
/// Made for the Null Sector by LZ22, use this however you please.
/// </summary>
/* # ===USAGE EXAMPLE=== (Customize as you please. The values provided are implicit with the component.)
 # Careful. There are a LOT of things the Emancipation Grid will delete.
 - type: EmancipationGrid
    emancipateSound: # Optional sound effect parameters
    path: /Audio/Effects/zzzt.ogg
        params:
    volume: -3
    useSound:
    path: /Audio/Effects/toilet_seat_down.ogg
        params:
    volume: -3
    whitelist: # Objects the emancipation grid WILL delete.
        tags:
        - etc
        components:
        - etc
    blacklist: # Objects the emancipation grid WILL delete.
        tags:
        - etc
        components:
        - etc
*/
[RegisterComponent, Access(typeof(EmancipationGridSystem)) /*, NetworkedComponent*/]
public sealed partial class EmancipationGridComponent : Component
{
    /// <summary>
    /// Number of items cleaned thus far. Should be fine to contain within an Int32.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)] public int ItemsCleaned { get; set; }

    /// <summary>
    /// Specific Components and Tags that the Emancipation Grid will delete. If empty, it will do nothing.
    /// This does not need to be set in the prototype, as basic trash is defined as default.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), DataField]
    public EntityWhitelist? Whitelist { get; set; } = new()
    {
        Components = ["SpaceGarbage", "Puddle"], // The Armada hates spills. No weakness, make no mistakes!
        Tags = ["Trash"],
    };

    /// <summary>
    /// Blacklisted qualities of items that will cause the emancipation grid to ignore them.
    /// </summary>
    public EntityWhitelist? Blacklist { get; set; } = new()
    {
        Components = ["Hypospray", "Food"],
        Tags = // I know this thing is pretty brutal, but it doesn't have to be monstrously bad.
        [
            "BoxCardboard",
            "GlassBeaker", "CentrifugeCompatible", "DrinkBottle", "ChemDispensable",
            "Meat", "Egg",
        ],
    };

    /// <summary>
    /// The current grid that this entity is emancipating of trash.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)] public EntityUid? EmancipatedGrid { get; set; }

    /// <summary>
    /// Check to see if this device is receiving power. Can be toggled, but it would be unwise due to possible
    /// unpredicted behaviour.
    /// </summary>
    public bool IsPowered = false;

    #region Default Audio Values

    /// <summary>
    /// Path to default soundbyte if one is not specified in the prototype file.
    /// </summary>
    public const string DefaultEmancipateSound = "/Audio/Effects/zzzt.ogg";

    public const string DefaultOutputSound = "/Audio/Effects/toilet_seat_down.ogg";

    [ValidatePrototypeId<MaterialPrototype>]
    public const string BiomassPrototype = "Biomass";

    public static AudioParams DefaultAudioParameters = AudioParams.Default.WithVolume(-3);

    #endregion

    /// <summary>
    ///  If not null, this sound will be played when an item is deleted both on the item and the machine.
    /// </summary>
    [DataField("emancipateSound")] public SoundSpecifier? SoundEmancipate;

    [DataField("useSound")] public SoundSpecifier? SoundUse;

    //-----------------------------------------------------------------------------------------------\\
    //---------------------------------------Biomass Variables---------------------------------------\\
    //-----------------------------Lifted from Biomass ReclaimerComponent----------------------------\\
    // WARN: Warning, there may be an issue regarding upgrades. The code appears sound, but adding matter-
    //  -bins via R.P.E.D. in testing appears to do nothing? Regardless, be wary. -Z

    /// <summary>
    /// Amount of biomass that the entity being processed will yield.
    /// This is calculated from the YieldPerUnitMass.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float CurrentExpectedYield = 0f;

    /// <summary>
    /// How many units of biomass it produces for each unit of mass.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float YieldPerUnitMass = default;

    /// <summary>
    /// The base yield per mass unit when no components are upgraded.
    /// </summary>
    [DataField("baseYieldPerUnitMass")]
    public float BaseYieldPerUnitMass = 0.4f;

    /// <summary>
    /// Machine part whose rating modifies the yield per mass.
    /// </summary>
    [DataField("machinePartYieldAmount", customTypeSerializer: typeof(PrototypeIdSerializer<MachinePartPrototype>))]
    public string MachinePartYieldAmount = "MatterBin";

    /// <summary>
    /// How much the machine part quality affects the yield.
    /// Going up a tier will multiply the yield by this amount.
    /// </summary>
    [DataField("partRatingYieldAmountMultiplier")]
    public float PartRatingYieldAmountMultiplier = 1.25f;

    //-----------------------------------------------------------------------------------------------\\
}
