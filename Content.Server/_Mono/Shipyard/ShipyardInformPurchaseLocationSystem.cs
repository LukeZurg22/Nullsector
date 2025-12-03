using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Shared.Chat;
using Content.Shared.Localizations;
using Robust.Server.GameObjects;
using Robust.Server.Player;

namespace Content.Server._Mono.Shipyard;

/// <summary>
/// A system that tells players which direction their newly purchased ship is located
/// </summary>
public sealed class ShipyardInformPurchaseLocationSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    /// <summary>
    /// Made for Null Sector by LZ22. I left this in here because it felt fair, and appropriate. -LZ22<br/><br/>
    /// Sends a message to the player indicating the literal coordinates of their newly purchased hull.
    /// </summary>
    /// <param name="player"></param>
    /// <param name="purchase"></param>
    public void SendShipLocationMessage(EntityUid player, EntityUid purchase)
    {
        // Try to get player's and ship's locations.
        if (!TryGetPositions(player, purchase, out _, out var shipPos))
            return;

        // Send message to player
        var message = Loc.GetString(
            "nullith-location-message", // Null Sector - For use with the Nullith. Localized vaguely to accomodate ships, if needed.
            ("location", shipPos));

        SendMessageToPlayer(player, message);
    }

    /// <summary>
    /// Sends a message to the player indicating the compass direction of their newly purchased ship
    /// </summary>
    public void SendShipDirectionMessage(EntityUid player, EntityUid ship)
    {
        if (!TryGetPositions(player, ship, out var playerPos, out var shipPos))
            return;

        // Calculate direction vector
        var direction = shipPos - playerPos;

        // Skip if they're at the same position (very unlikely but just in case)
        if (direction.LengthSquared() < 0.01f)
            return;

        // Get compass direction
        var directionName = ContentLocalizationManager.FormatDirection(direction.GetDir()).ToLower(); //lua localization
        var distance = Math.Round(direction.Length(), 1);

        // Send message to player
        var message = Loc.GetString("shipyard-direction-message",
            ("direction", directionName),
            ("distance", distance));

        SendMessageToPlayer(player, message);
    }

    /// <summary>
    /// [Null Sector] Sends a provided message to player.
    /// </summary>
    private void SendMessageToPlayer(EntityUid player, string message)
    {
        if (_playerManager.TryGetSessionByEntity(player, out var session))
        {
            _chatManager.ChatMessageToOne(ChatChannel.Server,
                message,
                message,
                EntityUid.Invalid,
                false,
                session.Channel);
        }
    }

    /// <summary>
    /// [Null Sector] Attempts to get the Player and Ship positions from provided Player and Ship Entities.
    /// </summary>
    /// <returns>Two Vector2 output-variables: player and ship positions.</returns>
    private bool TryGetPositions(EntityUid player, EntityUid ship, out Vector2 playerPos, out Vector2 shipPos)
    {
        playerPos = Vector2.NaN;
        shipPos = Vector2.NaN;
        // Try to get player's and ship's transform components.
        if (!EntityManager.TryGetComponent<TransformComponent>(player, out var playerTransform) ||
            !EntityManager.TryGetComponent<TransformComponent>(ship, out var shipTransform))
            return false;

        // Make sure both entities are on the same map
        if (playerTransform.MapID != shipTransform.MapID)
            return false;

        // Get positions of both entities
        playerPos = _transform.GetWorldPosition(player);
        shipPos = _transform.GetWorldPosition(ship);
        return true;
    }

    //lua start
    ///// <summary>
    ///// Converts a direction vector to a compass direction
    ///// </summary>
    //private string GetCompassDirection(Vector2 direction)
    //{
    //    var angle = new Angle(direction);
    //    var dir = angle.GetDir();

    //    return dir switch
    //    {
    //        Direction.North => "North",
    //        Direction.NorthEast => "North East",
    //        Direction.East => "East",
    //        Direction.SouthEast => "South East",
    //        Direction.South => "South",
    //        Direction.SouthWest => "South West",
    //        Direction.West => "West",
    //        Direction.NorthWest => "North West",
    //        _ => "Unknown"
    //    };
    //}
    //lua end
}
