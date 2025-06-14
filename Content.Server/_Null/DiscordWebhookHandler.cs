using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.GameRule;
using Content.Server.Discord;
using Content.Shared._Null.CCVar;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;

// ReSharper disable once CheckNamespace :: KEEP THIS HERE
namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    // Discord limits
    private const int MaxEmbedCharacters = 6000;
    private const int MaxFieldsPerEmbed = 25;
    private const int MaxFieldValueLength = 1024;
    private const int MaxFieldNameLength = 256;

    /// <summary>
    /// Mono + Null : Sends manifest to discord webhook.
    /// </summary>
    /// <param name="playerInfo"></param>
    private async void SendCrewManifestDiscordMessage(RoundEndMessageEvent.RoundEndPlayerInfo[]? playerInfo)
    {
        var webhookUrl = _cfg.GetCVar(CCVars.DiscordCrewManifestWebhook);
        if (string.IsNullOrEmpty(webhookUrl))
            return;

        var webhookData = await _discord.GetWebhook(webhookUrl);
        if (webhookData == null)
            return;

        var webhookIdentifier = webhookData.Value.ToIdentifier();

        if (playerInfo == null || playerInfo.Length == 0)
            return;

        // Filter out observers and sort by antag status (antags first), then by player name
        var sortedPlayers = playerInfo
            .Where(p => !p.Observer && !string.IsNullOrEmpty(p.PlayerICName))
            .OrderBy(p => !p.Antag)
            .ThenBy(p => p.PlayerOOCName)
            .ToList();

        if (sortedPlayers.Count == 0)
            return; // Short-circuit if there's nobody 'board.

        // Create the manifest text in the same format as the round end summary
        var manifestLines = new HashSet<string>();
        var profitLines = new HashSet<string>();

        // Get the NFAdventureRuleSystem to access profit data
        var adventureSystem = EntityManager.System<NFAdventureRuleSystem>();

        foreach (var player in sortedPlayers)
        {
            #region Setup

            // Use localization to get the proper job name instead of the key
            var roleName = Loc.GetString(player.Role);
            var showUsername = false; // 'false' by default, it is an opt-in feature.t

            if (_playerManager.TryGetSessionById(player.PlayerGuid, out var ds))
            {
                try
                {
                    // This can fail to get client CVar. It is not optimal, but this prevents an all-out crash.
                    showUsername = _netConfigManager.GetClientCVar(ds.Channel, NullCCVars.DisplayUsernameInSummary);
                }
                catch (Exception e)
                {
                    Logger.Error(e.ToString());
                }
            }

            #endregion Null Sector End

            // - PLAYER was CHARACTER playing role of ROLE. |OR| // - CHARACTER playing role of ROLE.
            var playerLine = showUsername
                ? $"- {player.PlayerOOCName} was {player.PlayerICName} playing as a {roleName}."
                : $"- {player.PlayerICName} playing as a {roleName}.";


            // HashSet.Add() will only add non-duplicate entries.
            manifestLines.Add(playerLine);

            // Try to get profit information for this player
            if (player.PlayerGuid == null || string.IsNullOrEmpty(player.PlayerICName))
                continue; // Short-circuit

            var profitInfo = adventureSystem.GetPlayerProfitInfo(player.PlayerGuid.Value, player.PlayerICName);

            if (profitInfo == null)
                continue; // Short-circuit if profit info is null

            // HashSet.Add() will only add non-duplicate entries.
            profitLines.Add(profitInfo);
        }

        // Split into multiple fields if the content is too long for a single Discord field
        // Calculate base embed character count (title + description + footer) to create embeds with proper limit handling
        // Prepare base embed content :: Using localization to make the lives of maintainers easier. Take notes. -Z
        var title = Loc.GetString("discord-round-end-summary-title");
        var description = Loc.GetString("discord-round-end-summary-description",
            ("round", RoundId),
            ("count", manifestLines.Count));
        var footerText = Loc.GetString("discord-round-end-summary-id",
            ("name", _baseServer.ServerName),
            ("id", RoundId));
        var initialCharacterCount = title.Length + description.Length + footerText.Length;
        var embeds = new List<WebhookEmbed>();
        var currentFields = new List<WebhookEmbedField>();
        var currentEmbedCharacterCount = initialCharacterCount;
        var embedCount = 0;
        var currentFieldLines = new List<string>();
        var currentFieldLength = 0;
        var manifestFieldCount = 0;

        foreach (var line in manifestLines)
        {
            // Check if adding this line would exceed field value limit
            if (currentFieldLength + line.Length + 1 > MaxFieldValueLength - 20 && currentFieldLines.Count > 0)
            {
                var fieldName = Loc.GetString("discord-round-end-summary-player-manifest");
                var fieldValue = string.Join("\n", currentFieldLines);
                var fieldCharacterCount = fieldName.Length + fieldValue.Length;

                // Check if adding this field would exceed embed limits
                if (currentFields.Count >= MaxFieldsPerEmbed - 1
                    || currentEmbedCharacterCount + fieldCharacterCount > MaxEmbedCharacters - 500)
                {
                    AddCurrentEmbed(
                        title,
                        description,
                        footerText,
                        ref currentFields,
                        ref embeds,
                        ref embedCount,
                        ref currentEmbedCharacterCount,
                        ref initialCharacterCount
                    );
                }

                currentFields.Add(new WebhookEmbedField
                {
                    Name = fieldName,
                    Value = fieldValue,
                    Inline = false,
                });
                currentEmbedCharacterCount += fieldCharacterCount;
                manifestFieldCount++;
                currentFieldLines.Clear();
                currentFieldLength = 0;
            }

            currentFieldLines.Add(line);
            currentFieldLength += line.Length + 1; // +1 for newline
        }

        // Add the remaining lines
        if (currentFieldLines.Count > 0)
        {
            var fieldName = manifestFieldCount == 0
                ? Loc.GetString("discord-round-end-summary-player-manifest")
                : Loc.GetString("discord-round-end-summary-player-manifest--continued");
            var fieldValue = string.Join("\n", currentFieldLines);
            var fieldCharacterCount = fieldName.Length + fieldValue.Length;

            // Check if adding this field would exceed embed limits
            if (currentFields.Count >= MaxFieldsPerEmbed - 1 ||
                currentEmbedCharacterCount + fieldCharacterCount > MaxEmbedCharacters - 500)
            {
                AddCurrentEmbed(
                    title,
                    description,
                    footerText,
                    ref currentFields,
                    ref embeds,
                    ref embedCount,
                    ref currentEmbedCharacterCount,
                    ref initialCharacterCount
                );
            }

            currentFields.Add(new WebhookEmbedField
            {
                Name = fieldName,
                Value = fieldValue,
                Inline = false,
            });
            currentEmbedCharacterCount += fieldCharacterCount;
        }

        // Add profit information if available
        if (profitLines.Count > 0)
        {
            // Split profit lines into fields if needed (same algorithm as Player Manifest)
            var currentProfitLines = new List<string>();
            var currentProfitLength = 0;
            var profitFieldCount = 0;

            foreach (var line in profitLines)
            {
                var fieldName = profitFieldCount == 0
                    ? Loc.GetString("bank-atm-menu-title")
                    : Loc.GetString("bank-atm-menu-title") + " (continued)";
                var fieldValue = string.Join("\n", currentProfitLines);
                var fieldCharacterCount = fieldName.Length + fieldValue.Length;

                // Check if adding this line would exceed field value limit
                if (currentProfitLength + line.Length + 1 > MaxFieldValueLength - 20 && currentProfitLines.Count > 0)
                {
                    currentFields.Add(new WebhookEmbedField
                    {
                        Name = fieldName,
                        Value = fieldValue,
                        Inline = false,
                    });
                    currentEmbedCharacterCount += fieldCharacterCount;
                    profitFieldCount++;
                    currentProfitLines.Clear();
                    currentProfitLength = 0;
                }

                currentProfitLines.Add(line);
                currentProfitLength += line.Length + 1; // +1 for newline
            }

            // Add remaining profit lines
            if (currentProfitLines.Count > 0)
            {
                var fieldName = profitFieldCount == 0
                    ? Loc.GetString("bank-atm-menu-title")
                    : Loc.GetString("bank-atm-menu-title") + " (continued)";
                var fieldValue = string.Join("\n", currentProfitLines);
                var fieldCharacterCount = fieldName.Length + fieldValue.Length;

                // Check if adding this field would exceed embed limits
                if (currentFields.Count >= MaxFieldsPerEmbed - 1 ||
                    currentEmbedCharacterCount + fieldCharacterCount > MaxEmbedCharacters - 500)
                {
                    AddCurrentEmbed(
                        title,
                        description,
                        footerText,
                        ref currentFields,
                        ref embeds,
                        ref embedCount,
                        ref currentEmbedCharacterCount,
                        ref initialCharacterCount
                    );
                }

                currentFields.Add(new WebhookEmbedField
                {
                    Name = fieldName,
                    Value = fieldValue,
                    Inline = false,
                });
                currentEmbedCharacterCount += fieldCharacterCount;
            }
        }

        // Add any remaining fields to the final embed
        AddCurrentEmbed(
            title,
            description,
            footerText,
            ref currentFields,
            ref embeds,
            ref embedCount,
            ref currentEmbedCharacterCount,
            ref initialCharacterCount
        );

        foreach (var payload in embeds.Select(embed => new WebhookPayload { Embeds = [embed], }))
        {
            await _discord.CreateMessage(webhookIdentifier, payload);

            // Small delay between messages to avoid rate limiting
            if (embeds.Count > 1)
                await Task.Delay(100);
        }
    }

    private void AddCurrentEmbed(
        string title,
        string description,
        string footer,
        ref List<WebhookEmbedField> currentFields,
        ref List<WebhookEmbed> embeds,
        ref int embedCount,
        ref int currentEmbedCharacterCount,
        ref int initialCharacterCount)
    {
        if (currentFields.Count <= 0)
            return; // Short-circuit

        embeds.Add(new WebhookEmbed
        {
            Title = title,
            Description = embedCount == 0 ? description : "",
            Color = 0x9999FF,
            Fields = [..currentFields],
            Footer = new WebhookEmbedFooter { Text = footer },
        });
        embedCount++;
        currentFields.Clear();
        currentEmbedCharacterCount = initialCharacterCount;
    }
}
