using System.Linq;
using Content.Server.Administration;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Mapping;
using Content.Shared.Administration;
using Content.Shared.Atmos;
using Robust.Server.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Shared._Null
{
    [AdminCommand(AdminFlags.Debug)]
    public sealed class GetFileCount : IConsoleCommand
    {
        [Dependency] private readonly IEntityManager _entManager = default!;

        public string Command => "gettilecount";
        public string Description => "Gets tile count of a provided grid ID.";
        public string Help => "gettilecount <GridEid>";

        [Obsolete("Obsolete")]
        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length < 1)
            {
                shell.WriteLine("Too few arguments.");
                return;
            }

            if (!NetEntity.TryParse(args[0], out var gridIdNet) || !_entManager.TryGetEntity(gridIdNet, out var gridId))
            {
                shell.WriteLine("Command not parsed.");
                return;
            }

            if (_entManager.TryGetComponent<TransformComponent>(gridId, out var xForm))
            {
                var grid = _entManager.GetComponent<MapGridComponent>(gridId.Value);
                var tiles = grid.GetAllTiles();
                shell.WriteLine($"Tile Count: {tiles.Count()}");
            }
            else
            {
                shell.WriteLine("Could not yield transform component from grid.");
            }
        }
    }
}
