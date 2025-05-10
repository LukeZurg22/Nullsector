using Content.Server.GameTicking;
using Content.Server.Labels.Components;
using Content.Server.Salvage.Expeditions;
using Content.Server.Station.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._Null.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.Paper;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Content.Server._Null.Systems;

/// <summary>
/// This handles expedition reward spawning.
/// </summary>
public sealed class ExpeditionRewardRecieverSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    [Dependency] private readonly TransformSystem _transformSystem = default!;
    [Dependency] private readonly StationSystem _station = default!;

    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly ServerMetaDataSystem _metaSystem = default!;
    [Dependency] private readonly PaperSystem _paperSystem = default!;
    [Dependency] private readonly GameTicker _timer = default!;


    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExpeditionRewardRecieverComponent, ComponentStartup>(OnComponentStartup);
    }

    private void OnComponentStartup(EntityUid uid, ExpeditionRewardRecieverComponent component, ComponentStartup args)
    {
        component.SpotLocation = _transformSystem.GetMapCoordinates(uid).Position;

        var owningStation = _station.GetOwningStation(uid);
        if (owningStation == null)
        {
            return; // Uh oh! Station not found! Abort, abort!
        }

        component.OwningStation = owningStation.Value;
    }

    /// <summary>
    /// Attempts to fill in Expeditionary Receipt information and attach it to the briefcase it spawns with.
    /// </summary>
    public void FillLabel(EntityUid expedShip,
        MapCoordinates depotCoordinates,
        EntityUid depotPallet,
        EntityUid reward,
        TimeSpan departTime)
    {
        var expRewardReceiverComp = Comp<ExpeditionRewardRecieverComponent>(depotPallet);
        var printedPaper = EntityManager.SpawnEntity(expRewardReceiverComp.ReceiptOutput, depotCoordinates);

        // Create a sheet of paper to write the order details on.
        if (!TryComp<PaperComponent>(printedPaper, out var paper))
            return; // Short circuit if a paper can't be made.

        // Can be a ship or station; probably is a
        // Sets Company Stamp Signature
        var stampedByInfo = new StampDisplayInfo
        {
            StampedName = Loc.GetString("exped-receipt-signature"), // "No Company!"
            StampedColor = Color.FromHex("#FFFFF4"), // Company Color
        };

        // Company handling. Merely modifies stamp info.
        CompanyComponent company = new();
        if (TryComp(expedShip, out CompanyComponent? neuComp))
        {
            company.CompanyName = neuComp.CompanyName;
            if (!company.CompanyName.Equals("None")) // If the company name isn't the Default "None"
            {
                // Try to get the prototype for the company in game config
                if (_prototypeManager.TryIndex<CompanyPrototype>(company.CompanyName, out var prototype))
                {
                    stampedByInfo.StampedName = prototype.Name;
                    stampedByInfo.StampedColor = prototype.Color;
                }
            }
        }
        paper.StampedBy = [ stampedByInfo ]; // The above merely modifies this.

        // fill in the order data
        var paperName = Loc.GetString("exped-receipt-name");
        _metaSystem.SetEntityName(printedPaper, paperName);

        var msg = new FormattedMessage();
        // Title
        msg.AddText("[head=2]Expeditionary Receipt[/head]");
        msg.PushNewline();

        // Ship IFF & Captains Name
        TryComp<ShuttleDeedComponent>(expedShip, out var shuttleDeedComponent);
        msg.AddText($"This receipt confirms the success of a recent expeditionary mission by one [bold]" +
                    $"{shuttleDeedComponent?.ShuttleName ?? "unknown shuttle "} " +
                    $"{shuttleDeedComponent?.ShuttleNameSuffix ?? "with an unknown IFF"}[/bold] under one captain [bold]" +
                    $"{shuttleDeedComponent?.ShuttleOwner ?? "with no discernible name"}[/bold].");
        msg.PushNewline();

        // Time Complete
        msg.AddText($"[bold]Time Completed:[/bold] {departTime.ToString()}");
        msg.PushNewline();

        // Reward
        var rewardMetaData = Comp<MetaDataComponent>(reward);
        msg.AddText($"[bold]Expedition Reward:[/bold] {rewardMetaData.EntityName}");
        msg.PushNewline();

        _paperSystem.SetContent((printedPaper, paper), msg.ToMarkup());

        // attempt to attach the label to the item
        if (TryComp<PaperLabelComponent>(reward, out var label))
        {
            _slots.TryInsert(reward, label.LabelSlot, printedPaper, null);
        }

        _adminLogger.Add(
            LogType.Action,
            LogImpact.Low,
            $"Expedition Reward Receipt @ Coords: {depotCoordinates}," +
            $"Reward: {reward}, Ship Value {expedShip} of {shuttleDeedComponent?.ShuttleName ?? "UnknownName"} " +
            $"{shuttleDeedComponent?.ShuttleNameSuffix ?? "UnknownIFF"}.");

    }

}
