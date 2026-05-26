using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using RustPlusDesk.Models;
using RustPlusDesk.Views;

namespace RustPlusDesk.Services.Data
{
    public static class BackupDataModule
    {
        private static readonly byte[] EncSig = System.Text.Encoding.UTF8.GetBytes("RUST+DESK_ENC");

        public static bool IsBackupEncrypted(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < EncSig.Length) return false;
                    byte[] header = new byte[EncSig.Length];
                    fs.Read(header, 0, header.Length);
                    for (int i = 0; i < EncSig.Length; i++)
                    {
                        if (header[i] != EncSig[i]) return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void CreateBackup(string zipPath, string password)
        {
            CreateBackup(zipPath, password, null);
        }

        public static void CreateBackup(string zipPath, string password, List<string>? profileIdsToBackup)
        {
            string tempDir = Path.Combine(DataManager.AppDir, "temp_backup_staging");
            string tempZip = Path.Combine(DataManager.AppDir, "temp_backup_archive.zip");

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                if (File.Exists(tempZip))
                    File.Delete(tempZip);

                var profilesPath = ProfileManager.ProfilesRootPath;
                var profilesIndexPath = Path.Combine(profilesPath, "profiles.json");
                ProfileList? profileList = null;

                if (File.Exists(profilesIndexPath))
                {
                    var json = File.ReadAllText(profilesIndexPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            profileList = JsonSerializer.Deserialize<ProfileList>(json);
                        }
                        catch
                        {
                            profileList = null;
                        }
                    }
                }

                if (profileList != null && profileIdsToBackup != null)
                {
                    profileList.Profiles = profileList.Profiles
                        .Where(p => profileIdsToBackup.Contains(p.Id))
                        .ToList();
                }

                if (profileList != null && profileList.Profiles.Count > 0)
                {
                    var filteredJson = JsonSerializer.Serialize(profileList, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(tempDir, "profiles.json"), filteredJson);

                    foreach (var profile in profileList.Profiles)
                    {
                        var profileFolder = profile.FolderPath;
                        if (Directory.Exists(profileFolder))
                        {
                            var destFolder = Path.Combine(tempDir, "profiles", profile.Id);
                            CopyDirectory(profileFolder, destFolder);
                        }
                    }
                }

                string minimapPath = Path.Combine(DataManager.CacheDir, "minimap_settings.json");
                if (File.Exists(minimapPath))
                {
                    File.Copy(minimapPath, Path.Combine(tempDir, "minimap_settings.json"), true);
                }

                string crosshairPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RustPlusDesk", "custom_crosshairs.json");
                if (File.Exists(crosshairPath))
                {
                    File.Copy(crosshairPath, Path.Combine(tempDir, "custom_crosshairs.json"), true);
                }

                ZipFile.CreateFromDirectory(tempDir, tempZip);

                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                if (!string.IsNullOrEmpty(password))
                {
                    EncryptFile(tempZip, zipPath, password);
                }
                else
                {
                    File.Copy(tempZip, zipPath, true);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
        }

        public static void RestoreBackup(string zipPath, string password)
        {
            RestoreBackup(zipPath, password, null, RestoreMode.Append);
        }

        public static void RestoreBackup(string zipPath, string password, List<string>? profileIdsToRestore, RestoreMode mode)
        {
            string tempDir = Path.Combine(DataManager.AppDir, "temp_restore_staging");
            string tempZip = Path.Combine(DataManager.AppDir, "temp_restore_archive.zip");

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                if (File.Exists(tempZip))
                    File.Delete(tempZip);

                if (IsBackupEncrypted(zipPath))
                    DecryptFile(zipPath, tempZip, password);
                else
                    File.Copy(zipPath, tempZip, true);

                ZipFile.ExtractToDirectory(tempZip, tempDir);

                string stagingProfiles = Path.Combine(tempDir, "profiles.json");
                ProfileList? backupProfileList = null;
                if (File.Exists(stagingProfiles))
                {
                    var json = File.ReadAllText(stagingProfiles);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            backupProfileList = JsonSerializer.Deserialize<ProfileList>(json);
                        }
                        catch
                        {
                            backupProfileList = null;
                        }
                    }
                }

                var profilesDir = ProfileManager.ProfilesRootPath;
                Directory.CreateDirectory(profilesDir);

                if (mode == RestoreMode.Replace && backupProfileList != null)
                {
                    foreach (var p in backupProfileList.Profiles)
                    {
                        var folder = Path.Combine(profilesDir, p.Id);
                        if (Directory.Exists(folder))
                            Directory.Delete(folder, true);
                    }
                    if (File.Exists(Path.Combine(ProfileManager.ProfilesRootPath, "profiles.json")))
                    {
                        var existingJson = File.ReadAllText(Path.Combine(ProfileManager.ProfilesRootPath, "profiles.json"));
                        var existingList = JsonSerializer.Deserialize<ProfileList>(existingJson);
                        if (existingList != null)
                        {
                            foreach (var p in existingList.Profiles.ToList())
                            {
                                if (backupProfileList.Profiles.All(bp => bp.Id != p.Id))
                                    existingList.Profiles.Remove(p);
                            }
                            File.WriteAllText(Path.Combine(ProfileManager.ProfilesRootPath, "profiles.json"), JsonSerializer.Serialize(existingList, new JsonSerializerOptions { WriteIndented = true }));
                        }
                    }
                }

                if (backupProfileList != null)
                {
                    var idsToRestore = profileIdsToRestore ?? backupProfileList.Profiles.Select(p => p.Id).ToList();
                    var profilesToRestore = backupProfileList.Profiles.Where(p => idsToRestore.Contains(p.Id)).ToList();

                    var stagingProfilesDir = Path.Combine(tempDir, "profiles");

                    ProfileList targetList;
                    if (File.Exists(Path.Combine(ProfileManager.ProfilesRootPath, "profiles.json")))
                    {
                        var existingJson = File.ReadAllText(Path.Combine(ProfileManager.ProfilesRootPath, "profiles.json"));
                        targetList = JsonSerializer.Deserialize<ProfileList>(existingJson) ?? new ProfileList();
                    }
                    else
                    {
                        targetList = new ProfileList();
                    }

                    foreach (var profile in profilesToRestore)
                    {
                        if (mode == RestoreMode.Append && targetList.Profiles.Any(p => p.Id == profile.Id))
                            continue;

                        var stagingFolder = Path.Combine(stagingProfilesDir, profile.Id);
                        if (Directory.Exists(stagingFolder))
                        {
                            var destFolder = Path.Combine(profilesDir, profile.Id);
                            if (Directory.Exists(destFolder) && mode == RestoreMode.Append)
                            {
                            }
                            else
                            {
                                if (Directory.Exists(destFolder))
                                    Directory.Delete(destFolder, true);
                                CopyDirectory(stagingFolder, destFolder);
                            }
                        }

                        if (!targetList.Profiles.Any(p => p.Id == profile.Id))
                            targetList.Profiles.Add(profile);
                    }

                    File.WriteAllText(Path.Combine(ProfileManager.ProfilesRootPath, "profiles.json"), JsonSerializer.Serialize(targetList, new JsonSerializerOptions { WriteIndented = true }));
                }

                string stagingMinimap = Path.Combine(tempDir, "minimap_settings.json");
                if (File.Exists(stagingMinimap))
                {
                    Directory.CreateDirectory(DataManager.CacheDir);
                    File.Copy(stagingMinimap, Path.Combine(DataManager.CacheDir, "minimap_settings.json"), true);
                }

                string stagingCrosshairs = Path.Combine(tempDir, "custom_crosshairs.json");
                if (File.Exists(stagingCrosshairs))
                {
                    string targetCrosshairs = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RustPlusDesk", "custom_crosshairs.json");
                    string targetDir = Path.GetDirectoryName(targetCrosshairs)!;
                    Directory.CreateDirectory(targetDir);
                    File.Copy(stagingCrosshairs, targetCrosshairs, true);
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
        }

        public static List<Profile> GetProfilesFromBackup(string zipPath, string password)
        {
            string tempDir = Path.Combine(DataManager.AppDir, "temp_backup_inspect");
            string tempZip = Path.Combine(DataManager.AppDir, "temp_backup_inspect.zip");

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                if (File.Exists(tempZip))
                    File.Delete(tempZip);

                if (IsBackupEncrypted(zipPath))
                    DecryptFile(zipPath, tempZip, password);
                else
                    File.Copy(zipPath, tempZip, true);

                ZipFile.ExtractToDirectory(tempZip, tempDir);

                string stagingProfiles = Path.Combine(tempDir, "profiles.json");
                if (File.Exists(stagingProfiles))
                {
                    var json = File.ReadAllText(stagingProfiles);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            var profileList = JsonSerializer.Deserialize<ProfileList>(json);
                            return profileList?.Profiles ?? new List<Profile>();
                        }
                        catch
                        {
                        }
                    }
                }

                return new List<Profile>();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }
        }

        private static void EncryptFile(string srcPath, string destPath, string password)
        {
            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
                rng.GetBytes(iv);
            }

            byte[] key;
            using (var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 10000, System.Security.Cryptography.HashAlgorithmName.SHA256))
            {
                key = pbkdf2.GetBytes(32);
            }

            using (var fsOut = new FileStream(destPath, FileMode.Create, FileAccess.Write))
            {
                fsOut.Write(EncSig, 0, EncSig.Length);
                fsOut.Write(salt, 0, salt.Length);
                fsOut.Write(iv, 0, iv.Length);

                using (var aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = System.Security.Cryptography.CipherMode.CBC;

                    using (var cryptoStream = new System.Security.Cryptography.CryptoStream(fsOut, aes.CreateEncryptor(), System.Security.Cryptography.CryptoStreamMode.Write))
                    {
                        using (var fsIn = new FileStream(srcPath, FileMode.Open, FileAccess.Read))
                        {
                            byte[] buffer = new byte[4096];
                            int read;
                            while ((read = fsIn.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cryptoStream.Write(buffer, 0, read);
                            }
                        }
                    }
                }
            }
        }

        private static void DecryptFile(string srcPath, string destPath, string password)
        {
            using (var fsIn = new FileStream(srcPath, FileMode.Open, FileAccess.Read))
            {
                byte[] header = new byte[EncSig.Length];
                fsIn.Read(header, 0, header.Length);
                for (int i = 0; i < EncSig.Length; i++)
                {
                    if (header[i] != EncSig[i])
                        throw new InvalidOperationException(Properties.Resources.InvalidBackupFormat);
                }

                byte[] salt = new byte[16];
                fsIn.Read(salt, 0, salt.Length);

                byte[] iv = new byte[16];
                fsIn.Read(iv, 0, iv.Length);

                byte[] key;
                using (var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 10000, System.Security.Cryptography.HashAlgorithmName.SHA256))
                {
                    key = pbkdf2.GetBytes(32);
                }

                using (var aes = System.Security.Cryptography.Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    aes.Mode = System.Security.Cryptography.CipherMode.CBC;

                    using (var fsOut = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                    {
                        using (var cryptoStream = new System.Security.Cryptography.CryptoStream(fsIn, aes.CreateDecryptor(), System.Security.Cryptography.CryptoStreamMode.Read))
                        {
                            byte[] buffer = new byte[4096];
                            int read;
                            while ((read = cryptoStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                fsOut.Write(buffer, 0, read);
                            }
                        }
                    }
                }
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string dest = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, dest);
            }
        }
    }
}
