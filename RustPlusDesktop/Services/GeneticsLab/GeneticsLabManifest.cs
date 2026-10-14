using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RustPlusDesk.Services.GeneticsLab
{
    /// <summary>
    /// The file list Genetics Lab publishes beside its build as <c>genetics-lab-manifest.json</c>
    /// (written by <c>Features/GeneticsLab/scripts/generate-update-manifest.mjs</c>).
    /// </summary>
    /// <remarks>
    /// The manifest comes off the network and its paths become file names on disk, so nothing in
    /// it is trusted until <see cref="TryParse"/> has checked it: every path must stay inside the
    /// install folder and every hash must be a SHA-256 the downloader can verify.
    /// </remarks>
    public sealed class GeneticsLabManifest
    {
        public const string FileName = "genetics-lab-manifest.json";
        public const int SupportedSchema = 1;

        // A build is ~42 MB today, almost all of it OCR data. The caps leave room to grow while
        // refusing a manifest that would fill the disk.
        public const long MaxFileSize = 200L * 1024 * 1024;
        public const long MaxTotalSize = 600L * 1024 * 1024;
        public const int MaxFileCount = 5000;

        private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        private static readonly Regex GitRefPattern = new("^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant);

        [JsonPropertyName("schema")] public int Schema { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("buildId")] public string BuildId { get; set; } = "";
        [JsonPropertyName("minDesktopVersion")] public string? MinDesktopVersion { get; set; }
        /// <summary>Git ref on GitHub holding exactly these files (set on GitHub releases only).</summary>
        [JsonPropertyName("distRef")] public string? DistRef { get; set; }
        [JsonPropertyName("files")] public List<GeneticsLabManifestFile> Files { get; set; } = new();
        /// <summary>Recent release notes, passed through to the page untouched.</summary>
        [JsonPropertyName("changelog")] public JsonElement? Changelog { get; set; }

        [JsonIgnore] public Version? ParsedVersion => ParseVersion(Version);

        public static GeneticsLabManifest? TryParse(string json, out string? error)
        {
            GeneticsLabManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<GeneticsLabManifest>(json);
            }
            catch (JsonException ex)
            {
                error = $"invalid JSON: {ex.Message}";
                return null;
            }

            if (manifest == null)
            {
                error = "empty manifest";
                return null;
            }

            error = manifest.Validate();
            return error == null ? manifest : null;
        }

        private string? Validate()
        {
            if (Schema != SupportedSchema) return $"unsupported schema {Schema}";
            if (!IsSha256(BuildId)) return "missing or malformed buildId";
            if (ParsedVersion == null) return $"malformed version '{Version}'";
            if (MinDesktopVersion != null && ParseVersion(MinDesktopVersion) == null)
                return $"malformed minDesktopVersion '{MinDesktopVersion}'";
            if (DistRef != null && (!GitRefPattern.IsMatch(DistRef) || DistRef.Contains("..")))
                return $"malformed distRef '{DistRef}'";
            if (Files.Count == 0 || Files.Count > MaxFileCount) return $"unexpected file count {Files.Count}";

            // Windows paths are case-insensitive, so two entries differing only in case would
            // write over each other.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (GeneticsLabManifestFile file in Files)
            {
                if (file == null) return "null file entry";
                if (!IsSafeRelativePath(file.Path)) return $"unsafe path '{file.Path}'";
                if (!seen.Add(file.Path)) return $"duplicate path '{file.Path}'";
                if (!IsSha256(file.Sha256)) return $"malformed sha256 for '{file.Path}'";
                if (file.Size < 0 || file.Size > MaxFileSize) return $"size out of range for '{file.Path}'";
                total += file.Size;
            }

            if (total > MaxTotalSize) return $"total size {total} exceeds limit";
            if (!seen.Contains("index.html")) return "index.html missing";
            return null;
        }

        public static bool IsSha256(string? value) => value != null && Sha256Pattern.IsMatch(value);

        /// <summary>
        /// True for a forward-slash relative path that cannot leave the folder it is joined to
        /// and that Windows will store under exactly that name.
        /// </summary>
        public static bool IsSafeRelativePath(string? path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 200) return false;
            if (path.Contains('\\') || path.Contains(':') || path.StartsWith('/')) return false;

            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..") return false;
                if (segment.IndexOfAny(invalid) >= 0 || segment.Any(char.IsControl)) return false;
                // Windows silently drops trailing dots and spaces, so "a." and "a" are one file.
                if (segment.EndsWith('.') || segment.EndsWith(' ')) return false;
                if (IsReservedDeviceName(segment)) return false;
            }

            return true;
        }

        private static bool IsReservedDeviceName(string segment)
        {
            string stem = segment.Split('.')[0].ToUpperInvariant();
            return stem is "CON" or "PRN" or "AUX" or "NUL"
                || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]));
        }

        /// <summary>Parses "1.7.0", "v1.7.0" or "1.7.0-beta.1" into a comparable version.</summary>
        public static Version? ParseVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            string core = value.Trim().TrimStart('v', 'V');
            int cut = core.IndexOfAny(new[] { '-', '+' });
            if (cut >= 0) core = core[..cut];

            if (!System.Version.TryParse(core, out Version? parsed)) return null;
            return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        }
    }

    public sealed class GeneticsLabManifestFile
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
