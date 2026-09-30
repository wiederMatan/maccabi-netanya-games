using UnityEngine;

namespace MathStrikers
{
    /// <summary>One Maccabi Netanya player the striker can be.</summary>
    public readonly struct SquadMember
    {
        public readonly int Number;
        public readonly string Name;
        public readonly string ResourcePath;

        /// <summary>Keepers stay in goal; they are never picked to take the shots.</summary>
        public readonly bool IsGoalkeeper;

        public SquadMember(int number, string name, string resourcePath, bool isGoalkeeper = false)
        {
            Number = number;
            Name = name;
            ResourcePath = resourcePath;
            IsGoalkeeper = isGoalkeeper;
        }

        /// <summary>Shirt number as shown on the portrait, blank for unnumbered squad photos.</summary>
        public string Shirt => Number > 0 ? $"#{Number}" : "";
    }

    /// <summary>
    /// The Maccabi Netanya squad, generated from the club portraits that ship with
    /// the games portal. The portrait is loaded from Resources on demand so the
    /// whole squad is not held in memory at once.
    /// </summary>
    public static class Roster
    {
        public static readonly SquadMember[] Squad =
        {
            new SquadMember(1, "Antma", "Players/1-antma", isGoalkeeper: true),
            new SquadMember(2, "Morozov", "Players/2-morozov"),
            new SquadMember(4, "Ben Shabat", "Players/4-ben-shabat"),
            new SquadMember(5, "Kolikov", "Players/5-kolikov"),
            new SquadMember(6, "Konate", "Players/6-konate"),
            new SquadMember(8, "Haziza", "Players/8-haziza"),
            new SquadMember(10, "Oz", "Players/10-oz"),
            new SquadMember(11, "Hugi", "Players/11-hugi"),
            new SquadMember(12, "Azugi", "Players/12-azugi"),
            new SquadMember(13, "Nidam", "Players/13-nidam"),
            new SquadMember(14, "Liem", "Players/14-liem"),
            new SquadMember(15, "Maor", "Players/15-maor"),
            new SquadMember(16, "Zarura", "Players/16-zarura"),
            new SquadMember(17, "Yarin", "Players/17-yarin"),
            new SquadMember(18, "Shamir", "Players/18-shamir"),
            new SquadMember(21, "Talpa", "Players/21-talpa"),
            new SquadMember(22, "Samu", "Players/22-samu"),
            new SquadMember(24, "Amit Cohen", "Players/24-amit-cohen"),
            new SquadMember(25, "Cifrian", "Players/25-cifrian"),
            new SquadMember(26, "Jabber", "Players/26-jabber"),
            new SquadMember(40, "Saba", "Players/40-saba"),
            new SquadMember(44, "Feldman", "Players/44-feldman"),
            new SquadMember(83, "Davo", "Players/83-davo"),
            new SquadMember(0, "Aziz", "Players/aziz"),
            new SquadMember(0, "Daniel Cohen", "Players/daniel-cohen"),
        };

        /// <summary>The outfield squad - everyone who could plausibly take a penalty.</summary>
        public static readonly SquadMember[] Strikers =
            System.Array.FindAll(Squad, member => !member.IsGoalkeeper);

        public static SquadMember Random() => Strikers[UnityEngine.Random.Range(0, Strikers.Length)];

        public static Texture2D LoadPortrait(SquadMember member) =>
            Resources.Load<Texture2D>(member.ResourcePath);
    }
}
