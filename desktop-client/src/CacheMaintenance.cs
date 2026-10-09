using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MuseDesktop
{
    static class CacheMaintenance
    {
        // DiskCache is separate from Cookies and AllDomStorage in WebView2.
        // https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2browsingdatakinds
        public static async Task<bool> ClearActiveAsync(WebView2 view, DateTime cutoff)
        {
            try
            {
                await view.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.DiskCache, new DateTime(2000, 1, 1), cutoff);
                return true;
            }
            catch { return false; }
        }

        public static async Task<bool> ClearInactiveAsync(Form owner, string profilePath, DateTime cutoff)
        {
            if (!Directory.Exists(profilePath) ||
                (File.GetAttributes(profilePath) & FileAttributes.ReparsePoint) != 0) return false;
            var view = new WebView2 { Location = new Point(-10000, -10000), Size = new Size(1, 1), TabStop = false };
            int processId = 0;
            bool cleaned = false;
            try
            {
                owner.Controls.Add(view);
                var environment = await CoreWebView2Environment.CreateAsync(null, profilePath, new CoreWebView2EnvironmentOptions());
                await view.EnsureCoreWebView2Async(environment);
                processId = (int)view.CoreWebView2.BrowserProcessId;
                // The maintenance view stays on about:blank and never visits Muse.
                await view.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.DiskCache, new DateTime(2000, 1, 1), cutoff);
                cleaned = true;
            }
            catch { }
            finally
            {
                owner.Controls.Remove(view);
                view.Dispose();
            }
            if (processId == 0) return false;
            bool exited = await Task.Run(delegate
            {
                try { using (var process = Process.GetProcessById(processId)) return process.WaitForExit(12000); }
                catch (ArgumentException) { return true; }
                catch (InvalidOperationException) { return true; }
                catch { return false; }
            });
            return cleaned && exited;
        }
    }
}
