using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Prototypes;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Content.Server._Null.Systems;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public interface INullExtensionSystem
{
    bool SimilarEntitiesArePresentOnGrid<T>(Entity<T> ent) where T : IComponent;
    EntProtoId? GetProtoID(EntityUid ent);
}

[SuppressMessage("Usage", "RA0030:Consider using the non-generic variant of this method")]
public sealed partial class NullExtensionSystem : EntitySystem, INullExtensionSystem
{
    public override void Initialize()
    {
        base.Initialize();
        IoCManager.Register<INullExtensionSystem>();
    }

    public bool SimilarEntitiesArePresentOnGrid<T>(Entity<T> ent) where T : IComponent
    {
        var query = AllEntityQuery<T>();

        while (query.MoveNext(out var otherComponent))
        {
            if (otherComponent.Owner.Equals(ent.Owner)) // If they are the same entity, skip.
                continue;

            if (Transform(otherComponent.Owner).GridUid == Transform(ent.Owner).GridUid)
                return true; // Both entities differ, and both share the same grid.
        }

        return false;
    }

    public EntProtoId? GetProtoID(EntityUid ent)
    {
        return !TryComp<MetaDataComponent>(ent, out var metaData) ? null : metaData.EntityPrototype?.ID;
    }
}
