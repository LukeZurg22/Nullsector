using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Guidebook;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

namespace Content.Shared._Null.Nullith;

/// <summary>
/// Various "VesselX" enums are used here, which were made for Frontier's Vessel Shipyards now repurposed for points
/// of Interest, which bears a very similar prototype to <see cref="VesselPrototype"/>. However, Buyable PoI's are not
/// limited to any particular "yard", because as of 20251202 they can only be purchased from one location anyway.
/// </summary>
[Prototype]
public sealed class BuyablePoIPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<BuyablePoIPrototype>))]
    public string[]? Parents { get; private set; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    /// <summary>
    ///     The location's name.
    /// </summary>
    [DataField] public string Name = string.Empty;

    /// <summary>
    ///     Short description of the location.
    /// </summary>
    [DataField] public string Description = string.Empty;

    /// <summary>
    /// The title that the user gains for being the buyer of the location.
    /// </summary>
    [DataField("title")]
    public string? TitleLoc = string.Empty; // TODO: make this actually mean something.

    /// <summary>
    ///     The price of the location
    /// </summary>
    [DataField(required: true)]
    public int Price;

    /// <summary>
    /// The listing that the location should be in.
    /// </summary>
    [DataField(required: true)]
    public NullithConsoleUiKey Group = NullithConsoleUiKey.Monolith;

    /// <summary>
    ///     The purpose of the location. (e.g. Service, Cargo, Engineering etc.)
    /// </summary>
    [DataField("category")]
    public List<PoICategory> PoICategories = [];

    /// <summary>
    /// The access required to buy the product. (e.g. Command, Mail, Bailiff, etc.)
    /// </summary>
    [DataField]
    public string Access = string.Empty;

    /// <summary>
    /// Relative directory path to the given point of interest, i.e. `/Maps/PointsOfInterest_Purchasable/your_grid.yml`
    /// </summary>
    [DataField(required: true)]
    public ResPath GridPath = default!;

    /// <summary>
    /// Guidebook page associated with a point of interest
    /// </summary>
    [DataField]
    public ProtoId<GuideEntryPrototype>? GuidebookPage = default!;

    /// <summary>
    ///     The price markup of the location testing
    /// </summary>
    [DataField]
    public float MinPriceMarkup = 1.05f;

    #region Warp Point

    /// <summary>
    ///     Should we set the warp point name based on the grid name?
    /// </summary>
    [DataField]
    public bool NameWarp { get; set; } = true;

    /// <summary>
    ///     If NameWarp is true, should the warp point be admin-only (hiding it for players)?
    /// </summary>
    [DataField]
    public bool HideWarp { get; set; } = false;

    #endregion

    #region Spawn Distance

    /// <summary>
    ///     Minimum range to spawn this POI at
    /// </summary>
    [DataField]
    public int MinimumDistance { get; private set; } = 5000;

    /// <summary>
    ///     Maximum range to spawn this POI at
    /// </summary>
    [DataField]
    public int MaximumDistance { get; private set; } = 10000;

    #endregion

    /// <summary>
    /// Components to be added to any spawned grids.
    /// </summary>
    [DataField]
    [AlwaysPushInheritance]
    public ComponentRegistry AddComponents { get; set; } = new();
}

public enum PoICategory : byte
{
    All, // Placeholder value to represent everything
    Scrapyard,
    Laboratory,
    Warehouse,
    Farm,
    Hospital,
    Restaurant,
    // Antagonist PoIs
    Hideout,    // Defense Stations or Piratical Areas
    Orbital,    // Armadan Defense Stations
}
