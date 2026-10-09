using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MuseDesktop
{
    public sealed class Preferences
    {
        public string Theme { get; set; }
        public bool SyncMuseTheme { get; set; }
        public int ZoomPercent { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }
        public bool SidebarCollapsed { get; set; }
        public bool CacheCleanupEnabled { get; set; }
        public Preferences() { Theme = "light"; SyncMuseTheme = true; ZoomPercent = 100; CacheCleanupEnabled = true; }
        public Preferences Copy() { return new Preferences { Theme = Theme, SyncMuseTheme = SyncMuseTheme, ZoomPercent = ZoomPercent,
            WindowWidth = WindowWidth, WindowHeight = WindowHeight, WindowMaximized = WindowMaximized, SidebarCollapsed = SidebarCollapsed,
            CacheCleanupEnabled = CacheCleanupEnabled }; }
    }

    public sealed class PreferenceStore
    {
        readonly string path;
        public Preferences Value { get; private set; }
        public PreferenceStore(string root)
        {
            path = Path.Combine(root, "settings.json");
            Value = new Preferences();
            if (File.Exists(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 16384)
                    throw new InvalidDataException("SETTINGS_FILE_INVALID");
                Value = new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(path, Encoding.UTF8));
                Validate(Value);
            }
        }
        static void Validate(Preferences value)
        {
            if (value == null || (value.Theme != "light" && value.Theme != "dark") || value.ZoomPercent < 80 || value.ZoomPercent > 160)
                throw new InvalidDataException("SETTINGS_VALUE_INVALID");
        }
        public void Save(Preferences value)
        {
            Validate(value);
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("SETTINGS_LINK_INVALID");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(value));
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                Value = value.Copy();
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public sealed class Palette
    {
        public Color Background, Surface, Sidebar, Text, Muted, Border, Accent, AccentSoft, Hover;
        public bool Dark;
        public Palette(string theme)
        {
            Dark = theme == "dark";
            // Sampled from the user-provided Muse light/dark screenshots.
            Background = Surface = Sidebar = Dark ? Color.FromArgb(24, 24, 25) : Color.FromArgb(252, 252, 252);
            Text = Dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(24, 24, 25);
            Muted = Dark ? Color.FromArgb(158, 158, 162) : Color.FromArgb(113, 113, 118);
            Border = Dark ? Color.FromArgb(49, 49, 51) : Color.FromArgb(228, 228, 231);
            Accent = Text;
            AccentSoft = Dark ? Color.FromArgb(38, 38, 40) : Color.FromArgb(243, 243, 245);
            Hover = Dark ? Color.FromArgb(46, 46, 48) : Color.FromArgb(237, 237, 240);
        }
    }

    public sealed class FreshButton : Button
    {
        public Palette Colors = new Palette("light");
        public bool Primary, Selected, AccountCard;
        public string IconKind;
        public bool IconOnly { get { return IconKind == "sun" || IconKind == "moon" || IconKind == "sidebar"; } }
        bool hover;
        public FreshButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MouseEnter += delegate { hover = true; Invalidate(); };
            MouseLeave += delegate { hover = false; Invalidate(); };
        }
        static GraphicsPath Rounded(RectangleF rectangle, int radius)
        {
            float diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
        protected override void OnPaint(PaintEventArgs args)
        {
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            args.Graphics.Clear(Parent == null ? Colors.Surface : Parent.BackColor);
            Color fill = Primary || Selected ? Colors.AccentSoft : hover && Enabled ? Colors.Hover : Colors.Surface;
            Color foreground = Enabled ? Colors.Text : Colors.Muted;
            using (var path = Rounded(new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F), 8))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Colors.Border))
            { args.Graphics.FillPath(brush, path); args.Graphics.DrawPath(pen, path); }
            if (AccountCard)
            {
                string[] lines = Text.Split('\n');
                string name = lines[0];
                using (var nameFont = new Font(Font, Selected ? FontStyle.Bold : FontStyle.Regular))
                    TextRenderer.DrawText(args.Graphics, name, nameFont, new Rectangle(12, 0, Width - 24, Height), Colors.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else if (IconOnly)
            {
                float centerX = Width / 2F, centerY = Height / 2F;
                using (var pen = new Pen(foreground, 1.5F))
                using (var brush = new SolidBrush(foreground))
                {
                    if (IconKind == "sidebar")
                    {
                        args.Graphics.DrawRectangle(pen, centerX - 8, centerY - 6, 16, 12);
                        args.Graphics.DrawLine(pen, centerX - 3, centerY - 6, centerX - 3, centerY + 6);
                    }
                    else if (IconKind == "sun")
                    {
                        args.Graphics.DrawEllipse(pen, centerX - 4, centerY - 4, 8, 8);
                        for (int ray = 0; ray < 8; ray++)
                        {
                            double angle = ray * Math.PI / 4;
                            args.Graphics.DrawLine(pen, centerX + (float)Math.Cos(angle) * 6, centerY + (float)Math.Sin(angle) * 6,
                                centerX + (float)Math.Cos(angle) * 9, centerY + (float)Math.Sin(angle) * 9);
                        }
                    }
                    else
                    {
                        args.Graphics.FillEllipse(brush, centerX - 7, centerY - 7, 14, 14);
                        using (var mask = new SolidBrush(fill)) args.Graphics.FillEllipse(mask, centerX - 2, centerY - 10, 13, 13);
                    }
                }
            }
            else TextRenderer.DrawText(args.Graphics, Text, Font, new Rectangle(5, 0, Width - 10, Height), foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(args.Graphics, new Rectangle(5, 5, Width - 10, Height - 10), foreground, fill);
        }
    }

    static class WindowChrome
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        public static void Apply(Form window, Palette palette)
        {
            if (!window.IsHandleCreated) return;
            try
            {
                int dark = palette.Dark ? 1 : 0;
                DwmSetWindowAttribute(window.Handle, 20, ref dark, 4);
                int background = palette.Background.R | (palette.Background.G << 8) | (palette.Background.B << 16);
                int foreground = palette.Text.R | (palette.Text.G << 8) | (palette.Text.B << 16);
                DwmSetWindowAttribute(window.Handle, 35, ref background, 4);
                DwmSetWindowAttribute(window.Handle, 36, ref foreground, 4);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
