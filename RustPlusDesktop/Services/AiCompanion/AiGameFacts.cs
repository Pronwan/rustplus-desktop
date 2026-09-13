using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RustPlusDesk.Models.Raid;
using RustPlusDesk.Services.Raid;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// The numbers this app already knows, handed to the model so it does not have to remember
    /// them.
    ///
    /// Raid costs are the case that forced this: a model asked how many rockets a metal wall
    /// takes will answer confidently and be one tier out, because Rust has five building tiers
    /// with similar names and the training data has every wrong answer in it too. Meanwhile the
    /// raid calculator on the next tab has the real table, generated from the game's own values.
    /// So the table goes in the prompt, and the model is told to use it over what it remembers.
    /// </summary>
    public static class AiGameFacts
    {
        private static string? _cached;

        /// <summary>
        /// The raid table as compact text, or empty when the data cannot be read.
        ///
        /// Built once and kept. It is a few hundred tokens on every request, which is a great
        /// deal cheaper than an answer that sends someone out with half the explosives.
        /// </summary>
        public static string RaidCosts()
        {
            if (_cached != null) return _cached;

            try
            {
                // Synchronously, never by blocking on the async one: this is reached from
                // inside a provider request, and blocking there on a method whose awaits
                // capture the dispatcher is what froze the app on send.
                _cached = Render(new RaidDataService().Load());
            }
            catch
            {
                // Never worth failing a question over. Without it the model answers from memory,
                // which is where it was before.
                _cached = "";
            }

            return _cached;
        }

        /// <summary>
        /// The explosives people actually raid with, in the order they come up.
        ///
        /// Not all twenty-one sources: flashbangs and bee grenades break nothing, and a table
        /// with every row in it costs tokens on every single question to answer one that nobody
        /// asks. Incendiary rockets go the same way: they are a fire weapon and their column
        /// against building blocks is almost entirely blank.
        /// </summary>
        private static readonly string[] Sources =
        {
            "Rocket",
            "Timed Explosive Charge",
            "Satchel Charge",
            "Beancan Grenade",
            "Explosive 5.56 Rifle Ammo",
            "High Velocity Rocket",
            "40mm HE Grenade",
        };

        /// <summary>
        /// What gets shot at: the building blocks and the doors.
        ///
        /// Deployables are left out on purpose — the list runs to a hundred and sixty entries,
        /// and the question that goes wrong is always about a wall or a door.
        /// </summary>
        private static bool IsWorthListing(RaidTarget target) =>
            target.ComponentType == "BuildingBlock" ||
            target.DisplayName.Contains("Door", StringComparison.OrdinalIgnoreCase) ||
            target.DisplayName.Contains("Window", StringComparison.OrdinalIgnoreCase) ||
            target.DisplayName.Contains("Embrasure", StringComparison.OrdinalIgnoreCase);

        private static string Render(RaidDataSet data)
        {
            var sources = Sources
                .Select(name => data.Sources.FirstOrDefault(s => s.DisplayName == name))
                .Where(source => source != null)
                .Select(source => source!)
                .ToList();

            if (sources.Count == 0) return "";

            var text = new StringBuilder();

            text.Append(
                "Raid costs and explosive crafting recipes for this build of Rust (from game data).\n" +
                "Building Tier Aliases: Top tier / HQM / High Quality Metal = 'Armored' (2000 HP). Metal / Sheet Metal = 'Metal' (1000 HP).\n\n" +
                "Boom & Explosives Crafting Recipes (and raw sulfur breakdown):\n" +
                "- Explosive 5.56 Rifle Ammo / Explo ammo [T3] (makes 2): 10 Metal Frags, 20 Gunpowder, 10 Sulfur (50 raw sulfur total for 2 rounds = 25 raw sulfur per bullet)\n" +
                "- Timed Explosive Charge / C4 [T3] (makes 1): 20 Explosives, 5 Cloth, 2 Tech Trash (2,200 raw sulfur, 1,000 gunpowder, 60 low grade, 60 metal frags)\n" +
                "- Rocket [T3] (makes 1): 10 Explosives, 150 Gunpowder, 2 Pipes (1,400 raw sulfur, 650 gunpowder, 30 low grade, 30 metal frags)\n" +
                "- High Velocity Rocket / HV Rocket [T2] (makes 1): 100 Gunpowder, 1 Pipe (200 raw sulfur)\n" +
                "- Satchel Charge [T1 / WB0] (makes 1): 4 Beancans, 1 Small Stash, 1 Rope (480 raw sulfur, 240 gunpowder, 80 metal frags)\n" +
                "- Beancan Grenade [T1 / WB0] (makes 1): 60 Gunpowder, 20 Metal Frags (120 raw sulfur)\n" +
                "- Explosives (component) [T3] (makes 1): 50 Gunpowder, 3 Low Grade Fuel, 10 Sulfur, 3 Metal Frags (110 raw sulfur)\n" +
                "- Gunpowder [T1 / mix table] (makes 10): 20 Sulfur, 30 Charcoal (2 raw sulfur per 1 gunpowder)\n" +
                "- F1 Grenade [T2] (makes 1): 30 Gunpowder, 25 Metal Frags (60 raw sulfur)\n" +
                "- 40mm HE Grenade & MLRS: Uncraftable (loot only)\n\n" +
                "Single Explosive Counts (to destroy from 100% HP using only one weapon):\n");

            text.Append("Object (tier) | HP | ").Append(string.Join(" | ", sources.Select(Short))).Append('\n');

            var engine = new RaidCalculatorEngine(data);
            var targets = data.Targets.Where(IsWorthListing).OrderBy(t => t.DisplayName).ToList();

            foreach (var target in targets)
            {
                var counts = sources
                    .Select(source => data.Hits.TryGetValue(source.SourceId, out var byTarget) &&
                                      byTarget.TryGetValue(target.TargetId, out var hits)
                        ? hits.ToString()
                        : "-")
                    .ToList();

                // A row of dashes is a target none of these touch — a window bar against
                // explosives, say. It is noise in a table that has to stay small.
                if (counts.All(c => c == "-")) continue;

                text.Append(target.DisplayName)
                    .Append(" | ").Append((int)target.StartHealth)
                    .Append(" | ").Append(string.Join(" | ", counts))
                    .Append('\n');
            }

            text.Append("\nOptimal / Cheapest Mixed Combos (Lowest Sulfur to avoid overkill damage):\n");

            foreach (var target in targets)
            {
                var mixes = engine.GetCuratedMixes(target);
                if (mixes.Count == 0) continue;

                var topMixes = mixes
                    .Select(mix => new
                    {
                        Parts = mix,
                        Sulfur = mix.Sum(m => m.SulfurCost),
                        Summary = string.Join(" + ", mix.Select(m => $"{m.RequiredItems} {Short(m.Source)}"))
                    })
                    .Where(m => m.Sulfur > 0)
                    .OrderBy(m => m.Sulfur)
                    .Take(2)
                    .ToList();

                if (topMixes.Count > 0)
                {
                    var mixStr = string.Join(" OR ", topMixes.Select(m => $"{m.Summary} ({(int)m.Sulfur} sulfur)"));
                    text.Append("- ").Append(target.DisplayName).Append(" (").Append((int)target.StartHealth).Append(" HP): ")
                        .Append(mixStr).Append('\n');
                }
            }

            return text.ToString();
        }

        /// <summary>Column headings short enough that the table stays one line per row.</summary>
        private static string Short(RaidSource source) => source.DisplayName switch
        {
            "Timed Explosive Charge" => "C4",
            "Satchel Charge" => "Satchel",
            "Beancan Grenade" => "Beancan",
            "Explosive 5.56 Rifle Ammo" => "Explo ammo",
            "High Velocity Rocket" => "HV rocket",
            "Incendiary Rocket" => "Incend rocket",
            "40mm HE Grenade" => "40mm HE",
            _ => source.DisplayName,
        };
    }
}
