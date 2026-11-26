using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LocalCrypto
{
    public sealed class MountedVault : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DefineDosDevice(uint dwFlags, string lpDeviceName, string? lpTargetPath);

        private const uint DDD_REMOVE_DEFINITION = 0x00000002;

        public string DriveLetter { get; }
        public string EncryptedFolder { get; }
        public string PlainCacheFolder { get; }

        private readonly byte[] _masterKey;
        private readonly FileSystemWatcher _watcher;
        private readonly System.Threading.Timer _debounceTimer;
        private readonly object _syncLock = new();

        public MountedVault(string driveLetter, string encryptedFolder, byte[] masterKey)
        {
            DriveLetter = driveLetter.TrimEnd('\\');
            EncryptedFolder = encryptedFolder;
            _masterKey = masterKey;

            PlainCacheFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CiaoDev", "LocalCrypto", "Mount", Guid.NewGuid().ToString("N"));

            VaultCrypto.DecryptAllToFolder(encryptedFolder, PlainCacheFolder, _masterKey);

            if (!DefineDosDevice(0, DriveLetter, PlainCacheFolder))
                throw new IOException($"Impossibile montare l'unita' locale {DriveLetter}.");

            _debounceTimer = new System.Threading.Timer(_ => SyncAllToEncrypted(), null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(PlainCacheFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Changed += (_, _) => ScheduleSync();
            _watcher.Created += (_, _) => ScheduleSync();
            _watcher.Renamed += (_, _) => ScheduleSync();
            _watcher.Deleted += (_, _) => ScheduleSync();

            Process.Start(new ProcessStartInfo("explorer.exe", DriveLetter + "\\") { UseShellExecute = true });
        }

        private void ScheduleSync()
        {
            _debounceTimer.Change(450, Timeout.Infinite);
        }

        public void SyncAllToEncrypted()
        {
            lock (_syncLock)
            {
                if (!Directory.Exists(PlainCacheFolder) || !Directory.Exists(EncryptedFolder))
                    return;

                try
                {
                    string tempStaging = Path.Combine(EncryptedFolder, ".sync_tmp");
                    if (Directory.Exists(tempStaging))
                        Directory.Delete(tempStaging, true);
                    Directory.CreateDirectory(tempStaging);

                    foreach (var plainFile in Directory.GetFiles(PlainCacheFolder, "*", SearchOption.AllDirectories))
                    {
                        VaultCrypto.EncryptRelativeFile(PlainCacheFolder, plainFile, tempStaging, _masterKey);
                    }

                    foreach (var oldLc in Directory.GetFiles(EncryptedFolder, "*.lc"))
                        File.Delete(oldLc);

                    foreach (var newLc in Directory.GetFiles(tempStaging, "*.lc"))
                        File.Move(newLc, Path.Combine(EncryptedFolder, Path.GetFileName(newLc)));

                    Directory.Delete(tempStaging, true);
                }
                catch
                {
                    // File temporaneamente in uso da un'applicazione: riprovera' al prossimo evento o allo smontaggio
                }
            }
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _debounceTimer.Dispose();

            SyncAllToEncrypted();

            DefineDosDevice(DDD_REMOVE_DEFINITION, DriveLetter, null);
            Array.Clear(_masterKey, 0, _masterKey.Length);

            try
            {
                if (Directory.Exists(PlainCacheFolder))
                    Directory.Delete(PlainCacheFolder, recursive: true);
            }
            catch { }
        }
    }
}