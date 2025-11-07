using System.Linq;
using Content.Server.NPC.Components;
using Content.Shared.CombatMode;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

// ReSharper disable InconsistentNaming

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly RotateToFaceSystem _rotate = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;

    private EntityQuery<CombatModeComponent> _combatQuery;
    private EntityQuery<NPCSteeringComponent> _steeringQuery;
    private EntityQuery<RechargeBasicEntityAmmoComponent> _rechargeQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<TagComponent> _tagQuery;


    // TODO: Don't predict for hitscan
    private const float ShootSpeed = 20f;

    /// <summary>
    /// Cooldown on raycasting to check LOS.
    /// </summary>
    public const float UnoccludedCooldown = 0.2f;

    private void InitializeRanged()
    {
        _combatQuery = GetEntityQuery<CombatModeComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _rechargeQuery = GetEntityQuery<RechargeBasicEntityAmmoComponent>();
        _steeringQuery = GetEntityQuery<NPCSteeringComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _tagQuery = GetEntityQuery<TagComponent>();

        SubscribeLocalEvent<NPCRangedCombatComponent, ComponentStartup>(OnRangedStartup);
        SubscribeLocalEvent<NPCRangedCombatComponent, ComponentShutdown>(OnRangedShutdown);
    }

    private void OnRangedStartup(EntityUid uid, NPCRangedCombatComponent component, ComponentStartup args)
    {
        if (TryComp<CombatModeComponent>(uid, out var combat))
        {
            _combat.SetInCombatMode(uid, true, combat);
        }
        else
        {
            component.Status = CombatStatus.Unspecified;
        }
    }

    private void OnRangedShutdown(EntityUid uid, NPCRangedCombatComponent component, ComponentShutdown args)
    {
        if (TryComp<CombatModeComponent>(uid, out var combat))
        {
            _combat.SetInCombatMode(uid, false, combat);
        }
    }

    private void UpdateRanged(float frameTime)
    {
        var query = EntityQueryEnumerator<NPCRangedCombatComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Status == CombatStatus.Unspecified)
                continue;

            if (_steeringQuery.TryGetComponent(uid, out var steering) && steering.Status == SteeringStatus.NoPath)
            {
                comp.Status = CombatStatus.TargetUnreachable;
                comp.ShootAccumulator = 0f;
                continue;
            }

            if (!_xformQuery.TryGetComponent(comp.Target, out var targetXform) ||
                !_physicsQuery.TryGetComponent(comp.Target, out var targetBody))
            {
                comp.Status = CombatStatus.TargetUnreachable;
                comp.ShootAccumulator = 0f;
                continue;
            }

            if (targetXform.MapID != xform.MapID)
            {
                comp.Status = CombatStatus.TargetUnreachable;
                comp.ShootAccumulator = 0f;
                continue;
            }

            if (_combatQuery.TryGetComponent(uid, out var combatMode))
            {
                _combat.SetInCombatMode(uid, true, combatMode);
            }

            if (!_gun.TryGetGun(uid, out var gunUid, out var gun))
            {
                comp.Status = CombatStatus.NoWeapon;
                comp.ShootAccumulator = 0f;
                continue;
            }

            var ammoEv = new GetAmmoCountEvent();
            RaiseLocalEvent(gunUid, ref ammoEv);

            if (ammoEv.Count == 0)
            {
                // Recharging then?
                if (_rechargeQuery.HasComponent(gunUid))
                {
                    continue;
                }

                comp.Status = CombatStatus.Unspecified;
                comp.ShootAccumulator = 0f;
                continue;
            }

            comp.LOSAccumulator -= frameTime;

            var worldPos = _transform.GetWorldPosition(xform);
            var targetPos = _transform.GetWorldPosition(targetXform);

            // We'll work out the projected spot of the target and shoot there instead of where they are.
            var distance = (targetPos - worldPos).Length();
            var oldInLos = comp.TargetInLOS;

            // TODO: Should be doing these raycasts in parallel
            // Ideally we'd have 2 steps, 1. to go over the normal details for shooting and then 2. to handle beep / rotate / shoot
            if (comp.LOSAccumulator < 0f)
            {
                comp.LOSAccumulator += UnoccludedCooldown;
                // For consistency with NPC steering.
                comp.TargetInLOS = _interaction.InRangeUnobstructed(uid,
                    comp.Target,
                    distance + 0.1f,
                    predicate: entity =>
                    {
                        if (!_fixturesQuery.TryGetComponent(entity, out var fixtures))
                            return false; // Don't ignore - Short-Circuit

                        // Null Sector - Start (Enemies shooting through windows)
                        const int glassMask = (int)(CollisionGroup.GlassLayer | CollisionGroup.GlassAirlockLayer);
                        const string WallTag = "Wall";

                        // Avoid walls. Checking Wall Collision Mask causes turret to ignore windows.
                        if (_tagQuery.TryGetComponent(entity, out var tagComponent)
                            && tagComponent.Tags.Any(tag => tag.Id.Equals(WallTag)))
                        {
                            return false;
                        }

                        // TODO: See about improving this by using Opaque mask only.

                        // If there's only glass, allow peeking through.
                        if (fixtures.Fixtures.Values.Any(f => (f.CollisionLayer & glassMask) != 0))
                            return true;
                        // Null Sector - End

                        return true;
                    });
            }

            if (!comp.TargetInLOS)
            {
                comp.ShootAccumulator = 0f;
                comp.Status = CombatStatus.NotInSight;

                if (TryComp(uid, out steering))
                {
                    steering.ForceMove = true;
                }

                continue;
            }

            if (!oldInLos && comp.SoundTargetInLOS != null)
            {
                _audio.PlayPvs(comp.SoundTargetInLOS, uid);
            }

            comp.ShootAccumulator += frameTime;

            if (comp.ShootAccumulator < comp.ShootDelay)
            {
                continue;
            }

            var mapVelocity = targetBody.LinearVelocity;
            var targetSpot = targetPos + mapVelocity * distance / ShootSpeed;

            // If we have a max rotation speed then do that.
            var goalRotation = (targetSpot - worldPos).ToWorldAngle();
            var rotationSpeed = comp.RotationSpeed;

            if (!_rotate.TryRotateTo(uid,
                    goalRotation,
                    frameTime,
                    comp.AccuracyThreshold,
                    rotationSpeed?.Theta ?? double.MaxValue,
                    xform))
            {
                continue;
            }

            // TODO: Ammo checks
            // TODO: Burst fire
            // TODO: Cycling
            // Max rotation speed

            // TODO: Check if we can face

            if (!Enabled || !_gun.CanShoot(gun))
                continue;

            var entityCoordinates = _mapManager.TryFindGridAt(xform.MapID, targetPos, out var gridUid, out var mapGrid)
                ? new EntityCoordinates(gridUid, _mapSystem.WorldToLocal(gridUid, mapGrid, targetSpot))
                : new EntityCoordinates(xform.MapUid!.Value, targetSpot);

            comp.Status = CombatStatus.Normal;

            if (gun.NextFire > _timing.CurTime)
            {
                return;
            }

            // Frontier - This ensures that the bullet won't fly over the target if it's downed
            _gun.SetTarget(gun, comp.Target);
            _gun.AttemptShoot(uid, gunUid, gun, entityCoordinates);
        }
    }
}
