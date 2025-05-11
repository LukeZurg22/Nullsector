using Content.Server.Chat.Managers;
using Content.Server.IdentityManagement;
using Content.Shared.Chat;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Content.Shared.CCVar;

namespace Content.Server._White.Examine
{
    public sealed class ExaminableCharacterSystem : EntitySystem
    {
        [Dependency] private readonly InventorySystem _inventorySystem = default!;
        [Dependency] private readonly IdentitySystem _identitySystem = default!;
        [Dependency] private readonly EntityManager _entityManager = default!;
        [Dependency] private readonly IChatManager _chatManager = default!;
        [Dependency] private readonly INetConfigurationManager _netConfigManager = default!;

        public override void Initialize()
        {
            SubscribeLocalEvent<ExaminableCharacterComponent, ExaminedEvent>(HandleExamine);
        }

        private void HandleExamine(EntityUid uid, ExaminableCharacterComponent comp, ExaminedEvent args)
        {
            if (!TryComp<ActorComponent>(args.Examiner, out var actorComponent)
                || !args.IsInDetailsRange)
                return;

            var showExamine =
                _netConfigManager.GetClientCVar(actorComponent.PlayerSession.Channel, CCVars.DetailedExamine);

            var selfaware = args.Examiner == args.Examined;
            var logLines = new List<string>();

            var canSeeLoc = "examine-can-see";
            var nameLoc = "examine-name";

            if (selfaware)
            {
                canSeeLoc += "-selfaware";
                nameLoc += "-selfaware";
            }

            var identity = _identitySystem.GetEntityIdentity(uid);
            var name = Loc.GetString(nameLoc, ("name", identity));
            logLines.Add($"[color=DarkGray][font size=10]{name}[/font][/color]");

            if (showExamine)
                args.PushMarkup($"[font size=10]{name}[/font]", 15);

            var canSee = Loc.GetString(canSeeLoc, ("ent", uid));
            logLines.Add($"[color=DarkGray][font size=10]{canSee}[/font][/color]");

            if (showExamine)
                args.PushMarkup($"[font size=10]{canSee}[/font]", 14);

            var slotLabels = new Dictionary<string, string>
            {
                { "head", "head-" },
                { "eyes", "eyes-" },
                { "mask", "mask-" },
                { "neck", "neck-" },
                { "ears", "ears-" },
                { "jumpsuit", "jumpsuit-" },
                { "outerClothing", "outer-" },
                { "back", "back-" },
                { "gloves", "gloves-" },
                { "belt", "belt-" },
                { "id", "id-" },
                { "shoes", "shoes-" },
                { "suitstorage", "suitstorage-" },
            };

            var priority = 13;

            foreach (var slotEntry in slotLabels)
            {
                var slotName = slotEntry.Key;
                var slotLabel = slotEntry.Value;

                slotLabel += "examine";

                if (selfaware)
                    slotLabel += "-selfaware";

                if (!_inventorySystem.TryGetSlotEntity(uid, slotName, out var slotEntity))
                    continue; // Short Circuit - No Slot Entity

                if (!_entityManager.TryGetComponent<MetaDataComponent>(slotEntity, out var metaData))
                    continue; // Short Circuit - No MetaData

                var item = Loc.GetString(slotLabel, ("item", metaData.EntityName), ("ent", uid));
                if (showExamine)
                    args.PushMarkup($"[font size=10]{item}[/font]", priority);
                logLines.Add($"[color=DarkGray][font size=10]{item}[/font][/color]");
                priority--;
            }

            if (priority >= 13)
            {
                var canSeeNothingLoc = "examine-can-see-nothing";

                if (selfaware)
                    canSeeNothingLoc += "-selfaware";

                var canseenothing = Loc.GetString(canSeeNothingLoc, ("ent", uid));
                logLines.Add($"[color=DarkGray][font size=10]{canseenothing}[/font][/color]");

                if (showExamine)
                    args.PushMarkup($"[font size=10]{canseenothing}[/font]", priority);
            }
            else
            {
                // We already pushed the basic description above
            }

            // Null Sector start : removing examine chat log
            if (!_netConfigManager.GetClientCVar(actorComponent.PlayerSession.Channel, CCVars.DetailedExamineChat))
                return; // Shot Circuit - Detailed Examine is Off

            var combinedLog = string.Join("\n", logLines); // Get all the logs from before.
            if (showExamine && _netConfigManager.GetClientCVar(actorComponent.PlayerSession.Channel, CCVars.LogInChat))
            {
                // Spit them out in chat.
                _chatManager.ChatMessageToOne(ChatChannel.Emotes,
                    combinedLog,
                    combinedLog,
                    EntityUid.Invalid,
                    false,
                    actorComponent.PlayerSession.Channel,
                    recordReplay: false);
            }
            // Null Sector end : removing examine chat log
        }
    }
}
