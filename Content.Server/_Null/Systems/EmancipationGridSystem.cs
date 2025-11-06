using System.Diagnostics.CodeAnalysis;
using Content.Server._Null.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Power;
using Content.Shared.Tag;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Null.Systems;

public sealed class EmancipationGridSystem : EntitySystem
{
    private float _updateTimer = 1.0f;
    private const float UpdateTime = 1.0f;

    [Dependency] private readonly TagSystem _tagSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EmancipationGridComponent, PowerChangedEvent>(HandlePowerChange);
        SubscribeLocalEvent<EmancipationGridComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<EmancipationGridComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<EmancipationGridComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<EmancipationGridComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("emancipation-grid-examined", ("amount", ent.Comp.ItemsCleaned)));

    private void OnComponentShutdown(Entity<EmancipationGridComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.EmancipatedGrid = null;
    }

    private void OnComponentInit(Entity<EmancipationGridComponent> ent, ref ComponentInit args)
    {
        ent.Comp.EmancipatedGrid = Transform(ent).GridUid;
    }

    private void HandlePowerChange(Entity<EmancipationGridComponent> emancipationGrid, ref PowerChangedEvent args)
    {
        emancipationGrid.Comp.IsPowered = this.IsPowered(emancipationGrid.Owner, EntityManager);
        if (!emancipationGrid.Comp.IsPowered)
        {
            Stop(emancipationGrid);
            return; // Short-circuit
        }

        emancipationGrid.Comp.EmancipatedGrid = Transform(emancipationGrid).GridUid; // Set current grid
    }

    private void HandleItemDeletion(EmancipationGridComponent emancipationGridMachine, EntityUid entityToDelete)
    {
        emancipationGridMachine.ItemsCleaned++; // Increment cleaned item counter.

        // Get locations of entity & machine.
        var deletedEntityLocation = Transform(entityToDelete).Coordinates;
        var machineLocation = Transform(emancipationGridMachine.Owner).Coordinates;

        // Play sound at Deleted Entity and Emancipation Grid Machine before deletion.
        Console.WriteLine($"Playing sound \"{emancipationGridMachine.EmancipateSound}\"");
        if (emancipationGridMachine.EmancipateSound != null)
        {
            _audio.PlayPvs(emancipationGridMachine.EmancipateSound, deletedEntityLocation); // At deleted entity.
            _audio.PlayPvs(emancipationGridMachine.EmancipateSound, machineLocation); // At machine.
        }

        EntityManager.QueueDeleteEntity(entityToDelete); // Finally delete the entity
    }

    private void Stop(Entity<EmancipationGridComponent> emancipationGridMachine)
    {
        emancipationGridMachine.Comp.EmancipatedGrid = null;
        Dirty(emancipationGridMachine);
    }

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
            if (!emancipationGridComponent.IsPowered)
                continue; // Short-circuit

            // Check for entities on grid.
            var gridEnumerator = Transform(emancipationGridComponent.EmancipatedGrid!.Value).ChildEnumerator;
            Stack<EntityUid> deleteList = [];

            // Loop over all possible entities that can be added to a list for deletion.
            while (gridEnumerator.MoveNext(out var gridEntity))
            {
                // Check all whitelisted components.
                foreach (var comp in emancipationGridComponent.Whitelist!.Components!)
                {
                    // Check if entity has component, and proceed if so.
                    var componentType = EntityManager.ComponentFactory.GetRegistration(comp).Type;
                    if (!HasComp(gridEntity, componentType))
                        continue; // Short-circuit the Foreach Component list.
                    deleteList.Push(gridEntity);
                    goto PROCEED;
                }
                // Check if entity has tag component, and if that component has any whitelisted tags.
                if (TryComp<TagComponent>(gridEntity, out var tagComponent) &&
                    _tagSystem.HasAnyTag(tagComponent, emancipationGridComponent.Whitelist?.Tags!))
                {
                    deleteList.Push(gridEntity);
                    goto PROCEED;
                }
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
