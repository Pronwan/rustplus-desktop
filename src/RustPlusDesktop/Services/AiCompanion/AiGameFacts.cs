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
                "Raid costs and explosive crafting recipes for this build of Rust (from raid-data.json).\n" +
                "Building Tier Aliases: Top tier / HQM / High Quality Metal = 'Armored' (2000 HP). Metal / Sheet Metal = 'Metal' (1000 HP).\n\n" +
                "Boom & Explosives Base Craft Costs (per item, broken down into raw resources):\n");

            foreach (var srcName in Sources)
            {
                var src = data.Sources.FirstOrDefault(s => s.DisplayName.Equals(srcName, StringComparison.OrdinalIgnoreCase));
                if (src == null) continue;

                var wb = src.WorkbenchLevelRequired.HasValue ? $" [T{src.WorkbenchLevelRequired.Value}]" : "";
                if (src.CraftCost != null && src.CraftCost.Count > 0)
                {
                    var costs = string.Join(", ", src.CraftCost.Select(c => $"{c.Amount} {c.DisplayName}"));
                    var note = src.ItemShortname == "ammo.rifle.explosive" ? " (per 1 bullet; crafts in batches of 2 with 10 Metal Frags, 20 Gunpowder, 10 Sulfur)" : "";
                    text.Append($"- {src.DisplayName}{wb}{note}: {costs}\n");
                }
                else
                {
                    text.Append($"- {src.DisplayName}: Uncraftable (loot only)\n");
                }
            }

            text.Append("\nSingle Explosive Counts (to destroy from 100% HP using only one weapon):\n");

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
