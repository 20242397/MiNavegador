using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MiNavegador
{
    public class TabInfo
    {
        public WebView2 Web { get; set; } = default!;
        public string LastUrl { get; set; } = "https://www.google.com/";
        public string Title { get; set; } = "Nueva pestaña";
        public string FindQuery { get; set; } = "";
        public double Zoom { get; set; } = 1.0;
    }

    public class NavegadorForm : Form
    {
        // UI
        private readonly ToolStrip _toolbar = new ToolStrip();
        private readonly ToolStripButton _btnBack = new ToolStripButton("←");
        private readonly ToolStripButton _btnForward = new ToolStripButton("→");
        private readonly ToolStripButton _btnRefresh = new ToolStripButton("↻");
        private readonly ToolStripButton _btnHome = new ToolStripButton("🏠");
        private readonly ToolStripTextBox _txtAddress = new ToolStripTextBox() { Width = 600 };
        private readonly ToolStripButton _btnGo = new ToolStripButton("Ir");
        private readonly ToolStripDropDownButton _menu = new ToolStripDropDownButton("☰");
        private readonly ToolStripDropDownButton _zoom = new ToolStripDropDownButton("100%");
        private readonly ToolStripButton _btnNewTab = new ToolStripButton("+");
        private readonly StatusStrip _status = new StatusStrip();
        private readonly ToolStripStatusLabel _lblStatus = new ToolStripStatusLabel("Listo");
        private readonly TabControl _tabs = new TabControl();

        // Find-in-page (Ctrl+F)
        private readonly Panel _findPanel = new Panel();
        private readonly TextBox _findBox = new TextBox();
        private readonly Button _findPrev = new Button();
        private readonly Button _findNext = new Button();
        private readonly Button _findClose = new Button();

        // Config / estado
        private const string HomeUrl = "https://www.google.com/";
        private bool _darkMode = true;
        private readonly string _bookmarksFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MiNavegador.bookmarks.json"
        );
        private List<string> _bookmarks = new List<string>();

        public NavegadorForm()
        {
            Text = "Mi Navegador";
            Width = 1200;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;

            // --- Estilo moderno básico ---
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(900, 600);
            UseModernTheme(_darkMode);

            // Toolbar
            _toolbar.GripStyle = ToolStripGripStyle.Hidden;
            _btnBack.ToolTipText = "Atrás (Alt+←)";
            _btnForward.ToolTipText = "Adelante (Alt+→)";
            _btnRefresh.ToolTipText = "Recargar (Ctrl+R)";
            _btnHome.ToolTipText = "Inicio";
            _btnNewTab.ToolTipText = "Nueva pestaña (Ctrl+T)";
            _txtAddress.ToolTipText = "Escribe URL o búsqueda (Enter)";
            _btnGo.ToolTipText = "Ir";

            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                _btnNewTab, new ToolStripSeparator(),
                _btnBack, _btnForward, new ToolStripSeparator(),
                _btnRefresh, _btnHome, new ToolStripSeparator(),
                new ToolStripLabel("URL: "), _txtAddress, _btnGo,
                new ToolStripSeparator(), _zoom, _menu
            });

            // Menú (config y utilidades)
            var miNewTab = new ToolStripMenuItem("Nueva pestaña (Ctrl+T)", null, (_, __) => AddTab(HomeUrl));
            var miCloseTab = new ToolStripMenuItem("Cerrar pestaña (Ctrl+W)", null, (_, __) => CloseCurrentTab());
            var miDark = new ToolStripMenuItem("Modo oscuro", null, (_, __) => ToggleDarkMode()) { Checked = _darkMode, CheckOnClick = true };
            var miFind = new ToolStripMenuItem("Buscar en la página (Ctrl+F)", null, (_, __) => ShowFindPanel());
            var miDownloads = new ToolStripMenuItem("Carpeta de descargas", null, (_, __) => OpenDownloadsFolder());
            var miAbout = new ToolStripMenuItem("Acerca de / Ayuda", null, (_, __) => ShowAbout());
            var miAddBookmark = new ToolStripMenuItem("Agregar a marcadores (Ctrl+D)", null, (_, __) => AddBookmark());
            var miBookmarks = new ToolStripMenuItem("Marcadores");
            _menu.DropDownItems.AddRange(new ToolStripItem[] { miNewTab, miCloseTab, new ToolStripSeparator(), miDark, miFind, miDownloads, new ToolStripSeparator(), miAddBookmark, miBookmarks, new ToolStripSeparator(), miAbout });

            // Zoom
            _zoom.DropDownItems.Add("50%", null, (_, __) => SetZoom(0.5));
            _zoom.DropDownItems.Add("75%", null, (_, __) => SetZoom(0.75));
            _zoom.DropDownItems.Add("100%", null, (_, __) => SetZoom(1.0));
            _zoom.DropDownItems.Add("125%", null, (_, __) => SetZoom(1.25));
            _zoom.DropDownItems.Add("150%", null, (_, __) => SetZoom(1.5));
            _zoom.DropDownItems.Add("Reset (Ctrl+0)", null, (_, __) => SetZoom(1.0));

            // Status
            _status.Items.Add(_lblStatus);

            // Tabs
            _tabs.Dock = DockStyle.Fill;
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.Padding = new Point(20, 5);
            _tabs.DrawItem += Tabs_DrawItem;
            _tabs.MouseDown += Tabs_MouseDown;
            _tabs.SelectedIndexChanged += (_, __) => SyncUIWithTab();

            // Find panel
            BuildFindPanel();

            // Layout
            Controls.Add(_tabs);
            Controls.Add(_findPanel);
            Controls.Add(_toolbar);
            Controls.Add(_status);

            // Eventos UI
            _btnBack.Click += (_, __) => CurrentWebAction(w => { if (w.CanGoBack) w.GoBack(); });
            _btnForward.Click += (_, __) => CurrentWebAction(w => { if (w.CanGoForward) w.GoForward(); });
            _btnRefresh.Click += (_, __) => CurrentWebAction(w => w.Reload());
            _btnHome.Click += (_, __) => NavigateTo(HomeUrl);
            _btnGo.Click += (_, __) => NavigateTo(_txtAddress.Text);
            _btnNewTab.Click += (_, __) => AddTab(HomeUrl);
            _txtAddress.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    NavigateTo(_txtAddress.Text);
                    e.SuppressKeyPress = true;
                }
            };

            // Atajos
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.L) { _txtAddress.Focus(); _txtAddress.SelectAll(); }
                if (e.Control && e.KeyCode == Keys.R) CurrentWebAction(w => w.Reload());
                if (e.Alt && e.KeyCode == Keys.Left) CurrentWebAction(w => { if (w.CanGoBack) w.GoBack(); });
                if (e.Alt && e.KeyCode == Keys.Right) CurrentWebAction(w => { if (w.CanGoForward) w.GoForward(); });
                if (e.Control && e.KeyCode == Keys.T) AddTab(HomeUrl);
                if (e.Control && e.KeyCode == Keys.W) CloseCurrentTab();
                if (e.Control && e.KeyCode == Keys.F) ShowFindPanel();
                if (e.Control && e.KeyCode == Keys.D) AddBookmark();
                if (e.Control && (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)) AdjustZoom(+0.1);
                if (e.Control && (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)) AdjustZoom(-0.1);

                if (e.Control && e.KeyCode == Keys.D0) SetZoom(1.0);
            };

            // Carga de marcadores y primera pestaña
            LoadBookmarks(miBookmarks);
            AddTab(HomeUrl);
        }

        private void UseModernTheme(bool dark)
        {
            var bg = dark ? Color.FromArgb(30, 30, 32) : Color.White;
            var fg = dark ? Color.Gainsboro : Color.Black;
            var chrome = dark ? Color.FromArgb(45, 45, 48) : Color.FromArgb(245, 245, 248);

            BackColor = bg;
            _toolbar.BackColor = chrome;
            _toolbar.ForeColor = fg;
            _status.BackColor = chrome;
            _status.ForeColor = fg;
            _lblStatus.ForeColor = fg;
            _tabs.BackColor = bg;
            _tabs.ForeColor = fg;
            _txtAddress.BackColor = dark ? Color.FromArgb(55, 55, 58) : Color.White;
            _txtAddress.ForeColor = fg;

            // Find panel
            _findPanel.BackColor = chrome;
            foreach (Control c in _findPanel.Controls)
            {
                if (c is TextBox tb)
                {
                    tb.BackColor = dark ? Color.FromArgb(55, 55, 58) : Color.White;
                    tb.ForeColor = fg;
                }
            }
        }

        private void BuildFindPanel()
        {
            _findPanel.Height = 36;
            _findPanel.Dock = DockStyle.Top;
            _findPanel.Visible = false;

            _findBox.PlaceholderText = "Buscar en la página…";
            _findBox.BorderStyle = BorderStyle.FixedSingle;
            _findBox.Width = 300;
            _findBox.Location = new Point(10, 6);

            _findPrev.Text = "↑";
            _findPrev.Width = 32;
            _findPrev.Location = new Point(320, 4);

            _findNext.Text = "↓";
            _findNext.Width = 32;
            _findNext.Location = new Point(356, 4);

            _findClose.Text = "✕";
            _findClose.Width = 32;
            _findClose.Location = new Point(392, 4);

            _findPrev.Click += (_, __) => FindInPage(false);
            _findNext.Click += (_, __) => FindInPage(true);
            _findClose.Click += (_, __) => { _findPanel.Visible = false; FocusCurrentWeb(); };

            _findBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { FindInPage(true); e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Escape) { _findPanel.Visible = false; FocusCurrentWeb(); }
            };

            _findPanel.Controls.AddRange(new Control[] { _findBox, _findPrev, _findNext, _findClose });
        }

        // --- Pestañas ---
        private void AddTab(string url)
        {
            var page = new TabPage("Cargando…");
            var web = new WebView2 { Dock = DockStyle.Fill };
            page.Controls.Add(web);
            _tabs.TabPages.Add(page);
            _tabs.SelectedTab = page;

            var info = new TabInfo { Web = web, LastUrl = url };
            page.Tag = info;

            InitializeWeb(info);
        }

        private void CloseCurrentTab()
        {
            if (_tabs.TabPages.Count <= 1)
            { // evita quedarte sin pestañas
                CurrentWebAction(w => w.CoreWebView2?.Navigate(HomeUrl));
                return;
            }
            var idx = _tabs.SelectedIndex;
            _tabs.TabPages.RemoveAt(idx);
            if (_tabs.TabPages.Count > 0)
                _tabs.SelectedIndex = Math.Max(0, idx - 1);
        }

        private void Tabs_DrawItem(object? sender, DrawItemEventArgs e)
        {
            var tab = _tabs.TabPages[e.Index];
            var rect = e.Bounds;
            var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            var bg = _darkMode
                ? (selected ? Color.FromArgb(60, 60, 64) : Color.FromArgb(40, 40, 44))
                : (selected ? Color.White : Color.FromArgb(235, 235, 238));
            var fg = _darkMode ? Color.Gainsboro : Color.Black;

            using var b = new SolidBrush(bg);
            e.Graphics.FillRectangle(b, rect);
            TextRenderer.DrawText(e.Graphics, tab.Text, Font, rect, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

            // Botón cerrar (x)
            var closeRect = new Rectangle(rect.Right - 20, rect.Top + (rect.Height - 16) / 2, 16, 16);
            TextRenderer.DrawText(e.Graphics, "x", Font, closeRect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void Tabs_MouseDown(object? sender, MouseEventArgs e)
        {
            for (int i = 0; i < _tabs.TabPages.Count; i++)
            {
                var rect = _tabs.GetTabRect(i);
                var closeRect = new Rectangle(rect.Right - 20, rect.Top + (rect.Height - 16) / 2, 16, 16);
                if (closeRect.Contains(e.Location))
                {
                    _tabs.TabPages.RemoveAt(i);
                    break;
                }
            }
        }

        // --- WebView2 ---
        private async void InitializeWeb(TabInfo info)
        {
            try
            {
                _lblStatus.Text = "Inicializando motor…";
                await info.Web.EnsureCoreWebView2Async();

                info.Web.CoreWebView2.HistoryChanged += (_, __) => SyncUIWithTab();
                info.Web.CoreWebView2.NavigationStarting += (_, e) =>
                {
                    _lblStatus.Text = "Cargando…";
                    if (!string.IsNullOrWhiteSpace(e.Uri)) _txtAddress.Text = e.Uri;
                    UpdateTabTitle("Cargando…");
                };
                info.Web.CoreWebView2.NavigationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess)
                    {
                        // Página de error amigable
                        var html = $@"
<html><body style='font-family:Segoe UI; background:#1e1e20; color:#eaeaea; display:flex; height:100vh; align-items:center; justify-content:center;'>
<div style='max-width:600px;'>
<h2>Ups… no se pudo cargar la página</h2>
<p><b>Error:</b> {e.WebErrorStatus}</p>
<button onclick='history.back()' style='padding:8px 12px;border:0;border-radius:8px;background:#3a3a3f;color:white;'>Volver</button>
</div></body></html>";
                        info.Web.CoreWebView2.NavigateToString(html);
                        _lblStatus.Text = $"Error: {e.WebErrorStatus}";
                        UpdateTabTitle("Error de carga");
                        return;
                    }
                    _lblStatus.Text = "Completado";
                    var title = info.Web.CoreWebView2.DocumentTitle;
                    if (string.IsNullOrWhiteSpace(title)) title = "Página";
                    UpdateTabTitle(title);
                };
                info.Web.CoreWebView2.NewWindowRequested += (_, e) =>
                {
                    // Abrir pop-ups en NUEVA pestaña (controlado)
                    e.Handled = true;
                    AddTab(string.IsNullOrWhiteSpace(e.Uri) ? HomeUrl : e.Uri);
                };
                info.Web.CoreWebView2.DownloadStarting += (_, e) =>
                {
                    _lblStatus.Text = $"Descargando: {Path.GetFileName(e.ResultFilePath)}";
                    e.ResultFilePath = e.ResultFilePath; // Deja por defecto; podrías personalizar carpeta
                    e.Handled = false; // deja que siga
                };
                info.Web.CoreWebView2.PermissionRequested += (_, e) =>
                {
                    // Decisión conservadora: pedir confirmación en cámara/mic
                    if (e.PermissionKind is CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Camera)
                        e.State = CoreWebView2PermissionState.Deny;
                    else
                        e.State = CoreWebView2PermissionState.Allow;
                };
                info.Web.CoreWebView2.DOMContentLoaded += (_, __) =>
                {
                    // Ajusta zoom en cada carga
                    info.Web.ZoomFactor = info.Zoom;
                };

                Navigate(info, info.LastUrl);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Error inicializando WebView2";
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Navigate(TabInfo info, string input)
        {
            var url = NormalizeUrl(input);
            info.LastUrl = url;
            try
            {
                info.Web.Source = new Uri(url);
            }
            catch
            {
                info.Web.Source = new Uri($"https://www.google.com/search?q={Uri.EscapeDataString(input)}");
            }
        }

        private void NavigateTo(string input)
        {
            var info = CurrentInfo();
            if (info == null) return;
            Navigate(info, input);
        }

        private TabInfo? CurrentInfo()
        {
            if (_tabs.SelectedTab == null) return null;
            return (TabInfo?)_tabs.SelectedTab.Tag;
        }

        private void CurrentWebAction(Action<WebView2> action)
        {
            var info = CurrentInfo();
            if (info?.Web != null && info.Web.CoreWebView2 != null)
                action(info.Web);
        }

        private void SyncUIWithTab()
        {
            var info = CurrentInfo();
            if (info?.Web?.CoreWebView2 == null) return;
            _btnBack.Enabled = info.Web.CanGoBack;
            _btnForward.Enabled = info.Web.CanGoForward;
            _txtAddress.Text = info.Web.Source?.ToString() ?? info.LastUrl;
            var title = info.Web.CoreWebView2.DocumentTitle;
            UpdateTabTitle(string.IsNullOrWhiteSpace(title) ? "Nueva pestaña" : title);
        }

        private void UpdateTabTitle(string title)
        {
            if (_tabs.SelectedTab == null) return;

            // 🔹 Mantiene el título fijo del formulario
            Text = "Mi Navegador";

            // 🔹 Mantiene el texto "Pestaña" o "Inicio" en la pestaña actual
            if (string.IsNullOrWhiteSpace(title) || title == "about:blank")
                _tabs.SelectedTab.Text = "Pestaña";
            else
                _tabs.SelectedTab.Text = "Pestaña";

            // 🔹 Mantiene el seguimiento interno (no afecta navegación)
            var info = CurrentInfo();
            if (info != null)
                info.Title = title;

            // 🔹 Actualiza la barra de direcciones si cambia la URL
            if (info?.Web?.CoreWebView2 != null)
            {
                try
                {
                    string currentUrl = info.Web.Source?.ToString() ?? info.LastUrl;
                    if (!string.IsNullOrWhiteSpace(currentUrl))
                        _txtAddress.Text = currentUrl;
                }
                catch
                {
                    // Ignorar errores de sincronización de URL
                }
            }
        }



        private static string NormalizeUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "https://www.google.com/";

            // 🔹 Si es una URL completa, la dejamos igual
            if (Uri.TryCreate(input, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            // 🔹 Si parece una dirección (tiene un punto y no tiene espacios), agregamos https://
            if (input.Contains(".") && !input.Contains(" "))
                return "https://" + input.Trim();

            // 🔹 Si no parece URL, asumimos que es una búsqueda
            return $"https://www.bing.com/search?q={Uri.EscapeDataString(input)}";

        }


        private static string Truncate(string s, int len) => s.Length <= len ? s : s[..len] + "…";

        // --- Zoom ---
        private void SetZoom(double factor)
        {
            var info = CurrentInfo();
            if (info?.Web?.CoreWebView2 == null) return;
            info.Zoom = factor;
            info.Web.ZoomFactor = factor;
            _zoom.Text = $"{(int)(factor * 100)}%";
        }

        private void AdjustZoom(double delta)
        {
            var info = CurrentInfo();
            if (info == null) return;
            var f = Math.Max(0.5, Math.Min(2.0, info.Zoom + delta));
            SetZoom(f);
        }

        // --- Find in page ---
        private void ShowFindPanel()
        {
            _findPanel.Visible = true;
            _findBox.Focus();
            _findBox.SelectAll();
        }

        private async void FindInPage(bool forward)
        {
            var info = CurrentInfo();
            if (info?.Web?.CoreWebView2 == null) return;
            var q = _findBox.Text ?? "";
            info.FindQuery = q;

            try
            {
                // Para versiones nuevas (≥ 1.0.2210.55)
                var method = info.Web.CoreWebView2.GetType().GetMethod("FindAsync");
                if (method != null)
                {
                    await (dynamic)method.Invoke(info.Web.CoreWebView2, new object[] { q, forward, false, false });
                }
                else
                {
                    // Versión vieja: usar script
                    await info.Web.ExecuteScriptAsync($"window.find({System.Text.Json.JsonSerializer.Serialize(q)});");
                }
            }
            catch
            {
                await info.Web.ExecuteScriptAsync($"window.find({System.Text.Json.JsonSerializer.Serialize(q)});");
            }
        }


        private void FocusCurrentWeb() => CurrentWebAction(w => w.Focus());

        // --- Bookmarks ---
        private void LoadBookmarks(ToolStripMenuItem miBookmarks)
        {
            try
            {
                if (File.Exists(_bookmarksFile))
                {
                    var json = File.ReadAllText(_bookmarksFile);
                    _bookmarks = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }
            catch { _bookmarks = new List<string>(); }

            miBookmarks.DropDownItems.Clear();
            if (_bookmarks.Count == 0)
            {
                miBookmarks.DropDownItems.Add("(sin marcadores)");
            }
            else
            {
                foreach (var url in _bookmarks)
                {
                    miBookmarks.DropDownItems.Add(url, null, (_, __) => NavigateTo(url));
                }
                miBookmarks.DropDownItems.Add(new ToolStripSeparator());
                miBookmarks.DropDownItems.Add("Administrar…", null, (_, __) => ManageBookmarks());
            }
        }

        private void AddBookmark()
        {
            var info = CurrentInfo();
            var url = info?.Web?.Source?.ToString() ?? info?.LastUrl ?? HomeUrl;
            if (string.IsNullOrWhiteSpace(url)) return;

            if (!_bookmarks.Contains(url))
            {
                _bookmarks.Add(url);
                SaveBookmarks();
                MessageBox.Show("Marcador agregado.", "Marcadores", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void SaveBookmarks()
        {
            try
            {
                File.WriteAllText(_bookmarksFile, JsonSerializer.Serialize(_bookmarks, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore */ }
        }

        private void ManageBookmarks()
        {
            using var dlg = new Form()
            {
                Text = "Marcadores",
                Width = 600,
                Height = 400,
                StartPosition = FormStartPosition.CenterParent
            };
            var list = new ListBox() { Dock = DockStyle.Fill };
            list.Items.AddRange(_bookmarks.ToArray());
            var panel = new FlowLayoutPanel() { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft };
            var btnDel = new Button() { Text = "Eliminar", Width = 100 };
            var btnClose = new Button() { Text = "Cerrar", Width = 100 };
            btnDel.Click += (_, __) =>
            {
                if (list.SelectedItem is string s)
                {
                    _bookmarks.Remove(s);
                    SaveBookmarks();
                    list.Items.Clear();
                    list.Items.AddRange(_bookmarks.ToArray());
                }
            };
            btnClose.Click += (_, __) => dlg.Close();
            panel.Controls.AddRange(new Control[] { btnClose, btnDel });
            dlg.Controls.Add(list);
            dlg.Controls.Add(panel);
            dlg.ShowDialog(this);
        }

        // --- Miscelánea ---
        private void ToggleDarkMode()
        {
            _darkMode = !_darkMode;
            UseModernTheme(_darkMode);
        }

        private void OpenDownloadsFolder()
        {
            try
            {
                var folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + Path.DirectorySeparatorChar + "Downloads";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo() { FileName = folder, UseShellExecute = true });
            }
            catch { }
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "Mi Navegador (WebView2)\n\n" +
                "• Pestañas, modo oscuro, zoom, búsqueda en página\n" +
                "• Marcadores y manejo básico de errores/descargas\n" +
                "Hecho por: <tu nombre>\n\n" +
                "Controles:\n" +
                "Ctrl+L (URL) • Ctrl+T (Nueva) • Ctrl+W (Cerrar)\n" +
                "Alt+←/→ (Historial) • Ctrl+F (Buscar)\n" +
                "Ctrl+±/0 (Zoom) • Ctrl+R (Recargar)",
                "Acerca de",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new NavegadorForm());
        }
    }
}
