using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace RustPlusDesk.Services.ModelPacks
{
    /// <summary>What one pass over a manifest found.</summary>
    /// <param name="Adopted">Copied into the store from the installed app.</param>
    /// <param name="AlreadyHere">Already in the store with the right hash.</param>
    /// <param name="Unavailable">Not in the store, and the app has no good copy either.</param>
    /// <param name="Deferred">Could not be written now, usually because a viewer has the file open.</param>
    public sealed record ModelAdoptionResult(int Adopted, int AlreadyHere, int Unavailable, int Deferred);

    /// <summary>
    /// The models on this machine, kept outside the app's install folder.
    /// </summary>
    /// <remarks>
    /// A Velopack update replaces the whole <c>current</c> folder, and a package without models
    /// would take them with it. Files here are unaffected, so copying them across before such a
    /// package ships is what lets existing users keep their models without downloading them again.
    ///
    /// A file only counts as present once its SHA-256 matches. The result of that check is kept in
    /// an index against the file's size and timestamp, so later starts cost a metadata scan.
    /// </remarks>
    public sealed class ModelStore
    {
        private const string IndexFileName = ".model-index.json";
        private const string StagingFolderName = ".staging";

        private readonly string _root;
        private readonly string _indexPath;
        private readonly Dictionary<string, IndexEntry> _index;

        private sealed record IndexEntry(long Length, long Ticks, string Sha256);

        public ModelStore(string root)
        {
            _root = root;
            _indexPath = Path.Combine(root, IndexFileName);
            _index = LoadIndex(_indexPath);
        }

        /// <summary>True when this exact version of the file is in the store.</summary>
        public bool Has(string relativePath, ModelFile file)
        {
            var info = new FileInfo(FullPath(_root, relativePath));
            if (!info.Exists || info.Length != file.Size) return false;

            long ticks = info.LastWriteTimeUtc.Ticks;
            if (_index.TryGetValue(relativePath, out IndexEntry? known) && known.Length == info.Length && known.Ticks == ticks)
                return known.Sha256 == file.Sha256;

            string sha256 = HashFile(info.FullName);
            _index[relativePath] = new IndexEntry(info.Length, ticks, sha256);
            return sha256 == file.Sha256;
        }

        /// <summary>
        /// Copies every model the store lacks from <paramref name="sourceRoot"/>, taking only files
        /// that match the manifest. A store file with no good replacement stays where it is: an older
        /// model is better than none.
        /// </summary>
        public ModelAdoptionResult Adopt(ModelManifest manifest, string sourceRoot, CancellationToken cancellationToken = default)
        {
            int adopted = 0, alreadyHere = 0, unavailable = 0, deferred = 0;
            string staging = Path.Combine(_root, StagingFolderName);
            Directory.CreateDirectory(staging);
            try
            {
                foreach ((string relativePath, ModelFile file) in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (Has(relativePath, file)) alreadyHere++;
                        else if (!IsGoodCopy(FullPath(sourceRoot, relativePath), file)) unavailable++;
                        else
                        {
                            Place(FullPath(sourceRoot, relativePath), relativePath, file, staging);
                            adopted++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        deferred++;
                    }
                }
            }
            finally
            {
                TryDeleteFolder(staging);
                SaveIndex();
            }
            return new ModelAdoptionResult(adopted, alreadyHere, unavailable, deferred);
        }

        /// <summary>The temporary folder for files on their way in. Staged files are named by hash.</summary>
        public string StagingFolder => Path.Combine(_root, StagingFolderName);

        /// <summary>The pack's files this machine lacks, or holds in another version.</summary>
        public List<KeyValuePair<string, ModelFile>> Missing(ModelManifest manifest, string pack) =>
            manifest.Files.Where(entry => entry.Value.Pack == pack && !Has(entry.Key, entry.Value)).ToList();

        /// <summary>
        /// True once any of the pack's files is here. Packs land whole and leave whole, so this is what
        /// "installed" means, and a machine with an older partial copy counts without any stored state.
        /// </summary>
        public bool HasAny(ModelManifest manifest, string pack) =>
            manifest.Files.Any(entry => entry.Value.Pack == pack && File.Exists(FullPath(_root, entry.Key)));

        /// <summary>Moves a verified staged file into place. True when it replaced an older version.</summary>
        public bool Accept(string staged, string relativePath, ModelFile file)
        {
            string target = FullPath(_root, relativePath);
            bool replaced = File.Exists(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(staged, target, overwrite: true);

            var info = new FileInfo(target);
            _index[relativePath] = new IndexEntry(info.Length, info.LastWriteTimeUtc.Ticks, file.Sha256);
            return replaced;
        }

        /// <summary>Deletes models the manifest no longer lists. Returns how many went.</summary>
        public int RemoveUnlisted(ModelManifest manifest)
        {
            string assets = Path.Combine(_root, "assets");
            if (!Directory.Exists(assets)) return 0;

            int removed = 0;
            foreach (string file in Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories).ToList())
            {
                string relativePath = Path.GetRelativePath(_root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (manifest.Files.ContainsKey(relativePath)) continue;
                try
                {
                    File.Delete(file);
                    _index.Remove(relativePath);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use: the next pass gets it.
                }
            }
            return removed;
        }

        public void Save() => SaveIndex();

        private void Place(string source, string relativePath, ModelFile file, string staging)
        {
            // Staged under the hash, so a long model path never gets longer, and a target only ever
            // changes in one step: a viewer or a crash cannot meet a half-written model.
            string staged = Path.Combine(staging, file.Sha256);
            File.Copy(source, staged, overwrite: true);
            Accept(staged, relativePath, file);
        }

        private static bool IsGoodCopy(string path, ModelFile file)
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == file.Size && HashFile(path) == file.Sha256;
        }

        private static string FullPath(string root, string relativePath) =>
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string HashFile(string path)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static Dictionary<string, IndexEntry> LoadIndex(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<Dictionary<string, IndexEntry>>(File.ReadAllText(path)) ?? new();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Only a shortcut: without it every file is hashed once more.
            }
            return new();
        }

        private void SaveIndex()
        {
            try
            {
                Directory.CreateDirectory(_root);
                string temporary = _indexPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_index));
                File.Move(temporary, _indexPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The next start rebuilds it.
            }
        }

        private static void TryDeleteFolder(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
