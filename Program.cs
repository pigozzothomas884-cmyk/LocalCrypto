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
        // Palette ufficiale CiaoDev™
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

        private readonly NotifyIcon _trayIcon;
        private readonly Label _lblFolderName;
        private readonly Label _lblFolderFullPath;
        private readonly PillBadge _badgeFolderState;
        private readonly Label _lblMountStatus;
        private readonly PillButton _btnOpenDrive;
        private readonly PillButton _btnUnmount;

        private readonly TextBox _txtDiskName;
        private readonly TextBox _txtSecret;
        private readonly CheckBox _chkRecovery;
        private readonly PillButton _btnDrive;
        private readonly PillButton _btnPrimaryAction;
        private readonly PillButton _btnSecondaryAction;

        public MainForm(string pipeName)
        {
            Text = "LocalCrypto — CiaoDev™";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(800, 545);
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = BgRoot;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9.5f);

            StartIpcListener(pipeName);

            // System Tray
            _trayIcon = new NotifyIcon
            {
                Icon = Icon,
                Text = "LocalCrypto — CiaoDev™",
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) => RestoreWindow();

            var trayMenu = new ContextMenuStrip
            {
                BackColor = BgRoot,
                ForeColor = TextPrimary,
                ShowImageMargin = false
            };
            trayMenu.Items.Add("Apri LocalCrypto", null, (_, _) => RestoreWindow());
            trayMenu.Items.Add("Blocca e cifra disco attivo", null, (_, _) => LockAndUnmount());
            trayMenu.Items.Add("Esci completamente", null, (_, _) =>
            {
                _forceExit = true;
                LockAndUnmount();
                _trayIcon.Visible = false;
                Application.Exit();
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
                            2500,
                            "LocalCrypto in esecuzione",
                            $"Il disco \"{_activeVault.DiskName}\" ({_activeVault.DriveLetter}) resta attivo. Doppio clic su !Blocca Disco.cmd nel disco per bloccarlo.",
                            ToolTipIcon.Info);
                    }
                }
            };

            // Layout principale
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(28, 22, 28, 20),
                BackColor = BgRoot
            };
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // --- HEADER ---
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 20)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var picLogo = new PictureBox
            {
                Size = new Size(42, 42),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = Icon.ToBitmap(),
                Margin = new Padding(0, 0, 14, 0),
                Anchor = AnchorStyles.Left
            };

            var titleStack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(0)
            };
            titleStack.Controls.Add(new Label
            {
                Text = "LocalCrypto",
                Font = new Font("Segoe UI", 16.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true,
                Margin = new Padding(0)
            });
            titleStack.Controls.Add(new Label
            {
                Text = "Dischi virtuali cifrati AES-256-GCM privi di condivisione di rete",
                Font = new Font("Segoe UI", 9f),
                ForeColor = TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 0)
            });

            var brandPill = new PillBadge("CiaoDev™", BgAlt, TextMuted)
            {
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };

            header.Controls.Add(picLogo, 0, 0);
            header.Controls.Add(titleStack, 1, 0);
            header.Controls.Add(brandPill, 2, 0);
            outer.Controls.Add(header, 0, 0);

            // --- CORPO A 2 COLONNE ---
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            // COLONNA SINISTRA: Cartella & Disco attivo
            var leftCard = new RoundedCardPanel(BgAlt, BorderSubtle, 22)
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22),
                Margin = new Padding(0, 0, 10, 0)
            };

            var leftLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var leftHeaderRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            };
            leftHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            leftHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            leftHeaderRow.Controls.Add(new Label
            {
                Text = "1. Cartella di origine",
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true
            }, 0, 0);

            _badgeFolderState = new PillBadge("Nessuna", BgRoot, TextMuted);
            leftHeaderRow.Controls.Add(_badgeFolderState, 1, 0);
            leftLayout.Controls.Add(leftHeaderRow);

            var pathBox = new RoundedCardPanel(BgRoot, BorderSubtle, 16)
            {
                Dock = DockStyle.Top,
                Height = 66,
                Padding = new Padding(14, 10, 14, 10),
                Margin = new Padding(0, 0, 0, 12)
            };
            var pathInner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = BgRoot
            };
            _lblFolderName = new Label
            {
                Text = "Nessuna cartella selezionata",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                Dock = DockStyle.Fill,
                AutoEllipsis = true
            };
            _lblFolderFullPath = new Label
            {
                Text = "Scegli una cartella su Google Drive o disco locale",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = TextMuted,
                Dock = DockStyle.Fill,
                AutoEllipsis = true
            };
            pathInner.Controls.Add(_lblFolderName, 0, 0);
            pathInner.Controls.Add(_lblFolderFullPath, 0, 1);
            pathBox.Controls.Add(pathInner);
            leftLayout.Controls.Add(pathBox);

            var btnSelectFolder = new PillButton("Scegli cartella...", BgRoot, Accent, Accent, Accent, Color.White)
            {
                Dock = DockStyle.Top,
                Height = 40,
                Margin = new Padding(0, 0, 0, 22)
            };
            btnSelectFolder.Click += (_, _) => PickFolder();
            leftLayout.Controls.Add(btnSelectFolder);

            // Sezione Controllo Disco Montato
            leftLayout.Controls.Add(new Label
            {
                Text = "Stato disco montato",
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            });

            _lblMountStatus = new Label
            {
                Text = "Nessun disco attivo al momento.",
                Font = new Font("Segoe UI", 8.8f),
                ForeColor = TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14)
            };
            leftLayout.Controls.Add(_lblMountStatus);

            var activeDriveActions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Height = 40
            };
            activeDriveActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            activeDriveActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));

            _btnOpenDrive = new PillButton("Apri disco", BgRoot, BorderSubtle, TextMuted, BgRoot, TextPrimary)
            {
                Dock = DockStyle.Fill,
                Enabled = false,
                Margin = new Padding(0, 0, 5, 0)
            };
            _btnOpenDrive.Click += (_, _) =>
            {
                if (_activeVault != null)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_activeVault.DriveLetter + "\\") { UseShellExecute = true });
            };

            _btnUnmount = new PillButton("Blocca e cifra ora", DangerRed, DangerRed, Color.White, DangerRedHover, Color.White)
            {
                Dock = DockStyle.Fill,
                Enabled = false,
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                Margin = new Padding(5, 0, 0, 0)
            };
            _btnUnmount.Click += (_, _) => LockAndUnmount();

            activeDriveActions.Controls.Add(_btnOpenDrive, 0, 0);
            activeDriveActions.Controls.Add(_btnUnmount, 1, 0);
            leftLayout.Controls.Add(activeDriveActions);

            leftCard.Controls.Add(leftLayout);
            grid.Controls.Add(leftCard, 0, 0);

            // COLONNA DESTRA: Nome disco, Chiave & Azioni
            var rightCard = new RoundedCardPanel(BgAlt, BorderSubtle, 22)
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22),
                Margin = new Padding(10, 0, 0, 0)
            };

            var rightLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            rightLayout.Controls.Add(new Label
            {
                Text = "2. Configurazione e accesso",
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 12)
            });

            // Input Nome Disco
            var nameBox = new RoundedCardPanel(BgRoot, BorderSubtle, 18)
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(16, 10, 16, 8),
                Margin = new Padding(0, 0, 0, 10)
            };
            _txtDiskName = new TextBox
            {
                PlaceholderText = "Nome disco (es. Archivio Personale)",
                BackColor = BgRoot,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.8f)
            };
            nameBox.Controls.Add(_txtDiskName);
            rightLayout.Controls.Add(nameBox);

            // Input Password
            var pwdBox = new RoundedCardPanel(BgRoot, BorderSubtle, 18)
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(16, 10, 16, 8),
                Margin = new Padding(0, 0, 0, 10)
            };
            _txtSecret = new TextBox
            {
                PlaceholderText = "Password disco (min. 6 caratteri)",
                UseSystemPasswordChar = true,
                BackColor = BgRoot,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.8f)
            };
            pwdBox.Controls.Add(_txtSecret);
            rightLayout.Controls.Add(pwdBox);

            // Checkbox recovery + Reset password
            var recRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                Margin = new Padding(2, 0, 2, 16)
            };
            recRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            recRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _chkRecovery = new CheckBox
            {
                Text = "Usa chiave di recupero",
                ForeColor = TextMuted,
                BackColor = BgAlt,
                AutoSize = true
            };
            _chkRecovery.CheckedChanged += (_, _) =>
            {
                _txtSecret.UseSystemPasswordChar = !_chkRecovery.Checked;
                _txtSecret.PlaceholderText = _chkRecovery.Checked
                    ? "XXXXXX-XXXXXX-XXXXXX-..."
                    : "Password disco (min. 6 caratteri)";
            };

            var lnkReset = new LinkLabel
            {
                Text = "Reimposta password",
                LinkColor = Accent,
                ActiveLinkColor = AccentHover,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = BgAlt,
                AutoSize = true,
                Margin = new Padding(8, 2, 0, 0)
            };
            lnkReset.LinkClicked += (_, _) => ResetPasswordFlow();

            recRow.Controls.Add(_chkRecovery, 0, 0);
            recRow.Controls.Add(lnkReset, 1, 0);
            rightLayout.Controls.Add(recRow);

            // Riga lettera + Sblocca
            var driveSelectRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Height = 42,
                Margin = new Padding(0, 0, 0, 10)
            };
            driveSelectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102f));
            driveSelectRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _btnDrive = new PillButton("Unita' V:", BgRoot, BorderSubtle, TextPrimary, BorderSubtle, TextPrimary)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 0)
            };
            _btnDrive.Click += (_, _) =>
            {
                _driveIndex = (_driveIndex + 1) % _drives.Length;
                _btnDrive.Text = $"Unita' {_drives[_driveIndex]}";
            };

            _btnPrimaryAction = new PillButton("Sblocca e monta disco", Accent, Accent, Color.White, AccentHover, Color.White)
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
            };
            _btnPrimaryAction.Click += (_, _) => UnlockAndMount();

            driveSelectRow.Controls.Add(_btnDrive, 0, 0);
            driveSelectRow.Controls.Add(_btnPrimaryAction, 1, 0);
            rightLayout.Controls.Add(driveSelectRow);

            // Bottone Crea / Cifra
            _btnSecondaryAction = new PillButton("Crea / Cifra cartella selezionata", BgRoot, Accent, Accent, Accent, Color.White)
            {
                Dock = DockStyle.Top,
                Height = 40
            };
            _btnSecondaryAction.Click += (_, _) => InitializeOrEncryptFolder();
            rightLayout.Controls.Add(_btnSecondaryAction);

            rightCard.Controls.Add(rightLayout);
            grid.Controls.Add(rightCard, 1, 0);

            outer.Controls.Add(grid, 0, 1);

            // --- FOOTER ---
            var footer = new Label
            {
                Text = "CiaoDev™  •  Nel disco montato trovi sempre \"!Blocca Disco.cmd\" in cima: doppio clic per bloccarlo e cifrarlo all'istante",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 14, 0, 0)
            };
            outer.Controls.Add(footer, 0, 2);

            Controls.Add(outer);
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
            using var dlg = new FolderBrowserDialog
            {
                Description = "Seleziona una cartella (Google Drive locale o disco interno)",
                UseDescriptionForTitle = true
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _selectedFolder = dlg.SelectedPath;
                RefreshSelectedFolderState();
            }
        }

        private void RefreshSelectedFolderState()
        {
            if (string.IsNullOrEmpty(_selectedFolder)) return;

            bool isVault = VaultCrypto.IsVault(_selectedFolder);
            _lblFolderName.Text = Path.GetFileName(_selectedFolder);
            _lblFolderFullPath.Text = _selectedFolder;

            if (isVault)
            {
                string savedName = VaultCrypto.GetVaultName(_selectedFolder);
                _txtDiskName.Text = savedName;
                _badgeFolderState.SetState("Cifrata", Color.FromArgb(219, 234, 254), Accent);
                _btnPrimaryAction.Enabled = true;
                _btnSecondaryAction.Enabled = false;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_txtDiskName.Text))
                    _txtDiskName.Text = Path.GetFileName(_selectedFolder);
                _badgeFolderState.SetState("In chiaro", Color.FromArgb(254, 243, 199), Color.FromArgb(180, 83, 9));
                _btnPrimaryAction.Enabled = false;
                _btnSecondaryAction.Enabled = true;
            }
        }

        private void InitializeOrEncryptFolder()
        {
            if (string.IsNullOrEmpty(_selectedFolder))
            {
                MessageBox.Show("Seleziona prima una cartella nella colonna di sinistra.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (VaultCrypto.IsVault(_selectedFolder))
            {
                MessageBox.Show("Questa cartella e' gia' cifrata con LocalCrypto.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_txtSecret.Text) || _txtSecret.Text.Length < 6)
            {
                MessageBox.Show("Inserisci una password di almeno 6 caratteri.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool hasItems = Directory.EnumerateFileSystemEntries(_selectedFolder).Any();
            if (hasItems)
            {
                var confirm = MessageBox.Show(
                    "La cartella contiene gia' file o sottocartelle.\nVerranno cifrati sul posto e gli originali in chiaro saranno rimossi.\n\nContinuare?",
                    "LocalCrypto — Cifratura cartella esistente",
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
                MessageBox.Show($"Impossibile creare o cifrare la cartella:\n{ex.Message}", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowRecoveryKeyDialog(string recoveryKey, string diskName)
        {
            string sheet =
                "LOCALCRYPTO — CHIAVE DI RECUPERO (CiaoDev™)\r\n" +
                $"Disco    : {diskName}\r\n" +
                $"Cartella : {_selectedFolder}\r\n" +
                $"Creata il: {DateTime.Now:dd/MM/yyyy HH:mm}\r\n\r\n" +
                $"CHIAVE DI RECUPERO:\r\n{recoveryKey}\r\n\r\n" +
                "Conserva questa chiave in un luogo sicuro.\r\n" +
                "Senza password o chiave di recupero i dati non sono recuperabili.";

            using var dlg = new Form
            {
                Text = "Chiave di recupero — LocalCrypto",
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(470, 265),
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
                Text = "Salva la tua chiave di recupero",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                Location = new Point(24, 20),
                AutoSize = true
            });

            dlg.Controls.Add(new Label
            {
                Text = "Serve per sbloccare il disco se dimentichi la password principale.",
                ForeColor = TextMuted,
                Location = new Point(24, 48),
                AutoSize = true
            });

            var keyCard = new RoundedCardPanel(BgAlt, BorderSubtle, 16)
            {
                Location = new Point(24, 80),
                Size = new Size(422, 48),
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
                Location = new Point(24, 146),
                Size = new Size(132, 38)
            };
            btnCopy.Click += (_, _) =>
            {
                Clipboard.SetText(recoveryKey);
                btnCopy.Text = "Copiata";
            };

            var btnSave = new PillButton("Salva .txt", Accent, Accent, Color.White, AccentHover, Color.White)
            {
                Location = new Point(168, 146),
                Size = new Size(138, 38)
            };
            btnSave.Click += (_, _) =>
            {
                using var sfd = new SaveFileDialog
                {
                    Title = "Salva chiave di recupero",
                    Filter = "File di testo (*.txt)|*.txt",
                    FileName = $"LocalCrypto_Recovery_{diskName}.txt"
                };
                if (sfd.ShowDialog() == DialogResult.OK)
                    File.WriteAllText(sfd.FileName, sheet);
            };

            var btnPrint = new PillButton("Stampa", BgRoot, Accent, Accent, Accent, Color.White)
            {
                Location = new Point(318, 146),
                Size = new Size(128, 38)
            };
            btnPrint.Click += (_, _) =>
            {
                using var pd = new PrintDocument();
                pd.DocumentName = "LocalCrypto Recovery Key";
                pd.PrintPage += (_, e) =>
                {
                    using var font = new Font("Consolas", 11f);
                    e.Graphics?.DrawString(sheet, font, Brushes.Black, 60, 60);
                };
                using var printDlg = new PrintDialog { Document = pd };
                if (printDlg.ShowDialog() == DialogResult.OK)
                    pd.Print();
            };

            var btnDone = new PillButton("Ho salvato la chiave, chiudi", BgRoot, BorderSubtle, TextMuted, BgAlt, TextPrimary)
            {
                Location = new Point(24, 200),
                Size = new Size(422, 38),
                DialogResult = DialogResult.OK
            };

            dlg.Controls.AddRange(new Control[] { btnCopy, btnSave, btnPrint, btnDone });
            dlg.ShowDialog(this);
        }

        private void UnlockAndMount()
        {
            if (string.IsNullOrEmpty(_selectedFolder) || !VaultCrypto.IsVault(_selectedFolder))
            {
                MessageBox.Show("Seleziona una cartella cifrata valida.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_txtSecret.Text))
            {
                MessageBox.Show("Inserisci la password o la chiave di recupero.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                LockAndUnmount();
                byte[] mk = VaultCrypto.UnlockMasterKey(_selectedFolder, _txtSecret.Text, _chkRecovery.Checked);
                try
                {
                    string drive = _drives[_driveIndex];
                    string diskName = string.IsNullOrWhiteSpace(_txtDiskName.Text)
                        ? VaultCrypto.GetVaultName(_selectedFolder)
                        : _txtDiskName.Text.Trim();

                    _activeVault = new MountedVault(drive, diskName, _selectedFolder, mk);
                    _lblMountStatus.Text = $"Attivo: {diskName} ({drive})";
                    _lblMountStatus.ForeColor = SuccessGreen;
                    _btnOpenDrive.Enabled = true;
                    _btnUnmount.Enabled = true;
                    _txtSecret.Clear();
                }
                finally
                {
                    Array.Clear(mk, 0, mk.Length);
                }
            }
            catch (CryptographicException)
            {
                MessageBox.Show("Password o chiave di recupero non valida.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            if (string.IsNullOrEmpty(_selectedFolder) || !VaultCrypto.IsVault(_selectedFolder))
            {
                MessageBox.Show("Seleziona prima la cartella cifrata.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_chkRecovery.Checked || string.IsNullOrWhiteSpace(_txtSecret.Text))
            {
                MessageBox.Show("Spunta 'Usa chiave di recupero' e incolla la chiave nel campo sopra.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string newPwd = PromptNewPassword();
            if (string.IsNullOrWhiteSpace(newPwd) || newPwd.Length < 6)
                return;

            try
            {
                VaultCrypto.ResetPasswordWithRecovery(_selectedFolder, _txtSecret.Text, newPwd);
                _chkRecovery.Checked = false;
                _txtSecret.Clear();
                MessageBox.Show("Nuova password impostata correttamente.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                Text = "Nuova password — LocalCrypto",
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(360, 155),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = BgRoot,
                ForeColor = TextPrimary
            };

            var card = new RoundedCardPanel(BgAlt, BorderSubtle, 18)
            {
                Location = new Point(22, 20),
                Size = new Size(316, 44),
                Padding = new Padding(16, 11, 16, 9)
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

            var ok = new PillButton("Conferma nuova password", Accent, Accent, Color.White, AccentHover, Color.White)
            {
                Location = new Point(22, 80),
                Size = new Size(316, 40),
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

            _lblMountStatus.Text = "Nessun disco attivo al momento.";
            _lblMountStatus.ForeColor = TextMuted;
            _btnOpenDrive.Enabled = false;
            _btnUnmount.Enabled = false;

            if (notifyLocked)
            {
                _trayIcon.ShowBalloonTip(
                    2500,
                    "Disco bloccato e cifrato",
                    $"\"{lockedName}\" ({lockedDrive}) e' stato cifrato e reso inaccessibile.",
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
            Size = new Size(84, 24);
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