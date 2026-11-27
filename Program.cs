using System;
using System.Drawing;
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
        private MountedVault? _activeVault;
        private string _selectedFolder = "";
        private int _driveIndex = 0;
        private readonly string[] _drives = { "V:", "W:", "X:", "Y:", "Z:" };
        private bool _recoveryMode = false;

        private readonly NotifyIcon _trayIcon;

        // Sezione disco attivo
        private readonly GroupBox _panelActiveDrive;
        private readonly Label _lblActiveTitle;
        private readonly Label _lblActiveSub;

        // Sezione cartella selezionata
        private readonly GroupBox _panelFolderCard;
        private readonly Label _lblFolderName;
        private readonly Label _lblFolderFullPath;
        private readonly Label _badgeFolderState;

        // Sezione form contestuale
        private readonly GroupBox _panelActionForm;
        private readonly TextBox _txtDiskName;
        private readonly TextBox _txtSecret;
        private readonly TableLayoutPanel _rowMount;
        private readonly Button _btnDrive;
        private readonly Button _btnPrimaryAction;
        private readonly Button _btnSecondaryAction;
        private readonly LinkLabel _lnkRecoveryToggle;
        private readonly LinkLabel _lnkReset;

        public MainForm(string pipeName)
        {
            Text = "LocalCrypto";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 620);
            MinimumSize = new Size(520, 500);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Font = SystemFonts.MessageBoxFont;

            Shown += (_, _) => RestoreWindow();

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
                ShowImageMargin = false
            };
            trayMenu.Items.Add("Apri", null, (_, _) => RestoreWindow());
            trayMenu.Items.Add("Blocca disco", null, (_, _) => LockAndUnmount());
            trayMenu.Items.Add("Esci", null, (_, _) =>
            {
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
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                    _trayIcon.ShowBalloonTip(
                        2000,
                        "LocalCrypto resta attivo",
                        _activeVault == null
                            ? "L'app è ancora disponibile nell'area di notifica."
                            : $"Il disco {_activeVault.DriveLetter} resta montato. Apri LocalCrypto dall'area di notifica per gestirlo.",
                        ToolTipIcon.Info);
                    return;
                }

                if (_activeVault != null)
                {
                    e.Cancel = true;
                    return;
                }

                _trayIcon.Visible = false;
            };

            FormClosed += (_, _) => _trayIcon.Dispose();

            var scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(16)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            scrollPanel.Controls.Add(content);
            Controls.Add(scrollPanel);

            void AddContentRow(Control control)
            {
                int row = content.RowCount++;
                content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                control.Margin = new Padding(0, 0, 0, 12);
                control.Dock = DockStyle.Top;
                content.Controls.Add(control, 0, row);
            }

            var header = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 16)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(new PictureBox
            {
                Size = new Size(36, 36),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = Icon.ToBitmap(),
                Margin = new Padding(0, 0, 10, 0)
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = "LocalCrypto",
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Anchor = AnchorStyles.Left
            }, 1, 0);
            header.Controls.Add(new Label
            {
                Text = "CiaoDev",
                AutoSize = true,
                Anchor = AnchorStyles.Right
            }, 2, 0);
            AddContentRow(header);

            _panelActiveDrive = new GroupBox
            {
                Text = "Disco attivo",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Visible = false,
                Padding = new Padding(10)
            };
            var activeLayout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top
            };
            activeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < activeLayout.RowCount; i++)
                activeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _lblActiveTitle = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
            _lblActiveSub = new Label { AutoSize = true, MaximumSize = new Size(520, 0) };
            var activeButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            var btnOpenDrive = new Button { Text = "Apri disco", AutoSize = true };
            btnOpenDrive.Click += (_, _) =>
            {
                if (_activeVault != null)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_activeVault.DriveLetter + "\\") { UseShellExecute = true });
            };
            var btnUnmount = new Button { Text = "Blocca disco", AutoSize = true };
            btnUnmount.Click += (_, _) => LockAndUnmount();
            activeButtons.Controls.AddRange(new Control[] { btnOpenDrive, btnUnmount });
            activeLayout.Controls.Add(_lblActiveTitle, 0, 0);
            activeLayout.Controls.Add(_lblActiveSub, 0, 1);
            activeLayout.Controls.Add(activeButtons, 0, 2);
            _panelActiveDrive.Controls.Add(activeLayout);
            AddContentRow(_panelActiveDrive);

            _panelFolderCard = new GroupBox
            {
                Text = "Cartella",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10)
            };
            var folderLayout = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top
            };
            folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < folderLayout.RowCount; i++)
                folderLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _lblFolderName = new Label
            {
                Text = "Nessuna cartella selezionata",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(3, 5, 3, 8)
            };
            _badgeFolderState = new Label
            {
                Text = "",
                AutoSize = true,
                Visible = false,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(3, 5, 3, 8)
            };
            _lblFolderFullPath = new Label
            {
                Text = "Seleziona una cartella locale o su Google Drive",
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Margin = new Padding(3, 0, 3, 8)
            };
            var btnSelectFolder = new Button { Text = "Scegli cartella...", AutoSize = true };
            btnSelectFolder.Click += (_, _) => PickFolder();
            folderLayout.Controls.Add(_lblFolderName, 0, 0);
            folderLayout.Controls.Add(_badgeFolderState, 1, 0);
            folderLayout.Controls.Add(_lblFolderFullPath, 0, 1);
            folderLayout.SetColumnSpan(_lblFolderFullPath, 2);
            folderLayout.Controls.Add(btnSelectFolder, 0, 2);
            folderLayout.SetColumnSpan(btnSelectFolder, 2);
            _panelFolderCard.Controls.Add(folderLayout);
            AddContentRow(_panelFolderCard);

            _panelActionForm = new GroupBox
            {
                Text = "Configurazione e accesso",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Visible = false,
                Padding = new Padding(10)
            };
            var actionLayout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 7,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top
            };
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < actionLayout.RowCount; i++)
                actionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            actionLayout.Controls.Add(new Label { Text = "Nome del disco", AutoSize = true }, 0, 0);
            _txtDiskName = new TextBox
            {
                Dock = DockStyle.Top,
                Margin = new Padding(3, 3, 3, 10)
            };
            actionLayout.Controls.Add(_txtDiskName, 0, 1);
            actionLayout.Controls.Add(new Label { Text = "Password o chiave di recupero", AutoSize = true }, 0, 2);
            _txtSecret = new TextBox
            {
                UseSystemPasswordChar = true,
                Dock = DockStyle.Top,
                Margin = new Padding(3, 3, 3, 10)
            };
            actionLayout.Controls.Add(_txtSecret, 0, 3);

            var recoveryActions = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            _lnkRecoveryToggle = new LinkLabel { Text = "Usa chiave di recupero", AutoSize = true, Margin = new Padding(3, 5, 16, 5) };
            _lnkRecoveryToggle.LinkClicked += (_, _) => ToggleRecoveryMode();
            _lnkReset = new LinkLabel { Text = "Reimposta password", AutoSize = true, Visible = false, Margin = new Padding(3, 5, 3, 5) };
            _lnkReset.LinkClicked += (_, _) => ResetPasswordFlow();
            recoveryActions.Controls.AddRange(new Control[] { _lnkRecoveryToggle, _lnkReset });
            actionLayout.Controls.Add(recoveryActions, 0, 4);

            _rowMount = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Dock = DockStyle.Top,
                Visible = false,
                Margin = new Padding(0, 0, 0, 8)
            };
            _rowMount.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _rowMount.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _btnDrive = new Button { Text = "V:", AutoSize = true, Margin = new Padding(3, 3, 8, 3) };
            _btnDrive.Click += (_, _) =>
            {
                _driveIndex = (_driveIndex + 1) % _drives.Length;
                _btnDrive.Text = _drives[_driveIndex];
            };
            _btnPrimaryAction = new Button { Text = "Sblocca e monta", AutoSize = true, Dock = DockStyle.Fill };
            _btnPrimaryAction.Click += (_, _) => UnlockAndMount();
            _rowMount.Controls.Add(_btnDrive, 0, 0);
            _rowMount.Controls.Add(_btnPrimaryAction, 1, 0);

            _btnSecondaryAction = new Button { Text = "Cifra cartella", AutoSize = true, Dock = DockStyle.Top, Visible = false };
            _btnSecondaryAction.Click += (_, _) => InitializeOrEncryptFolder();
            actionLayout.Controls.Add(_rowMount, 0, 5);
            actionLayout.Controls.Add(_btnSecondaryAction, 0, 6);
            _panelActionForm.Controls.Add(actionLayout);
            AddContentRow(_panelActionForm);

            FormClosed += (_, _) =>
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            };
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
                _panelFolderCard.Visible = true;
                _panelActionForm.Visible = false;
                PerformLayout();
                return;
            }

            _panelActiveDrive.Visible = false;
            _panelFolderCard.Visible = true;

            if (string.IsNullOrEmpty(_selectedFolder))
            {
                _panelActionForm.Visible = false;
                _lblFolderName.Text = "Nessuna cartella selezionata";
                _lblFolderFullPath.Text = "Seleziona una cartella locale o su Google Drive";
                _badgeFolderState.Visible = false;
                PerformLayout();
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
                SetFolderState("Stato: Cifrata");
                _rowMount.Visible = true;
                _btnSecondaryAction.Visible = false;
                _lnkRecoveryToggle.Visible = true;
                _lnkReset.Visible = _recoveryMode;
                _txtSecret.PlaceholderText = _recoveryMode ? "Chiave di recupero" : "Password";
            }
            else
            {
                _txtDiskName.Text = Path.GetFileName(_selectedFolder);
                _recoveryMode = false;
                _txtSecret.UseSystemPasswordChar = true;
                _txtSecret.PlaceholderText = "Password (min. 6 caratteri)";
                SetFolderState("Stato: Non cifrata");
                _rowMount.Visible = false;
                _btnSecondaryAction.Visible = true;
                _lnkRecoveryToggle.Visible = false;
                _lnkReset.Visible = false;
            }

            PerformLayout();
        }

        private void SetFolderState(string text)
        {
            _badgeFolderState.Text = text;
            _badgeFolderState.AccessibleName = text;
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
                ClientSize = new Size(500, 250),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = "Conserva questa chiave in un luogo sicuro.",
                AutoSize = true,
                Margin = new Padding(3, 3, 3, 10)
            }, 0, 0);
            var txtKey = new TextBox
            {
                Text = recoveryKey,
                ReadOnly = true,
                Dock = DockStyle.Top,
                Font = new Font("Consolas", 10f, FontStyle.Regular),
                Margin = new Padding(3, 3, 3, 12)
            };
            layout.Controls.Add(txtKey, 0, 1);
            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            var btnDone = new Button { Text = "Chiudi", AutoSize = true, DialogResult = DialogResult.OK };
            var btnPrint = new Button { Text = "Stampa...", AutoSize = true };
            var btnSave = new Button { Text = "Salva...", AutoSize = true };
            var btnCopy = new Button { Text = "Copia", AutoSize = true };
            btnCopy.Click += (_, _) =>
            {
                Clipboard.SetText(recoveryKey);
                btnCopy.Text = "Copiata";
            };
            btnSave.Click += (_, _) =>
            {
                using var sfd = new SaveFileDialog
                {
                    Filter = "File di testo (*.txt)|*.txt",
                    FileName = $"Recovery_{string.Join("_", diskName.Split(Path.GetInvalidFileNameChars()))}.txt"
                };
                if (sfd.ShowDialog() == DialogResult.OK)
                    File.WriteAllText(sfd.FileName, sheet);
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
            buttons.Controls.AddRange(new Control[] { btnDone, btnPrint, btnSave, btnCopy });
            layout.Controls.Add(buttons, 0, 3);
            dlg.Controls.Add(layout);
            dlg.AcceptButton = btnDone;
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
                ClientSize = new Size(380, 160),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                ColumnCount = 1,
                RowCount = 3
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = "Inserisci una password di almeno 6 caratteri:",
                AutoSize = true,
                Margin = new Padding(3, 3, 3, 8)
            }, 0, 0);
            var box = new TextBox
            {
                UseSystemPasswordChar = true,
                Dock = DockStyle.Top,
                Margin = new Padding(3, 3, 3, 10)
            };
            layout.Controls.Add(box, 0, 1);
            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            var ok = new Button { Text = "Salva", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Annulla", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange(new Control[] { cancel, ok });
            layout.Controls.Add(buttons, 0, 2);
            dlg.Controls.Add(layout);
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;

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

}