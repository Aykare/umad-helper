using System.Linq;
using Dalamud.Game.ClientState.Party;

namespace SamplePlugin;

/// <summary>A party member's raid position.</summary>
public enum RaidPosition
{
    None = 0,
    MT,
    OT,
    H1,
    H2,
    M1,
    M2,
    R1,
    R2,
}

/// <summary>The outcome of looking up the local player's Forsaken buddy; Message explains why there is none.</summary>
internal readonly record struct BuddyResolution(IPartyMember? Buddy, string Message);

/// <summary>Helpers for raid positions: roles, sides and the defaults of a new configuration.</summary>
internal static class RaidPositions
{
    /// <summary>All assignable positions, in raid order.</summary>
    public static readonly RaidPosition[] Assignable =
        [RaidPosition.MT, RaidPosition.OT, RaidPosition.H1, RaidPosition.H2, RaidPosition.M1, RaidPosition.M2, RaidPosition.R1, RaidPosition.R2];

    /// <summary>Bump when <see cref="Defaults"/> gains entries so existing configurations pick them up once.</summary>
    public const int DefaultsVersion = 2;

    /// <summary>The positions a new configuration starts with, by character name.</summary>
    public static readonly (string Name, RaidPosition Position)[] Defaults =
    [
        ("Celia Nyrell", RaidPosition.R1),
        ("Luna Tancredi", RaidPosition.H2),
        ("M'iriam Yukikazu", RaidPosition.M1),
        ("Nimral Lebanel", RaidPosition.OT),
        ("Ranko Mizuki", RaidPosition.M2),
        ("Cian Avino", RaidPosition.H1),
        ("Vin Enryu", RaidPosition.R2),
        ("Selene Celeste", RaidPosition.MT),
    ];

    /// <summary>Supports are the tanks and healers; everyone else is a dps.</summary>
    public static bool IsSupport(RaidPosition position)
        => position is RaidPosition.MT or RaidPosition.OT or RaidPosition.H1 or RaidPosition.H2;

    /// <summary>The Forsaken buddy pairs are MT/H1, OT/H2, M1/R1 and M2/R2.</summary>
    public static RaidPosition Partner(RaidPosition position)
    {
        return position switch
        {
            RaidPosition.MT => RaidPosition.H1,
            RaidPosition.H1 => RaidPosition.MT,
            RaidPosition.OT => RaidPosition.H2,
            RaidPosition.H2 => RaidPosition.OT,
            RaidPosition.M1 => RaidPosition.R1,
            RaidPosition.R1 => RaidPosition.M1,
            RaidPosition.M2 => RaidPosition.R2,
            RaidPosition.R2 => RaidPosition.M2,
            _ => RaidPosition.None,
        };
    }

    /// <summary>Finds the local player's Forsaken buddy in the current party from the configured raid positions.</summary>
    public static BuddyResolution ResolveBuddy(Configuration configuration)
    {
        if (Plugin.ObjectTable.LocalPlayer is not { } localPlayer
            || Plugin.PartyList.FirstOrDefault(member => member.EntityId == localPlayer.EntityId) is not { } localMember)
        {
            return new BuddyResolution(null, "Not in a party.");
        }

        var own = configuration.GetPosition(localMember.Name.TextValue);
        if (own == RaidPosition.None)
        {
            return new BuddyResolution(null, "Set your raid position in the settings.");
        }

        var partner = Partner(own);
        var buddy = Plugin.PartyList.FirstOrDefault(member =>
            member.EntityId != localPlayer.EntityId && configuration.GetPosition(member.Name.TextValue) == partner);
        return buddy is null
            ? new BuddyResolution(null, $"Your buddy ({partner}) is not in the party. Set raid positions in the settings.")
            : new BuddyResolution(buddy, string.Empty);
    }

    /// <summary>Supports take the left tower and side, dps the right one.</summary>
    public static string Side(RaidPosition position)
    {
        return position switch
        {
            RaidPosition.None => string.Empty,
            _ when IsSupport(position) => "left",
            _ => "right",
        };
    }

    /// <summary>Short text such as "R1, right side"; empty when no position is set.</summary>
    public static string Describe(RaidPosition position)
        => position == RaidPosition.None ? string.Empty : $"{position}, {Side(position)} side";
}
