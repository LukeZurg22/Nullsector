using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server._NF.GameRule;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._NF.CCVar;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._Null.Components;
using Content.Shared._Null.Nullith;
using Content.Shared._Null.Nullith.Events;
using Content.Shared.GameTicking;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;
using Robust.Shared.Utility;

namespace Content.Server._Null.Systems;

public sealed partial class NullithSystem : SharedNullithSystem
{
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly PointOfInterestSystem _poiSystem = default!;

    /// <summary>
    /// A list of Prototype IDs shared between all NullithConsoleComponents that prevents the same
    /// Point of Interest from being purchased more than once.
    /// </summary>
    [ViewVariables, DataField(customTypeSerializer: typeof(PrototypeIdListSerializer<BuyablePoIPrototype>))]
    public static List<string> AlreadyPurchasedPointsOfInterest = [];

    #region Constant Prototype ID's

    /// <summary>
    /// The prototype of the paper that the monolith will spawn on purchase of a location.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public const string DeedPrototype = "PaperMonolithDeed";

    /// <summary>
    /// This is applied to both the player's customized stamp's visual, AND the deed's stamp visual.
    /// </summary>
    public const string DefaultStampState = "paper_stamp-company";

    /// <summary>
    /// Generic stamp that spawns for the user, which is customized to their hearts content based on the POI purchased.
    /// </summary>
    public const string GenericStampPrototype = "RubberStampCompanyGeneric";

    #endregion

    public MapId? ShipyardMap { get; private set; }
    private float _shuttleIndex;
    private const float ShuttleSpawnBuffer = 1f;
    private ISawmill _sawmill = default!;
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        // FIXME: Load-bearing jank - game doesn't want to create a shipyard map at this point.
        _enabled = _configManager.GetCVar(NFCCVars.Shipyard);
        _configManager.OnValueChanged(NFCCVars.Shipyard,
            SetShipyardEnabled); // NOTE: run immediately set to false, see comment above

        _sawmill = Logger.GetSawmill("shipyard");

        SubscribeLocalEvent<NullithConsoleComponent, ComponentStartup>(OnShipyardStartup);
        SubscribeLocalEvent<NullithConsoleComponent, BoundUIOpenedEvent>(OnConsoleUIOpened);
        SubscribeLocalEvent<NullithConsoleComponent, NullithConsolePurchaseMessage>(OnPurchaseMessage);
        SubscribeLocalEvent<NullithConsoleComponent, EntInsertedIntoContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<NullithConsoleComponent, EntRemovedFromContainerMessage>(OnItemSlotChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Shutdown()
    {
        _configManager.UnsubValueChanged(NFCCVars.Shipyard, SetShipyardEnabled);
    }

    private void OnShipyardStartup(EntityUid uid, NullithConsoleComponent component, ComponentStartup args)
    {
        if (!_enabled)
            return;
        InitializeConsole();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        CleanupShipyard();
    }

    private void SetShipyardEnabled(bool value)
    {
        if (_enabled == value)
            return;

        _enabled = value;

        if (value)
            SetupShipyardIfNeeded();
        else
            CleanupShipyard();
    }

    /// <summary>
    /// Adds a ship to the shipyard, calculates its price, and attempts to ftl-dock it to the given station
    /// </summary>
    /// <param name="stationMapTuple">Item1 is the station the purchase was made. Item2 is the map the purchase took place.</param>
    /// <param name="poiPrototype">The prototype of the PoI to load. Must be a grid file!</param>
    /// <param name="purchasedLocation">The EntityUid of the shuttle that was purchased</param>
    public bool TryPurchaseLocation(
        (EntityUid, MapId) stationMapTuple,
        BuyablePoIPrototype poiPrototype,
        [NotNullWhen(true)] out EntityUid? purchasedLocation)
    {
        var gridPath = poiPrototype.GridPath;

        if (!TryComp<StationDataComponent>(stationMapTuple.Item1, out var stationData))
        {
            purchasedLocation = null;
            return false;
        }

        var price = _pricing.AppraiseGrid(stationMapTuple.Item1);
        var targetGrid = _station.GetLargestGrid(stationData);
        if (targetGrid == null) // How are we even here with no station grid
        {
            QueueDel(stationMapTuple.Item1);
            purchasedLocation = null;
            return false;
        }

        _sawmill.Info($"PoI {gridPath} was purchased at {ToPrettyString(stationMapTuple.Item1)} for {price:f2}");

        // Use the provided map and generate the POI!
        _poiSystem.GeneratePurchased(stationMapTuple.Item2, poiPrototype, out var generatedPoI);
        purchasedLocation = generatedPoI;
        if (generatedPoI != null)
            purchasedLocation = generatedPoI.Value;
        else
            return false;

        return true;
    }

    /// <summary>
    /// Loads a shuttle into the ShipyardMap from a file path
    /// </summary>
    /// <param name="shuttlePath">The path to the grid file to load. Must be a grid file!</param>
    /// <param name="shuttleGrid"></param>
    /// <returns>Returns the EntityUid of the shuttle</returns>
    private bool TryAddShuttle(ResPath shuttlePath, [NotNullWhen(true)] out EntityUid? shuttleGrid)
    {
        shuttleGrid = null;
        SetupShipyardIfNeeded();
        if (ShipyardMap == null)
            return false;

        if (!_mapLoader.TryLoadGrid(ShipyardMap.Value,
                shuttlePath,
                out var grid,
                offset: new Vector2(500f + _shuttleIndex, 1f)))
        {
            _sawmill.Error($"Unable to spawn shuttle {shuttlePath}");
            return false;
        }

        _shuttleIndex += grid.Value.Comp.LocalAABB.Width + ShuttleSpawnBuffer;

        shuttleGrid = grid.Value.Owner;
        return true;
    }

    ///<returns>False if provided uId has ShipyardPreserveOnSaleComponent, and true if otherwise.</returns>
    private bool LacksPreserveOnSaleComp(EntityUid uid)
    {
        return !TryComp<ShipyardSellConditionComponent>(uid, out var comp) || comp.PreserveOnSale == false;
    }

    private void CleanupShipyard()
    {
        if (ShipyardMap == null || !_map.MapExists(ShipyardMap.Value))
        {
            ShipyardMap = null;
            return;
        }

        _map.DeleteMap(ShipyardMap.Value);
    }

    public void SetupShipyardIfNeeded()
    {
        if (ShipyardMap != null && _map.MapExists(ShipyardMap.Value))
            return;

        _map.CreateMap(out var shipyardMap);
        ShipyardMap = shipyardMap;

        _map.SetPaused(ShipyardMap.Value, false);
    }

    // <summary>
    // Tries to rename a shuttle deed and update the respective components.
    // Returns true if successful.
    //
    // Null name parts are promptly ignored.
    // </summary>
    public bool TryRenameShuttle(EntityUid uid, ShuttleDeedComponent? shuttleDeed, string? newName, string? newSuffix)
    {
        if (!Resolve(uid, ref shuttleDeed))
            return false;

        var shuttle = shuttleDeed.ShuttleUid;
        if (shuttle != null
            && _station.GetOwningStation(shuttle.Value) is { Valid: true } shuttleStation)
        {
            // Null Sector - No such thing as deeds, here!
            /*shuttleDeed.ShuttleName = newName;
            shuttleDeed.ShuttleNameSuffix = newSuffix;*/
            Dirty(uid, shuttleDeed);

            var fullName = GetFullName(shuttleDeed);
            _station.RenameStation(shuttleStation, fullName, loud: false);
            _metaData.SetEntityName(shuttle.Value, fullName);
            _metaData.SetEntityName(shuttleStation, fullName);
        }
        else
        {
            _sawmill.Error($"Could not rename shuttle {ToPrettyString(shuttle):entity} to {newName}");
            return false;
        }

        //TODO: move this to an event that others hook into.
        if (TryGetNetEntity(shuttleDeed.ShuttleUid, out var shuttleNetEntity) &&
            _shuttleRecordsSystem.TryGetRecord(shuttleNetEntity.Value, out var record))
        {
            record.Name = newName ?? "";
            record.Suffix = newSuffix ?? "";
            _shuttleRecordsSystem.TryUpdateRecord(record);
        }

        return true;
    }

    /// <summary>
    /// Returns the full name of the shuttle component in the form of [prefix] [name] [suffix].
    /// </summary>
    public static string GetFullName(ShuttleDeedComponent comp)
    {
        string?[] parts = { comp.ShuttleName, comp.ShuttleNameSuffix };
        return string.Join(' ', parts.Where(it => it != null));
    }
}
