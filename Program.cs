using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LocalCrypto
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new LocalCryptoTrayApp());
        }
    }

    public sealed class LocalCryptoTrayApp : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly CiaoDevPopupForm _popup;

        public LocalCryptoTrayApp()
        {
            _popup = new CiaoDevPopupForm();

            _trayIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield,
                Text = "LocalCrypto - CiaoDev",
                Visible = true
            };

            _trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    _popup.ToggleNearTray();
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Apri LocalCrypto", null, (_, _) => _popup.ToggleNearTray());
            menu.Items.Add("Blocca tutto ed esci", null, (_, _) =>
            {
                _popup.LockAndUnmount();
                _trayIcon.Visible = false;
                Application.Exit();
            });
            _trayIcon.ContextMenuStrip = menu;
        }

        private static Icon CreateFlatShieldIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bgBrush = new SolidBrush(Color.FromArgb(37, 99, 235));
            using var path = new GraphicsPath();
            path.AddRoundedRectangle(new Rectangle(2, 2, 28, 28), 7);
            g.FillPath(bgBrush, path);

            using var pen = new Pen(Color.White, 2.4f);
            g.DrawArc(pen, 11, 8, 10, 9, 180, 180);
            g.DrawLine(pen, 11, 12, 11, 15);
            g.DrawLine(pen, 21, 12, 21, 15);
            using var whiteBrush = new SolidBrush(Color.White);
            g.FillRectangle(whiteBrush, 9, 15, 14, 10);

            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    internal static class GraphicsExtensions
    {
        public static void AddRoundedRectangle(this GraphicsPath path, Rectangle bounds, int radius)
        {
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
        }
    }

    public sealed class CiaoDevPopupForm : Form
    {
        // Palette CiaoDev Dark Flat
        private static readonly Color BgRoot = Color.FromArgb(18, 20, 24);
        private static readonly Color BgSurface = Color.FromArgb(26, 29, 36);
        private static readonly Color BorderSubtle = Color.FromArgb(44, 49, 60);
        private static readonly Color TextPrimary = Color.FromArgb(243, 244, 246);
        private static readonly Color TextMuted = Color.FromArgb(148, 156, 170);
        private static readonly Color AccentBlue = Color.FromArgb(37, 99, 235);
        private static readonly Color DangerRed = Color.FromArgb(185, 28, 28);

        private MountedVault? _activeVault;
        private string _selectedFolder = "";

        private readonly Label _lblFolder;
        private readonly Label _lblMountBadge;
        private readonly TextBox _txtSecret;
        private readonly CheckBox _chkRecovery;
        private readonly ComboBox _cmbDrive;

        public CiaoDevPopupForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            Size = new Size(360, 345);
            BackColor = BgRoot;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9.5f);

            Deactivate += (_, _) =>
            {
                if (!Modal) Hide();
            };

            Paint += (_, e) =>
            {
                using var pen = new Pen(BorderSubtle, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            };

            // Header
            var lblApp = new Label
            {
                Text = "LocalCrypto",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                Location = new Point(18, 14),
                AutoSize = true
            };

            var lblCompany = new Label
            {
                Text = "CiaoDev",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = TextMuted,
                Location = new Point(292, 17),
                AutoSize = true
            };

            _lblMountBadge = new Label
            {
                Text = "Stato: Nessuna unita' montata",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = TextMuted,
                Location = new Point(18, 40),
                Size = new Size(324, 18)
            };

            // Selezione cartella
            _lblFolder = new Label
            {
                Text = "Nessuna cartella selezionata",
                BackColor = BgSurface,
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                Location = new Point(18, 68),
                Size = new Size(324, 30)
            };

            var btnBrowse = CreateFlatButton("Scegli cartella...", 18, 106, 158, BgSurface);
            btnBrowse.Click += (_, _) => PickFolder();

            var btnInit = CreateFlatButton("Crea / Cifra cartella", 184, 106, 158, BgSurface);
            btnInit.Click += (_, _) => InitializeOrEncryptFolder();

            // Input password / recovery key
            _txtSecret = new TextBox
            {
                PlaceholderText = "Password cartella",
                UseSystemPasswordChar = true,
                BackColor = BgSurface,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(18, 150),
                Size = new Size(324, 28)
            };

            _chkRecovery = new CheckBox
            {
                Text = "Sblocca con chiave di recupero",
                ForeColor = TextMuted,
                Location = new Point(18, 184),
                AutoSize = true
            };
            _chkRecovery.CheckedChanged += (_, _) =>
            {
                _txtSecret.UseSystemPasswordChar = !_chkRecovery.Checked;
                _txtSecret.PlaceholderText = _chkRecovery.Checked
                    ? "XXXXXX-XXXXXX-XXXXXX-..."
                    : "Password cartella";
            };

            var lnkResetPwd = new LinkLabel
            {
                Text = "Reimposta password",
                LinkColor = AccentBlue,
                ActiveLinkColor = TextPrimary,
                Location = new Point(230, 185),
                AutoSize = true
            };
            lnkResetPwd.LinkClicked += (_, _) => ResetPasswordFlow();

            // Lettera unita' + Monta
            _cmbDrive = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = BgSurface,
                ForeColor = TextPrimary,
                Location = new Point(18, 218),
                Size = new Size(82, 28)
            };
            _cmbDrive.Items.AddRange(new object[] { "V:", "W:", "X:", "Y:", "Z:" });
            _cmbDrive.SelectedIndex = 0;

            var btnMount = CreateFlatButton("Sblocca e monta disco", 108, 217, 234, AccentBlue);
            btnMount.Click += (_, _) => UnlockAndMount();

            // Blocca e smonta
            var btnLock = CreateFlatButton("Blocca e smonta unita' attiva", 18, 260, 324, BgSurface);
            btnLock.Click += (_, _) => LockAndUnmount();

            // Footer
            var lblFooter = new Label
            {
                Text = "Disco locale privato (non condiviso in rete)",
                Font = new Font("Segoe UI", 8f),
                ForeColor = TextMuted,
                Location = new Point(18, 312),
                AutoSize = true
            };

            Controls.AddRange(new Control[]
            {
                lblApp, lblCompany, _lblMountBadge,
                _lblFolder, btnBrowse, btnInit,
                _txtSecret, _chkRecovery, lnkResetPwd,
                _cmbDrive, btnMount, btnLock, lblFooter
            });
        }

        private static Button CreateFlatButton(string text, int x, int y, int w, Color bg)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = TextPrimary,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = BorderSubtle;
            btn.FlatAppearance.BorderSize = 1;
            return btn;
        }

        public void ToggleNearTray()
        {
            if (Visible)
            {
                Hide();
                return;
            }

            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
            Show();
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
                bool isVault = VaultCrypto.IsVault(_selectedFolder);
                _lblFolder.Text = (isVault ? "[Cifrata] " : "[In chiaro] ") + _selectedFolder;
                _lblFolder.ForeColor = TextPrimary;
            }
        }

        private void InitializeOrEncryptFolder()
        {
            if (string.IsNullOrEmpty(_selectedFolder))
            {
                MessageBox.Show("Seleziona prima una cartella.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            bool hasFiles = Directory.EnumerateFileSystemEntries(_selectedFolder).Any();
            if (hasFiles)
            {
                var confirm = MessageBox.Show(
                    "La cartella contiene gia' file o sottocartelle.\nVerranno cifrati sul posto e i file originali in chiaro saranno rimossi.\n\nContinuare?",
                    "LocalCrypto - Cifratura cartella esistente",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes) return;
            }

            string password = _txtSecret.Text;
            string recoveryKey = VaultCrypto.CreateVault(_selectedFolder, Path.GetFileName(_selectedFolder), password);

            if (hasFiles)
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

            _lblFolder.Text = "[Cifrata] " + _selectedFolder;
            _txtSecret.Clear();
            ShowRecoveryKeyOptions(recoveryKey);
        }

        private void ShowRecoveryKeyOptions(string recoveryKey)
        {
            string sheet =
                "====================================================\r\n" +
                "  LOCALCRYPTO - CHIAVE DI RECUPERO (CiaoDev)\r\n" +
                "====================================================\r\n" +
                $"Cartella : {_selectedFolder}\r\n" +
                $"Creata il: {DateTime.Now:dd/MM/yyyy HH:mm}\r\n\r\n" +
                "CHIAVE DI RECUPERO:\r\n" +
                $"{recoveryKey}\r\n\r\n" +
                "Conserva questa chiave in un luogo sicuro.\r\n" +
                "Senza password o chiave di recupero i dati non sono recuperabili.\r\n" +
                "====================================================";

            var choice = MessageBox.Show(
                sheet + "\n\n[Si] Salva su file (.txt)\n[No] Stampa documento\n[Annulla] Chiudi",
                "LocalCrypto - Chiave di recupero",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Information);

            if (choice == DialogResult.Yes)
            {
                using var sfd = new SaveFileDialog
                {
                    Title = "Salva chiave di recupero",
                    Filter = "File di testo (*.txt)|*.txt",
                    FileName = $"LocalCrypto_Recovery_{Path.GetFileName(_selectedFolder)}.txt"
                };
                if (sfd.ShowDialog() == DialogResult.OK)
                    File.WriteAllText(sfd.FileName, sheet);
            }
            else if (choice == DialogResult.No)
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
            }
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
                string drive = _cmbDrive.SelectedItem?.ToString() ?? "V:";

                _activeVault = new MountedVault(drive, _selectedFolder, mk);
                _lblMountBadge.Text = $"Attivo: Unita' {drive} -> {Path.GetFileName(_selectedFolder)}";
                _lblMountBadge.ForeColor = Color.FromArgb(96, 165, 250);
                _txtSecret.Clear();
            }
            catch
            {
                MessageBox.Show("Password o chiave di recupero non valida.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show("Spunta 'Sblocca con chiave di recupero' e incolla la chiave nel campo sopra.", "LocalCrypto", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                Text = "LocalCrypto - Nuova password",
                Size = new Size(320, 145),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = BgRoot,
                ForeColor = TextPrimary
            };

            var box = new TextBox
            {
                PlaceholderText = "Nuova password (min. 6 caratteri)",
                UseSystemPasswordChar = true,
                BackColor = BgSurface,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(16, 18),
                Size = new Size(270, 26)
            };

            var ok = CreateFlatButton("Conferma", 16, 56, 270, AccentBlue);
            ok.DialogResult = DialogResult.OK;

            dlg.Controls.AddRange(new Control[] { box, ok });
            dlg.AcceptButton = ok;

            return dlg.ShowDialog() == DialogResult.OK ? box.Text : "";
        }

        public void LockAndUnmount()
        {
            _activeVault?.Dispose();
            _activeVault = null;
            _lblMountBadge.Text = "Stato: Nessuna unita' montata";
            _lblMountBadge.ForeColor = TextMuted;
        }
    }
}