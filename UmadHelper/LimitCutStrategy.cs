using System;

namespace SamplePlugin;

/// <summary>The Limit Cut resolution table (see LC-strat) and the arena geometry it relies on.</summary>
internal static class LimitCutStrategy
{
    /// <summary>Waymarks clockwise from north: A is north, then 2, B, 3, C, 4, D, 1.</summary>
    public static readonly string[] RingLabels = ["A", "2", "B", "3", "C", "4", "D", "1"];

    /// <summary>World positions (X, Z) of the waymarks, in <see cref="RingLabels"/> order, taken from the pull logs.</summary>
    public static readonly (float X, float Z)[] WaymarkPositions =
    [
        (100.0f, 88.0f),
        (106.0f, 94.0f),
        (112.0f, 100.0f),
        (106.0f, 106.0f),
        (100.0f, 112.0f),
        (94.0f, 106.0f),
        (88.0f, 100.0f),
        (94.0f, 94.0f),
    ];

    private const float ArenaCenterX = 100.0f;
    private const float ArenaCenterZ = 100.0f;
    private const int RingSize = 8;
    private const float GapToleranceDegrees = 5.0f;

    /// <summary>Gets the clockwise angle in degrees from north of a world position around the arena center.</summary>
    public static float AngleFromNorth(float x, float z)
    {
        var degrees = MathF.Atan2(x - ArenaCenterX, -(z - ArenaCenterZ)) * 180.0f / MathF.PI;
        return degrees < 0.0f ? degrees + 360.0f : degrees;
    }

    /// <summary>Converts a game rotation (0 faces south, pi faces north) to degrees clockwise from north.</summary>
    public static float FacingDegrees(float rotation)
    {
        var degrees = (180.0f - (rotation * 180.0f / MathF.PI)) % 360.0f;
        return degrees < 0.0f ? degrees + 360.0f : degrees;
    }

    /// <summary>Snaps an angle to the nearest of the eight waymark directions.</summary>
    public static int RingIndexFromAngle(float angleDegrees)
    {
        return (int)MathF.Round(angleDegrees / 45.0f) % RingSize;
    }

    /// <summary>
    /// Finds the gap between two neighbouring waymarks where a player ends up. Gap i lies between
    /// ring marker i and i + 1, so gap 1 is "2B". The origin picks one of four colour groups
    /// (D/4 purple, A/1 red, B/2 yellow, C/3 blue) and the rotation direction picks how the numbers advance.
    /// </summary>
    public static bool TryGetFinalGap(LimitCutRotation rotation, int originIndex, int number, out int gapIndex)
    {
        gapIndex = 0;
        if (rotation == LimitCutRotation.Unknown || originIndex is < 0 or >= RingSize || number is < 1 or > RingSize)
        {
            return false;
        }

        // Purple covers ring indices 5 and 6, red 7 and 0, yellow 1 and 2, blue 3 and 4.
        var colorGroup = ((originIndex - 5 + RingSize) % RingSize) / 2;
        var offset = rotation == LimitCutRotation.CounterClockwise
            ? (2 * colorGroup) + number
            : (2 * colorGroup) + 2 - number;

        gapIndex = ((offset % RingSize) + RingSize) % RingSize;
        return true;
    }

    /// <summary>
    /// Finds the gap a position lies in. Standing within a few degrees past the edge of the expected gap still
    /// counts as that gap, so a player hugging a waymark line is not reported as wrong.
    /// </summary>
    public static int GetActualGap(float x, float z, int? expectedGapIndex)
    {
        var angle = AngleFromNorth(x, z);
        if (expectedGapIndex is int expected)
        {
            var centerAngle = (expected * 45.0f) + 22.5f;
            var difference = MathF.Abs(((angle - centerAngle + 540.0f) % 360.0f) - 180.0f);
            if (difference <= 22.5f + GapToleranceDegrees)
            {
                return expected;
            }
        }

        return (int)(angle / 45.0f) % RingSize;
    }

    /// <summary>Returns the two waymark labels bordering a gap, in clockwise order.</summary>
    public static (string First, string Second) GapLabels(int gapIndex)
    {
        return (RingLabels[gapIndex], RingLabels[(gapIndex + 1) % RingSize]);
    }

    /// <summary>Returns the image file for a waymark label.</summary>
    public static string ImageName(string label)
    {
        return $"way_{label.ToLowerInvariant()}.png";
    }

    /// <summary>Returns a short readable rotation name.</summary>
    public static string RotationName(LimitCutRotation rotation)
    {
        return rotation switch
        {
            LimitCutRotation.Clockwise => "CW",
            LimitCutRotation.CounterClockwise => "CCW",
            _ => "?",
        };
    }
}
