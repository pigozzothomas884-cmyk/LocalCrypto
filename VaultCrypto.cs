using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LocalCrypto
{
    public class VaultMetadata
    {
        public string VaultName { get; set; } = "Disco Cifrato";
        public string SaltPasswordB64 { get; set; } = "";
        public string WrappedKeyPasswordB64 { get; set; } = "";
        public string SaltRecoveryB64 { get; set; } = "";
        public string WrappedKeyRecoveryB64 { get; set; } = "";
    }

    public static class VaultCrypto
    {
        private const int Iterations = 600_000;
        public const string MetaFileName = ".localcrypto";
        public const string LockTriggerFileName = "!Blocca Disco.cmd";

        public static bool IsVault(string folderPath) =>
            Directory.Exists(folderPath) && File.Exists(Path.Combine(folderPath, MetaFileName));

        public static string GetVaultName(string folderPath)
        {
            try
            {
                string metaPath = Path.Combine(folderPath, MetaFileName);
                var meta = JsonSerializer.Deserialize<VaultMetadata>(File.ReadAllText(metaPath));
                return string.IsNullOrWhiteSpace(meta?.VaultName) ? Path.GetFileName(folderPath) : meta.VaultName;
            }
            catch
            {
                return Path.GetFileName(folderPath);
            }
        }

        public static string GenerateRecoveryKey()
        {
            byte[] raw = RandomNumberGenerator.GetBytes(24);
            string hex = Convert.ToHexString(raw);
            var sb = new StringBuilder();
            for (int i = 0; i < hex.Length; i += 6)
            {
                if (i > 0) sb.Append('-');
                sb.Append(hex.Substring(i, 6));
            }
            return sb.ToString();
        }

        private static byte[] DeriveKey(string secret, byte[] salt) =>
            Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(secret),
                salt,
                Iterations,
                HashAlgorithmName.SHA512,
                32);

        private static byte[] EncryptBytes(byte[] plain, byte[] key)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] cipher = new byte[plain.Length];
            byte[] tag = new byte[16];

            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag);

            byte[] result = new byte[12 + 16 + cipher.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, 12);
            Buffer.BlockCopy(tag, 0, result, 12, 16);
            Buffer.BlockCopy(cipher, 0, result, 28, cipher.Length);
            return result;
        }

        private static byte[] DecryptBytes(byte[] blob, byte[] key)
        {
            if (blob.Length < 28)
                throw new CryptographicException("File cifrato corrotto.");

            byte[] nonce = blob[..12];
            byte[] tag = blob[12..28];
            byte[] cipher = blob[28..];
            byte[] plain = new byte[cipher.Length];

            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }

        private static string ToBase64Url(byte[] data) =>
            Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        private static byte[] FromBase64Url(string b64Url)
        {
            string b64 = b64Url.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
            }
            return Convert.FromBase64String(b64);
        }

        public static string CreateVault(string encryptedFolder, string diskName, string password)
        {
            Directory.CreateDirectory(encryptedFolder);
            byte[] masterKey = RandomNumberGenerator.GetBytes(32);
            string recoveryKey = GenerateRecoveryKey();

            byte[] saltPwd = RandomNumberGenerator.GetBytes(16);
            byte[] saltRec = RandomNumberGenerator.GetBytes(16);

            var meta = new VaultMetadata
            {
                VaultName = string.IsNullOrWhiteSpace(diskName) ? Path.GetFileName(encryptedFolder) : diskName.Trim(),
                SaltPasswordB64 = Convert.ToBase64String(saltPwd),
                WrappedKeyPasswordB64 = Convert.ToBase64String(EncryptBytes(masterKey, DeriveKey(password, saltPwd))),
                SaltRecoveryB64 = Convert.ToBase64String(saltRec),
                WrappedKeyRecoveryB64 = Convert.ToBase64String(EncryptBytes(masterKey, DeriveKey(recoveryKey, saltRec)))
            };

            File.WriteAllText(
                Path.Combine(encryptedFolder, MetaFileName),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

            Array.Clear(masterKey, 0, masterKey.Length);
            return recoveryKey;
        }

        public static byte[] UnlockMasterKey(string encryptedFolder, string secret, bool isRecoveryKey)
        {
            string metaPath = Path.Combine(encryptedFolder, MetaFileName);
            var meta = JsonSerializer.Deserialize<VaultMetadata>(File.ReadAllText(metaPath))
                       ?? throw new InvalidOperationException("Metadati non validi.");

            byte[] salt = Convert.FromBase64String(isRecoveryKey ? meta.SaltRecoveryB64 : meta.SaltPasswordB64);
            byte[] wrapped = Convert.FromBase64String(isRecoveryKey ? meta.WrappedKeyRecoveryB64 : meta.WrappedKeyPasswordB64);

            return DecryptBytes(wrapped, DeriveKey(secret.Trim(), salt));
        }

        public static void ResetPasswordWithRecovery(string encryptedFolder, string recoveryKey, string newPassword)
        {
            byte[] masterKey = UnlockMasterKey(encryptedFolder, recoveryKey, isRecoveryKey: true);
            try
            {
                string metaPath = Path.Combine(encryptedFolder, MetaFileName);
                var meta = JsonSerializer.Deserialize<VaultMetadata>(File.ReadAllText(metaPath))!;
                byte[] newSalt = RandomNumberGenerator.GetBytes(16);
                meta.SaltPasswordB64 = Convert.ToBase64String(newSalt);
                meta.WrappedKeyPasswordB64 = Convert.ToBase64String(EncryptBytes(masterKey, DeriveKey(newPassword, newSalt)));

                File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally
            {
                Array.Clear(masterKey, 0, masterKey.Length);
            }
        }

        public static void EncryptRelativeDirectory(string plainRoot, string sourcePlainDir, string encryptedRoot, byte[] masterKey)
        {
            string relPath = Path.GetRelativePath(plainRoot, sourcePlainDir);
            if (relPath == ".") return;
            string encName = ToBase64Url(EncryptBytes(Encoding.UTF8.GetBytes(relPath), masterKey)) + ".lcd";
            File.WriteAllBytes(Path.Combine(encryptedRoot, encName), Array.Empty<byte>());
        }

        public static void EncryptRelativeFile(string plainRoot, string sourcePlainFile, string encryptedRoot, byte[] masterKey)
        {
            string relPath = Path.GetRelativePath(plainRoot, sourcePlainFile);
            if (relPath.Equals(LockTriggerFileName, StringComparison.OrdinalIgnoreCase))
                return;

            string encName = ToBase64Url(EncryptBytes(Encoding.UTF8.GetBytes(relPath), masterKey)) + ".lc";
            byte[] plainData = File.ReadAllBytes(sourcePlainFile);
            byte[] encData = EncryptBytes(plainData, masterKey);
            File.WriteAllBytes(Path.Combine(encryptedRoot, encName), encData);
        }

        public static void EncryptExistingFolderInPlace(string folderRoot, byte[] masterKey)
        {
            string stagingRoot = Path.Combine(
                Path.GetTempPath(),
                "CiaoDev_LocalCrypto",
                "encrypt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingRoot);

            var sourceDirs = Directory.GetDirectories(folderRoot, "*", SearchOption.AllDirectories).ToArray();
            var sourceFiles = Directory.GetFiles(folderRoot, "*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string name = Path.GetFileName(f);
                    return !name.Equals(MetaFileName, StringComparison.OrdinalIgnoreCase)
                        && !name.EndsWith(".lc", StringComparison.OrdinalIgnoreCase)
                        && !name.EndsWith(".lcd", StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();

            try
            {
                foreach (var dir in sourceDirs)
                    EncryptRelativeDirectory(folderRoot, dir, stagingRoot, masterKey);

                foreach (var file in sourceFiles)
                    EncryptRelativeFile(folderRoot, file, stagingRoot, masterKey);

                foreach (var staged in Directory.GetFiles(stagingRoot))
                {
                    string destination = Path.Combine(folderRoot, Path.GetFileName(staged));
                    File.Move(staged, destination);
                }

                foreach (var file in sourceFiles)
                    File.Delete(file);

                foreach (var dir in Directory.GetDirectories(folderRoot))
                {
                    if (!dir.EndsWith(".lc", StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(dir, recursive: true);
                }
            }
            finally
            {
                try { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true); }
                catch { }
            }
        }

        public static void DecryptAllToFolder(string encryptedRoot, string plainTargetRoot, byte[] masterKey)
        {
            Directory.CreateDirectory(plainTargetRoot);
            string targetRoot = Path.GetFullPath(plainTargetRoot);
            if (!targetRoot.EndsWith(Path.DirectorySeparatorChar))
                targetRoot += Path.DirectorySeparatorChar;

            foreach (var encDir in Directory.GetFiles(encryptedRoot, "*.lcd"))
            {
                string b64Url = Path.GetFileNameWithoutExtension(encDir);
                string relPath = Encoding.UTF8.GetString(DecryptBytes(FromBase64Url(b64Url), masterKey));
                string destPath = GetSafeDestinationPath(targetRoot, relPath);
                Directory.CreateDirectory(destPath);
            }

            foreach (var encFile in Directory.GetFiles(encryptedRoot, "*.lc"))
            {
                string b64Url = Path.GetFileNameWithoutExtension(encFile);
                string relPath = Encoding.UTF8.GetString(DecryptBytes(FromBase64Url(b64Url), masterKey));
                string destPath = GetSafeDestinationPath(targetRoot, relPath);

                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                byte[] plainData = DecryptBytes(File.ReadAllBytes(encFile), masterKey);
                File.WriteAllBytes(destPath, plainData);
            }
        }

        private static string GetSafeDestinationPath(string targetRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)
                || Path.IsPathRooted(relativePath)
                || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part.Equals("..", StringComparison.Ordinal)))
            {
                throw new CryptographicException("Percorso cifrato non valido.");
            }

            string destPath = Path.GetFullPath(Path.Combine(targetRoot, relativePath));
            if (!destPath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("Percorso cifrato non valido.");

            return destPath;
        }
    }
}