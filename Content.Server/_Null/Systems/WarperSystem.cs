using System.Linq;
using System.Numerics;
using Content.Server._Null.Components;
using Content.Server.Administration;
using Content.Server.Bible.Components;
using Content.Server.Popups;
using Content.Shared.Administration;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Content.Server._Null.Systems;

/// <summary>
/// This is lifted from Mining Station 14 and redesigned to be used for Vault Station's Dungeon Layers.
/// However, it was repurposed for the Null Sector by LukeZurg22, who wrote most of this code anyhow.
/// <br/><br/>
/// As such, this system is designed to accomodate this arrangement and manual changes will be
/// necessary in order to accomplish procedural-only levels.
/// -Z
/// </summary>
public sealed class WarperSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly PhysicsSystem _physics = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WarperComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<WarperComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<WarperComponent> ent, ref ExaminedEvent args)
    {
        // Show source ID destination ID message.
        args.PushMarkup(string.Concat(!string.IsNullOrEmpty(ent.Comp.CurrentId)
                ? Loc.GetString($"warper-on-examine-source", ("location", ent.Comp.CurrentId))
                : Loc.GetString($"warper-cancelled-no-source"),
            !string.IsNullOrEmpty(ent.Comp.DestinationId)
                ? Loc.GetString($"warper-on-examine-destination", ("location", ent.Comp.DestinationId))
                : Loc.GetString($"warper-cancelled-no-destination")));
    }

    private void OnActivate(Entity<WarperComponent> ent, ref ActivateInWorldEvent args)
    {
        TryWarp(ent, args.Target, args.User);
    }


    /// <summary>
    /// Gets a warper using an id, which may be a source or destination ID. This assumes
    /// it is a destination ID by default.
    /// </summary>
    /// <returns>The first warper with a provided destination.</returns>
    private Entity<WarperComponent>? GetWarper(string id, bool useSourceId = false)
    {
        foreach (var warper in EntityManager.EntityQuery<WarperComponent>())
        {
            if (useSourceId && warper.CurrentId == null)
                continue;
            if (warper.DestinationId == null)
                continue;

            if (useSourceId)
            {
                if (warper.DestinationId!.Equals(id))
                    return new Entity<WarperComponent>(warper.Owner, warper);
            }
            else
            {
                if (warper.CurrentId!.Equals(id))
                    return new Entity<WarperComponent>(warper.Owner, warper);
            }
        }

        return null;
    }

    private void DisplayLocale(EntityUid? user, string locale)
    {
        if (user == null)
            return;
        var message = Loc.GetString(locale);
        _popupSystem.PopupEntity(message, user.Value, PopupType.Medium);
    }

    /// <summary>
    /// Checks whether a dungeon level is complete if a certain amount of monsters within the dungeon are dead.
    /// This is specific to all monsters containing one or more faction tags.
    /// </summary>
    /// <param name="warper"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    private bool CanWarp(Entity<WarperComponent> warper, EntityUid? user = null)
    {
        // If it's sealed, it simply cannot warp.
        if (warper.Comp.Sealed)
        {
            DisplayLocale(user, "warper-cancelled-sealed");
            return false;
        }

        // If there is no assigned destination, then the user can't warp.
        if (warper.Comp.DestinationId == null)
        {
            DisplayLocale(user, "warper-cancelled-no-destination");
            return false;
        }

        var dest = GetWarper(warper.Comp.DestinationId);
        if (dest is null)
        {
            DisplayLocale(user, "warper-cancelled-invalid-destination");
            return false;
        }

        // If it isn't sealed, and there isn't any need to clean hostiles, then warp anyway.
        if (!warper.Comp.LevelClearRequired)
        {
            return true;
        }

        var hostileFactions = warper.Comp.HostileFactions;
        int monsterCount = 0, aliveCount = 0;
        foreach (var mob in EntityManager.EntityQuery<NpcFactionMemberComponent>())
        {
            // NPCs not on the same map - skipped // TODO: Rewrite this to accomodate grid, instead.
            if (Transform(mob.Owner).GridUid == Transform(warper).GridUid)
                continue;
            // NPC not of a hostile faction - skipped
            if (!mob.Factions.Any(faction => hostileFactions.Contains(faction.ToString())))
                continue;
            // NPC is a pet - skipped
            if (HasComp<FamiliarComponent>(mob.Owner))
                continue;
            // Add to monster count.
            monsterCount++;
            // If it's dead, don't continue.
            if (_mobState.IsDead(mob.Owner))
                continue;
            // Monster is a Boss - dungeon is straight up NOT DONE.
            if (_tags.HasTag(mob.Owner, "Boss"))
                return false;
            // So if it's not a pet, not dead, not a boss,
            aliveCount++;
        }

        // 20% bottom limit for how many hostiles could be alive to be considered complete.
        if (aliveCount <= 0.2 * monsterCount)
            return true;

        return false;
    }

    private void TryWarp(Entity<WarperComponent> warper, EntityUid user, EntityUid victim)
    {
        if (!CanWarp(warper, user))
            return;

        if (string.IsNullOrEmpty(warper.Comp.DestinationId))
        {
            DisplayLocale(user, "warper-cancelled-no-destination");
            return;
        }

        var destination = GetWarper(warper.Comp.DestinationId);
        var entityManager = IoCManager.Resolve<IEntityManager>();
        entityManager.TryGetComponent(destination, out TransformComponent? destXform);
        if (destXform is null)
        {
            Logger.DebugS("warper", $"Warp destination '{warper.Comp.DestinationId}' has no transform");
            var message = Loc.GetString("warper-map-invalid");
            _popupSystem.PopupEntity(message, user);
            return;
        }

        // Check that the destination map is initialized and return unless in aghost mode.
        var mapManager = IoCManager.Resolve<IMapManager>();
        var destinationMap = destXform.MapID;
        if (!mapManager.IsMapInitialized(destinationMap) || mapManager.IsMapPaused(destinationMap))
        {
            if (!entityManager.HasComponent<GhostComponent>(user))
            {
                // Normal ghosts cannot interact, so if we're here this is already an admin ghost.
                Logger.DebugS("warper",
                    $"Player tried to warp to '{warper.Comp.DestinationId}', which is not on a running map");
                DisplayLocale(user, "warper-map-invalid");
                return;
            }
        }

        _audio.PlayPvs(warper.Comp.TeleportSound, user);
        var transform = entityManager.GetComponent<TransformComponent>(victim);
        transform.Coordinates = destXform.Coordinates;
        transform.AttachToGridOrMap();
        if (entityManager.TryGetComponent(victim, out PhysicsComponent? _))
        {
            _physics.SetLinearVelocity(victim, Vector2.Zero);
        }
    }

    [AdminCommand(AdminFlags.Debug)]
    private sealed class WarpToCommand : IConsoleCommand
    {
        public string Command => "warpto";
        public string Description => "Finds the nearest warper and attempts to warp to it.";
        public string Help => "warpto";

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (shell.Player?.AttachedEntity == null /*|| shell.Player.AttachedEntityTransform == null*/)
            {
                shell.WriteLine("You need a player and attached entity to use this command.");
                return;
            }

            if (args.Length < 1)
            {
                shell.WriteLine("Invalid argument length. This requires a destination ID.");
                return;
            }

            var destination = args[0];

            var sysMan = IoCManager.Resolve<IEntitySystemManager>();
            var warpSystem = sysMan.GetEntitySystem<WarperSystem>();
            var player = shell.Player.AttachedEntity.Value;
            var warper = warpSystem.GetWarper(destination);

            if (warper == null)
            {
                shell.WriteLine("No warp found!");
                return;
            }

            if (!warpSystem.CanWarp(warper.Value, player))
            {
                shell.WriteLine("Cannot warp!");
                return;
            }

            var evt = new ActivateInWorldEvent(player, warper.Value.Comp.Owner, false);
            warpSystem.TryWarp(warper.Value, evt.Target, evt.User);
        }
    }
}
