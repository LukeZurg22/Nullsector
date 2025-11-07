using System.Linq;
using Content.Server.Interaction;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Shared.Physics;

// ReSharper disable InconsistentNaming

namespace Content.Server.NPC.HTN.Preconditions;

public sealed partial class TargetInLOSPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private InteractionSystem _interaction = default!;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<TagComponent> _tagQuery;


    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("rangeKey")]
    public string RangeKey = "RangeKey";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _interaction = sysManager.GetEntitySystem<InteractionSystem>();
        _fixturesQuery = _entManager.GetEntityQuery<FixturesComponent>();
        _tagQuery = _entManager.GetEntityQuery<TagComponent>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);

        return _interaction.InRangeUnobstructed(owner,
            target,
            range,
            predicate: entity =>
            {
                if (!_fixturesQuery.TryGetComponent(entity, out var fixtures))
                    return false; // Don't ignore - Short Circuit

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
}
