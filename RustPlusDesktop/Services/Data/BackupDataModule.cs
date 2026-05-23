using System;
using System.IO;
using System.IO.Compression;

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
            string tempDir = Path.Combine(DataManager.AppDir, "temp_backup_staging");
            string tempZip = Path.Combine(DataManager.AppDir, "temp_backup_archive.zip");
            
            try
            {
                // 1. Clean previous staging just in case
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);

                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }

                // 2. Copy profiles.json
                if (File.Exists(DataManager.ProfilesPath))
                {
                    File.Copy(DataManager.ProfilesPath, Path.Combine(tempDir, "profiles.json"), true);
                }

                // 2b. Copy rustplusjs-config.json
                string fcmConfigPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RustPlusDesk", "rustplusjs-config.json");
                if (File.Exists(fcmConfigPath))
                {
                    File.Copy(fcmConfigPath, Path.Combine(tempDir, "rustplusjs-config.json"), true);
                }

                // 3. Copy custom_crosshairs.json
                string crosshairPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RustPlusDesk", "custom_crosshairs.json");
                if (File.Exists(crosshairPath))
                {
                    File.Copy(crosshairPath, Path.Combine(tempDir, "custom_crosshairs.json"), true);
                }

                // 4. Copy minimap_settings.json
                string minimapPath = Path.Combine(DataManager.CacheDir, "minimap_settings.json");
                if (File.Exists(minimapPath))
                {
                    File.Copy(minimapPath, Path.Combine(tempDir, "minimap_settings.json"), true);
                }

                // 5. Copy Overlays folder
                string overlaysSource = Path.Combine(DataManager.AppDir, "Overlays");
                if (Directory.Exists(overlaysSource))
                {
                    CopyDirectory(overlaysSource, Path.Combine(tempDir, "Overlays"));
                }

                // 6. Zip everything to a temp zip file first
                ZipFile.CreateFromDirectory(tempDir, tempZip);

                // 7. If password is provided, encrypt; else copy standard zip directly
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

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
                // 8. Cleanup staging folders and temp zip
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }
            }
        }

        public static void RestoreBackup(string zipPath, string password)
        {
            string tempDir = Path.Combine(DataManager.AppDir, "temp_restore_staging");
            string tempZip = Path.Combine(DataManager.AppDir, "temp_restore_archive.zip");

            try
            {
                // 1. Clean staging just in case
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }

                // 2. Decrypt zip first if encrypted; otherwise copy directly
                if (IsBackupEncrypted(zipPath))
                {
                    DecryptFile(zipPath, tempZip, password);
                }
                else
                {
                    File.Copy(zipPath, tempZip, true);
                }

                // 3. Extract standard zip to staging folder
                ZipFile.ExtractToDirectory(tempZip, tempDir);

                // 4. Restore profiles.json
                string stagingProfiles = Path.Combine(tempDir, "profiles.json");
                if (File.Exists(stagingProfiles))
                {
                    Directory.CreateDirectory(DataManager.AppDir);
                    File.Copy(stagingProfiles, DataManager.ProfilesPath, true);
                }

                // 4b. Restore rustplusjs-config.json
                string stagingFcm = Path.Combine(tempDir, "rustplusjs-config.json");
                if (File.Exists(stagingFcm))
                {
                    string targetFcm = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "RustPlusDesk", "rustplusjs-config.json");
                    string targetDir = Path.GetDirectoryName(targetFcm);
                    if (targetDir != null)
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    File.Copy(stagingFcm, targetFcm, true);
                }

                // 5. Restore custom_crosshairs.json
                string stagingCrosshairs = Path.Combine(tempDir, "custom_crosshairs.json");
                if (File.Exists(stagingCrosshairs))
                {
                    string targetCrosshairs = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "RustPlusDesk", "custom_crosshairs.json");
                    string targetDir = Path.GetDirectoryName(targetCrosshairs);
                    if (targetDir != null)
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    File.Copy(stagingCrosshairs, targetCrosshairs, true);
                }

                // 6. Restore minimap_settings.json
                string stagingMinimap = Path.Combine(tempDir, "minimap_settings.json");
                if (File.Exists(stagingMinimap))
                {
                    Directory.CreateDirectory(DataManager.CacheDir);
                    File.Copy(stagingMinimap, Path.Combine(DataManager.CacheDir, "minimap_settings.json"), true);
                }

                // 7. Restore Overlays folder
                string stagingOverlays = Path.Combine(tempDir, "Overlays");
                if (Directory.Exists(stagingOverlays))
                {
                    string targetOverlays = Path.Combine(DataManager.AppDir, "Overlays");
                    if (Directory.Exists(targetOverlays))
                    {
                        Directory.Delete(targetOverlays, true);
                    }
                    CopyDirectory(stagingOverlays, targetOverlays);
                }
            }
            finally
            {
                // 8. Cleanup staging
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }
            }
        }

        private static void EncryptFile(string srcPath, string destPath, string password)
        {
            // 1. Generate salt and IV
            byte[] salt = new byte[16];
            byte[] iv = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
                rng.GetBytes(iv);
            }

            // 2. Derive key from password using PBKDF2
            byte[] key;
            using (var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 10000, System.Security.Cryptography.HashAlgorithmName.SHA256))
            {
                key = pbkdf2.GetBytes(32); // AES-256 key
            }

            // 3. Perform AES encryption
            using (var fsOut = new FileStream(destPath, FileMode.Create, FileAccess.Write))
            {
                // Write magic header signature
                fsOut.Write(EncSig, 0, EncSig.Length);
                // Write salt
                fsOut.Write(salt, 0, salt.Length);
                // Write IV
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
                // 1. Verify header
                byte[] header = new byte[EncSig.Length];
                fsIn.Read(header, 0, header.Length);
                for (int i = 0; i < EncSig.Length; i++)
                {
                    if (header[i] != EncSig[i])
                        throw new InvalidOperationException(Properties.Resources.InvalidBackupFormat);
                }

                // 2. Read Salt
                byte[] salt = new byte[16];
                fsIn.Read(salt, 0, salt.Length);

                // 3. Read IV
                byte[] iv = new byte[16];
                fsIn.Read(iv, 0, iv.Length);

                // 4. Derive key using PBKDF2
                byte[] key;
                using (var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(password, salt, 10000, System.Security.Cryptography.HashAlgorithmName.SHA256))
                {
                    key = pbkdf2.GetBytes(32);
                }

                // 5. Perform AES decryption
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
