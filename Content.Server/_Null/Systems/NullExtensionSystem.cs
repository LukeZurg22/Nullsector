using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Prototypes;

namespace Content.Server._Null.Systems;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public interface INullExtensionSystem
{
    bool AreAkinEntitiesPresentOnGrid<T>(Entity<T> ent) where T : IComponent;
    EntProtoId? GetProtoID(EntityUid ent);
}

public sealed partial class NullExtensionSystem : EntitySystem, INullExtensionSystem
{
    public override void Initialize()
    {
        base.Initialize();
        IoCManager.Register<INullExtensionSystem>();
    }
    public bool AreAkinEntitiesPresentOnGrid<T>(Entity<T> ent) where T : IComponent
    {
        var query = AllEntityQuery<T>();

        while (query.MoveNext(out var other))
        {
            if (other.Equals(ent.Comp))
                continue;

            if (other.GetType() != ent.Comp.GetType())
                continue;

            return true;
        }

        return false;
    }

    public EntProtoId? GetProtoID(EntityUid ent)
    {
        if (!TryComp<MetaDataComponent>(ent, out var metaData))
            return null;

        return metaData.EntityPrototype?.ID;
    }
}
