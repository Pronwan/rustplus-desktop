using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RustPlusDesk.Services.ModelPacks
{
    /// <summary>
    /// Every 3D model the app can use, written by MapParser's <c>scripts/generate-model-manifest.mjs</c>.
    /// </summary>
    /// <remarks>
    /// Paths become file names on disk and a manifest may come off the network, so nothing in it is
    /// trusted until <see cref="TryParse"/> has checked it.
    /// </remarks>
    public sealed class ModelManifest
    {
        public const string FileName = "models-manifest.json";
        public const int SupportedSchema = 1;

        private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        [JsonPropertyName("schema")] public int Schema { get; set; }
        [JsonPropertyName("contentId")] public string ContentId { get; set; } = "";
        [JsonPropertyName("packs")] public Dictionary<string, ModelPack> Packs { get; set; } = new();
        [JsonPropertyName("files")] public Dictionary<string, ModelFile> Files { get; set; } = new();

        public static ModelManifest? TryParse(string json)
        {
            ModelManifest? manifest;
            try { manifest = JsonSerializer.Deserialize<ModelManifest>(json); }
            catch (JsonException) { return null; }

            if (manifest == null || manifest.Schema != SupportedSchema || string.IsNullOrWhiteSpace(manifest.ContentId)) return null;
            bool valid = manifest.Files.All(entry =>
                IsSafePath(entry.Key)
                && entry.Value.Size >= 0
                && Sha256Pattern.IsMatch(entry.Value.Sha256)
                && manifest.Packs.ContainsKey(entry.Value.Pack));
            return valid ? manifest : null;
        }

        /// <summary>True for a relative path that stays inside the model folder's <c>assets</c> tree.</summary>
        public static bool IsSafePath(string path)
        {
            if (!path.StartsWith("assets/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':')) return false;
            return path.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
        }
    }

    public sealed class ModelPack
    {
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("files")] public int Files { get; set; }
        [JsonPropertyName("bytes")] public long Bytes { get; set; }
    }

    public sealed class ModelFile
    {
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("pack")] public string Pack { get; set; } = "";
    }
}
