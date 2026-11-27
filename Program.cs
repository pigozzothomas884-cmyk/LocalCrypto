using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LocalCrypto
{
    internal static class Program
    {
        private const string PipeName = "CiaoDev_LocalCrypto_ControlPipe";
        private static Mutex? _singleInstanceMutex;

        [STAThread]
        static void Main(string[] args)
        {
            bool isLockCommand = args.Any(a => a.Equals("--lock", StringComparison.OrdinalIgnoreCase));

            _singleInstanceMutex = new Mutex(true, "Local\\CiaoDev_LocalCrypto_SingleInstance", out bool createdNew);

            if (!createdNew)
            {
                SendIpcCommand(isLockCommand ? "LOCK" : "SHOW");
                return;
            }

            if (isLockCommand)
                return;

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(PipeName));
            GC.KeepAlive(_singleInstanceMutex);
        }

        private static void SendIpcCommand(string command)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1200);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(command);
            }
            catch { }
        }
    }

    public sealed class MainForm : Form
    {
        private static readonly Color BgRoot = Color.White;
        private static readonly Color BgAlt = Color.FromArgb(245, 245, 247);      // #f5f5f7
        private static readonly Color TextPrimary = Color.FromArgb(29, 29, 31);     // #1d1d1f
        private static readonly Color TextMuted = Color.FromArgb(110, 110, 115);    // #6e6e73
        private static readonly Color Accent = Color.FromArgb(0, 113, 227);         // #0071e3
        private static readonly Color AccentHover = Color.FromArgb(0, 119, 237);    // #0077ed
        private static readonly Color BorderSubtle = Color.FromArgb(229, 229, 234); // #e5e5ea
        private static readonly Color DangerRed = Color.FromArgb(220, 38, 38);
        private static readonly Color DangerRedHover = Color.FromArgb(185, 28, 28);
        private static readonly Color SuccessGreen = Color.FromArgb(22, 163, 74);

        private MountedVault? _activeVault;
        private string _selectedFolder = "";
        private int _driveIndex = 0;
        private readonly string[] _drives = { "V:", "W:", "X:", "Y:", "Z:" };
        private bool _forceExit = false;
        private bool _recoveryMode = false;

        private readonly NotifyIcon _trayIcon;

        // Sezione disco attivo
        private readonly RoundedCardPanel _panelActiveDrive;
        private readonly Label _lblActiveTitle;
        private readonly Label _lblActiveSub;

        // Sezione cartella selezionata
        private readonly RoundedCardPanel _panelFolderCard;
        private readonly Label _lblFolderName;
        private readonly Label _lblFolderFullPath;
        private readonly PillBadge _badgeFolderState;

        // Sezione form contestuale
        private readonly Panel _panelActionForm;
        private readonly TextBox _txtDiskName;
        private readonly TextBox _txtSecret;
        private readonly Panel _rowMount;
        private readonly PillButton _btnDrive;
        private readonly PillButton _btnPrimaryAction;
        private readonly PillButton _btnSecondaryAction;
        private readonly LinkLabel _lnkRecoveryToggle;
        private readonly LinkLabel _lnkReset;

        public MainForm(string pipeName)
        {
            Text = "LocalCrypto";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(460, 232);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = BgRoot;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9.5f);

            StartIpcListener(pipeName);

            _trayIcon = new NotifyIcon
            {
                Icon = Icon,
                Text = "LocalCrypto",
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => RestoreWindow();

            var trayMenu = new ContextMenuStrip
            {
                BackColor = BgRoot,
                ForeColor = TextPrimary,
                ShowImageMargin = false
            };
            trayMenu.Items.Add("Apri", null, (_, _) => RestoreWindow());
            trayMenu.Items.Add("Blocca disco", null, (_, _) => LockAndUnmount());
            trayMenu.Items.Add("Esci", null, (_, _) =>
            {
                _forceExit = true;
                LockAndUnmount();
                if (_activeVault == null)
                {
                    _trayIcon.Visible = false;
                    Application.Exit();
                }
            });
            _trayIcon.ContextMenuStrip = trayMenu;

            FormClosing += (_, e) =>
            {
                if (!_forceExit && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    if (_activeVault != null)
                    {
                        _trayIcon.ShowBalloonTip(
                            2000,
                            "LocalCrypto",
                            $"{_activeVault.DiskName} ({_activeVault.DriveLetter}) attivo in background.",
                            ToolTipIcon.Info);
                    }
                }
            };

            // --- HEADER ---
            var picLogo = new PictureBox
            {
                Size = new Size(34, 34),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = Icon.ToBitmap(),
                Location = new Point(28, 24)
            };

            var lblTitle = new Label
            {
                Text = "LocalCrypto",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(72, 24),
                AutoSize = true
            };

            var lblBrand = new Label
            {
                Text = "CiaoDev",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = TextMuted,
                Location = new Point(362, 32),
                Size = new Size(70, 20),
                TextAlign = ContentAlignment.MiddleRight
            };

            Controls.AddRange(new Control[] { picLogo, lblTitle, lblBrand });

            // --- CARD DISCO ATTIVO (visibile solo a disco montato) ---
            _panelActiveDrive = new RoundedCardPanel(BgAlt, BorderSubtle, 20)
            {
                Location = new Point(28, 76),
                Size = new Size(404, 138),
                Visible = false
            };

            var lblActiveTag = new Label
            {
                Text = "ATTIVO",
                Font = new Font("Segoe UI Semibold", 7.8f, FontStyle.Bold),
                ForeColor = SuccessGreen,
                Location = new Point(20, 16),
                AutoSize = true
            };

            _lblActiveTitle = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(20, 34),
                Size = new Size(364, 26),
                AutoEllipsis = true
            };

            _lblActiveSub = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = TextMuted,
                Location = new Point(20, 62),
                Size = new Size(364, 18),
                AutoEllipsis = true
            };

            var btnOpenDrive = new PillButton("Apri disco", BgRoot, BorderSubtle, TextPrimary, BgRoot, Accent)
            {
                Location = new Point(20, 88),
                Size = new Size(174, 36)
            };
            btnOpenDrive.Click += (_, _) =>
            {
                if (_activeVault != null)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_activeVault.DriveLetter + "\\") { UseShellExecute = true });
            };

            var btnUnmount = new PillButton("Blocca", DangerRed, DangerRed, Color.White, DangerRedHover, Color.White)
            {
                Location = new Point(206, 88),
                Size = new Size(178, 36),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
            };
            btnUnmount.Click += (_, _) => LockAndUnmount();

            _panelActiveDrive.Controls.AddRange(new Control[] { lblActiveTag, _lblActiveTitle, _lblActiveSub, btnOpenDrive, btnUnmount });
            Controls.Add(_panelActiveDrive);

            // --- CARD CARTELLA ---
            _panelFolderCard = new RoundedCardPanel(BgAlt, BorderSubtle, 20)
            {
                Location = new Point(28, 76),
                Size = new Size(404, 126)
            };

            _lblFolderName = new Label
            {
                Text = "Nessuna cartella",
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(18, 18),
                Size = new Size(280, 22),
                AutoEllipsis = true
            };

            _badgeFolderState = new PillBadge("", BgRoot, TextMuted)
            {
                Location = new Point(306, 17),
                Size = new Size(80, 24),
                Visible = false
            };

            _lblFolderFullPath = new Label
            {
                Text = "Seleziona una cartella locale o su Google Drive",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = TextMuted,
                Location = new Point(18, 42),
                Size = new Size(368, 18),
                AutoEllipsis = true
            };

            var btnSelectFolder = new PillButton("Scegli cartella", BgRoot, BorderSubtle, TextPrimary, BgRoot, Accent)
            {
                Location = new Point(18, 72),
                Size = new Size(368, 38)
            };
            btnSelectFolder.Click += (_, _) => PickFolder();

            _panelFolderCard.Controls.AddRange(new Control[] { _lblFolderName, _badgeFolderState, _lblFolderFullPath, btnSelectFolder });
            Controls.Add(_panelFolderCard);

            // --- FORM CONTESTUALE ---
            _panelActionForm = new Panel
            {
                Location = new Point(28, 216),
                Size = new Size(404, 200),
                BackColor = BgRoot,
                Visible = false
            };

            var nameBox = new RoundedCardPanel(BgAlt, BorderSubtle, 18)
            {
                Location = new Point(0, 0),
                Size = new Size(404, 42),
                Padding = new Padding(16, 10, 16, 8)
            };
            _txtDiskName = new TextBox
            {
                PlaceholderText = "Nome del disco",
                BackColor = BgAlt,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.8f)
            };
            nameBox.Controls.Add(_txtDiskName);

            var pwdBox = new RoundedCardPanel(BgAlt, BorderSubtle, 18)
            {
                Location = new Point(0, 52),
                Size = new Size(404, 42),
                Padding = new Padding(16, 10, 16, 8)
            };
            _txtSecret = new TextBox
            {
                PlaceholderText = "Password",
                UseSystemPasswordChar = true,
                BackColor = BgAlt,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.8f)
            };
            pwdBox.Controls.Add(_txtSecret);

            _rowMount = new Panel
            {
                Location = new Point(0, 108),
                Size = new Size(404, 42)
            };

            _btnDrive = new PillButton("V:", BgAlt, BorderSubtle, TextPrimary, BorderSubtle, TextPrimary)
            {
                Location = new Point(0, 0),
                Size = new Size(76, 42)
            };
            _btnDrive.Click += (_, _) =>
            {
                _driveIndex = (_driveIndex + 1) % _drives.Length;
                _btnDrive.Text = _drives[_driveIndex];
            };

            _btnPrimaryAction = new PillButton("Sblocca", Accent, Accent, Color.White, AccentHover, Color.White)
            {
                Location = new Point(86, 0),
                Size = new Size(318, 42),
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold)
            };
            _btnPrimaryAction.Click += (_, _) => UnlockAndMount();
            _rowMount.Controls.AddRange(new Control[] { _btnDrive, _btnPrimaryAction });

            _btnSecondaryAction = new PillButton("Cifra cartella", Accent, Accent, Color.White, AccentHover, Color.White)
            {
                Location = new Point(0, 108),
                Size = new Size(404, 42),
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                Visible = false
            };
            _btnSecondaryAction.Click += (_, _) => InitializeOrEncryptFolder();

            _lnkRecoveryToggle = new LinkLabel
            {
                Text = "Usa chiave di recupero",
                LinkColor = TextMuted,
                ActiveLinkColor = Accent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Location = new Point(4, 162),
                AutoSize = true
            };
            _lnkRecoveryToggle.LinkClicked += (_, _) => ToggleRecoveryMode();

            _lnkReset = new LinkLabel
            {
                Text = "Nuova password",
                LinkColor = Accent,
                ActiveLinkColor = AccentHover,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Location = new Point(292, 162),
                Size = new Size(108, 20),
                TextAlign = ContentAlignment.MiddleRight,
                Visible = false
            };
            _lnkReset.LinkClicked += (_, _) => ResetPasswordFlow();

            _panelActionForm.Controls.AddRange(new Control[]
            {
                nameBox, pwdBox, _rowMount, _btnSecondaryAction, _lnkRecoveryToggle, _lnkReset
            });
            Controls.Add(_panelActionForm);
        }

        private void ToggleRecoveryMode()
        {
            _recoveryMode = !_recoveryMode;
            _txtSecret.Clear();
            _txtSecret.UseSystemPasswordChar = !_recoveryMode;
            _txtSecret.PlaceholderText = _recoveryMode ? "Chiave di recupero" : "Password";
            _lnkRecoveryToggle.Text = _recoveryMode ? "Usa password" : "Usa chiave di recupero";
            _lnkReset.Visible = _recoveryMode;
        }

        private void RefreshSelectedFolderState()
        {
            if (_activeVault != null)
            {
                _panelActiveDrive.Visible = true;
                _lblActiveTitle.Text = $"{_activeVault.DiskName} ({_activeVault.DriveLetter})";
                _lblActiveSub.Text = _activeVault.EncryptedFolder;

                _panelFolderCard.Location = new Point(28, 228);
                _panelActionForm.Visible = false;
                ClientSize = new Size(460, 382);
                return;
            }

            _panelActiveDrive.Visible = false;
            _panelFolderCard.Location = new Point(28, 76);

            if (string.IsNullOrEmpty(_selectedFolder))
            {
                _panelActionForm.Visible = false;
                ClientSize = new Size(460, 232);
                return;
            }

            bool isVault = VaultCrypto.IsVault(_selectedFolder);
            _lblFolderName.Text = Path.GetFileName(_selectedFolder);
            _lblFolderFullPath.Text = _selectedFolder;
            _badgeFolderState.Visible = true;
            _panelActionForm.Visible = true;

            if (isVault)
            {
                string savedName = VaultCrypto.GetVaultName(_selectedFolder);
                _txtDiskName.Text = savedName;
                _badgeFolderState.SetState("Cifrata", Color.FromArgb(219, 234, 254), Accent);
                _rowMount.Visible = true;
                _btnSecondaryAction.Visible = false;
                _lnkRecoveryToggle.Visible = true;
                _lnkReset.Visible = _recoveryMode;
                ClientSize = new Size(460, 430);
            }
            else
            {
                _txtDiskName.Text = Path.GetFileName(_selectedFolder);
                _recoveryMode = false;
                _txtSecret.UseSystemPasswordChar = true;
                _txtSecret.PlaceholderText = "Password (min. 6 caratteri)";
                _badgeFolderState.SetState("In chiaro", Color.FromArgb(254, 243, 199), Color.FromArgb(180, 83, 9));
                _rowMount.Visible = false;
                _btnSecondaryAction.Visible = true;
                _lnkRecoveryToggle.Visible = false;
                _lnkReset.Visible = false;
                ClientSize = new Size(460, 405);
            }
        }

        private void StartIpcListener(string pipeName)
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(pipeName, PipeDirection.In);
                        await server.WaitForConnectionAsync();
                        using var reader = new StreamReader(server);
                        string? cmd = await reader.ReadLineAsync();

                        if (cmd == "LOCK")
                        {
                            BeginInvoke(() => LockAndUnmount(notifyLocked: true));
                        }
                        else if (cmd == "SHOW")
                        {
                            BeginInvoke(RestoreWindow);
                        }
                    }
                    catch
                    {
                        await Task.Delay(200);
                    }
                }
            });
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        private void PickFolder()
        {
            using var dlg = new FolderBrowserDialog();
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _selectedFolder = dlg.SelectedPath;
                _txtSecret.Clear();
                RefreshSelectedFolderState();
            }
        }

        private void InitializeOrEncryptFolder()
        {
            if (string.IsNullOrEmpty(_selectedFolder) || VaultCrypto.IsVault(_selectedFolder))
                return;

            if (string.IsNullOrWhiteSpace(_txtSecret.Text) || _txtSecret.Text.Length < 6)
            {
                MessageBox.Show("Inserisci una password di almeno 6 caratteri.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool hasItems = Directory.EnumerateFileSystemEntries(_selectedFolder).Any();
            if (hasItems)
            {
                var confirm = MessageBox.Show(
                    "I file esistenti nella cartella verranno cifrati sul posto. Continuare?",
                    "LocalCrypto",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes) return;
            }

            string diskName = string.IsNullOrWhiteSpace(_txtDiskName.Text)
                ? Path.GetFileName(_selectedFolder)
                : _txtDiskName.Text.Trim();

            string password = _txtSecret.Text;
            try
            {
                string recoveryKey = VaultCrypto.CreateVault(_selectedFolder, diskName, password);

                if (hasItems)
                {
                    byte[] mk = VaultCrypto.UnlockMasterKey(_selectedFolder, password, isRecoveryKey: false);
                    try
                    {
                        VaultCrypto.EncryptExistingFolderInPlace(_selectedFolder, mk);
                    }
                    finally
                    {
                        Array.Clear(mk, 0, mk.Length);
                    }
                }

                _txtSecret.Clear();
                RefreshSelectedFolderState();
                ShowRecoveryKeyDialog(recoveryKey, diskName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossibile cifrare la cartella:\n{ex.Message}", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowRecoveryKeyDialog(string recoveryKey, string diskName)
        {
            string sheet =
                "LOCALCRYPTO — CHIAVE DI RECUPERO\r\n" +
                $"Disco: {diskName}\r\n" +
                $"Data : {DateTime.Now:dd/MM/yyyy HH:mm}\r\n\r\n" +
                $"{recoveryKey}\r\n";

            using var dlg = new Form
            {
                Text = "Chiave di recupero",
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(440, 230),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = BgRoot,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 9.5f)
            };

            dlg.Controls.Add(new Label
            {
                Text = "Chiave di recupero",
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                Location = new Point(22, 18),
                AutoSize = true
            });

            var keyCard = new RoundedCardPanel(BgAlt, BorderSubtle, 16)
            {
                Location = new Point(22, 56),
                Size = new Size(396, 46),
                Padding = new Padding(12, 12, 12, 10)
            };

            var txtKey = new TextBox
            {
                Text = recoveryKey,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = BgAlt,
                ForeColor = TextPrimary,
                Font = new Font("Consolas", 10.5f, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                Dock = DockStyle.Fill
            };
            keyCard.Controls.Add(txtKey);
            dlg.Controls.Add(keyCard);

            var btnCopy = new PillButton("Copia", BgAlt, BorderSubtle, TextPrimary, BorderSubtle, TextPrimary)
            {
                Location = new Point(22, 118),
                Size = new Size(124, 38)
            };
            btnCopy.Click += (_, _) =>
            {
                Clipboard.SetText(recoveryKey);
                btnCopy.Text = "Copiata";
            };

            var btnSave = new PillButton("Salva .txt", Accent, Accent, Color.White, AccentHover, Color.White)
            {
                Location = new Point(156, 118),
                Size = new Size(130, 38)
            };
            btnSave.Click += (_, _) =>
            {
                using var sfd = new SaveFileDialog
                {
                    Filter = "File di testo (*.txt)|*.txt",
                    FileName = $"Recovery_{diskName}.txt"
                };
                if (sfd.ShowDialog() == DialogResult.OK)
                    File.WriteAllText(sfd.FileName, sheet);
            };

            var btnPrint = new PillButton("Stampa", BgRoot, BorderSubtle, TextPrimary, BgAlt, TextPrimary)
            {
                Location = new Point(296, 118),
                Size = new Size(122, 38)
            };
            btnPrint.Click += (_, _) =>
            {
                using var pd = new PrintDocument();
                pd.PrintPage += (_, e) =>
                {
                    using var font = new Font("Consolas", 11f);
                    e.Graphics?.DrawString(sheet, font, Brushes.Black, 60, 60);
                };
                using var printDlg = new PrintDialog { Document = pd };
                if (printDlg.ShowDialog() == DialogResult.OK)
                    pd.Print();
            };

            var btnDone = new PillButton("Chiudi", BgRoot, BorderSubtle, TextMuted, BgAlt, TextPrimary)
            {
                Location = new Point(22, 170),
                Size = new Size(396, 38),
                DialogResult = DialogResult.OK
            };

            dlg.Controls.AddRange(new Control[] { btnCopy, btnSave, btnPrint, btnDone });
            dlg.ShowDialog(this);
        }

        private void UnlockAndMount()
        {
            if (string.IsNullOrEmpty(_selectedFolder) || !VaultCrypto.IsVault(_selectedFolder))
                return;

            if (string.IsNullOrWhiteSpace(_txtSecret.Text))
            {
                MessageBox.Show("Inserisci la password.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                LockAndUnmount();
                if (_activeVault != null)
                    return;

                byte[] mk = VaultCrypto.UnlockMasterKey(_selectedFolder, _txtSecret.Text, _recoveryMode);
                try
                {
                    string drive = _drives[_driveIndex];
                    string diskName = string.IsNullOrWhiteSpace(_txtDiskName.Text)
                        ? VaultCrypto.GetVaultName(_selectedFolder)
                        : _txtDiskName.Text.Trim();

                    _activeVault = new MountedVault(drive, diskName, _selectedFolder, mk);
                    _txtSecret.Clear();
                    RefreshSelectedFolderState();
                }
                finally
                {
                    Array.Clear(mk, 0, mk.Length);
                }
            }
            catch (CryptographicException)
            {
                MessageBox.Show("Credenziali non valide.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (IOException ex)
            {
                MessageBox.Show($"Impossibile montare il disco:\n{ex.Message}", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossibile sbloccare il disco:\n{ex.Message}", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ResetPasswordFlow()
        {
            if (string.IsNullOrEmpty(_selectedFolder) || !VaultCrypto.IsVault(_selectedFolder) || string.IsNullOrWhiteSpace(_txtSecret.Text))
            {
                MessageBox.Show("Inserisci prima la chiave di recupero.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string newPwd = PromptNewPassword();
            if (string.IsNullOrWhiteSpace(newPwd) || newPwd.Length < 6)
                return;

            try
            {
                VaultCrypto.ResetPasswordWithRecovery(_selectedFolder, _txtSecret.Text, newPwd);
                ToggleRecoveryMode();
                MessageBox.Show("Password aggiornata.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                MessageBox.Show("Chiave di recupero non valida.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string PromptNewPassword()
        {
            using var dlg = new Form
            {
                Text = "Nuova password",
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(340, 145),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = BgRoot,
                ForeColor = TextPrimary
            };

            var card = new RoundedCardPanel(BgAlt, BorderSubtle, 18)
            {
                Location = new Point(20, 18),
                Size = new Size(300, 42),
                Padding = new Padding(14, 10, 14, 8)
            };

            var box = new TextBox
            {
                PlaceholderText = "Nuova password (min. 6 caratteri)",
                UseSystemPasswordChar = true,
                BackColor = BgAlt,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill
            };
            card.Controls.Add(box);

            var ok = new PillButton("Salva", Accent, Accent, Color.White, AccentHover, Color.White)
            {
                Location = new Point(20, 76),
                Size = new Size(300, 40),
                DialogResult = DialogResult.OK
            };

            dlg.Controls.AddRange(new Control[] { card, ok });
            dlg.AcceptButton = ok;

            return dlg.ShowDialog() == DialogResult.OK ? box.Text : "";
        }

        public void LockAndUnmount(bool notifyLocked = false)
        {
            if (_activeVault == null) return;

            string lockedName = _activeVault.DiskName;
            string lockedDrive = _activeVault.DriveLetter;

            _activeVault.Dispose();
            if (!_activeVault.SyncSucceeded)
            {
                MessageBox.Show(
                    "Sincronizzazione fallita. Il disco resta montato per evitare la perdita di dati. Chiudere le applicazioni che usano i file e riprovare.",
                    "LocalCrypto",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            _activeVault = null;
            RefreshSelectedFolderState();

            if (notifyLocked)
            {
                _trayIcon.ShowBalloonTip(
                    2000,
                    "Disco bloccato",
                    $"{lockedName} ({lockedDrive}) cifrato.",
                    ToolTipIcon.Info);
            }
        }
    }

    public sealed class RoundedCardPanel : Panel
    {
        private readonly Color _fill;
        private readonly Color _border;
        private readonly int _radius;

        public RoundedCardPanel(Color fill, Color border, int radius)
        {
            _fill = fill;
            _border = border;
            _radius = radius;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = PillButton.CreateRoundRect(rect, _radius);
            using var brush = new SolidBrush(_fill);
            using var pen = new Pen(_border, 1);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
        }
    }

    public sealed class PillBadge : Control
    {
        private Color _bg;
        public PillBadge(string text, Color bg, Color fg)
        {
            Text = text;
            _bg = bg;
            ForeColor = fg;
            Font = new Font("Segoe UI Semibold", 8.2f, FontStyle.Bold);
            Size = new Size(80, 24);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        }

        public void SetState(string text, Color bg, Color fg)
        {
            Text = text;
            _bg = bg;
            ForeColor = fg;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = PillButton.CreateRoundRect(rect, Height / 2);
            using var brush = new SolidBrush(_bg);
            e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    public sealed class PillButton : Control, IButtonControl
    {
        private readonly Color _bg;
        private readonly Color _border;
        private readonly Color _fg;
        private readonly Color _hoverBg;
        private readonly Color _hoverFg;
        private bool _hover;

        public DialogResult DialogResult { get; set; } = DialogResult.None;

        public PillButton(string text, Color bg, Color border, Color fg, Color hoverBg, Color hoverFg)
        {
            Text = text;
            _bg = bg;
            _border = border;
            _fg = fg;
            _hoverBg = hoverBg;
            _hoverFg = hoverFg;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            base.OnClick(e);
            if (DialogResult != DialogResult.None && FindForm() is Form f)
                f.DialogResult = DialogResult;
        }

        public void NotifyDefault(bool value) { }

        public void PerformClick()
        {
            if (Enabled)
                OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundRect(rect, Height / 2);

            Color fill = !Enabled ? Color.FromArgb(245, 245, 247) : (_hover ? _hoverBg : _bg);
            Color textCol = !Enabled ? Color.FromArgb(174, 174, 178) : (_hover ? _hoverFg : _fg);
            Color borderCol = !Enabled ? Color.FromArgb(229, 229, 234) : (_hover ? _hoverBg : _border);

            using var brush = new SolidBrush(fill);
            using var pen = new Pen(borderCol, 1);

            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);

            TextRenderer.DrawText(e.Graphics, Text, Font, rect, textCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        public static GraphicsPath CreateRoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}