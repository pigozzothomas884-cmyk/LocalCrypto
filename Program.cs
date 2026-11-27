using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
                Text = "LocalCrypto - CiaoDev™",
                Visible = true
            };

            _trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    _popup.ToggleNearTray();
            };

            var menu = new ContextMenuStrip
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(29, 29, 31),
                ShowImageMargin = false
            };
            menu.Items.Add("Apri LocalCrypto", null, (_, _) => _popup.ToggleNearTray());
            menu.Items.Add("Blocca tutto ed esci", null, (_, _) =>
            {
                _popup.LockAndUnmount();
                _trayIcon.Visible = false;
                Application.Exit();
            });
            _trayIcon.ContextMenuStrip = menu;
        }
    }

    public sealed class CiaoDevPopupForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // Palette ufficiale CiaoDev™ (style.css)
        private static readonly Color BgRoot = Color.White;
        private static readonly Color BgAlt = Color.FromArgb(245, 245, 247);      // --bg-alt: #f5f5f7
        private static readonly Color TextPrimary = Color.FromArgb(29, 29, 31);     // --text: #1d1d1f
        private static readonly Color TextMuted = Color.FromArgb(110, 110, 115);    // --text-muted: #6e6e73
        private static readonly Color Accent = Color.FromArgb(0, 113, 227);         // --accent: #0071e3
        private static readonly Color AccentHover = Color.FromArgb(0, 119, 237);    // --accent-hover: #0077ed
        private static readonly Color BorderSubtle = Color.FromArgb(229, 229, 234); // --border: #e5e5ea

        private MountedVault? _activeVault;
        private string _selectedFolder = "";
        private int _driveIndex = 0;
        private readonly string[] _drives = { "V:", "W:", "X:", "Y:", "Z:" };

        private readonly Label _lblStatus;
        private readonly Label _lblFolderPath;
        private readonly TextBox _txtSecret;
        private readonly CheckBox _chkRecovery;
        private readonly PillButton _btnDrive;

        public CiaoDevPopupForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = BgRoot;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9.5f);
            Padding = new Padding(22);
            MinimumSize = new Size(420, 440);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            Deactivate += (_, _) =>
            {
                if (!Modal) Hide();
            };

            Paint += (_, e) =>
            {
                using var pen = new Pen(BorderSubtle, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = BgRoot
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            // 1. Header con logo e marchio CiaoDev™
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var picLogo = new PictureBox
            {
                Size = new Size(28, 28),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = (Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield).ToBitmap(),
                Margin = new Padding(0, 0, 10, 0),
                Anchor = AnchorStyles.Left
            };

            var lblTitle = new Label
            {
                Text = "LocalCrypto",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = TextPrimary,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };

            var lblBrand = new PillBadge("CiaoDev™", BgAlt, TextMuted)
            {
                Anchor = AnchorStyles.Right
            };

            header.Controls.Add(picLogo, 0, 0);
            header.Controls.Add(lblTitle, 1, 0);
            header.Controls.Add(lblBrand, 2, 0);
            root.Controls.Add(header);

            // 2. Stato attivo
            _lblStatus = new Label
            {
                Text = "Nessuna unita' montata",
                Font = new Font("Segoe UI", 9f),
                ForeColor = TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 16)
            };
            root.Controls.Add(_lblStatus);

            // 3. Card Cartella arrotondata (#f5f5f7)
            var folderCard = new RoundedCardPanel(BgAlt, BorderSubtle, 16)
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(14, 0, 14, 0),
                Margin = new Padding(0, 0, 0, 10)
            };

            _lblFolderPath = new Label
            {
                Text = "Seleziona una cartella su Drive o locale...",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            folderCard.Controls.Add(_lblFolderPath);
            root.Controls.Add(folderCard);

            // 4. Pulsanti a pillola Scegli / Cifra
            var folderActions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Height = 40,
                Margin = new Padding(0, 0, 0, 16)
            };
            folderActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            folderActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            var btnBrowse = new PillButton("Scegli cartella", BgRoot, Accent, Accent, Accent, Color.White)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 6, 0)
            };
            btnBrowse.Click += (_, _) => PickFolder();

            var btnEncrypt = new PillButton("Crea / Cifra", BgRoot, BorderSubtle, TextPrimary, BgAlt, TextPrimary)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(6, 0, 0, 0)
            };
            btnEncrypt.Click += (_, _) => InitializeOrEncryptFolder();

            folderActions.Controls.Add(btnBrowse, 0, 0);
            folderActions.Controls.Add(btnEncrypt, 1, 0);
            root.Controls.Add(folderActions);

            // 5. Campo Password a pillola (.search-input style)
            var pwdCard = new RoundedCardPanel(BgAlt, BorderSubtle, 20)
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(16, 10, 16, 8),
                Margin = new Padding(0, 0, 0, 10)
            };

            _txtSecret = new TextBox
            {
                PlaceholderText = "Password cartella",
                UseSystemPasswordChar = true,
                BackColor = BgAlt,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 10f)
            };
            pwdCard.Controls.Add(_txtSecret);
            root.Controls.Add(pwdCard);

            // 6. Riga Recovery Key + Reimposta password
            var recoveryRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                AutoSize = true,
                Margin = new Padding(2, 0, 2, 16)
            };
            recoveryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            recoveryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _chkRecovery = new CheckBox
            {
                Text = "Usa chiave di recupero",
                ForeColor = TextMuted,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            _chkRecovery.CheckedChanged += (_, _) =>
            {
                _txtSecret.UseSystemPasswordChar = !_chkRecovery.Checked;
                _txtSecret.PlaceholderText = _chkRecovery.Checked
                    ? "XXXXXX-XXXXXX-XXXXXX-..."
                    : "Password cartella";
            };

            var lnkReset = new LinkLabel
            {
                Text = "Reimposta password",
                LinkColor = Accent,
                ActiveLinkColor = AccentHover,
                LinkBehavior = LinkBehavior.HoverUnderline,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(12, 2, 0, 0)
            };
            lnkReset.LinkClicked += (_, _) => ResetPasswordFlow();

            recoveryRow.Controls.Add(_chkRecovery, 0, 0);
            recoveryRow.Controls.Add(lnkReset, 1, 0);
            root.Controls.Add(recoveryRow);

            // 7. Selettore unita' a pillola + CTA Primaria (#0071e3)
            var mountRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                Height = 42,
                Margin = new Padding(0, 0, 0, 10)
            };
            mountRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
            mountRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _btnDrive = new PillButton("Unita' V:", BgAlt, BorderSubtle, TextPrimary, BorderSubtle, TextPrimary)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 8, 0)
            };
            _btnDrive.Click += (_, _) =>
            {
                _driveIndex = (_driveIndex + 1) % _drives.Length;
                _btnDrive.Text = $"Unita' {_drives[_driveIndex]}";
            };

            var btnMount = new PillButton("Sblocca e monta disco", Accent, Accent, Color.White, AccentHover, Color.White)
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
            };
            btnMount.Click += (_, _) => UnlockAndMount();

            mountRow.Controls.Add(_btnDrive, 0, 0);
            mountRow.Controls.Add(btnMount, 1, 0);
            root.Controls.Add(mountRow);

            // 8. Bottone secondario Blocca e smonta
            var btnLock = new PillButton("Blocca e smonta unita' attiva", BgRoot, BorderSubtle, TextMuted, BgAlt, TextPrimary)
            {
                Dock = DockStyle.Top,
                Height = 38,
                Margin = new Padding(0, 0, 0, 14)
            };
            btnLock.Click += (_, _) => LockAndUnmount();
            root.Controls.Add(btnLock);

            // 9. Footer minimale
            var lblFooter = new Label
            {
                Text = "Disco locale privato non condiviso in rete  •  CiaoDev™",
                Font = new Font("Segoe UI", 8.2f),
                ForeColor = TextMuted,
                AutoSize = true
            };
            root.Controls.Add(lblFooter);

            Controls.Add(root);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int cornerPreference = 2; // DWMWCP_ROUND su Windows 11
            DwmSetWindowAttribute(Handle, 33, ref cornerPreference, sizeof(int));
        }

        public void ToggleNearTray()
        {
            if (Visible)
            {
                Hide();
                return;
            }

            PerformLayout();
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
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
                _lblFolderPath.Text = (isVault ? "[Cifrata] " : "[In chiaro] ") + _selectedFolder;
                _lblFolderPath.ForeColor = TextPrimary;
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

            _lblFolderPath.Text = "[Cifrata] " + _selectedFolder;
            _txtSecret.Clear();
            ShowRecoveryKeyOptions(recoveryKey);
        }

        private void ShowRecoveryKeyOptions(string recoveryKey)
        {
            string sheet =
                "====================================================\r\n" +
                "  LOCALCRYPTO - CHIAVE DI RECUPERO (CiaoDev™)\r\n" +
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
                string drive = _drives[_driveIndex];

                _activeVault = new MountedVault(drive, _selectedFolder, mk);
                _lblStatus.Text = $"Attivo: {drive} -> {Path.GetFileName(_selectedFolder)}";
                _lblStatus.ForeColor = Accent;
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
                Text = "LocalCrypto - Nuova password",
                Size = new Size(360, 175),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = BgRoot,
                ForeColor = TextPrimary
            };

            var card = new RoundedCardPanel(BgAlt, BorderSubtle, 20)
            {
                Location = new Point(20, 20),
                Size = new Size(304, 42),
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

            var ok = new PillButton("Conferma", Accent, Accent, Color.White, AccentHover, Color.White)
            {
                Location = new Point(20, 76),
                Size = new Size(304, 40),
                DialogResult = DialogResult.OK
            };

            dlg.Controls.AddRange(new Control[] { card, ok });
            dlg.AcceptButton = ok;

            return dlg.ShowDialog() == DialogResult.OK ? box.Text : "";
        }

        public void LockAndUnmount()
        {
            _activeVault?.Dispose();
            _activeVault = null;
            _lblStatus.Text = "Nessuna unita' montata";
            _lblStatus.ForeColor = TextMuted;
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
        private readonly Color _bg;
        public PillBadge(string text, Color bg, Color fg)
        {
            Text = text;
            _bg = bg;
            ForeColor = fg;
            Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
            Size = new Size(76, 24);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
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
            base.OnClick(e);
            if (DialogResult != DialogResult.None && FindForm() is Form f)
                f.DialogResult = DialogResult;
        }

        public void NotifyDefault(bool value)
        {
        }

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

            Color fill = _hover ? _hoverBg : _bg;
            Color textCol = _hover ? _hoverFg : _fg;
            Color borderCol = _hover ? _hoverBg : _border;

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