using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MuseDesktop
{
    public sealed class MainWindow : Form
    {
        readonly AccountStore store;
        readonly bool smoke;
        readonly PreferenceStore preferences;
        Palette palette;
        Color background { get { return palette.Background; } }
        Color sidebarColor { get { return palette.Surface; } }
        Color accent { get { return palette.Accent; } }
        readonly FlowLayoutPanel list = new FlowLayoutPanel();
        readonly Panel content = new Panel();
        readonly Label heading = new Label();
        readonly Label state = new Label();
        readonly Label location = new Label();
        readonly Panel empty = new Panel();
        readonly List<Form> popups = new List<Form>();
        readonly List<Task> pendingPopups = new List<Task>();
        readonly List<Button> actions = new List<Button>();
        readonly ToolTip hints = new ToolTip { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 5000 };
        FreshButton themeButton;
        readonly Panel sidebar;
        FreshButton sidebarToggle;
        FormWindowState lastUsableWindowState;
        int themeRevision;
        Task cacheTask = Task.FromResult(0);
        internal int LastCacheCleanupProfiles;
        internal int LastCacheCleanupFailures;
        internal string LastThemeSync = "idle";
        WebView2 browser;
        CoreWebView2Environment environment;
        Account current;
        Account selected;
        int pendingBrowserPid;
        bool busy, closing, allowClose;
        string testHome;
        internal readonly List<string> SmokeChecks = new List<string>();

        public MainWindow(AccountStore accountStore, bool smokeMode)
        {
            store = accountStore;
            smoke = smokeMode;
            preferences = new PreferenceStore(store.Root);
            palette = new Palette(preferences.Value.Theme);
            Text = "Muse 管理系统";
            Name = "MuseDesktop";
            MinimumSize = new Size(960, 660);
            Size = new Size(1360, 860);
            RestoreWindowLayout();
            lastUsableWindowState = WindowState;
            Resize += delegate { if (WindowState != FormWindowState.Minimized) lastUsableWindowState = WindowState; };
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = background;
            ForeColor = palette.Text;
            Font = new Font("Microsoft YaHei UI", 9.5F);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            sidebar = new Panel { Name = "AccountSidebar", Dock = DockStyle.Left, Width = 192, Visible = !preferences.Value.SidebarCollapsed, BackColor = palette.Sidebar, Padding = new Padding(10, 12, 10, 8), Tag = "sidebar" };
            sidebar.Paint += delegate(object sender, PaintEventArgs args) { using (var pen = new Pen(palette.Border)) args.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height); };
            var brand = new Label { Text = "Muse AI", AutoSize = false, Height = 34, Dock = DockStyle.Top, Font = new Font("Segoe UI", 18F, FontStyle.Bold), ForeColor = palette.Text };
            var add = MakeButton("＋ 添加账号", async delegate { await AddAccount(); });
            add.Dock = DockStyle.Top; add.Height = 32; add.Primary = true;
            var listTitle = new Label { Text = "账号", Dock = DockStyle.Top, Height = 32, Padding = new Padding(0, 12, 0, 0), ForeColor = palette.Muted, Tag = "muted", Font = new Font(Font.FontFamily, 8.5F) };
            list.Dock = DockStyle.Fill; list.FlowDirection = FlowDirection.TopDown; list.WrapContents = false; list.AutoScroll = true;
            list.Padding = new Padding(0, 5, 0, 0);
            var footer = new Label { Text = "v1.0.0", Dock = DockStyle.Bottom, Height = 18, ForeColor = palette.Muted, Font = new Font("Microsoft YaHei UI", 8F), Tag = "muted" };
            sidebar.Controls.Add(list); sidebar.Controls.Add(listTitle); sidebar.Controls.Add(add); sidebar.Controls.Add(brand); sidebar.Controls.Add(footer);

            var workspace = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            var toolbar = new Panel { Name = "CompactToolbar", Dock = DockStyle.Top, Height = 42 };
            toolbar.Paint += delegate(object sender, PaintEventArgs args) { using (var pen = new Pen(palette.Border)) args.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1); };
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(8, 4, 8, 4) };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
            heading.Text = "选择账号"; heading.Dock = DockStyle.Fill; heading.Margin = new Padding(0); heading.AutoEllipsis = true; heading.TextAlign = ContentAlignment.MiddleLeft;
            heading.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            heading.TextChanged += delegate { hints.SetToolTip(heading, heading.Text); };
            sidebarToggle = MakeButton("", delegate { ToggleSidebar(); return Task.FromResult(0); });
            sidebarToggle.Name = "SidebarToggle"; sidebarToggle.Width = 32; sidebarToggle.IconKind = "sidebar";
            UpdateSidebarHint(); header.Controls.Add(sidebarToggle, 0, 0);
            header.Controls.Add(heading, 1, 0);
            var utilities = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            themeButton = MakeButton("", async delegate { await SetAppearance(preferences.Value.Theme == "dark" ? "light" : "dark"); });
            themeButton.Name = "ThemeIconButton"; themeButton.Width = 32; themeButton.Height = 32;
            var settingsButton = MakeButton("设置", ShowSettings); settingsButton.Width = 56; settingsButton.Height = 32;
            utilities.Controls.Add(themeButton); utilities.Controls.Add(settingsButton); header.Controls.Add(utilities, 3, 0);
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            var back = MakeButton("←", delegate { if (browser != null && browser.CoreWebView2 != null && browser.CoreWebView2.CanGoBack) browser.CoreWebView2.GoBack(); return Task.FromResult(0); }); back.Width = 36; back.AccessibleName = "返回"; hints.SetToolTip(back, "返回上一页");
            var refresh = MakeButton("刷新", delegate { if (browser != null && browser.CoreWebView2 != null) browser.CoreWebView2.Reload(); return Task.FromResult(0); }); refresh.Width = 54;
            var home = MakeButton("首页", delegate { if (browser != null && browser.CoreWebView2 != null) browser.CoreWebView2.Navigate(Home); return Task.FromResult(0); }); home.Width = 54; hints.SetToolTip(home, "Muse 首页");
            row.Controls.Add(back); row.Controls.Add(refresh); row.Controls.Add(home); header.Controls.Add(row, 2, 0);
            toolbar.Controls.Add(header);
            state.Name = "OperationStatus"; state.Dock = DockStyle.Bottom; state.Height = 20; state.Padding = new Padding(8, 2, 0, 0); state.ForeColor = palette.Muted; state.Tag = "muted"; state.Font = new Font(Font.FontFamily, 8F);
            state.TextChanged += delegate { state.Visible = ImportantStatus(state.Text); hints.SetToolTip(themeButton, (palette.Dark ? "当前夜间，切换昼间" : "当前昼间，切换夜间") + "\n" + state.Text); };
            state.Text = "登录与聊天由 Muse 官方网页处理";
            content.Name = "MuseViewport"; content.Dock = DockStyle.Fill; content.BackColor = sidebarColor; content.Tag = "surface";
            empty.Dock = DockStyle.Fill; empty.BackColor = sidebarColor; empty.Tag = "surface";
            var welcome = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(35) };
            welcome.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            welcome.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
            welcome.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            welcome.Tag = "surface";
            var note = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei UI", 13F), Tag = "surface",
                Text = "先添加一个账号\n\n在 Muse 网页中自行登录，随后即可开始对话。\n你的账号登录状态会独立保存。" };
            welcome.Controls.Add(note, 0, 1); empty.Controls.Add(welcome); content.Controls.Add(empty);
            workspace.Controls.Add(content); workspace.Controls.Add(state); workspace.Controls.Add(toolbar);
            Controls.Add(workspace); Controls.Add(sidebar);
            RenderAccounts();
            ApplyAppearance();
            HandleCreated += delegate { WindowChrome.Apply(this, palette); };
            FormClosed += delegate { hints.Dispose(); };
            FormClosing += OnClosing;
            Shown += async delegate
            {
                if (smoke) await RunSmoke();
            };
        }

        string Home { get { return smoke ? testHome : "https://muse.ai/"; } }

        void RestoreWindowLayout()
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int width = preferences.Value.WindowWidth, height = preferences.Value.WindowHeight;
            if (width < 960 || width > 16384) width = 1360;
            if (height < 660 || height > 16384) height = 860;
            MinimumSize = new Size(Math.Min(960, area.Width), Math.Min(660, area.Height));
            Size = new Size(Math.Min(width, area.Width), Math.Min(height, area.Height));
            if (preferences.Value.WindowMaximized) WindowState = FormWindowState.Maximized;
        }

        internal void SaveWindowLayout()
        {
            Size size = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
            var value = preferences.Value.Copy();
            value.WindowWidth = size.Width; value.WindowHeight = size.Height;
            value.WindowMaximized = lastUsableWindowState == FormWindowState.Maximized;
            preferences.Save(value);
        }

        internal void ToggleSidebar()
        {
            var value = preferences.Value.Copy(); value.SidebarCollapsed = !value.SidebarCollapsed;
            preferences.Save(value);
            sidebar.Visible = !value.SidebarCollapsed;
            UpdateSidebarHint(); PerformLayout();
        }

        void UpdateSidebarHint()
        {
            sidebarToggle.AccessibleName = preferences.Value.SidebarCollapsed ? "展开账号栏" : "收起账号栏";
            hints.SetToolTip(sidebarToggle, sidebarToggle.AccessibleName); sidebarToggle.Invalidate();
        }

        static bool ImportantStatus(string value)
        {
            string[] markers = { "失败", "未完成", "未能", "未匹配", "已阻止", "无法", "仍在", "异常", "FAILED", "BLOCKED", "正在切换" };
            return markers.Any(marker => (value ?? "").Contains(marker));
        }

        void ApplyAppearance()
        {
            palette = new Palette(preferences.Value.Theme); BackColor = palette.Background; ForeColor = palette.Text;
            themeButton.Text = ""; themeButton.IconKind = palette.Dark ? "moon" : "sun";
            themeButton.AccessibleName = palette.Dark ? "切换到昼间模式" : "切换到夜间模式";
            hints.SetToolTip(themeButton, palette.Dark ? "当前夜间，切换昼间" : "当前昼间，切换夜间");
            Action<Control, Color> visit = null;
            visit = delegate(Control control, Color inherited)
            {
                var view = control as WebView2;
                if (view != null) { view.DefaultBackgroundColor = palette.Surface; return; }
                string tag = control.Tag as string;
                Color fill = tag == "surface" ? palette.Surface : tag == "sidebar" ? palette.Sidebar : inherited;
                control.BackColor = fill;
                control.ForeColor = tag == "muted" ? palette.Muted : tag == "accent" ? palette.Accent : palette.Text;
                var button = control as FreshButton;
                if (button != null) { button.Colors = palette; button.Invalidate(); }
                foreach (Control child in control.Controls) visit(child, fill);
            };
            foreach (Control child in Controls) visit(child, palette.Background);
            WindowChrome.Apply(this, palette);
            Invalidate(true);
        }

        internal async Task SetAppearance(string theme)
        {
            var value = preferences.Value.Copy(); value.Theme = theme;
            preferences.Save(value); ApplyAppearance();
            await SyncMuseAppearance();
        }

        async Task ShowSettings()
        {
            using (var dialog = new SettingsDialog(preferences.Value, store.Accounts, current ?? selected))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                preferences.Save(dialog.Draft); ApplyAppearance();
                if (browser != null) browser.ZoomFactor = preferences.Value.ZoomPercent / 100.0;
                foreach (Form popup in popups)
                    if (!popup.IsDisposed && popup.Controls.Count > 0) ((WebView2)popup.Controls[0]).ZoomFactor = preferences.Value.ZoomPercent / 100.0;
                await SyncMuseAppearance();
                if (dialog.Command == "add") await AddAccount();
                if (dialog.Command == "rename") await RenameAccount(dialog.Target);
                if (dialog.Command == "delete") await DeleteAccount(dialog.Target);
            }
        }

        async Task SyncMuseAppearance()
        {
            int revision = ++themeRevision;
            if (!preferences.Value.SyncMuseTheme)
            {
                LastThemeSync = "disabled";
                state.Text = "客户端主题已更新 · 网页联动已关闭";
                return;
            }
            if (browser == null || browser.CoreWebView2 == null || current == null)
            {
                LastThemeSync = "idle"; state.Text = "主题已保存 · 打开账号后同步到 Muse 网页"; return;
            }
            CoreWebView2 core = browser.CoreWebView2;
            Uri source;
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out source) ||
                (!smoke && (source.Scheme != "https" || source.Host != "muse.ai")))
            {
                LastThemeSync = "login-page"; state.Text = "客户端主题已更新 · 返回 Muse 对话页后同步"; return;
            }
            try
            {
                LastThemeSync = await ThemeBridge.ApplyAsync(core, preferences.Value.Theme, delegate
                {
                    return revision == themeRevision && !closing && browser != null && browser.CoreWebView2 == core && current != null && core.Source == source.AbsoluteUri;
                });
                if (revision != themeRevision || closing) return;
                state.Text = LastThemeSync == "confirmed" ? "昼夜主题已同步 · 此账号登录状态独立保存" :
                    LastThemeSync == "cancelled" ? "页面正在切换 · 主题将在加载后同步" : "客户端主题已更新 · 网页主题控件未匹配，请在 Muse 中手动设置";
            }
            catch
            {
                if (revision == themeRevision && !closing) { LastThemeSync = "failed"; state.Text = "客户端主题已更新 · 网页联动暂未完成"; }
            }
        }

        FreshButton MakeButton(string label, Func<Task> action)
        {
            var button = new FreshButton { Text = label, AutoSize = false, Size = new Size(96, 32), Margin = new Padding(0, 0, 4, 0), Colors = palette };
            button.Click += async delegate
            {
                if (busy || closing) return;
                SetBusy(true);
                try { await action(); }
                catch { Error("操作未完成，请重试。账号数据不会自动清空。", "OPERATION_FAILED"); }
                finally { if (!closing) SetBusy(false); }
            };
            actions.Add(button);
            return button;
        }

        void SetBusy(bool value)
        {
            busy = value;
            list.Enabled = !value;
            foreach (var button in actions) if (!button.IsDisposed) button.Enabled = !value;
            UseWaitCursor = value;
        }

        void RenderAccounts()
        {
            // List buttons are recreated; remove their disposed references as well.
            foreach (Control child in list.Controls.Cast<Control>().ToArray()) child.Dispose();
            list.Controls.Clear();
            actions.RemoveAll(b => b.IsDisposed);
            foreach (var item in store.Accounts)
            {
                Account account = item;
                bool selected = current != null && current.Id == account.Id;
                var button = MakeButton(account.Name, async delegate { await SelectAccount(account); });
                button.AccountCard = true; button.Selected = selected;
                button.AccessibleName = account.Name + (selected ? "，当前使用" : "，切换账号");
                hints.SetToolTip(button, account.Name + (selected ? "\n当前使用" : "\n点击切换，独立保存登录状态"));
                button.Width = 166; button.Height = 44; button.TextAlign = ContentAlignment.MiddleLeft;
                button.Padding = new Padding(0); button.Margin = new Padding(0, 0, 0, 6);
                button.Enabled = !busy;
                list.Controls.Add(button);
            }
        }

        async Task AddAccount()
        {
            string name = Prompt("添加账号", "填写本地备注（例如：日常账号）", "账号 " + (store.Accounts.Count + 1));
            if (name == null) return;
            var account = store.Add(name);
            RenderAccounts();
            await SelectAccount(account);
        }

        Task RenameAccount(Account target)
        {
            if (target == null) { state.Text = "请先选择一个账号"; return Task.FromResult(0); }
            string name = Prompt("重命名账号", "只修改本地备注，不修改 Muse 账号", target.Name);
            if (name != null) { store.Rename(target, name); if (current == target) heading.Text = name; RenderAccounts(); }
            return Task.FromResult(0);
        }

        async Task DeleteAccount(Account target)
        {
            if (target == null) { state.Text = "请先选择需要删除的账号"; return; }
            if (MessageBox.Show(this, "删除“" + target.Name + "”？\n\n将清理此客户端保存的登录状态。\n不会删除 Muse 云端账号、对话或 Google 云盘文件。", "删除账号", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await cacheTask;
            if (current == target && !await CloseSession()) { Error("网页仍在退出，请稍后重试删除。", "PROFILE_BUSY"); return; }
            try { store.Remove(target); if (selected == target) selected = null; state.Text = "账号和本地登录状态已删除"; }
            catch { Error("本地登录状态未能完全清理，账号记录已保留。请关闭客户端后重试。", "PROFILE_DELETE_FAILED"); }
            RenderAccounts();
        }

        async Task SelectAccount(Account account)
        {
            if (current == account && browser != null) return;
            bool switching = current != null;
            selected = account;
            state.Text = "正在切换账号…";
            if (!await CloseSession()) { Error("上个网页仍在退出，请稍后再切换。", "PROFILE_BUSY"); return; }
            string errorCode = null, errorMessage = null;
            try
            {
                environment = await CoreWebView2Environment.CreateAsync(null, store.Profile(account), new CoreWebView2EnvironmentOptions());
                pendingBrowserPid = 0;
                browser = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = sidebarColor };
                content.Controls.Add(browser); browser.BringToFront();
                await browser.EnsureCoreWebView2Async(environment);
                browser.ZoomFactor = preferences.Value.ZoomPercent / 100.0;
                pendingBrowserPid = (int)browser.CoreWebView2.BrowserProcessId;
                Configure(browser.CoreWebView2);
                current = account; heading.Text = account.Name; empty.Visible = false;
                browser.CoreWebView2.SourceChanged += delegate
                {
                    if (current != account || closing) return;
                    if (!smoke)
                    {
                        string page = AccountStore.SafePage(browser.CoreWebView2.Source);
                        if (page != "https://muse.ai/" || browser.CoreWebView2.Source == "https://muse.ai/")
                        {
                            account.LastPage = page;
                            try { store.Save(); } catch { state.Text = "网页已打开，但对话地址未能保存 · SAVE_FAILED"; }
                        }
                    }
                    Uri uri;
                    location.Text = Uri.TryCreate(browser.CoreWebView2.Source, UriKind.Absolute, out uri) ? uri.Host : "";
                };
                browser.CoreWebView2.NavigationCompleted += async delegate(object sender, CoreWebView2NavigationCompletedEventArgs args)
                {
                    if (!args.IsSuccess && args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
                    state.Text = args.IsSuccess ? "官方网页登录状态由网页显示 · 当前账号独立保存" : "页面加载失败 · " + args.WebErrorStatus.ToString() + " · 可手动刷新";
                    if (args.IsSuccess) await SyncMuseAppearance();
                };
                browser.CoreWebView2.ProcessFailed += delegate
                {
                    state.Text = "网页进程异常 · BROWSER_PROCESS_FAILED · 请切换账号后重新打开";
                };
                browser.CoreWebView2.Navigate(smoke ? Home : AccountStore.SafePage(account.LastPage));
                state.Text = "正在打开网页，请在网页中自行登录";
                if (switching && preferences.Value.CacheCleanupEnabled)
                    cacheTask = CleanOldCaches(account, browser);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                errorMessage = "此电脑缺少 Microsoft Edge WebView2 Runtime。请从微软官方网站安装后重试。";
                errorCode = "WEBVIEW2_RUNTIME_MISSING";
            }
            catch
            {
                errorMessage = "网页无法初始化。请确认 WebView2 可用且账号目录可写。";
                errorCode = "BROWSER_INIT_FAILED";
            }
            if (errorCode != null) { await CloseSession(); Error(errorMessage, errorCode); }
            RenderAccounts();
        }

        async Task CleanOldCaches(Account active, WebView2 activeView)
        {
            int visited = 0, failures = 0;
            DateTime cutoff = DateTime.Now.AddHours(-24);
            foreach (Account account in store.Accounts.ToArray())
            {
                try
                {
                    string profile = store.Profile(account);
                    if (!Directory.Exists(profile)) continue;
                    bool cleaned = account == active ? await CacheMaintenance.ClearActiveAsync(activeView, cutoff) :
                        await CacheMaintenance.ClearInactiveAsync(this, profile, cutoff);
                    visited++;
                    if (!cleaned) failures++;
                }
                catch { failures++; }
            }
            LastCacheCleanupProfiles = visited;
            LastCacheCleanupFailures = failures;
            if (failures > 0 && !closing && !IsDisposed)
                state.Text = "部分账号网页缓存未能清理，将在下次切换时重试 · CACHE_CLEANUP_FAILED";
        }

        internal Task WaitForCacheMaintenance() { return cacheTask; }

        void Configure(CoreWebView2 core)
        {
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Profile.IsPasswordAutosaveEnabled = false;
            core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs args)
            {
                if (!AllowedNavigation(args.Uri)) { args.Cancel = true; state.Text = "已阻止非网页链接 · NAVIGATION_SCHEME_BLOCKED"; }
            };
            core.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs args)
            {
                args.State = CoreWebView2PermissionState.Deny;
                if (smoke || !args.IsUserInitiated || closing || busy) return;
                Uri origin; string host = Uri.TryCreate(args.Uri, UriKind.Absolute, out origin) ? origin.Host : "此网页";
                if (MessageBox.Show(this, host + " 请求使用：" + args.PermissionKind.ToString() + "\n是否允许本次操作？", "网页权限", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    args.State = CoreWebView2PermissionState.Allow;
            };
            core.NewWindowRequested += OpenPopup;
            if (smoke)
            {
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs args)
                {
                    if (!AllowedNavigation(args.Request.Uri))
                        args.Response = environment.CreateWebResourceResponse(null, 403, "Offline test only", "");
                };
            }
        }

        bool AllowedNavigation(string value)
        {
            if (value == "about:blank") return true;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || !String.IsNullOrEmpty(uri.UserInfo)) return false;
            if (smoke) return uri.Scheme == "http" && uri.Host == "127.0.0.1" && new Uri(Home).Port == uri.Port;
            return uri.Scheme == "https";
        }

        async void OpenPopup(object sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            Task task = CreatePopup(args);
            pendingPopups.Add(task);
            try { await task; }
            catch { state.Text = "登录弹窗无法打开 · LOGIN_POPUP_FAILED"; }
            finally { pendingPopups.Remove(task); }
        }

        async Task CreatePopup(CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            if (closing || busy || !args.IsUserInitiated || !AllowedNavigation(args.Uri)) return;
            if (popups.Count >= 3) { state.Text = "请先关闭已有登录弹窗"; return; }
            var deferral = args.GetDeferral();
            CoreWebView2Environment popupEnvironment = environment;
            Account popupAccount = current;
            Form dialog = null;
            try
            {
                dialog = new Form { Text = "网页登录 — " + popupAccount.Name, Size = new Size(720, 800), StartPosition = FormStartPosition.CenterParent, Icon = Icon };
                var view = new WebView2 { Dock = DockStyle.Fill };
                dialog.Controls.Add(view); popups.Add(dialog);
                dialog.FormClosed += delegate { popups.Remove(dialog); view.Dispose(); };
                dialog.Show(this);
                await view.EnsureCoreWebView2Async(popupEnvironment);
                view.ZoomFactor = preferences.Value.ZoomPercent / 100.0;
                if (closing || popupAccount != current || dialog.IsDisposed) { if (!dialog.IsDisposed) dialog.Close(); return; }
                Configure(view.CoreWebView2);
                view.CoreWebView2.SourceChanged += delegate
                {
                    Uri origin;
                    if (!dialog.IsDisposed && Uri.TryCreate(view.CoreWebView2.Source, UriKind.Absolute, out origin))
                        dialog.Text = popupAccount.Name + " — " + origin.Host;
                };
                view.CoreWebView2.WindowCloseRequested += delegate { if (!dialog.IsDisposed) dialog.Close(); };
                args.NewWindow = view.CoreWebView2;
            }
            catch { if (dialog != null && !dialog.IsDisposed) dialog.Close(); state.Text = "登录弹窗无法打开 · LOGIN_POPUP_FAILED"; }
            finally { try { deferral.Complete(); } catch { } }
        }

        async Task<bool> CloseSession()
        {
            await cacheTask;
            themeRevision++;
            foreach (Form popup in popups.ToArray()) if (!popup.IsDisposed) popup.Close();
            popups.Clear();
            if (pendingPopups.Count > 0)
            {
                Task pending = Task.WhenAll(pendingPopups.ToArray());
                if (await Task.WhenAny(pending, Task.Delay(5000)) != pending) return false;
            }
            if (browser != null) { browser.Dispose(); browser = null; }
            environment = null; current = null; location.Text = "";
            heading.Text = "选择账号，开始对话"; empty.Visible = true;
            if (pendingBrowserPid == 0) return true;
            int processId = pendingBrowserPid;
            bool exited = await Task.Run(delegate
            {
                try { using (var process = Process.GetProcessById(processId)) return process.WaitForExit(12000); }
                catch (ArgumentException) { return true; }
                catch (InvalidOperationException) { return true; }
                catch { return false; }
            });
            if (exited) pendingBrowserPid = 0;
            return exited;
        }

        async void OnClosing(object sender, FormClosingEventArgs args)
        {
            if (allowClose) return;
            args.Cancel = true;
            if (closing || busy) { state.Text = "正在处理账号，请稍后关闭"; return; }
            closing = true; SetBusy(true);
            bool released = await CloseSession();
            if (!released && !smoke)
            {
                state.Text = "网页仍在退出，请稍后再关闭 · PROFILE_BUSY";
                closing = false; SetBusy(false); return;
            }
            try { SaveWindowLayout(); }
            catch { state.Text = "窗口尺寸未能保存 · WINDOW_SAVE_FAILED"; closing = false; SetBusy(false); return; }
            allowClose = true; Close();
        }

        void Error(string message, string code)
        {
            state.Text = message + " · " + code;
            if (!smoke) MessageBox.Show(this, message + "\n错误码：" + code, "Muse 管理系统", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        string Prompt(string title, string explanation, string initial)
        {
            using (var dialog = new Form { Text = title, Size = new Size(440, 220), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, BackColor = palette.Surface, ForeColor = palette.Text, Font = Font })
            {
                var label = new Label { Text = explanation, Left = 20, Top = 18, Width = 360, Height = 30 };
                var input = new TextBox { Text = initial, Left = 20, Top = 55, Width = 380, MaxLength = 32, BackColor = palette.Surface, ForeColor = palette.Text };
                var ok = new FreshButton { Text = "确定", Left = 200, Top = 110, Width = 90, Height = 36, DialogResult = DialogResult.OK, Colors = palette, Primary = true };
                var cancel = new FreshButton { Text = "取消", Left = 305, Top = 110, Width = 90, Height = 36, DialogResult = DialogResult.Cancel, Colors = palette };
                dialog.Controls.AddRange(new Control[] { label, input, ok, cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                return AccountStore.ValidateName(input.Text);
            }
        }

        internal async Task RunSmoke()
        {
            await Tests.RunBrowserTests(this, store, delegate(string home) { testHome = home; }, SelectAccount, CloseSession,
                delegate { return browser; }, delegate { return popups; });
            allowClose = true; Close();
        }
    }
}
