using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MuseDesktop
{
    public sealed class SettingsDialog : Form
    {
        public readonly Preferences Draft;
        public string Command = "save";
        public Account Target;
        public readonly ComboBox AccountPicker = new ComboBox();
        readonly FreshButton light = new FreshButton(), dark = new FreshButton();
        Palette colors;

        public SettingsDialog(Preferences preferences, List<Account> accounts, Account selected)
        {
            Draft = preferences.Copy(); colors = new Palette(Draft.Theme);
            Text = "Muse AI · 设置"; Font = new Font("Microsoft YaHei UI", 10F);
            Size = new Size(570, 660); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 8 };
            foreach (int height in new [] { 48, 36, 56, 50, 86, 50, 135, 56 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var title = new Label { Text = "设置", Font = new Font(Font.FontFamily, 19F, FontStyle.Bold), Dock = DockStyle.Fill };
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(new Label { Text = "外观", Dock = DockStyle.Fill, Tag = "muted" }, 0, 1);
            var themes = new FlowLayoutPanel { Dock = DockStyle.Fill };
            light.Text = "昼间 · 清新浅色"; dark.Text = "夜间 · 柔和深色";
            foreach (var button in new [] { light, dark }) { button.Size = new Size(190, 42); button.Margin = new Padding(0, 0, 12, 0); themes.Controls.Add(button); }
            light.Click += delegate { Draft.Theme = "light"; Recolor(); };
            dark.Click += delegate { Draft.Theme = "dark"; Recolor(); };
            layout.Controls.Add(themes, 0, 2);
            var sync = new CheckBox { Text = "昼夜切换同步到 Muse 网页", Checked = Draft.SyncMuseTheme, Dock = DockStyle.Fill, AutoSize = true };
            sync.CheckedChanged += delegate { Draft.SyncMuseTheme = sync.Checked; };
            layout.Controls.Add(sync, 0, 3);
            var zoomPanel = new Panel { Dock = DockStyle.Fill };
            zoomPanel.Controls.Add(new Label { Text = "网页缩放", Top = 4, Width = 120, Tag = "muted" });
            var zoom = new NumericUpDown { Minimum = 80, Maximum = 160, Increment = 10, Value = Draft.ZoomPercent, Left = 130, Top = 0, Width = 90 };
            zoom.ValueChanged += delegate { Draft.ZoomPercent = (int)zoom.Value; };
            zoomPanel.Controls.Add(zoom); zoomPanel.Controls.Add(new Label { Text = "%", Left = 230, Top = 4, Width = 40 });
            zoomPanel.Controls.Add(new Label { Text = "主题和缩放会保存，重新构建不会清空账号登录状态。", Top = 40, Width = 490, Tag = "muted", Font = new Font(Font.FontFamily, 9F) });
            layout.Controls.Add(zoomPanel, 0, 4);
            var cleanup = new CheckBox { Name = "CacheCleanupToggle", Text = "切换账号时清理所有账号 24 小时前的网页磁盘缓存", Checked = Draft.CacheCleanupEnabled,
                Dock = DockStyle.Fill, AutoSize = true };
            cleanup.CheckedChanged += delegate { Draft.CacheCleanupEnabled = cleanup.Checked; };
            layout.Controls.Add(cleanup, 0, 5);
            var manage = new Panel { Dock = DockStyle.Fill };
            manage.Controls.Add(new Label { Text = "账号管理", Top = 0, Width = 200, Tag = "muted" });
            AccountPicker.DropDownStyle = ComboBoxStyle.DropDownList; AccountPicker.Left = 0; AccountPicker.Top = 30; AccountPicker.Width = 490;
            foreach (var account in accounts) AccountPicker.Items.Add(account);
            AccountPicker.DisplayMember = "Name";
            if (selected != null) AccountPicker.SelectedItem = selected;
            if (AccountPicker.SelectedIndex < 0 && accounts.Count > 0) AccountPicker.SelectedIndex = 0;
            manage.Controls.Add(AccountPicker);
            var accountButtons = new FlowLayoutPanel { Left = 0, Top = 72, Width = 500, Height = 48 };
            foreach (var entry in new [] { new [] { "添加账号", "add" }, new [] { "重命名", "rename" }, new [] { "删除账号", "delete" } })
            {
                string command = entry[1];
                var button = new FreshButton { Text = entry[0], Size = new Size(135, 38), Margin = new Padding(0, 0, 12, 0), Enabled = command == "add" || accounts.Count > 0 };
                button.Click += delegate { Target = AccountPicker.SelectedItem as Account; Command = command; DialogResult = DialogResult.OK; };
                accountButtons.Controls.Add(button);
            }
            manage.Controls.Add(accountButtons); layout.Controls.Add(manage, 0, 6);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var apply = new FreshButton { Text = "保存设置", Primary = true, Size = new Size(120, 40), DialogResult = DialogResult.OK, Margin = new Padding(0) };
            var cancel = new FreshButton { Text = "取消", Size = new Size(100, 40), DialogResult = DialogResult.Cancel, Margin = new Padding(0, 0, 12, 0) };
            bottom.Controls.Add(apply); bottom.Controls.Add(cancel); layout.Controls.Add(bottom, 0, 7);
            Controls.Add(layout); AcceptButton = apply; CancelButton = cancel;
            HandleCreated += delegate { WindowChrome.Apply(this, colors); };
            Recolor();
        }

        void Recolor()
        {
            colors = new Palette(Draft.Theme); BackColor = colors.Surface; ForeColor = colors.Text;
            Action<Control> visit = null;
            visit = delegate(Control control)
            {
                control.BackColor = colors.Surface;
                control.ForeColor = (string)control.Tag == "muted" ? colors.Muted : colors.Text;
                var button = control as FreshButton;
                if (button != null) { button.Colors = colors; button.Invalidate(); }
                foreach (Control child in control.Controls) visit(child);
            };
            light.Selected = Draft.Theme == "light"; dark.Selected = Draft.Theme == "dark";
            foreach (Control child in Controls) visit(child);
            WindowChrome.Apply(this, colors);
        }
    }
}
