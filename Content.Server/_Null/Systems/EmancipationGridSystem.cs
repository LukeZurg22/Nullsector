using System.Diagnostics.CodeAnalysis;
using Content.Server._Null.Components;
using Content.Server.Construction;
using Content.Server.Explosion.Components;
using Content.Server.Materials;
using Content.Server.Power.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Content.Server._Null.Systems;

public sealed class EmancipationGridSystem : EntitySystem
{
    private float _updateTimer = 1.0f;
    private const float UpdateTime = 1.0f;

    [Dependency] private readonly TagSystem _tagSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly MaterialStorageSystem _material = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly INullExtensionSystem _nullExt = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EmancipationGridComponent, PowerChangedEvent>(HandlePowerChange);
        SubscribeLocalEvent<EmancipationGridComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<EmancipationGridComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<EmancipationGridComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<EmancipationGridComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<EmancipationGridComponent, UpgradeExamineEvent>(OnUpgradeExamine);
        SubscribeLocalEvent<EmancipationGridComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnRefreshParts(EntityUid uid, EmancipationGridComponent component, RefreshPartsEvent args)
    {
        var matterBinRating = args.PartRatings[component.MachinePartYieldAmount];

        // Yield slopes upwards with part rating.
        component.YieldPerUnitMass =
            component.BaseYieldPerUnitMass *
            MathF.Pow(component.PartRatingYieldAmountMultiplier, matterBinRating - 1);
    }

    private void OnUpgradeExamine(EntityUid uid, EmancipationGridComponent component, UpgradeExamineEvent args)
    {
        args.AddPercentageUpgrade("emancipation-grid-component-upgrade-biomass-yield",
            component.YieldPerUnitMass / component.BaseYieldPerUnitMass);
    }

    private void OnExamined(Entity<EmancipationGridComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("emancipation-grid-examined-amount", ("amount", ent.Comp.ItemsCleaned)));
        args.PushMarkup(Loc.GetString("emancipation-grid-examined-yield",
            ("yield", Math.Round(ent.Comp.CurrentExpectedYield, 2))));
    }

    private void OnComponentShutdown(Entity<EmancipationGridComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.EmancipatedGrid = null;
    }

    private void OnComponentInit(Entity<EmancipationGridComponent> device, ref ComponentInit args)
    {
        EnsureComp<TriggerOnActivateComponent>(device);

        // Sound fallbacks
        device.Comp.SoundEmancipate ??=
            new SoundPathSpecifier(EmancipationGridComponent.DefaultEmancipateSound,
                EmancipationGridComponent.DefaultAudioParameters);
        device.Comp.SoundUse ??=
            new SoundPathSpecifier(EmancipationGridComponent.DefaultOutputSound,
                EmancipationGridComponent.DefaultAudioParameters);

        if (_nullExt.SimilarEntitiesArePresentOnGrid(device))
        {
            _popup.PopupEntity(Loc.GetString("emancipation-grid-alone-popup"), device, PopupType.Medium);
            this.Stop(EntityManager, device);
            device.Comp.EmancipatedGrid = null;
            return;
        }

        this.Start(EntityManager, device);
        device.Comp.EmancipatedGrid = Transform(device).GridUid; // Set current grid
    }

    private void HandlePowerChange(Entity<EmancipationGridComponent> device, ref PowerChangedEvent args)
    {
        device.Comp.IsPowered = this.IsPowered(device.Owner, EntityManager);
        if (_nullExt.SimilarEntitiesArePresentOnGrid(device) || !device.Comp.IsPowered)
        {
            _popup.PopupEntity(Loc.GetString("emancipation-grid-alone-popup"), device, PopupType.Medium);
            this.Stop(EntityManager, device);
            device.Comp.EmancipatedGrid = null;
            return;
        }

        this.Start(EntityManager, device);
        device.Comp.EmancipatedGrid = Transform(device).GridUid; // Set current grid
    }

    private void HandleItemDeletion(EmancipationGridComponent emancipationComponent, EntityUid entityToDelete)
    {
        emancipationComponent.ItemsCleaned++; // Increment cleaned item counter.

        // Get locations of entity & machine.
        var deletedEntityLocation = Transform(entityToDelete).Coordinates;
        var machineLocation = Transform(emancipationComponent.Owner).Coordinates;

        // Play sound at Deleted Entity and Emancipation Grid Machine before deletion.
        //  EmancipateSound should never be null thanks to Component Initialization defaulting to one.
        _audio.PlayPvs(emancipationComponent.SoundEmancipate, deletedEntityLocation); // At deleted entity.
        _audio.PlayPvs(emancipationComponent.SoundEmancipate, machineLocation); // At machine.

        var expectedYield = emancipationComponent.BaseYieldPerUnitMass;
        if (TryComp<PhysicsComponent>(entityToDelete, out var physics))
        {
            expectedYield *= physics.FixturesMass;
        }

        emancipationComponent.CurrentExpectedYield += expectedYield;

        EntityManager.QueueDeleteEntity(entityToDelete); // Finally delete the entity
    }

    #region OnActivate / On / Off Methods

    /// <summary>
    /// Toggles Emancipation Grid device on or off.
    /// </summary>
    /// <param name="ent"></param>
    /// <param name="args"></param>
    private void OnActivate(Entity<EmancipationGridComponent> ent, ref ActivateInWorldEvent args)
    {
        // Complex Action, skip if user can't do Complex actions.
        if (args.Handled || !args.Complex)
            return;

        // Doesn't matter whether it is powered or not, if there are other Emancipation Grids, this turns-off NOW.
        if (_nullExt.SimilarEntitiesArePresentOnGrid(ent))
        {
            _popup.PopupEntity(Loc.GetString("emancipation-grid-alone-popup"), ent, PopupType.Medium);
            this.Stop(EntityManager, ent);
            args.Handled = true;
            return;
        }

        // if the device isn't powered, simply turn it on.
        if (!ent.Comp.IsPowered)
        {
            this.Start(EntityManager, ent);
            ent.Comp.EmancipatedGrid = Transform(ent).GridUid; // Set current grid
            args.Handled = true;
            return;
        }

        // If the device is powered, then it may have biomass. If it has biomass, it should prioritize-
        //  -releasing that biomass rather than disable the device.
        var actualYield = (int)ent.Comp.CurrentExpectedYield; // Can only have an integer of biomass, for comparisons.
        if (actualYield == 0) // If it has nothing, then clearly, we must turn this thing off.
        {
            this.Stop(EntityManager, ent);
            ent.Comp.EmancipatedGrid = null;
            args.Handled = true;
            return;
        }

        // Given that it must have some kind of yield, we may instead spawn biomass rather than disable the machine.
        ent.Comp.CurrentExpectedYield -= actualYield; // store non-integer leftovers

        _material.SpawnMultipleFromMaterial(
            amount: actualYield,
            material: EmancipationGridComponent.BiomassPrototype,
            coordinates: Transform(ent).Coordinates);

        _audio.PlayPvs(ent.Comp.SoundUse, Transform(ent).Coordinates); // Play "Use" sound at machine.

        args.Handled = true;
    }

    #endregion

    [SuppressMessage("ReSharper", "RedundantJumpStatement")]
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _updateTimer += frameTime;

        if (!(_updateTimer >= UpdateTime))
            return; // Short-circuit from timer. All actions of the grid rely on this timer anyhow.

        _updateTimer -= UpdateTime;

        // Get all Emancipation Grid machines.
        var emancipationGrids = AllEntityQuery<EmancipationGridComponent>();
        while (emancipationGrids.MoveNext(out var emancipationGridComponent))
        {
            if (!emancipationGridComponent.IsPowered || !emancipationGridComponent.EmancipatedGrid.HasValue)
                continue; // Short-circuit

            // Check for entities on grid.
            var gridEnumerator = Transform(emancipationGridComponent.EmancipatedGrid.Value).ChildEnumerator;
            Stack<EntityUid> deleteList = [];

            // Loop over all possible entities that can be added to a list for deletion.
            while (gridEnumerator.MoveNext(out var gridEntity))
            {
                #region Filters

                // Cache lookups
                var blacklist = emancipationGridComponent.Blacklist;
                var whitelist = emancipationGridComponent.Whitelist;

                // Blacklist checks
                if (blacklist is { Components.Length: > 0 })
                {
                    foreach (var comp in blacklist.Components)
                    {
                        var type = EntityManager.ComponentFactory.GetRegistration(comp).Type;
                        if (HasComp(gridEntity, type))
                            goto PROCEED; // Immediately skip processing if blacklisted
                    }
                }

                if (TryComp<TagComponent>(gridEntity, out var tagComp) &&
                    _tagSystem.HasAnyTag(tagComp, blacklist?.Tags!))
                {
                    goto PROCEED;
                }

                // Whitelist checks
                if (whitelist is { Components.Length: > 0 })
                {
                    foreach (var comp in whitelist.Components)
                    {
                        var type = EntityManager.ComponentFactory.GetRegistration(comp).Type;
                        if (!HasComp(gridEntity, type))
                            continue; // Short-circuit in foreach loop
                        deleteList.Push(gridEntity);
                        goto PROCEED;
                    }
                }

                if (TryComp(gridEntity, out tagComp) &&
                    _tagSystem.HasAnyTag(tagComp, whitelist?.Tags!))
                {
                    deleteList.Push(gridEntity);
                }

                #endregion

                PROCEED: ; // Logic that I personally can cope with in the late evening. It works. -Z
            }

            // Handle item deletions in bulk.
            var count = deleteList.ToArray().Length;
            for (var index = 0; index < count; index++)
            {
                var entity = deleteList.Pop();
                HandleItemDeletion(emancipationGridComponent, entity);
            }
        }
    }
}
