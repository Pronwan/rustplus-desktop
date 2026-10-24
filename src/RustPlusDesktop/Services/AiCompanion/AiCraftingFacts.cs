using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustPlusDesk.Services.AiCompanion
{
    /// <summary>
    /// Every crafting recipe in the game, as a table the model can read.
    ///
    /// The case that forced this is Workbench Level 3: ask a model and it answers 1250 scrap,
    /// which was true until the blueprint-fragment update and is now three ingredients wrong.
    /// That is the shape of the whole problem — Rust changes recipes every few months, a model's
    /// training data has every past version of them, and it has no way to know which one it is
    /// reciting. The shipped data does.
    /// </summary>
    public static class AiCraftingFacts
    {
        private static string? _cached;

        /// <summary>The recipe table as compact text, or empty when the data cannot be read.</summary>
        public static string Recipes()
        {
            if (_cached != null) return _cached;

            try
            {
                _cached = Render(
                    Load<Dictionary<string, Recipe>>("Crafting-Data.json"),
                    Load<List<ItemName>>("rust-item-list.json"));
            }
            catch
            {
                // Never worth failing a question over. Without it the model answers from memory,
                // which is where it was before.
                _cached = "";
            }

            return _cached;
        }

        private sealed class Recipe
        {
            [JsonPropertyName("amountToCreate")] public int AmountToCreate { get; set; } = 1;
            [JsonPropertyName("workbenchLevelRequired")] public int WorkbenchLevelRequired { get; set; }
            [JsonPropertyName("ingredients")] public List<Ingredient> Ingredients { get; set; } = new();
        }

        private sealed class Ingredient
        {
            [JsonPropertyName("shortname")] public string Shortname { get; set; } = "";
            [JsonPropertyName("quantity")] public double Quantity { get; set; }
        }

        private sealed class ItemName
        {
            [JsonPropertyName("shortName")] public string ShortName { get; set; } = "";
            [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
        }

        private static T Load<T>(string fileName)
        {
            using var stream = Open(fileName);
            return JsonSerializer.Deserialize<T>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidDataException($"{fileName} is empty.");
        }

        /// <summary>
        /// The loose file beside the exe first, then the copy compiled into the assembly — the
        /// same order the raid data uses, so a trimmed or single-file build still works.
        /// </summary>
        private static Stream Open(string fileName)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Data", fileName);
            if (File.Exists(path)) return File.OpenRead(path);

            var assembly = typeof(AiCraftingFacts).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("Assets.Data." + fileName, StringComparison.OrdinalIgnoreCase));

            return resource != null
                ? assembly.GetManifestResourceStream(resource)!
                : throw new FileNotFoundException("Packaged asset missing.", path);
        }

        private static string Render(Dictionary<string, Recipe> recipes, List<ItemName> items)
        {
            // Shortname to display name. The data keys and every ingredient are shortnames, and
            // nobody asks how to craft a "wall.frame.shopfront".
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.ShortName)) continue;

                // Some display names are still localisation placeholders like "#50cal". The
                // shortname is worse to read but at least it is a real name.
                var display = item.DisplayName;
                names[item.ShortName] = string.IsNullOrEmpty(display) || display.StartsWith("#", StringComparison.Ordinal)
                    ? item.ShortName
                    : display;
            }

            string Name(string shortname) =>
                names.TryGetValue(shortname, out var display) ? display : shortname;

            var text = new StringBuilder();

            text.Append(
                "Crafting recipes for this build of Rust, taken from the game's own data. [T2] " +
                "means it needs a level 2 workbench. Use these rather than any recipe you " +
                "remember — Rust changes them regularly, and an answer from memory is as likely " +
                "to be a retired version as the current one. If an item is not listed here, say " +
                "you are not certain of its current recipe instead of reciting one.\n\n");

            foreach (var (shortname, recipe) in recipes.OrderBy(entry => Name(entry.Key), StringComparer.OrdinalIgnoreCase))
            {
                if (recipe.Ingredients.Count == 0) continue;

                text.Append(Name(shortname));

                if (recipe.WorkbenchLevelRequired > 0)
                    text.Append(" [T").Append(recipe.WorkbenchLevelRequired).Append(']');

                if (recipe.AmountToCreate > 1)
                    text.Append(" (makes ").Append(recipe.AmountToCreate).Append(')');

                text.Append(": ").Append(string.Join(", ", recipe.Ingredients
                    .Select(i => $"{i.Quantity:0.##}x {Name(i.Shortname)}")));

                text.Append('\n');
            }

            return text.ToString();
        }
    }
}
