using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace LocalCrypto
{
    public sealed class MountedVault : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DefineDosDevice(uint dwFlags, string lpDeviceName, string? lpTargetPath);

        private const uint DDD_REMOVE_DEFINITION = 0x00000002;

        public string DriveLetter { get; }
        public string DiskName { get; }
        public string EncryptedFolder { get; }
        public string PlainCacheFolder { get; }

        private readonly byte[] _masterKey;
        private readonly FileSystemWatcher _watcher;
        private readonly System.Threading.Timer _debounceTimer;
        private readonly object _syncLock = new();
        private bool _isDisposing = false;
        public bool SyncSucceeded { get; private set; } = true;

        public MountedVault(string driveLetter, string diskName, string encryptedFolder, byte[] masterKey)
        {
            DriveLetter = driveLetter.TrimEnd('\\');
            DiskName = string.IsNullOrWhiteSpace(diskName) ? "Disco Cifrato" : diskName.Trim();
            EncryptedFolder = encryptedFolder;
            _masterKey = masterKey;

            PlainCacheFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CiaoDev", "LocalCrypto", "Mount", Guid.NewGuid().ToString("N"));

            try
            {
                VaultCrypto.DecryptAllToFolder(encryptedFolder, PlainCacheFolder, _masterKey);
                EnsureProtectedLockTrigger();

                if (!DefineDosDevice(0, DriveLetter, PlainCacheFolder))
                    throw new IOException($"Impossibile montare l'unita' locale {DriveLetter}.");

                SetDriveDisplayLabel(DriveLetter, DiskName);

                _debounceTimer = new System.Threading.Timer(_ => SyncAllToEncrypted(), null, Timeout.Infinite, Timeout.Infinite);

                _watcher = new FileSystemWatcher(PlainCacheFolder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
                    EnableRaisingEvents = true
                };

                _watcher.Changed += (_, e) => OnFsChanged(e.Name);
                _watcher.Created += (_, e) => OnFsChanged(e.Name);
                _watcher.Renamed += (_, e) => OnFsChanged(e.OldName, e.Name);
                _watcher.Deleted += (_, e) => OnFsChanged(e.Name);

                Process.Start(new ProcessStartInfo("explorer.exe", DriveLetter + "\\") { UseShellExecute = true });
            }
            catch
            {
                DefineDosDevice(DDD_REMOVE_DEFINITION, DriveLetter, null);
                Array.Clear(_masterKey, 0, _masterKey.Length);
                try
                {
                    if (Directory.Exists(PlainCacheFolder))
                        Directory.Delete(PlainCacheFolder, recursive: true);
                }
                catch { }
                throw;
            }
        }

        private void OnFsChanged(params string?[] names)
        {
            if (_isDisposing) return;

            foreach (var n in names)
            {
                if (!string.IsNullOrEmpty(n) &&
                    Path.GetFileName(n).Equals(VaultCrypto.LockTriggerFileName, StringComparison.OrdinalIgnoreCase))
                {
                    EnsureProtectedLockTrigger();
                    return;
                }
            }

            _debounceTimer.Change(400, Timeout.Infinite);
        }

        public void EnsureProtectedLockTrigger()
        {
            if (_isDisposing || !Directory.Exists(PlainCacheFolder)) return;
            try
            {
                string triggerPath = Path.Combine(PlainCacheFolder, VaultCrypto.LockTriggerFileName);
                string exePath = Application.ExecutablePath;

                string script =
                    "@echo off\r\n" +
                    "cd /d %USERPROFILE%\r\n" +
                    $"\"{exePath}\" --lock\r\n" +
                    "exit /b 0\r\n";

                if (File.Exists(triggerPath))
                    File.SetAttributes(triggerPath, FileAttributes.Normal);

                File.WriteAllText(triggerPath, script, Encoding.ASCII);
                File.SetAttributes(triggerPath, FileAttributes.ReadOnly | FileAttributes.System);
            }
            catch { }
        }

        private static void SetDriveDisplayLabel(string driveLetter, string label)
        {
            try
            {
                char letter = char.ToUpperInvariant(driveLetter[0]);
                using var key = Registry.CurrentUser.CreateSubKey(
                    $@"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2\{letter}");
                key.SetValue("_LabelFromReg", label, RegistryValueKind.String);
            }
            catch { }
        }

        private static void ClearDriveDisplayLabel(string driveLetter)
        {
            try
            {
                char letter = char.ToUpperInvariant(driveLetter[0]);
                Registry.CurrentUser.DeleteSubKeyTree(
                    $@"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2\{letter}",
                    throwOnMissingSubKey: false);
            }
            catch { }
        }

        public bool SyncAllToEncrypted()
        {
            lock (_syncLock)
            {
                if (!Directory.Exists(PlainCacheFolder) || !Directory.Exists(EncryptedFolder))
                    return false;

                string? tempRoot = null;
                bool backupReady = false;
                try
                {
                    tempRoot = Path.Combine(Path.GetTempPath(), "CiaoDev_LocalCrypto", Guid.NewGuid().ToString("N"));
                    string tempStaging = Path.Combine(tempRoot, "staging");
                    string backupRoot = Path.Combine(tempRoot, "backup");
                    Directory.CreateDirectory(tempStaging);
                    Directory.CreateDirectory(backupRoot);

                    foreach (var plainDir in Directory.GetDirectories(PlainCacheFolder, "*", SearchOption.AllDirectories))
                        VaultCrypto.EncryptRelativeDirectory(PlainCacheFolder, plainDir, tempStaging, _masterKey);

                    foreach (var plainFile in Directory.GetFiles(PlainCacheFolder, "*", SearchOption.AllDirectories))
                    {
                        if (Path.GetFileName(plainFile).Equals(VaultCrypto.LockTriggerFileName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        VaultCrypto.EncryptRelativeFile(PlainCacheFolder, plainFile, tempStaging, _masterKey);
                    }

                    foreach (var oldLc in Directory.GetFiles(EncryptedFolder, "*.lc"))
                        File.Copy(oldLc, Path.Combine(backupRoot, Path.GetFileName(oldLc)));
                    foreach (var oldLcd in Directory.GetFiles(EncryptedFolder, "*.lcd"))
                        File.Copy(oldLcd, Path.Combine(backupRoot, Path.GetFileName(oldLcd)));
                    backupReady = true;

                    foreach (var oldLc in Directory.GetFiles(EncryptedFolder, "*.lc"))
                        File.Delete(oldLc);
                    foreach (var oldLcd in Directory.GetFiles(EncryptedFolder, "*.lcd"))
                        File.Delete(oldLcd);

                    foreach (var newItem in Directory.GetFiles(tempStaging))
                        File.Move(newItem, Path.Combine(EncryptedFolder, Path.GetFileName(newItem)));

                    SyncSucceeded = true;
                    return true;
                }
                catch
                {
                    try
                    {
                        if (backupReady)
                        {
                            foreach (var currentLc in Directory.GetFiles(EncryptedFolder, "*.lc"))
                                File.Delete(currentLc);
                            foreach (var currentLcd in Directory.GetFiles(EncryptedFolder, "*.lcd"))
                                File.Delete(currentLcd);

                            if (tempRoot != null)
                            {
                                string backupRoot = Path.Combine(tempRoot, "backup");
                                if (Directory.Exists(backupRoot))
                                {
                                    foreach (var backup in Directory.GetFiles(backupRoot))
                                        File.Copy(backup, Path.Combine(EncryptedFolder, Path.GetFileName(backup)), overwrite: true);
                                }
                            }
                        }
                    }
                    catch { }

                    SyncSucceeded = false;
                    return false;
                }
                finally
                {
                    if (tempRoot != null)
                    {
                        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
                        catch { }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (!SyncAllToEncrypted())
            {
                SyncSucceeded = false;
                return;
            }

            _isDisposing = true;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _debounceTimer.Dispose();

            ClearDriveDisplayLabel(DriveLetter);
            DefineDosDevice(DDD_REMOVE_DEFINITION, DriveLetter, null);
            Array.Clear(_masterKey, 0, _masterKey.Length);

            try
            {
                if (Directory.Exists(PlainCacheFolder))
                {
                    string triggerPath = Path.Combine(PlainCacheFolder, VaultCrypto.LockTriggerFileName);
                    if (File.Exists(triggerPath))
                        File.SetAttributes(triggerPath, FileAttributes.Normal);

                    Directory.Delete(PlainCacheFolder, recursive: true);
                }
            }
            catch { }
        }
    }
}