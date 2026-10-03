using UnityEngine;

namespace FreeKick
{
    /// <summary>One Maccabi Netanya player who can take the free kicks.</summary>
    public readonly struct SquadMember
    {
        public readonly int Number;
        public readonly string Name;

        /// <summary>Keepers stay in goal; they are never picked to take a free kick.</summary>
        public readonly bool IsGoalkeeper;

        public SquadMember(int number, string name, bool isGoalkeeper = false)
        {
            Number = number;
            Name = name;
            IsGoalkeeper = isGoalkeeper;
        }

        /// <summary>Shirt number, blank for squad members without one.</summary>
        public string Shirt => Number > 0 ? $"#{Number}" : "";
    }

    /// <summary>
    /// The Maccabi Netanya squad, the same list the other games use. Each round a
    /// different outfield player steps up to take the free kicks.
    /// </summary>
    public static class Roster
    {
        public static readonly SquadMember[] Squad =
        {
            new SquadMember(1, "Antma", isGoalkeeper: true),
            new SquadMember(2, "Morozov"),
            new SquadMember(4, "Ben Shabat"),
            new SquadMember(5, "Kolikov"),
            new SquadMember(6, "Konate"),
            new SquadMember(8, "Haziza"),
            new SquadMember(10, "Oz"),
            new SquadMember(11, "Hugi"),
            new SquadMember(12, "Azugi"),
            new SquadMember(13, "Nidam"),
            new SquadMember(14, "Liem"),
            new SquadMember(15, "Maor"),
            new SquadMember(16, "Zarura"),
            new SquadMember(17, "Yarin"),
            new SquadMember(18, "Shamir"),
            new SquadMember(21, "Talpa"),
            new SquadMember(22, "Samu"),
            new SquadMember(24, "Amit Cohen"),
            new SquadMember(25, "Cifrian"),
            new SquadMember(26, "Jabber"),
            new SquadMember(40, "Saba"),
            new SquadMember(44, "Feldman"),
            new SquadMember(83, "Davo"),
            new SquadMember(0, "Aziz"),
            new SquadMember(0, "Daniel Cohen"),
        };

        /// <summary>The outfield squad - everyone who could plausibly take a free kick.</summary>
        public static readonly SquadMember[] Strikers =
            System.Array.FindAll(Squad, member => !member.IsGoalkeeper);

        public static SquadMember Random() => Strikers[UnityEngine.Random.Range(0, Strikers.Length)];
    }
}
