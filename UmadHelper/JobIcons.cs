using System.Collections.Generic;

namespace SamplePlugin;

/// <summary>Maps game job IDs to the job icon images shipped in the Data folder.</summary>
internal static class JobIcons
{
    private static readonly Dictionary<uint, string> ImageNames = new()
    {
        [19] = "Paladin_Icon_11.png",
        [20] = "Monk_Icon_11.png",
        [21] = "Warrior_Icon_11.png",
        [22] = "Dragoon_Icon_11.png",
        [23] = "Bard_Icon_11.png",
        [24] = "White_Mage_Icon_11.png",
        [25] = "Black_Mage_Icon_11.png",
        [27] = "Summoner_Icon_11.png",
        [28] = "Scholar_Icon_11.png",
        [30] = "Ninja_Icon_11.png",
        [31] = "Machinist_Icon_11.png",
        [32] = "Dark_Knight_Icon_11.png",
        [33] = "Astrologian_Icon_11.png",
        [34] = "Samurai_Icon_11.png",
        [35] = "Red_Mage_Icon_11.png",
        [37] = "Gunbreaker_Icon_11.png",
        [38] = "Dancer_Icon_11.png",
        [39] = "Reaper_Icon_11.png",
        [40] = "Sage_Icon_11.png",
        [41] = "Viper_Icon_11.png",
        [42] = "Pictomancer_Icon_11.png",
    };

    /// <summary>Returns true for the tank and healer jobs, which take the support marks for stack and spread.</summary>
    public static bool IsSupport(uint jobId)
    {
        return jobId is 1 or 3 or 19 or 21 or 32 or 37 or 6 or 24 or 28 or 33 or 40;
    }

    /// <summary>Returns the icon file for a job ID, or null when the job has no icon.</summary>
    public static string? ImageName(uint jobId)
    {
        return ImageNames.GetValueOrDefault(jobId);
    }
}
