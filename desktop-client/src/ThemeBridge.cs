using System;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;

namespace MuseDesktop
{
    public sealed class ThemeResult
    {
        public string status { get; set; }
        public string mode { get; set; }
    }

    public static class ThemeBridge
    {
        // This bridge reads appearance controls only. It does not read cookies,
        // storage, chat contents, credentials or perform any model operation.
        const string Script = @"(() => {
 const desired = '__MODE__';
 const visible = e => !!e && !!(e.offsetWidth || e.offsetHeight || e.getClientRects().length);
 const label = e => (e.getAttribute('aria-label') || e.textContent || '').replace(/\s+/g,' ').trim();
 const find = (scope, names) => [...scope.querySelectorAll('button,[role=button],[role=menuitem]')].find(e => visible(e) && names.includes(label(e)));
 const lightNames = ['浅色','Light','Light mode'];
 const darkNames = ['深色','Dark','Dark mode'];
 const root = document.documentElement;
 const actual = () => {
   if(root.classList.contains('dark')) return 'dark';
   if(root.classList.contains('light')) return 'light';
   const token=root.getAttribute('data-theme');
   if(token==='dark'||token==='light') return token;
   const scheme=getComputedStyle(root).colorScheme;
   return scheme==='dark'||scheme==='light' ? scheme : 'unknown';
 };
 let context=window.__museDesktopAppearance;
 if(!context || context.desired!==desired) context=window.__museDesktopAppearance={desired,openedMenu:false,openedDialog:false,clicked:false};
 const panels=[...document.querySelectorAll('[role=dialog],dialog,[data-testid=settings-dialog]')].filter(visible);
 const panel=panels.find(e => find(e,lightNames)&&find(e,darkNames));
 if(panel) {
   if(!context.clicked) {
     const control=find(panel,desired==='dark'?darkNames:lightNames);
     control.click(); context.clicked=true;
     return {status:'pending',mode:actual()};
   }
   if(actual()===desired) {
     if(context.openedDialog) { const close=find(panel,['关闭','Close']); if(close)close.click(); }
     window.__museDesktopAppearance=null;
     return {status:'confirmed',mode:desired};
   }
   return {status:'pending',mode:actual()};
 }
 if(actual()===desired && !context.openedDialog) {
   if(context.openedMenu) { const trigger=find(document,['设置','Settings']); if(trigger)trigger.click(); }
   window.__museDesktopAppearance=null;
   return {status:'confirmed',mode:desired};
 }
 const menu=[...document.querySelectorAll('[role=menu]')].find(visible);
 if(menu && !context.openedDialog) {
   const item=[...menu.querySelectorAll('[role=menuitem]')].find(e=>visible(e)&&/^(设置|Settings)(\s|$)/.test(label(e)));
   if(item) { item.click(); context.openedDialog=true; return {status:'pending',mode:actual()}; }
 }
 if(!context.openedMenu && !context.openedDialog) {
   const trigger=find(document,['设置','Settings','Open settings']);
   if(trigger) { trigger.click(); context.openedMenu=true; return {status:'pending',mode:actual()}; }
 }
 return {status:'pending',mode:actual()};
})()";

        public static async Task<string> ApplyAsync(CoreWebView2 core, string mode, Func<bool> valid)
        {
            if (mode != "light" && mode != "dark") throw new ArgumentException("THEME_MODE_INVALID");
            core.Profile.PreferredColorScheme = mode == "dark" ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            var serializer = new JavaScriptSerializer();
            for (int attempt = 0; attempt < 18; attempt++)
            {
                if (!valid()) return "cancelled";
                var result = serializer.Deserialize<ThemeResult>(await core.ExecuteScriptAsync(Script.Replace("__MODE__", mode)));
                if (result != null && result.status == "confirmed" && result.mode == mode) return "confirmed";
                await Task.Delay(120);
            }
            return "unsupported";
        }
    }
}
