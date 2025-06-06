using System.Numerics;
using Robust.Client.Graphics;

namespace Content.Client._Null;

public static class DrawingHandleExtensions
{

    // Null Sector port of Monolith w. Reposition
    /// <summary>
    /// Draws a shield ring with constant thickness regardless of zoom level.
    /// </summary>
    public static void DrawRing(this DrawingHandleBase handle, Vector2 position, float radius, Color color)
    {
        // Draw the shield outline as a ring with constant thickness
        const float ringThickness = 2.0f; // Fixed thickness in pixels

        // Draw multiple circles with slightly different radii to create a solid ring effect
        for (float offset = 0; offset <= ringThickness; offset += 0.5f)
        {
            handle.DrawCircle(position, radius + offset, color.WithAlpha(0.5f), false);
        }
    }
}
