using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services.ModelPacks
{
    /// <summary>
    /// Moves this install's models into the <see cref="ModelStore"/> in the background at start-up.
    /// </summary>
    /// <remarks>
    /// The store is the viewer folder the 3D map already serves from, in roaming app data. That folder
    /// survives updates, and also a re-run of Setup, which replaces all of
    /// <c>%LocalAppData%\RustPlusDesk</c>. Anyone who has opened the 3D map already has most of it.
    /// </remarks>
    public sealed class ModelStoreService
    {
        // Rust_Assets layouts from before the models moved to FP/. No build ships or reads them, but
        // the viewer copy never deleted anything, so they sit in this folder on older installs.
        private static readonly string[] RetiredFolders = { "Monuments", "optimized_monuments", "Nature", "models" };

        public static ModelStoreService Instance { get; } = new();

        private Task _adoption = Task.CompletedTask;

        public static string ViewerRoot => Path.Combine(DataManager.AppDir, "Map3DViewer");
        public static string StoreRoot => Path.Combine(ViewerRoot, "Rust_Assets", "FP");

        /// <summary>The viewer shipped with this install; null when it is missing.</summary>
        public string? ViewerSourceRoot { get; private set; }

        /// <summary>The last start-up pass; null until one has finished.</summary>
        public ModelAdoptionResult? LastResult { get; private set; }

        /// <summary>Starts the pass. <paramref name="viewerSourceRoot"/> is the installed viewer, when there is one.</summary>
        public void StartAdoption(string? viewerSourceRoot)
        {
            ViewerSourceRoot = viewerSourceRoot;
            if (viewerSourceRoot == null) return;
            _adoption = Task.Factory.StartNew(() => Run(viewerSourceRoot), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>Viewers wait for this, so two writers never touch one model file.</summary>
        public Task WaitForAdoptionAsync() => _adoption;

        private void Run(string viewerSourceRoot)
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            try
            {
                string source = Path.Combine(viewerSourceRoot, "Rust_Assets", "FP");
                string manifestPath = Path.Combine(source, ModelManifest.FileName);
                if (!File.Exists(manifestPath)) return;

                ModelManifest? manifest = ModelManifest.TryParse(File.ReadAllText(manifestPath));
                if (manifest == null)
                {
                    Debug.WriteLine("[Models] The installed model manifest is not valid.");
                    return;
                }

                LastResult = new ModelStore(StoreRoot).Adopt(manifest, source);
                Debug.WriteLine($"[Models] {manifest.ContentId}: {LastResult}");
                RemoveRetiredFolders();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Models] Adoption failed: " + ex.Message);
            }
        }

        private static void RemoveRetiredFolders()
        {
            foreach (string name in RetiredFolders)
            {
                string folder = Path.Combine(ViewerRoot, "Rust_Assets", name);
                try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[Models] Could not remove {name}: {ex.Message}");
                }
            }
        }
    }
}
