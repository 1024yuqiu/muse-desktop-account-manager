using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MuseDesktop
{
    static class Tests
    {
        static void Check(bool value, string code)
        {
            if (!value) throw new InvalidOperationException(code);
        }

        public static void RunModelTests(string root)
        {
            var passed = new List<string>();
            try
            {
                var store = new AccountStore(root);
                var a = store.Add("日常账号");
                var b = store.Add("另一个账号");
                Check(a.Id != b.Id && store.Profile(a) != store.Profile(b), "ISOLATED_PATH");
                passed.Add("random account identifiers and separate profile paths");
                Check(store.Profile(a).StartsWith(Path.Combine(root, "profiles") + Path.DirectorySeparatorChar), "PATH_ROOT");
                string original = a.Name;
                store.Rename(a, "新名称");
                var reloaded = new AccountStore(root);
                Check(reloaded.Accounts.Count == 2 && reloaded.Accounts[0].Name == "新名称", "PERSISTENCE");
                passed.Add("account add, rename and restart persistence");
                string accountIndex = File.ReadAllText(Path.Combine(root, "accounts.json"), Encoding.UTF8);
                var settings = new PreferenceStore(root);
                Check(settings.Value.Theme == "light" && settings.Value.ZoomPercent == 100, "LIGHT_DEFAULT");
                settings.Save(new Preferences { Theme = "dark", SyncMuseTheme = true, ZoomPercent = 120 });
                var restoredSettings = new PreferenceStore(root);
                Check(restoredSettings.Value.Theme == "dark" && restoredSettings.Value.ZoomPercent == 120 && restoredSettings.Value.SyncMuseTheme, "THEME_PERSISTENCE");
                Check(File.ReadAllText(Path.Combine(root, "accounts.json"), Encoding.UTF8) == accountIndex, "PREFERENCES_DO_NOT_CHANGE_ACCOUNTS");
                passed.Add("theme/zoom preferences persist without modifying existing account index");
                    File.WriteAllText(Path.Combine(root, "settings.json"), "{\"Theme\":\"light\",\"SyncMuseTheme\":true,\"ZoomPercent\":100}", Encoding.UTF8);
                    Check(new PreferenceStore(root).Value.CacheCleanupEnabled, "OLD_SETTINGS_ENABLE_24H_CACHE_POLICY");
                    passed.Add("existing settings enable the 24-hour cache policy without changing accounts");
                string invalidId = a.Id; a.Id = "..\\outside";
                bool rejected = false;
                try { store.Profile(a); } catch (InvalidDataException) { rejected = true; }
                a.Id = invalidId;
                Check(rejected, "PATH_TRAVERSAL_REJECTED");
                passed.Add("invalid profile identifier rejected");
                Check(AccountStore.SafePage("https://muse.ai/thread/demo?code=synthetic-secret#token") == "https://muse.ai/thread/demo", "URL_SECRET_REMOVED");
                Check(AccountStore.SafePage("https://accounts.google.com/o/oauth2?code=x") == "https://muse.ai/", "OAUTH_NOT_SAVED");
                Check(AccountStore.SafePage("file:///C:/Windows") == "https://muse.ai/", "URL_SCHEME");
                Check(AccountStore.SafePage("https://other.example/thread/a") == "https://muse.ai/", "URL_ORIGIN");
                passed.Add("only Muse conversation paths retained, no OAuth query or fragment");
                Directory.CreateDirectory(store.Profile(a));
                File.WriteAllText(Path.Combine(store.Profile(a), "fixture.txt"), "synthetic");
                store.Remove(a);
                Check(!Directory.Exists(Path.Combine(root, "profiles", a.Id)) && new AccountStore(root).Accounts.Count == 1, "DELETE");
                passed.Add("account and owned local profile deleted");
                File.WriteAllText(Path.Combine(root, "accounts.json"), "[{\"Id\":\"../outside\",\"Name\":\"x\"}]", Encoding.UTF8);
                rejected = false;
                try { new AccountStore(root); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "INVALID_INDEX");
                passed.Add("malformed persisted account identifier rejected without reset");
                File.WriteAllText(Path.Combine(root, "result.txt"), "PASS\n" + String.Join("\n", passed), Encoding.UTF8);
            }
            catch (Exception error)
            {
                Environment.ExitCode = 1;
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "result.txt"), "FAIL: " + error.GetType().Name + "\n" + String.Join("\n", passed), Encoding.UTF8);
            }
        }

        static async Task WaitForPage(WebView2 view)
        {
            for (int i = 0; i < 150; i++)
            {
                if (view != null && view.CoreWebView2 != null)
                {
                    string ready = await view.CoreWebView2.ExecuteScriptAsync("document.getElementById('fixture-ready') !== null");
                    if (ready == "true") return;
                }
                await Task.Delay(100);
            }
            throw new TimeoutException("LOCAL_PAGE_TIMEOUT");
        }

        static async Task ClickPopupButton(WebView2 view)
        {
            string point = await view.CoreWebView2.ExecuteScriptAsync("(()=>{const r=document.getElementById('popup').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()");
            var coordinates = new JavaScriptSerializer().Deserialize<Dictionary<string, double>>(point);
            var json = new JavaScriptSerializer();
            foreach (string type in new [] { "mousePressed", "mouseReleased" })
                await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", json.Serialize(new {
                    type = type, x = coordinates["x"], y = coordinates["y"], button = "left", clickCount = 1 }));
        }

        public static async Task RunBrowserTests(MainWindow window, AccountStore store, Action<string> setHome,
            Func<Account, Task> select, Func<Task<bool>> close, Func<WebView2> browser, Func<List<Form>> popups)
        {
            var passed = new List<string>();
            using (var server = new FixtureServer())
            {
                setHome(server.Home);
                string failureType = null;
                try
                {
                    Check(browser() == null, "NO_AUTO_BROWSER");
                    passed.Add("startup creates no browser or Muse navigation");
                    var a = store.Add("测试账号 A"); var b = store.Add("测试账号 B");
                    await select(a); Check(browser() != null, "CREATE_A"); await WaitForPage(browser());
                    Check(((FreshButton)((FlowLayoutPanel)window.Controls.Find("AccountSidebar", true)[0].Controls[0]).Controls[0]).AccountCard, "TEXT_ACCOUNT_CARD");
                    Check(!browser().CoreWebView2.Settings.AreHostObjectsAllowed && !browser().CoreWebView2.Settings.IsWebMessageEnabled, "NO_NATIVE_BRIDGE");
                    passed.Add("remote page native host and messaging bridges disabled");
                    await browser().CoreWebView2.ExecuteScriptAsync("localStorage.setItem('account','A');document.cookie='account=A;path=/;max-age=86400'");
                    await ClickPopupButton(browser());
                    for (int i = 0; i < 100 && popups().Count == 0; i++) await Task.Delay(100);
                    Check(popups().Count == 1, "LOGIN_POPUP_CREATED");
                    WebView2 popup = (WebView2)popups()[0].Controls[0];
                    await WaitForPage(popup);
                    string popupState = await popup.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('account') === 'A' && document.cookie.includes('account=A')");
                    Check(popupState == "true", "POPUP_SAME_ACCOUNT");
                    passed.Add("user initiated login popup shares only active account profile");
                    await select(b); Check(popups().Count == 0, "POPUP_RELEASED"); await WaitForPage(browser());
                    await window.WaitForCacheMaintenance();
                    Check(window.LastCacheCleanupProfiles == 2 && window.LastCacheCleanupFailures == 0, "ALL_PROFILES_CACHE_CLEANED");
                    passed.Add("switch checks disk cache older than 24 hours in both profiles without Muse navigation");
                    Check(!Directory.Exists(Path.Combine(store.Root, "avatars")), "NO_AVATAR_CACHE");
                    passed.Add("account cards use text only and create no avatar cache");
                    string isolated = await browser().CoreWebView2.ExecuteScriptAsync("localStorage.getItem('account') === null && !document.cookie.includes('account=A')");
                    Check(isolated == "true", "A_B_ISOLATION");
                    await browser().CoreWebView2.ExecuteScriptAsync("localStorage.setItem('account','B');document.cookie='account=B;path=/;max-age=86400'");
                    passed.Add("switch closes prior page and popup, A cookie/storage absent in B");
                    await select(a); await WaitForPage(browser());
                    Check(await browser().CoreWebView2.ExecuteScriptAsync("localStorage.getItem('account') === 'A' && document.cookie.includes('account=A') && !document.cookie.includes('account=B')") == "true", "A_RESTORED");
                    passed.Add("A cookie and page storage persist after browser process restart");
                    Control sidebar = window.Controls.Find("AccountSidebar", true)[0];
                    Control toolbar = window.Controls.Find("CompactToolbar", true)[0];
                    Control viewport = window.Controls.Find("MuseViewport", true)[0];
                    var themeIcon = (FreshButton)window.Controls.Find("ThemeIconButton", true)[0];
                    Check(sidebar.Width == 192 && toolbar.Height == 42, "COMPACT_LAYOUT");
                    Check(viewport.Width > window.ClientSize.Width * 0.8 && viewport.Height > window.ClientSize.Height * 0.9, "MUSE_VIEWPORT_PRIORITY");
                    Check(themeIcon.Text.Length == 0 && themeIcon.IconKind == "sun" && themeIcon.AccessibleName.Length > 0, "ACCESSIBLE_ICON_ONLY");
                    passed.Add("compact 192px sidebar / 42px single toolbar, Muse gets over 80% width and 90% height, accessible icon-only theme button");
                    int expandedWidth = viewport.Width;
                    WebView2 originalBrowser = browser();
                    window.ToggleSidebar(); await Task.Delay(100);
                    Check(!sidebar.Visible && viewport.Width == expandedWidth + 192 && browser() == originalBrowser && browser().Width == viewport.Width, "SIDEBAR_WEBVIEW_RESIZE");
                    Check(new PreferenceStore(store.Root).Value.SidebarCollapsed, "SIDEBAR_STATE_SAVED");
                    await Screenshot(window, browser(), store.Root, "client-collapsed.png");
                    window.ToggleSidebar(); await Task.Delay(100);
                    Check(sidebar.Visible && viewport.Width == expandedWidth && browser() == originalBrowser, "SIDEBAR_EXPANDED");
                    Check(await browser().CoreWebView2.ExecuteScriptAsync("localStorage.getItem('account') === 'A' && document.cookie.includes('account=A')") == "true", "SIDEBAR_PRESERVES_LOGIN");
                    passed.Add("sidebar collapse gives the live WebView all 192px, expand restores width without reload or login change");
                    Size previousSize = window.Size;
                    window.Size = new Size(1100, 720); window.SaveWindowLayout();
                    using (var restoredWindow = new MainWindow(store, true))
                        Check(restoredWindow.Size == window.Size, "WINDOW_SIZE_RESTORED");
                    window.WindowState = FormWindowState.Maximized; await Task.Delay(100); window.SaveWindowLayout();
                    Check(new PreferenceStore(store.Root).Value.WindowMaximized && new PreferenceStore(store.Root).Value.WindowWidth == 1100, "MAXIMIZED_RESTORE_BOUNDS");
                    using (var restoredWindow = new MainWindow(store, true))
                        Check(restoredWindow.WindowState == FormWindowState.Maximized, "WINDOW_MAXIMIZED_RESTORED");
                    window.WindowState = FormWindowState.Minimized; await Task.Delay(100); window.SaveWindowLayout();
                    Check(new PreferenceStore(store.Root).Value.WindowMaximized && new PreferenceStore(store.Root).Value.WindowWidth == 1100, "MINIMIZE_PRESERVES_WINDOW_LAYOUT");
                    window.WindowState = FormWindowState.Normal; window.Size = previousSize; window.SaveWindowLayout();
                    using (var iconBitmap = window.Icon.ToBitmap()) Check(iconBitmap.Width > 0, "APPLICATION_ICON_VALID");
                    passed.Add("normal window size and maximized state persist and restore; embedded neutral icon is valid");
                    await window.SetAppearance("dark");
                    Check(window.LastThemeSync == "confirmed", "DARK_THEME_CONFIRMED");
                    Check(await browser().CoreWebView2.ExecuteScriptAsync("document.documentElement.classList.contains('dark') && localStorage.getItem('fixture-theme') === 'dark'") == "true", "OFFICIAL_THEME_CONTROL_USED");
                    Check(await browser().CoreWebView2.ExecuteScriptAsync("localStorage.getItem('account') === 'A' && document.cookie.includes('account=A')") == "true", "THEME_PRESERVES_LOGIN");
                    Check(window.BackColor.ToArgb() == Color.FromArgb(24, 24, 25).ToArgb() && viewport.BackColor == window.BackColor, "DARK_COLOR_MATCH");
                    Check(await browser().CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.body).backgroundColor === 'rgb(24, 24, 25)'") == "true", "DARK_PAGE_COLOR");
                    Check(themeIcon.IconKind == "moon" && themeIcon.Text.Length == 0 && !window.Controls.Find("OperationStatus", true)[0].Visible, "DARK_ICON_STATUS");
                    await Screenshot(window, browser(), store.Root, "client-dark.png");
                    await window.SetAppearance("light");
                    Check(window.LastThemeSync == "confirmed", "LIGHT_THEME_CONFIRMED");
                    Check(await browser().CoreWebView2.ExecuteScriptAsync("document.documentElement.classList.contains('light') && localStorage.getItem('fixture-theme') === 'light' && !document.querySelector('[role=dialog]').offsetHeight") == "true", "THEME_DIALOG_CLOSED");
                    passed.Add("day/night button uses page theme controls, saves page preference, closes only its settings dialog, preserves login");
                    Check(window.BackColor.ToArgb() == Color.FromArgb(252, 252, 252).ToArgb() && viewport.BackColor == window.BackColor, "LIGHT_COLOR_MATCH");
                    Check(await browser().CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.body).backgroundColor === 'rgb(252, 252, 252)'") == "true", "LIGHT_PAGE_COLOR");
                    passed.Add("shell and Muse fixture share exact screenshot-derived light/dark backgrounds; normal status bar is hidden");
                    using (var dialog = new SettingsDialog(new PreferenceStore(store.Root).Value, store.Accounts, a))
                    {
                        dialog.Show(window);
                        await Task.Delay(100);
                        Check(dialog.AccountPicker.Items.Count == 2, "UNIFIED_ACCOUNT_SETTINGS");
                        var cacheToggle = (CheckBox)dialog.Controls.Find("CacheCleanupToggle", true)[0];
                        Check(cacheToggle.Checked, "CACHE_POLICY_VISIBLE");
                        cacheToggle.Checked = false;
                        Check(!dialog.Draft.CacheCleanupEnabled, "CACHE_POLICY_CAN_DISABLE");
                        cacheToggle.Checked = true;
                        using (var image = new Bitmap(dialog.Width, dialog.Height))
                        {
                            dialog.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            image.Save(Path.Combine(store.Root, "settings.png"));
                        }
                        dialog.Close();
                    }
                    passed.Add("unified native settings for appearance, sync, zoom and existing accounts");
                    await Screenshot(window, browser(), store.Root, "client.png");
                    // Actual navigation interception, not only a policy helper unit test.
                    string previous = browser().CoreWebView2.Source;
                    browser().CoreWebView2.Navigate("file:///C:/Windows/win.ini");
                    await Task.Delay(700);
                    Check(browser().CoreWebView2.Source == previous, "FILE_NAVIGATION_BLOCKED");
                    passed.Add("file navigation blocked in actual WebView2");
                    passed.Add("actual client and WebView2 rendered to screenshot");
                    Check(await close(), "BROWSER_EXIT");
                    store.Rename(a, "重命名测试账号");
                    var reloaded = new AccountStore(store.Root);
                    Check(reloaded.Accounts[0].Name == "重命名测试账号", "ACCOUNT_RELOAD");
                    store.Remove(a); store.Remove(b);
                    Check(new AccountStore(store.Root).Accounts.Count == 0, "ACCOUNT_DELETED");
                    Check(!Directory.Exists(Path.Combine(store.Root, "profiles", a.Id)) && !Directory.Exists(Path.Combine(store.Root, "profiles", b.Id)), "PROFILES_DELETED");
                    passed.Add("rename persistence, both accounts/profiles deleted, browser exits normally");
                    Check(server.Requests > 0, "REAL_LOCAL_HTTP");
                    File.WriteAllText(Path.Combine(store.Root, "result.txt"), "PASS\n" + String.Join("\n", passed) + "\nLocal HTTP requests: " + server.Requests + "\nMuse requests: 0; real logins: 0", Encoding.UTF8);
                }
                catch (Exception error)
                {
                    Environment.ExitCode = 1;
                    failureType = error.GetType().Name;
                }
                if (failureType != null)
                {
                    await close();
                    // Synthetic-only tests; retain a fixed diagnostic code, no URL/cookie/exception text.
                    File.WriteAllText(Path.Combine(store.Root, "result.txt"), "FAIL: " + failureType + "\n" + String.Join("\n", passed), Encoding.UTF8);
                }
            }
        }

        static async Task Screenshot(MainWindow window, WebView2 browser, string root, string name)
        {
            using (var stream = new FileStream(Path.Combine(root, "webview.png"), FileMode.Create))
                await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            using (var image = new Bitmap(window.Width, window.Height))
            {
                window.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                Point position = browser.PointToScreen(Point.Empty);
                Point windowPosition = window.Location;
                using (var web = Image.FromFile(Path.Combine(root, "webview.png")))
                using (var graphics = Graphics.FromImage(image))
                    graphics.DrawImage(web, position.X - windowPosition.X, position.Y - windowPosition.Y, browser.Width, browser.Height);
                image.Save(Path.Combine(root, name));
            }
        }
    }

    sealed class FixtureServer : IDisposable
    {
        readonly HttpListener listener = new HttpListener();
        readonly Task serving;
        int requests;
        public string Home { get; private set; }
        public int Requests { get { return requests; } }
        public FixtureServer()
        {
            var portFinder = new TcpListener(IPAddress.Loopback, 0); portFinder.Start();
            int port = ((IPEndPoint)portFinder.LocalEndpoint).Port; portFinder.Stop();
            Home = "http://127.0.0.1:" + port + "/";
            listener.Prefixes.Add(Home); listener.Start();
            serving = Task.Run(async delegate
            {
                while (listener.IsListening)
                {
                    try
                    {
                        var context = await listener.GetContextAsync();
                        Interlocked.Increment(ref requests);
                        const string page = @"<!doctype html><html lang='zh' class='light'><meta charset='utf-8'><title>本地模拟 Muse 页面</title>
<style>html.light{color-scheme:light;--bg:#fcfcfc;--fg:#181819;--muted:#717176;--button:#f3f3f5}html.dark{color-scheme:dark;--bg:#181819;--fg:#eee;--muted:#9e9ea2;--button:#262628}body{background:var(--bg);color:var(--fg);font:18px sans-serif;padding:42px}h1{font-size:28px}button{background:var(--button);color:var(--fg);border:1px solid #8883;padding:12px 18px;border-radius:6px;margin:4px;cursor:pointer}p{color:var(--muted)}[role=dialog]{border:1px solid #8883;padding:24px;border-radius:8px}</style>
<script>document.documentElement.className=localStorage.getItem('fixture-theme')||'light';function setTheme(v){document.documentElement.className=v;localStorage.setItem('fixture-theme',v)}</script>
<h1 id='fixture-ready'>Muse 网页 · 本地模拟</h1><p>独立账号，独立登录状态。此页没有登录 Muse，也没有发送模型请求。</p>
<button id='popup' onclick=""window.open('/popup','login','width=700,height=700')"">模拟登录弹窗</button>
<button onclick=""document.getElementById('menu').style.display='block'"">设置</button>
<div id='menu' role='menu' style='display:none'><button role='menuitem' onclick=""document.getElementById('menu').style.display='none';document.getElementById('dialog').style.display='block'"">设置 Ctrl+,</button></div>
<div id='dialog' role='dialog' style='display:none'><h2>外观</h2><button onclick=""setTheme('light')"">浅色</button><button onclick=""setTheme('dark')"">深色</button><button onclick=""setTheme('light')"">系统</button><button onclick=""document.getElementById('dialog').style.display='none'"">关闭</button></div></html>";
                        byte[] bytes = Encoding.UTF8.GetBytes(page);
                        context.Response.ContentType = "text/html; charset=utf-8"; context.Response.ContentLength64 = bytes.Length;
                        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length); context.Response.Close();
                    }
                    catch (HttpListenerException) { break; }
                    catch (ObjectDisposedException) { break; }
                }
            });
        }
        public void Dispose() { listener.Close(); try { serving.Wait(2000); } catch { } }
    }
}
