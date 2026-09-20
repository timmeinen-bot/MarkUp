using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

// MUP-20260912-102451348-e535: the version is READ, not typed.
//
// 🔴 There used to be a literal "v1.0" on the splash screen. Bump the
// csproj to 1.1.0 and the UI would still say "v1.0" -- and nobody would
// notice, because both numbers look plausible. There is no state in which
// anything looks obviously broken.
// The same mistake as MFY-20260902-085158364-20d3 in Massfyle.
static class AppInfo {
    public const string Herausgeber = "Eselchen Labs";
    // 🔴 Required by CC BY 4.0, not a courtesy. THIRD-PARTY-NOTICES.md
    // claimed the notice was "reachable from the user interface" --
    // which was untrue until today.
    public const string EmojiHinweis = "Emoji: Twemoji (CC BY 4.0)";

    public static string VersionText() {
        var v = System.Reflection.Assembly
            .GetExecutingAssembly().GetName().Version;
        // Better nothing at all than an invented number -- the same stance
        // as utils/lizenz.py in Massfyle.
        if (v == null) return "";
        return "v" + v.Major + "." + v.Minor
             + (v.Build > 0 ? "." + v.Build : "");
    }

    public static string Fusszeile() {
        string v = VersionText();
        return "MarkUp" + (v.Length > 0 ? "  " + v : "")
             + "  \u00b7  " + Herausgeber + "  \u00b7  " + EmojiHinweis;
    }
}

class Program {
    [STAThread] static void Main() {
        try {
            Application.SetHighDpiMode(HighDpiMode.SystemAware); // MUP-b45e: SystemAware fixes multi-monitor coordinate mismatch
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // One toolbar, one overlay per monitor
            MarkUpApp app = new MarkUpApp();
            app.Run();
        } catch (Exception ex) {
            MessageBox.Show(ex.ToString(), "MarkUp error");
        }
    }
}

// ── P/Invoke ──────────────────────────────────────────────────────────────────
static class Win32 {
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int n, int v);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pDst, ref SIZE sz, IntPtr hdcSrc, ref POINT pSrc, uint crKey, ref BLEND bf, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")]  public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]  public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]  public static extern IntPtr SelectObject(IntPtr hdc, IntPtr o);
    [DllImport("gdi32.dll")]  public static extern bool DeleteObject(IntPtr o);
    [DllImport("user32.dll")] public static extern bool SetWindowRgn(IntPtr hwnd, IntPtr hRgn, bool bRedraw);
    [DllImport("gdi32.dll")]  public static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")]  public static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);
    public const int RGN_DIFF = 4;
    public const int GWL_EXSTYLE = -20, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20;
    public const uint ULW_ALPHA = 2;
    public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

    // MKP-133745: Low-level mouse hook P/Invoke
    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string lpModuleName);
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201, WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202;
    // MUP-234951: Right-click support for context menu on overlay
    public const int WM_RBUTTONDOWN = 0x0204;
    [StructLayout(LayoutKind.Sequential)] public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE  { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    public struct BLEND { public byte Op, Flags, Alpha, Format; }

    // MUP-409e: Physical monitor enumeration for DPI-correct coordinates
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOACTIVATE = 0x0010, SWP_NOZORDER = 0x0004, SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // MUP-409e: Compute physical (unscaled) virtual screen bounds from all monitors
    public static Rectangle GetPhysicalVirtualScreen() {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) => {
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(hMon, ref mi)) {
                if (mi.rcMonitor.Left   < minX) minX = mi.rcMonitor.Left;
                if (mi.rcMonitor.Top    < minY) minY = mi.rcMonitor.Top;
                if (mi.rcMonitor.Right  > maxX) maxX = mi.rcMonitor.Right;
                if (mi.rcMonitor.Bottom > maxY) maxY = mi.rcMonitor.Bottom;
            }
            return true;
        }, IntPtr.Zero);
        if (minX == int.MaxValue) return SystemInformation.VirtualScreen; // fallback
        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }
}

// ── MUP-13df: COM IShellLink for shortcut creation ──────────────────────────
[ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink {}
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IShellLink {
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

// ── Shapes ───────────────────────────────────────────────────────────────────
abstract class Shape { public Color Color; public float Width; public static int RainbowFrame; public abstract void Draw(Graphics g); public virtual Rectangle GetBounds() => Rectangle.Empty; }

class FreehandShape : Shape {
    public List<Point> Points = new List<Point>();
    public DashStyle Dash = DashStyle.Solid;
    public LineCap Cap = LineCap.Round;
    public bool Rainbow = false;
    public bool AnimatedRainbow = false;
    static readonly Color[] RAINBOW = { Color.Red, Color.FromArgb(255,140,0), Color.Yellow, Color.FromArgb(0,200,80), Color.FromArgb(0,120,255), Color.FromArgb(130,0,220), Color.Magenta };
    public override Rectangle GetBounds() { if(Points.Count==0) return Rectangle.Empty; int x1=int.MaxValue,y1=int.MaxValue,x2=int.MinValue,y2=int.MinValue; foreach(var p in Points){if(p.X<x1)x1=p.X;if(p.Y<y1)y1=p.Y;if(p.X>x2)x2=p.X;if(p.Y>y2)y2=p.Y;} int pad=(int)Width+4; return Rectangle.FromLTRB(x1-pad,y1-pad,x2+pad,y2+pad); }
    public override void Draw(Graphics g) {
        if (Points.Count < 2) return;
        if (Rainbow) {
            // MUP-66e2 v2: LinearGradientBrush instead of per-segment Pen creation
            Rectangle bounds = GetBounds();
            if (bounds.Width < 2) bounds.Width = 2;
            if (bounds.Height < 2) bounds.Height = 2;
            int off = AnimatedRainbow ? RainbowFrame : 0;
            using (LinearGradientBrush lb = new LinearGradientBrush(bounds, Color.Red, Color.Magenta, LinearGradientMode.Horizontal)) {
                ColorBlend cb = new ColorBlend(7);
                for (int i=0;i<7;i++){cb.Colors[i]=RAINBOW[(i+off)%7];cb.Positions[i]=i/6f;}
                lb.InterpolationColors = cb;
                using (Pen p = new Pen(lb, Width)) { p.StartCap=Cap; p.EndCap=Cap; p.LineJoin=LineJoin.Round; g.DrawLines(p, Points.ToArray()); }
            }
        } else {
            using (Pen p = new Pen(Color, Width)) { p.DashStyle=Dash; p.StartCap=Cap; p.EndCap=Cap; p.LineJoin=LineJoin.Round; g.DrawLines(p, Points.ToArray()); }
        }
    }
}
class ArrowShape : Shape {
    public Point A, B;
    public ArrowHead Head = ArrowHead.Filled;
    public bool Dual = false;
    public bool Rainbow = false;
    public bool AnimatedRainbow = false;
    static readonly Color[] RAINBOW = { Color.Red, Color.FromArgb(255,140,0), Color.Yellow, Color.FromArgb(0,200,80), Color.FromArgb(0,120,255), Color.FromArgb(130,0,220), Color.Magenta };
    public override Rectangle GetBounds() { int pad=(int)Width+20; return Rectangle.FromLTRB(Math.Min(A.X,B.X)-pad,Math.Min(A.Y,B.Y)-pad,Math.Max(A.X,B.X)+pad,Math.Max(A.Y,B.Y)+pad); }
    public override void Draw(Graphics g) {
        if (A == B) return;
        Color c = Rainbow ? RAINBOW[0] : Color;
        if (Rainbow) {
            Rectangle r = Rectangle.FromLTRB(Math.Min(A.X,B.X)-1,Math.Min(A.Y,B.Y)-1,Math.Max(A.X,B.X)+1,Math.Max(A.Y,B.Y)+1);
            if (r.Width < 2) r.Width = 2; if (r.Height < 2) r.Height = 2;
            using (LinearGradientBrush lb = new LinearGradientBrush(r, Color.Red, Color.Magenta, LinearGradientMode.Horizontal)) {
                ColorBlend cb = new ColorBlend(7);
                int off = AnimatedRainbow ? RainbowFrame : 0;
                for (int i=0;i<7;i++){cb.Colors[i]=RAINBOW[(i+off) % RAINBOW.Length]; cb.Positions[i]=i/6f;}
                lb.InterpolationColors = cb;
                using (Pen p = new Pen(lb, Width)) { ApplyCaps(p); g.DrawLine(p,A,B); }
            }
        } else {
            using (Pen p = new Pen(c, Width)) { ApplyCaps(p); g.DrawLine(p,A,B); }
        }
    }
    // Cached arrow caps to avoid GDI+ resource leaks (created once, reused)
    static readonly AdjustableArrowCap _capFilled = new AdjustableArrowCap(6,6,true);
    static readonly AdjustableArrowCap _capOpen   = new AdjustableArrowCap(6,6,false);
    static readonly AdjustableArrowCap _capDiamond= new AdjustableArrowCap(5,5,true);
    void ApplyCaps(Pen p) {
        switch(Head) {
            case ArrowHead.Filled: p.CustomEndCap=_capFilled; break;
            case ArrowHead.Open:   p.CustomEndCap=_capOpen; break;
            case ArrowHead.Diamond:p.CustomEndCap=_capDiamond; break;
            case ArrowHead.Dot:    p.EndCap=LineCap.RoundAnchor; break;
        }
        if (Dual) {
            switch(Head) {
                case ArrowHead.Filled: p.CustomStartCap=_capFilled; break;
                case ArrowHead.Open:   p.CustomStartCap=_capOpen; break;
                default:               p.StartCap=LineCap.RoundAnchor; break;
            }
        } else { p.StartCap=LineCap.Round; }
    }
}
class RectShape : Shape {
    public Rectangle Rect; public bool Fill;
    public int CornerRadius = 0;
    public bool Rainbow = false;
    public bool AnimatedRainbow = false;
    static readonly Color[] RAINBOW = { Color.Red, Color.FromArgb(255,140,0), Color.Yellow, Color.FromArgb(0,200,80), Color.FromArgb(0,120,255), Color.FromArgb(130,0,220), Color.Magenta };
    public override Rectangle GetBounds() { int pad=(int)Width+4; Rectangle r=Rect; r.Inflate(pad,pad); return r; }
    Color[] GetRainbow() { if (!AnimatedRainbow) return RAINBOW; Color[] r=new Color[7]; int off=RainbowFrame; for(int i=0;i<7;i++) r[i]=RAINBOW[(i+off)%7]; return r; }
    public override void Draw(Graphics g) {
        if (Rect.Width<1||Rect.Height<1) return;
        if (Fill) {
            Brush fb;
            if (Rainbow) {
                Rectangle br = Rect; if (br.Width<2)br.Width=2; if (br.Height<2)br.Height=2;
                LinearGradientBrush lb = new LinearGradientBrush(br, Color.Red, Color.Magenta, LinearGradientMode.Horizontal);
                Color[] rc=GetRainbow(); ColorBlend cb = new ColorBlend(7); for(int i=0;i<7;i++){cb.Colors[i]=rc[i];cb.Positions[i]=i/6f;} lb.InterpolationColors=cb;
                fb = lb;
            } else { fb = new SolidBrush(Color.FromArgb(80,Color)); }
            using(fb) { if(CornerRadius>0){using(GraphicsPath p=RoundRect(Rect,CornerRadius))g.FillPath(fb,p);}else g.FillRectangle(fb,Rect); }
        } else {
            Pen pen;
            if (Rainbow) {
                Rectangle br = Rect; if (br.Width<2)br.Width=2; if (br.Height<2)br.Height=2;
                LinearGradientBrush lb = new LinearGradientBrush(br, Color.Red, Color.Magenta, LinearGradientMode.Horizontal);
                Color[] rc=GetRainbow(); ColorBlend cb = new ColorBlend(7); for(int i=0;i<7;i++){cb.Colors[i]=rc[i];cb.Positions[i]=i/6f;} lb.InterpolationColors=cb;
                pen = new Pen(lb, Width);
            } else { pen = new Pen(Color,Width); }
            using(pen) { if(CornerRadius>0){using(GraphicsPath p=RoundRect(Rect,CornerRadius))g.DrawPath(pen,p);}else g.DrawRectangle(pen,Rect); }
        }
    }
    static GraphicsPath RoundRect(Rectangle r, int rad) {
        GraphicsPath p=new GraphicsPath(); int d=rad*2;
        p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90);
        p.CloseFigure(); return p;
    }
}
class EllipseShape : Shape {
    public Rectangle Rect;
    public bool Rainbow = false;
    public bool AnimatedRainbow = false;
    public bool Fill = false;
    static readonly Color[] RAINBOW = { Color.Red, Color.FromArgb(255,140,0), Color.Yellow, Color.FromArgb(0,200,80), Color.FromArgb(0,120,255), Color.FromArgb(130,0,220), Color.Magenta };
    public override Rectangle GetBounds() { int pad=(int)Width+4; Rectangle r=Rect; r.Inflate(pad,pad); return r; }
    Color[] GetRainbow() { if (!AnimatedRainbow) return RAINBOW; Color[] r=new Color[7]; int off=RainbowFrame; for(int i=0;i<7;i++) r[i]=RAINBOW[(i+off)%7]; return r; }
    public override void Draw(Graphics g) {
        if (Rect.Width<1||Rect.Height<1) return;
        if (Fill) {
            if (Rainbow) {
                Rectangle br = Rect; if (br.Width<2)br.Width=2; if (br.Height<2)br.Height=2;
                using (LinearGradientBrush lb = new LinearGradientBrush(br, Color.Red, Color.Magenta, LinearGradientMode.Horizontal)) {
                    Color[] rc=GetRainbow(); ColorBlend cb = new ColorBlend(7); for(int i=0;i<7;i++){cb.Colors[i]=rc[i];cb.Positions[i]=i/6f;} lb.InterpolationColors=cb;
                    g.FillEllipse(lb,Rect);
                }
            } else {
                using (SolidBrush fb=new SolidBrush(Color.FromArgb(80,Color))) g.FillEllipse(fb,Rect);
            }
        }
        if (Rainbow) {
            Rectangle br = Rect; if (br.Width<2)br.Width=2; if (br.Height<2)br.Height=2;
            using (LinearGradientBrush lb = new LinearGradientBrush(br, Color.Red, Color.Magenta, LinearGradientMode.Horizontal)) {
                Color[] rc=GetRainbow(); ColorBlend cb = new ColorBlend(7); for(int i=0;i<7;i++){cb.Colors[i]=rc[i];cb.Positions[i]=i/6f;} lb.InterpolationColors=cb;
                using (Pen p=new Pen(lb,Width)) g.DrawEllipse(p,Rect);
            }
        } else {
            using (Pen p=new Pen(Color,Width)) g.DrawEllipse(p,Rect);
        }
    }
}
class TextShape : Shape {
    public Point Loc; public string Text=""; public float FontSize=20f;
    public bool Rainbow = false; // MUP-e0d2: Rainbow text support
    public override Rectangle GetBounds() { using(Font f=new Font("Segoe UI",FontSize,FontStyle.Bold)){using(Bitmap b=new Bitmap(1,1)){using(Graphics g=Graphics.FromImage(b)){SizeF sz=g.MeasureString(Text,f);return new Rectangle(Loc.X-4,Loc.Y-4,(int)sz.Width+12,(int)sz.Height+12);}}} }
    public bool AnimatedRainbow = false; // MUP-e0d2: Animated rainbow text
    public override void Draw(Graphics g) {
        if (string.IsNullOrEmpty(Text)) return;
        using (Font f=new Font("Segoe UI",FontSize,FontStyle.Bold)) {
            if (Rainbow) {
                // MUP-66e2: Single LinearGradientBrush instead of per-character loop (97% fewer GDI calls)
                SizeF sz=g.MeasureString(Text,f);
                Rectangle r=new Rectangle(Loc.X,Loc.Y,Math.Max((int)sz.Width,2),Math.Max((int)sz.Height,2));
                int off=AnimatedRainbow?RainbowFrame:0;
                Color[] rc={Color.Red,Color.FromArgb(255,140,0),Color.Yellow,Color.FromArgb(0,200,80),Color.FromArgb(0,120,255),Color.FromArgb(130,0,220),Color.Magenta};
                using (LinearGradientBrush lb=new LinearGradientBrush(r,Color.Red,Color.Magenta,LinearGradientMode.Horizontal)) {
                    ColorBlend cb=new ColorBlend(7);
                    for(int i=0;i<7;i++){cb.Colors[i]=rc[(i+off)%7];cb.Positions[i]=i/6f;}
                    lb.InterpolationColors=cb;
                    using (SolidBrush sh=new SolidBrush(Color.FromArgb(160,0,0,0)))
                        g.DrawString(Text,f,sh,Loc.X+2,Loc.Y+2);
                    g.DrawString(Text,f,lb,Loc.X,Loc.Y);
                }
            } else {
                using (SolidBrush sh=new SolidBrush(Color.FromArgb(160,0,0,0)))
                using (SolidBrush br=new SolidBrush(Color)) {
                    g.DrawString(Text,f,sh,Loc.X+2,Loc.Y+2);
                    g.DrawString(Text,f,br,Loc.X,Loc.Y);
                }
            }
        }
    }
    static Color HueColor(float h) {
        float s=1,v=1; int hi=(int)(h/60)%6; float f=h/60-hi; float q=1-f; float t=f;
        return hi switch { 0=>Color.FromArgb((int)(v*255),(int)(t*255),0), 1=>Color.FromArgb((int)(q*255),(int)(v*255),0),
            2=>Color.FromArgb(0,(int)(v*255),(int)(t*255)), 3=>Color.FromArgb(0,(int)(q*255),(int)(v*255)),
            4=>Color.FromArgb((int)(t*255),0,(int)(v*255)), _=>Color.FromArgb((int)(v*255),0,(int)(q*255)) };
    }
}
class CensorShape : Shape {
    public Rectangle Rect;
    public bool Rainbow = false;
    public bool AnimatedRainbow = false;
    public CensorMethod Method = CensorMethod.BlackBar;
    public CensorIntensity Intensity = CensorIntensity.Medium;
    public Bitmap PixelCache; // MUP-0aa8: Cached pixelated screenshot for Pixelate method
    static readonly Color[] RAINBOW = { Color.Red, Color.FromArgb(255,140,0), Color.Yellow, Color.FromArgb(0,200,80), Color.FromArgb(0,120,255), Color.FromArgb(130,0,220), Color.Magenta };
    public override Rectangle GetBounds() { int pad=(int)Width+4; Rectangle r=Rect; r.Inflate(pad,pad); return r; }
    int Alpha { get { return Intensity==CensorIntensity.Light?150:Intensity==CensorIntensity.Heavy?245:210; } }
    public override void Draw(Graphics g) {
        if (Rect.Width<1||Rect.Height<1) return;
        if (Rainbow) {
            Rectangle br = Rect; if (br.Width<2)br.Width=2; if (br.Height<2)br.Height=2;
            using (LinearGradientBrush lb = new LinearGradientBrush(br, Color.Red, Color.Magenta, LinearGradientMode.Horizontal)) {
                Color[] rc=RAINBOW; if(AnimatedRainbow){rc=new Color[7]; int off=RainbowFrame; for(int i=0;i<7;i++) rc[i]=RAINBOW[(i+off)%7];}
                ColorBlend cb = new ColorBlend(7); for(int i=0;i<7;i++){cb.Colors[i]=rc[i];cb.Positions[i]=i/6f;} lb.InterpolationColors=cb;
                using (SolidBrush b=new SolidBrush(Color.FromArgb(180,10,10,20))) g.FillRectangle(b,Rect);
                g.FillRectangle(lb,Rect);
            }
            return;
        }
        // MUP-234826: Multiple censor methods
        int a=Alpha;
        switch(Method) {
            case CensorMethod.BlackBar:
                using (SolidBrush b=new SolidBrush(Color.FromArgb(a,10,10,20))) g.FillRectangle(b,Rect);
                using (HatchBrush h=new HatchBrush(HatchStyle.LargeGrid,Color.FromArgb(20,80,80,80),Color.FromArgb(a,10,10,20))) g.FillRectangle(h,Rect);
                break;
            case CensorMethod.Pixelate:
                // MUP-0aa8: Greenshot-style pixelation - grid of colored blocks
                int bs = Intensity==CensorIntensity.Light ? 14 : Intensity==CensorIntensity.Heavy ? 6 : 10;
                if (PixelCache != null) {
                    // Draw cached pixelated screenshot
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(PixelCache, Rect);
                    g.InterpolationMode = InterpolationMode.Default;
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                } else {
                    // Fallback: procedural mosaic pattern (for live drawing before commit)
                    for (int by = Rect.Y; by < Rect.Bottom; by += bs) {
                        for (int bx = Rect.X; bx < Rect.Right; bx += bs) {
                            int hash = ((bx / bs) * 17 + (by / bs) * 31) & 0xFF;
                            int shade = 10 + (hash % 40);
                            int bw = Math.Min(bs, Rect.Right - bx);
                            int bh = Math.Min(bs, Rect.Bottom - by);
                            using (SolidBrush sb = new SolidBrush(Color.FromArgb(a, shade, shade, shade + 10)))
                                g.FillRectangle(sb, bx, by, bw, bh);
                        }
                    }
                }
                break;
            case CensorMethod.DiagonalHatch:
                using (SolidBrush b=new SolidBrush(Color.FromArgb(a-40,10,10,20))) g.FillRectangle(b,Rect);
                using (HatchBrush h=new HatchBrush(HatchStyle.ForwardDiagonal,Color.FromArgb(a,40,40,60),Color.FromArgb(a,10,10,20))) g.FillRectangle(h,Rect);
                break;
            case CensorMethod.CrossHatch:
                using (SolidBrush b=new SolidBrush(Color.FromArgb(a-60,20,10,10))) g.FillRectangle(b,Rect);
                using (HatchBrush h=new HatchBrush(HatchStyle.DiagonalCross,Color.FromArgb(a,180,30,30),Color.FromArgb(a,30,10,10))) g.FillRectangle(h,Rect);
                break;
            case CensorMethod.Blur:
                // Simulated blur: multiple semi-transparent offset layers
                for(int i=0;i<5;i++) {
                    int off=i*2; Rectangle r2=new Rectangle(Rect.X-off,Rect.Y-off,Rect.Width+off*2,Rect.Height+off*2);
                    using (SolidBrush b=new SolidBrush(Color.FromArgb(a/5,10,10,20))) g.FillRectangle(b,r2);
                }
                using (SolidBrush b2=new SolidBrush(Color.FromArgb(a,10,10,20))) g.FillRectangle(b2,Rect);
                break;
        }
    }
}

class StampShape : Shape {
    public Point Loc; public string Emoji="✔"; public float Size=40f;
    public override void Draw(Graphics g) {
        if (string.IsNullOrEmpty(Emoji)) return;
        using (Font f=new Font("Segoe UI Emoji",Size,FontStyle.Regular)) {
            SizeF ms=g.MeasureString(Emoji,f);
            float cx=Loc.X-ms.Width/2, cy=Loc.Y-ms.Height/2;
            using (SolidBrush sh=new SolidBrush(Color.FromArgb(100,0,0,0)))
                g.DrawString(Emoji,f,sh,cx+2,cy+2);
            // MUP-234951: Use TextRenderer for native emoji colors instead of solid white brush
            TextRenderer.DrawText(g, Emoji, f, new Point((int)cx,(int)cy), Color.Black, TextFormatFlags.NoPrefix);
        }
    }
}

class ImageStampShape : Shape {
    public Point Loc; public Bitmap StampImage; public float Scale=1.0f;
    public override void Draw(Graphics g) {
        if (StampImage==null) return;
        int w=(int)(StampImage.Width*Scale), h=(int)(StampImage.Height*Scale);
        int x=Loc.X-w/2, y=Loc.Y-h/2;
        // MUP-9687: Removed drop shadow - was causing gray background on icons
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        g.DrawImage(StampImage,x,y,w,h);
    }
}

// MUP-20260328-170558899-8e36: Numbered circle annotation tool
class NumberedCircleShape : Shape {
    public Rectangle Rect;
    public int Number = 1;
    public bool Fill = false;
    public bool Rainbow = false;
    public bool AnimatedRainbow = false;
    static readonly Color[] RAINBOW = { Color.Red, Color.FromArgb(255,140,0), Color.Yellow, Color.FromArgb(0,200,80), Color.FromArgb(0,120,255), Color.FromArgb(130,0,220), Color.Magenta };
    public override Rectangle GetBounds() { int pad=(int)Width+4; Rectangle r=Rect; r.Inflate(pad,pad); return r; }
    Color[] GetRainbow() { if (!AnimatedRainbow) return RAINBOW; Color[] r=new Color[7]; int off=RainbowFrame; for(int i=0;i<7;i++) r[i]=RAINBOW[(i+off)%7]; return r; }
    public override void Draw(Graphics g) {
        if (Rect.Width<1||Rect.Height<1) return;
        if (Fill) {
            if (Rainbow) {
                Rectangle br=Rect; if(br.Width<2)br.Width=2; if(br.Height<2)br.Height=2;
                using (LinearGradientBrush lb=new LinearGradientBrush(br,Color.Red,Color.Magenta,LinearGradientMode.Horizontal)) {
                    Color[] rc=GetRainbow(); ColorBlend cb=new ColorBlend(7); for(int i=0;i<7;i++){cb.Colors[i]=rc[i];cb.Positions[i]=i/6f;} lb.InterpolationColors=cb;
                    g.FillEllipse(lb,Rect);
                }
            } else {
                using (SolidBrush fb=new SolidBrush(Color.FromArgb(80,Color))) g.FillEllipse(fb,Rect);
            }
        }
        // FB-8e36 v3: Draw thick black outline FIRST for prominent border
        float borderW=Math.Max(Width+3f, 5f);
        using (Pen bp=new Pen(Color.Black,borderW)) g.DrawEllipse(bp,Rect);
        if (Rainbow) {
            Rectangle br=Rect; if(br.Width<2)br.Width=2; if(br.Height<2)br.Height=2;
            using (LinearGradientBrush lb=new LinearGradientBrush(br,Color.Red,Color.Magenta,LinearGradientMode.Horizontal)) {
                Color[] rc=GetRainbow(); ColorBlend cb=new ColorBlend(7); for(int i=0;i<7;i++){cb.Colors[i]=rc[i];cb.Positions[i]=i/6f;} lb.InterpolationColors=cb;
                using (Pen p=new Pen(lb,Width)) g.DrawEllipse(p,Rect);
            }
        } else {
            using (Pen p=new Pen(Color,Width)) g.DrawEllipse(p,Rect);
        }
        // Draw centered number text - font size ~40% of smaller dimension
        string txt=Number.ToString();
        float dim=Math.Min(Rect.Width,Rect.Height);
        float fontSize=Math.Max(8f, dim*0.4f);
        using (Font f=new Font("Segoe UI",fontSize,FontStyle.Bold))
        using (StringFormat sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}) {
            RectangleF textRect=new RectangleF(Rect.X,Rect.Y,Rect.Width,Rect.Height);
            // FB-8e36: White text with black outline for readability (rainbow or not)
            if (Rainbow) {
                using (SolidBrush ob=new SolidBrush(System.Drawing.Color.Black))
                    for(int dx=-1;dx<=1;dx++) for(int dy=-1;dy<=1;dy++) { if(dx==0&&dy==0)continue; RectangleF or2=textRect; or2.Offset(dx,dy); g.DrawString(txt,f,ob,or2,sf); }
                using (SolidBrush wb=new SolidBrush(System.Drawing.Color.White)) g.DrawString(txt,f,wb,textRect,sf);
            } else {
                using (SolidBrush sb=new SolidBrush(Color)) g.DrawString(txt,f,sb,textRect,sf);
            }
        }
    }
}

// ── Stamp Manager - loads/generates all stamp images ────────────────────────
static class StampManager {
    static Dictionary<string,Bitmap> _cache = new Dictionary<string,Bitmap>();
    static bool _initialized = false;

    // MUP-20260912-101909129-ca2d: two third-party company logos used to
    // sit here as base64 (~39 KB). They named the employer, and `#if` only
    // removes them from the BUILD, not from the FILE.
    // Logos now come from stamps/ -- see LadeLogosAusOrdner.
    // Note from MUP-234715: a logo may be named .png and actually be WebP;
    // .NET then cannot load it. LoadLogo catches that and puts a text stamp
    // in its place.

    // 🔴 No longer `readonly`: LadeLogosAusOrdner appends whatever is
    // in stamps/. The four text stamps are always present.
    public static string[] ImageStampKeys = { "approved","rejected","draft","confidential" };
    public static string[] ImageStampNames = { "Approved","Rejected","Draft","Confidential" };

    public static void Init() {
        if (_initialized) return;
        _initialized = true;
        // Generate text-based stamps
        _cache["approved"]     = MakeTextStamp("APPROVED",     Color.FromArgb(30,160,60),  Color.White);
        _cache["rejected"]     = MakeTextStamp("REJECTED",     Color.FromArgb(200,40,40),  Color.White);
        _cache["draft"]        = MakeTextStamp("DRAFT",        Color.FromArgb(180,140,20), Color.FromArgb(40,40,40));
        _cache["confidential"] = MakeTextStamp("CONFIDENTIAL", Color.FromArgb(140,40,160), Color.White);
        // Load logo PNGs from stamps/ directory next to the executable
        string stampDir = GetStampDir();
        // MUP-20260912-101909129-ca2d: no more hard-wired company names.
        // Whatever sits in stamps/ as *_logo.png is offered -- the label
        // comes from the file name. That keeps the source free of
        // third-party marks AND lets the private edition keep its logos.
        LadeLogosAusOrdner(stampDir);
    }

    // MUP-20260912-101909129-ca2d: logos from the folder instead of from
    // the source. Two third-party company logos used to sit in Program.cs as
    // base64 (~28 KB) with their names in two arrays -- `#if` removes them
    // from the BUILD, not from the FILE. Anyone reading the repository would
    // still have seen them.
    static void LadeLogosAusOrdner(string stampDir) {
        try {
            if (!Directory.Exists(stampDir)) return;
            var dateien = Directory.GetFiles(stampDir, "*_logo.png");
            Array.Sort(dateien, StringComparer.OrdinalIgnoreCase);
            var keys = new List<string>(ImageStampKeys);
            var namen = new List<string>(ImageStampNames);
            foreach (string pfad in dateien) {
                string key = Path.GetFileNameWithoutExtension(pfad);
                if (key.EndsWith("_logo", StringComparison.OrdinalIgnoreCase))
                    key = key.Substring(0, key.Length - 5);
                if (key.Length == 0 || keys.Contains(key)) continue;
                LoadLogo(key, pfad);
                keys.Add(key);
                namen.Add(char.ToUpper(key[0]) + key.Substring(1) + " Logo");
            }
            ImageStampKeys = keys.ToArray();
            ImageStampNames = namen.ToArray();
        } catch {
            // A missing or unreadable folder must not prevent startup --
            // the four text stamps are still there.
        }
    }

    static string GetStampDir() {
        // MUP-234715: Use EXE location as primary path (reliable for framework-dependent SingleFile)
        string[] candidates = {
            Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), "stamps"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stamps"),
            Path.Combine(AppContext.BaseDirectory, "stamps"),
            Path.Combine(Directory.GetCurrentDirectory(), "stamps"),
        };
        foreach (string dir in candidates) {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        }
        return candidates[0]; // fallback
    }



    // MUP-234715 v7: Try hardcoded byte[] first, then filesystem fallback
    static void LoadLogo(string key, string path) {
        try {
            // Hardcoded data - guaranteed to work regardless of filesystem
            byte[] data = null;
            if (data != null) {
                using (var ms = new System.IO.MemoryStream(data))
                using (Bitmap tmp = new Bitmap(ms)) {
                    _cache[key] = new Bitmap(tmp);
                    return;
                }
            }
            // Filesystem fallback
            if (File.Exists(path)) {
                using (Bitmap tmp = new Bitmap(path)) {
                    _cache[key] = new Bitmap(tmp);
                }
            } else {
                _cache[key] = MakeTextStamp(key.ToUpper(), Color.FromArgb(80,80,120), Color.White);
            }
        } catch {
            _cache[key] = MakeTextStamp(key.ToUpper(), Color.FromArgb(80,80,120), Color.White);
        }
    }

    static Bitmap MakeTextStamp(string text, Color bg, Color fg) {
        // Render a badge-style stamp image
        int pad=16, borderR=10;
        using (Font f=new Font("Segoe UI",16f,FontStyle.Bold)) {
            // Measure text
            SizeF sz;
            using (Bitmap tmp=new Bitmap(1,1)) using (Graphics gt=Graphics.FromImage(tmp)) sz=gt.MeasureString(text,f);
            int w=(int)sz.Width+pad*2, h=(int)sz.Height+pad;
            Bitmap bmp=new Bitmap(w,h,PixelFormat.Format32bppArgb);
            using (Graphics g=Graphics.FromImage(bmp)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                // Rounded rect background
                using (GraphicsPath gp=new GraphicsPath()) {
                    int d=borderR*2;
                    gp.AddArc(0,0,d,d,180,90); gp.AddArc(w-d,0,d,d,270,90);
                    gp.AddArc(w-d,h-d,d,d,0,90); gp.AddArc(0,h-d,d,d,90,90);
                    gp.CloseFigure();
                    using (SolidBrush bb=new SolidBrush(bg)) g.FillPath(bb,gp);
                    using (Pen bp=new Pen(Color.FromArgb(60,255,255,255),2f)) g.DrawPath(bp,gp);
                }
                // Centered text
                using (SolidBrush fb=new SolidBrush(fg)) {
                    StringFormat sf=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
                    g.DrawString(text,f,fb,new RectangleF(0,0,w,h),sf);
                }
            }
            return bmp;
        }
    }

    // MUP-234951 v3: Pre-bundled PNG color emojis (Twemoji) - guaranteed color on all Windows
    static readonly Dictionary<string,string> EmojiPngMap = new Dictionary<string,string> {
        {"\u2714","check"},{"\u274C","cross"},{"\u2757","exclamation"},{"\u2753","question"},
        {"\u2B50","star"},{"\uD83D\uDD25","fire"},{"\uD83D\uDC4D","thumbsup"},{"\uD83D\uDC4E","thumbsdown"},
        {"\uD83D\uDCA1","lightbulb"},{"\u26A0","warning"},{"\uD83D\uDD12","lock"},{"\u2764","heart"},
        {"\uD83D\uDE80","rocket"},{"\uD83C\uDFAF","target"},{"\uD83D\uDCDD","memo"},{"\uD83C\uDF89","party"},
        {"\uD83D\uDED1","stop"},{"\uD83D\uDD04","refresh"},{"\uD83D\uDCCC","pin"},{"\uD83E\uDD84","unicorn"},
        {"\uD83D\uDCA9","poop"},{"\uD83E\uDECF","donkey"},{"\uD83D\uDD95","middle_finger"}
    };

    public static Bitmap RenderColorEmoji(string emoji, int size=64) {
        string cacheKey = "emoji_" + emoji + "_" + size;
        if (_cache.ContainsKey(cacheKey)) return _cache[cacheKey];
        try {
            string pngName;
            if (!EmojiPngMap.TryGetValue(emoji, out pngName)) return null;
            Bitmap src = null;
            {
                string pngPath = Path.Combine(GetStampDir(), "emoji", pngName + ".png");
                if (!File.Exists(pngPath)) return null;
                src = new Bitmap(pngPath);
            }
            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result)) {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, size, size);
            }
            src.Dispose();
            _cache[cacheKey] = result;
            return result;
        } catch {
            return null;
        }
    }

    public static Bitmap Get(string key) {
        Init();
        Bitmap bmp;
        return _cache.TryGetValue(key, out bmp) ? bmp : null;
    }

    // Generate a small thumbnail for the context menu (24x24 or proportional)
    public static Image GetThumbnail(string key, int maxH=24) {
        Bitmap src=Get(key);
        if (src==null) return null;
        float scale=(float)maxH/src.Height;
        int w=(int)(src.Width*scale), h=maxH;
        Bitmap thumb=new Bitmap(w,h,PixelFormat.Format32bppArgb);
        using (Graphics g=Graphics.FromImage(thumb)) {
            g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.DrawImage(src,0,0,w,h);
        }
        return thumb;
    }
}

// MKP-133745: Global low-level mouse hook - captures mouse events regardless of focus
static class MouseHookManager {
    static IntPtr _hookId = IntPtr.Zero;
    static Win32.HookProc _proc; // prevent GC collection of delegate

    // MUP-130727: Swallow mouse clicks so they don't reach windows below the overlay
    public static bool SwallowClicks;
    // MUP-3205 v2: Skip all hook processing when markup is inactive
    public static bool Active;
    // MUP-c5be: Toolbar bounds - clicks inside this rect are NOT swallowed
    public static Rectangle ToolbarRect;

    // MKP-133745 v2: Async queue - hook callback returns instantly, UI thread drains
    static ConcurrentQueue<(int msg, Point pt)> _queue = new ConcurrentQueue<(int, Point)>();
    static bool _isDown;

    public static bool TryDequeue(out (int msg, Point pt) evt) { return _queue.TryDequeue(out evt); }

    public static void Install() {
        _proc = Callback;
        // IntPtr.Zero is correct for WH_MOUSE_LL in .NET (no module handle needed)
        _hookId = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
    }

    public static void Uninstall() {
        if (_hookId != IntPtr.Zero) { Win32.UnhookWindowsHookEx(_hookId); _hookId = IntPtr.Zero; }
    }

    static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam) {
        // MUP-3205 v2: Skip ALL processing when markup mode is OFF - zero overhead
        if (nCode >= 0 && Active) {
            int msg = (int)wParam;
            if (msg == Win32.WM_LBUTTONDOWN || (msg == Win32.WM_MOUSEMOVE && _isDown) || msg == Win32.WM_LBUTTONUP
                || msg == Win32.WM_RBUTTONDOWN) {
                var info = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                Point pt = new Point(info.pt.x, info.pt.y);
                if (msg == Win32.WM_LBUTTONDOWN) _isDown = true;
                else if (msg == Win32.WM_LBUTTONUP) _isDown = false;
                _queue.Enqueue((msg, pt));
                // MUP-c5be: Don't swallow clicks on the toolbar - let them pass through
                if (SwallowClicks && (msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_LBUTTONUP || msg == Win32.WM_RBUTTONDOWN)
                    && !ToolbarRect.Contains(pt))
                    return (IntPtr)1;
            }
        }
        return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}

enum DrawTool { Pen, Arrow, Rect, Ellipse, Text, Highlight, Censor, Stamp, NumberedCircle }
enum ArrowHead { Filled, Open, Diamond, Dot }
// MUP-234826: Censor methods and intensity
enum CensorMethod { BlackBar, Pixelate, DiagonalHatch, CrossHatch, Blur }
enum CensorIntensity { Light, Medium, Heavy }

// ── Shared drawing state (all overlays share tool/colour/width) ───────────
class DrawState {
    public DrawTool Tool      = DrawTool.Pen;
    public Color    Color     = Color.FromArgb(255, 60, 60);
    public float    Width     = 4f;
    public bool     PassThru  = true;  // MUP-234541: Markup mode OFF at startup
    public bool     ShowHelp  = false;
    // MUP-20260912-102455567-e140: position of the help box.
    // null = centred as before -- anyone who never drags it notices no
    // difference. 🔴 Belongs in the SHARED state: otherwise the help
    // box moves on one monitor and stays put on the other.
    public Point?   HelpPos   = null;

    // Stamp tool
    public string StampEmoji = "\u2714"; // ✔ checkmark default
    public string StampImageKey = null;  // null = emoji mode, non-null = image stamp key
    public bool   IsImageStamp { get { return StampImageKey != null; } }
    // Per-tool options
    public DashStyle PenDash  = DashStyle.Solid;
    public LineCap   PenCap   = LineCap.Round;
    public ArrowHead ArrowHeadStyle = ArrowHead.Filled;
    public bool      ArrowDual = false;
    public int       RectRadius = 0;
    public bool      RectFill   = false;
    // Rainbow / Unicorn color
    public bool      IsRainbow = false;
    public bool      IsAnimatedRainbow = false;
    // MUP-234826: Additional tool options
    public CensorMethod    CensorMode = CensorMethod.BlackBar;
    public CensorIntensity CensorLevel = CensorIntensity.Medium;
    public bool      EllipseFill  = false;
    public float     TextSize     = 20f;
    public float     HighlightAlpha = 64f; // MUP-133846: 25% opacity (64/255)
    // MUP-20260328-170558899-8e36: Numbered circle tool
    public int       NextNumCircle = 1;
    public bool      NumCircleFill = false;

    // Hotkeys - FB-3792 v3: Ctrl+Shift+letter defaults (freely configurable)
    public Keys KPen=Keys.Control|Keys.Shift|Keys.D, KArrow=Keys.Control|Keys.Shift|Keys.A, KRect=Keys.Control|Keys.Shift|Keys.R, KEll=Keys.Control|Keys.Shift|Keys.C,
                KText=Keys.Control|Keys.Shift|Keys.T, KHl=Keys.Control|Keys.Shift|Keys.H, KCen=Keys.Control|Keys.Shift|Keys.B, KStamp=Keys.Control|Keys.Shift|Keys.S,
                KNumCircle=Keys.Control|Keys.Shift|Keys.N,
                KUndo=Keys.Z, KClear=Keys.Control|Keys.Shift|Keys.X, KHelp=Keys.Control|Keys.Shift|Keys.I, KTray=Keys.Control|Keys.Shift|Keys.Y, KPass=Keys.Control|Keys.Shift|Keys.P,
                // FB-a295: New configurable shortcuts for tray/show/quit (were hardcoded)
                KShow=Keys.Control|Keys.Shift|Keys.M, KQuit=Keys.Control|Keys.Shift|Keys.Q;

    // FB-3792: Human-readable label for any Keys combo (e.g. "Shift+D", "Ctrl+Z")
    // FB-a295: OEM keys (Ä, Ö, Ü, etc.) are resolved via MapVirtualKeyW to show actual characters
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint uCode, uint uMapType);
    public static string FormatKey(Keys k) {
        var parts = new System.Collections.Generic.List<string>();
        if ((k & Keys.Control) != 0) parts.Add("Ctrl");
        if ((k & Keys.Shift) != 0) parts.Add("Shift");
        if ((k & Keys.Alt) != 0) parts.Add("Alt");
        Keys code = k & Keys.KeyCode;
        if (code != Keys.None) {
            string name = code.ToString();
            // Resolve OEM keys to actual characters (e.g. Oem7 -> Ä on German keyboard)
            if (name.StartsWith("Oem") || name.StartsWith("D") && name.Length == 2 && char.IsDigit(name[1])) {
                uint vk = (uint)code;
                uint ch = MapVirtualKeyW(vk, 2); // MAPVK_VK_TO_CHAR
                if (ch > 32 && ch < 127) name = ((char)ch).ToString().ToUpper();
                else if (ch >= 127) name = ((char)ch).ToString().ToUpper();
            }
            parts.Add(name);
        }
        return parts.Count > 0 ? string.Join("+", parts) : k.ToString();
    }

    public string HotkeyLabel(DrawTool t) {
        switch(t){ case DrawTool.Pen:return FormatKey(KPen); case DrawTool.Arrow:return FormatKey(KArrow);
                   case DrawTool.Rect:return FormatKey(KRect); case DrawTool.Ellipse:return FormatKey(KEll);
                   case DrawTool.Text:return FormatKey(KText); case DrawTool.Highlight:return FormatKey(KHl);
                   case DrawTool.Censor:return FormatKey(KCen); case DrawTool.Stamp:return FormatKey(KStamp);
                   case DrawTool.NumberedCircle:return FormatKey(KNumCircle); }
        return "";
    }

    public event Action Changed;
    public void Fire() { Changed?.Invoke(); }
}

// ── Splash Screen ────────────────────────────────────────────────────────────
class SplashScreen : Form {
    Label _status;
    int _progressPercent;
    int _rainbowOff;
    Timer _rainbowTimer;
    static readonly Color[] RC = { Color.Red, Color.FromArgb(255,140,0), Color.Yellow, Color.Lime, Color.Cyan, Color.DodgerBlue, Color.BlueViolet };

    public SplashScreen() {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ShowInTaskbar = false;
        Size = new Size(360, 220);
        DoubleBuffered = true;
        // Status label (only child - everything else painted)
        _status = new Label {
            Text = "Starte...",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(220, 220, 240),
            AutoSize = false, Size = new Size(360, 20),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Location = new Point(0, 178)
        };
        Controls.Add(_status);
        _rainbowTimer = new Timer { Interval = 40 };
        _rainbowTimer.Tick += (s, e) => { _rainbowOff++; Invalidate(); };
        _rainbowTimer.Start();
    }
    public void SetProgress(int percent, string status) {
        if (InvokeRequired) { Invoke(new Action(() => SetProgress(percent, status))); return; }
        _status.Text = status; _progressPercent = percent;
        Invalidate(); Update(); Application.DoEvents();
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int W = Width, H = Height;
        // MUP-73d3 v3: Gradient background (dark purple → medium blue)
        using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(25,20,50), Color.FromArgb(35,30,70), LinearGradientMode.Vertical))
            g.FillRectangle(bg, ClientRectangle);
        // Animated rainbow border (3px, bright)
        float phase = (_rainbowOff % 210) / 30f;
        int n = RC.Length;
        {
            Rectangle br = new Rectangle(0, 0, W-1, H-1);
            if (br.Width < 2) br.Width = 2; if (br.Height < 2) br.Height = 2;
            Color c1 = RC[((int)phase) % n], c2 = RC[((int)phase + 3) % n];
            using (var lb = new LinearGradientBrush(br, c1, c2, (float)(_rainbowOff * 2 % 360)))
            using (Pen p = new Pen(lb, 3)) { p.LineJoin = System.Drawing.Drawing2D.LineJoin.Round; g.DrawRectangle(p, 1, 1, W-3, H-3); }
        }
        // Rainbow title "MarkUp" - full gradient text
        using (Font tf = new Font("Segoe UI", 28f, FontStyle.Bold)) {
            SizeF tsz = g.MeasureString("MarkUp", tf);
            float tx = (W - tsz.Width) / 2, ty = 40;
            Rectangle tr = new Rectangle((int)tx, (int)ty, Math.Max((int)tsz.Width, 2), Math.Max((int)tsz.Height, 2));
            int off = (_rainbowOff / 3) % n;
            using (var lb = new LinearGradientBrush(tr, Color.Red, Color.Magenta, LinearGradientMode.Horizontal)) {
                ColorBlend cb = new ColorBlend(n);
                for (int i = 0; i < n; i++) { cb.Colors[i] = RC[(i + off) % n]; cb.Positions[i] = i / (float)(n - 1); }
                lb.InterpolationColors = cb;
                // Shadow
                using (var sh = new SolidBrush(Color.FromArgb(100, 0, 0, 0))) g.DrawString("MarkUp", tf, sh, tx + 2, ty + 2);
                g.DrawString("MarkUp", tf, lb, tx, ty);
            }
        }
        // Version text
        using (Font vf = new Font("Segoe UI", 10f))
        using (var vb = new SolidBrush(Color.FromArgb(160, 140, 120, 255)))
            g.DrawString(AppInfo.VersionText(), vf, vb, (W - 30) / 2, 90);
        // Progress bar track (wider, taller)
        int barX = 40, barY = 150, barMaxW = W - 80, barH = 8;
        using (var track = new SolidBrush(Color.FromArgb(60, 50, 80))) {
            g.FillRectangle(track, barX, barY, barMaxW, barH);
        }
        // Rainbow progress fill
        int barW = Math.Max(20, (int)(barMaxW * _progressPercent / 100.0));
        if (barW > 1) {
            Rectangle pr = new Rectangle(barX, barY, barW, barH);
            Color pc1 = RC[((int)phase + 1) % n], pc2 = RC[((int)phase + 4) % n];
            using (var pb = new LinearGradientBrush(pr, pc1, pc2, LinearGradientMode.Horizontal))
                g.FillRectangle(pb, pr);
        }
    }
    protected override void OnFormClosing(FormClosingEventArgs e) {
        _rainbowTimer?.Stop(); _rainbowTimer?.Dispose(); base.OnFormClosing(e);
    }
}

// ── Application controller ────────────────────────────────────────────────────
class MarkUpApp {
    DrawState        _state = new DrawState();
    List<ScreenOverlay> _overlays = new List<ScreenOverlay>();
    FloatingBar      _bar;
    NotifyIcon       _tray;
    Timer            _drainTimer;
    Timer            _rainbowTimer;
    bool             _hidden = false;

    // FB-1452: Settings persistence
    static readonly string _settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarkUp");
    static readonly string _settingsPath = Path.Combine(_settingsDir, "settings.json");
    bool _needHotkeyMigrationSave = false; // FB-3792 v3: trigger save after migration

    void LoadSettings() {
        try {
            if (!File.Exists(_settingsPath)) return;
            string json = File.ReadAllText(_settingsPath);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("width", out var w)) _state.Width = (float)w.GetDouble();
            if (r.TryGetProperty("color", out var c)) _state.Color = Color.FromArgb(c.GetInt32());
            if (r.TryGetProperty("tool", out var t) && Enum.TryParse<DrawTool>(t.GetString(), out var tv)) _state.Tool = tv;
            if (r.TryGetProperty("highlightAlpha", out var ha)) _state.HighlightAlpha = (float)ha.GetDouble();
            if (r.TryGetProperty("textSize", out var ts)) _state.TextSize = (float)ts.GetDouble();
            if (r.TryGetProperty("penDash", out var pd) && Enum.TryParse<DashStyle>(pd.GetString(), out var pdv)) _state.PenDash = pdv;
            if (r.TryGetProperty("penCap", out var pc) && Enum.TryParse<LineCap>(pc.GetString(), out var pcv)) _state.PenCap = pcv;
            if (r.TryGetProperty("arrowHead", out var ah) && Enum.TryParse<ArrowHead>(ah.GetString(), out var ahv)) _state.ArrowHeadStyle = ahv;
            if (r.TryGetProperty("arrowDual", out var ad)) _state.ArrowDual = ad.GetBoolean();
            if (r.TryGetProperty("rectRadius", out var rr)) _state.RectRadius = rr.GetInt32();
            if (r.TryGetProperty("rectFill", out var rf)) _state.RectFill = rf.GetBoolean();
            if (r.TryGetProperty("ellipseFill", out var ef)) _state.EllipseFill = ef.GetBoolean();
            if (r.TryGetProperty("censorMode", out var cm) && Enum.TryParse<CensorMethod>(cm.GetString(), out var cmv)) _state.CensorMode = cmv;
            if (r.TryGetProperty("censorLevel", out var cl) && Enum.TryParse<CensorIntensity>(cl.GetString(), out var clv)) _state.CensorLevel = clv;
            // FB-3792 v5: One-time hotkey migration to Ctrl+Shift+letter defaults (no F-keys, no Delete)
            int hotkeyVer = 0;
            if (r.TryGetProperty("hotkeyVersion", out var hv)) hotkeyVer = hv.GetInt32();
            if (hotkeyVer < 3) {
                // Migration from v0-v2: reset ALL hotkeys to new Ctrl+Shift defaults
                _state.KPen   = Keys.Control|Keys.Shift|Keys.D;
                _state.KArrow = Keys.Control|Keys.Shift|Keys.A;
                _state.KRect  = Keys.Control|Keys.Shift|Keys.R;
                _state.KEll   = Keys.Control|Keys.Shift|Keys.C;
                _state.KText  = Keys.Control|Keys.Shift|Keys.T;
                _state.KHl    = Keys.Control|Keys.Shift|Keys.H;
                _state.KCen   = Keys.Control|Keys.Shift|Keys.B;
                _state.KStamp = Keys.Control|Keys.Shift|Keys.S;
                _state.KNumCircle = Keys.Control|Keys.Shift|Keys.N;
                _state.KClear = Keys.Control|Keys.Shift|Keys.X;
                _state.KHelp  = Keys.Control|Keys.Shift|Keys.I;
                _state.KTray  = Keys.Control|Keys.Shift|Keys.Y;
                _state.KPass  = Keys.Control|Keys.Shift|Keys.P;
                // FB-a295: Also set new v6 keys for full migration
                _state.KShow  = Keys.Control|Keys.Shift|Keys.M;
                _state.KQuit  = Keys.Control|Keys.Shift|Keys.Q;
                _state.KUndo  = Keys.Z;
                _needHotkeyMigrationSave = true;
            } else if (hotkeyVer < 5) {
                // Migration from v3-v4: only reset the 4 keys that changed (F1/F2/F3/Delete → letters)
                _state.KClear = Keys.Control|Keys.Shift|Keys.X;
                _state.KHelp  = Keys.Control|Keys.Shift|Keys.I;
                _state.KTray  = Keys.Control|Keys.Shift|Keys.Y;
                _state.KPass  = Keys.Control|Keys.Shift|Keys.P;
                // FB-a295: Also set new v6 keys
                _state.KShow  = Keys.Control|Keys.Shift|Keys.M;
                _state.KQuit  = Keys.Control|Keys.Shift|Keys.Q;
                _state.KUndo  = Keys.Z;
                _needHotkeyMigrationSave = true;
                // Load the rest from saved settings
                if (r.TryGetProperty("kPen", out var kp2) && Enum.TryParse<Keys>(kp2.GetString(), out var kpv2)) _state.KPen = kpv2;
                if (r.TryGetProperty("kArrow", out var ka2) && Enum.TryParse<Keys>(ka2.GetString(), out var kav2)) _state.KArrow = kav2;
                if (r.TryGetProperty("kRect", out var kr2) && Enum.TryParse<Keys>(kr2.GetString(), out var krv2)) _state.KRect = krv2;
                if (r.TryGetProperty("kEll", out var ke2) && Enum.TryParse<Keys>(ke2.GetString(), out var kev2)) _state.KEll = kev2;
                if (r.TryGetProperty("kText", out var kt2) && Enum.TryParse<Keys>(kt2.GetString(), out var ktv2)) _state.KText = ktv2;
                if (r.TryGetProperty("kHl", out var kh2) && Enum.TryParse<Keys>(kh2.GetString(), out var khv2)) _state.KHl = khv2;
                if (r.TryGetProperty("kCen", out var kc2) && Enum.TryParse<Keys>(kc2.GetString(), out var kcv2)) _state.KCen = kcv2;
                if (r.TryGetProperty("kStamp", out var ks2) && Enum.TryParse<Keys>(ks2.GetString(), out var ksv2)) _state.KStamp = ksv2;
                if (r.TryGetProperty("kNumCircle", out var knc2) && Enum.TryParse<Keys>(knc2.GetString(), out var kncv2)) _state.KNumCircle = kncv2;
            } else if (hotkeyVer < 6) {
                // FB-a295: Migration v5->v6: load existing keys, set defaults for new KShow/KQuit/KUndo
                if (r.TryGetProperty("kPen", out var kp5) && Enum.TryParse<Keys>(kp5.GetString(), out var kpv5)) _state.KPen = kpv5;
                if (r.TryGetProperty("kArrow", out var ka5) && Enum.TryParse<Keys>(ka5.GetString(), out var kav5)) _state.KArrow = kav5;
                if (r.TryGetProperty("kRect", out var kr5) && Enum.TryParse<Keys>(kr5.GetString(), out var krv5)) _state.KRect = krv5;
                if (r.TryGetProperty("kEll", out var ke5) && Enum.TryParse<Keys>(ke5.GetString(), out var kev5)) _state.KEll = kev5;
                if (r.TryGetProperty("kText", out var kt5) && Enum.TryParse<Keys>(kt5.GetString(), out var ktv5)) _state.KText = ktv5;
                if (r.TryGetProperty("kHl", out var kh5) && Enum.TryParse<Keys>(kh5.GetString(), out var khv5)) _state.KHl = khv5;
                if (r.TryGetProperty("kCen", out var kc5) && Enum.TryParse<Keys>(kc5.GetString(), out var kcv5)) _state.KCen = kcv5;
                if (r.TryGetProperty("kStamp", out var ks5) && Enum.TryParse<Keys>(ks5.GetString(), out var ksv5)) _state.KStamp = ksv5;
                if (r.TryGetProperty("kNumCircle", out var knc5) && Enum.TryParse<Keys>(knc5.GetString(), out var kncv5)) _state.KNumCircle = kncv5;
                if (r.TryGetProperty("kClear", out var kcl5) && Enum.TryParse<Keys>(kcl5.GetString(), out var kclv5)) _state.KClear = kclv5;
                if (r.TryGetProperty("kTray", out var ktr5) && Enum.TryParse<Keys>(ktr5.GetString(), out var ktrv5)) _state.KTray = ktrv5;
                if (r.TryGetProperty("kPass", out var kpa5) && Enum.TryParse<Keys>(kpa5.GetString(), out var kpav5)) _state.KPass = kpav5;
                if (r.TryGetProperty("kHelp", out var khe5) && Enum.TryParse<Keys>(khe5.GetString(), out var khev5)) _state.KHelp = khev5;
                // New keys get defaults (already set in DrawState field initializers)
                _state.KShow = Keys.Control|Keys.Shift|Keys.M;
                _state.KQuit = Keys.Control|Keys.Shift|Keys.Q;
                _state.KUndo = Keys.Z;
                _needHotkeyMigrationSave = true;
            } else {
                // FB-a295 v6: Load all hotkeys
                if (r.TryGetProperty("kPen", out var kp) && Enum.TryParse<Keys>(kp.GetString(), out var kpv)) _state.KPen = kpv;
                if (r.TryGetProperty("kArrow", out var ka) && Enum.TryParse<Keys>(ka.GetString(), out var kav)) _state.KArrow = kav;
                if (r.TryGetProperty("kRect", out var kr) && Enum.TryParse<Keys>(kr.GetString(), out var krv)) _state.KRect = krv;
                if (r.TryGetProperty("kEll", out var ke) && Enum.TryParse<Keys>(ke.GetString(), out var kev)) _state.KEll = kev;
                if (r.TryGetProperty("kText", out var kt) && Enum.TryParse<Keys>(kt.GetString(), out var ktv)) _state.KText = ktv;
                if (r.TryGetProperty("kHl", out var kh) && Enum.TryParse<Keys>(kh.GetString(), out var khv)) _state.KHl = khv;
                if (r.TryGetProperty("kCen", out var kc) && Enum.TryParse<Keys>(kc.GetString(), out var kcv)) _state.KCen = kcv;
                if (r.TryGetProperty("kStamp", out var ks) && Enum.TryParse<Keys>(ks.GetString(), out var ksv)) _state.KStamp = ksv;
                if (r.TryGetProperty("kNumCircle", out var knc) && Enum.TryParse<Keys>(knc.GetString(), out var kncv)) _state.KNumCircle = kncv;
                if (r.TryGetProperty("kClear", out var kcl) && Enum.TryParse<Keys>(kcl.GetString(), out var kclv)) _state.KClear = kclv;
                if (r.TryGetProperty("kTray", out var ktr) && Enum.TryParse<Keys>(ktr.GetString(), out var ktrv)) _state.KTray = ktrv;
                if (r.TryGetProperty("kPass", out var kpa) && Enum.TryParse<Keys>(kpa.GetString(), out var kpav)) _state.KPass = kpav;
                if (r.TryGetProperty("kHelp", out var khe) && Enum.TryParse<Keys>(khe.GetString(), out var khev)) _state.KHelp = khev;
                // FB-a295: Load new keys
                if (r.TryGetProperty("kUndo", out var kun) && Enum.TryParse<Keys>(kun.GetString(), out var kunv)) _state.KUndo = kunv;
                if (r.TryGetProperty("kShow", out var ksh) && Enum.TryParse<Keys>(ksh.GetString(), out var kshv)) _state.KShow = kshv;
                if (r.TryGetProperty("kQuit", out var kqu) && Enum.TryParse<Keys>(kqu.GetString(), out var kquv)) _state.KQuit = kquv;
            }
            if (r.TryGetProperty("numCircleFill", out var ncf)) _state.NumCircleFill = ncf.GetBoolean();
        } catch { /* corrupt file - use defaults */ }
    }

    void SaveSettings() {
        try {
            if (!Directory.Exists(_settingsDir)) Directory.CreateDirectory(_settingsDir);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var dict = new Dictionary<string, object> {
                ["width"] = _state.Width,
                ["color"] = _state.Color.ToArgb(),
                ["tool"] = _state.Tool.ToString(),
                ["highlightAlpha"] = _state.HighlightAlpha,
                ["textSize"] = _state.TextSize,
                ["penDash"] = _state.PenDash.ToString(),
                ["penCap"] = _state.PenCap.ToString(),
                ["arrowHead"] = _state.ArrowHeadStyle.ToString(),
                ["arrowDual"] = _state.ArrowDual,
                ["rectRadius"] = _state.RectRadius,
                ["rectFill"] = _state.RectFill,
                ["ellipseFill"] = _state.EllipseFill,
                ["censorMode"] = _state.CensorMode.ToString(),
                ["censorLevel"] = _state.CensorLevel.ToString(),
                // FB-a295 v6: Persist all hotkeys including new KShow, KQuit, KUndo
                ["hotkeyVersion"] = 6,
                ["kPen"] = _state.KPen.ToString(), ["kArrow"] = _state.KArrow.ToString(),
                ["kRect"] = _state.KRect.ToString(), ["kEll"] = _state.KEll.ToString(),
                ["kText"] = _state.KText.ToString(), ["kHl"] = _state.KHl.ToString(),
                ["kCen"] = _state.KCen.ToString(), ["kStamp"] = _state.KStamp.ToString(),
                ["kNumCircle"] = _state.KNumCircle.ToString(), ["numCircleFill"] = _state.NumCircleFill,
                ["kClear"] = _state.KClear.ToString(),
                ["kTray"] = _state.KTray.ToString(), ["kPass"] = _state.KPass.ToString(),
                ["kHelp"] = _state.KHelp.ToString(),
                ["kUndo"] = _state.KUndo.ToString(), ["kShow"] = _state.KShow.ToString(),
                ["kQuit"] = _state.KQuit.ToString(),
            };
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(dict, opts));
        } catch { /* best-effort save */ }
    }

    // MUP-234654 + MUP-234728 + MUP-2a27 + FB-871d: Highlight-Rahmen icon (brackets + stroke)
    static Bitmap _iconBmp; // MUP-2a27: Keep bitmap alive so GDI handle stays valid
    public static Icon MakeAppIcon() {
        // FB-871d: Render at 256x256 for crisp display at all DPI scales, then
        // create a multi-size icon. Windows will pick the best size automatically.
        int sz=256;
        _iconBmp = new Bitmap(sz,sz,PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(_iconBmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            // Rounded rectangle background (dark purple-blue)
            using (var path = new System.Drawing.Drawing2D.GraphicsPath()) {
                float r = 48; // FB-871d: scaled from 6 at 32px to 48 at 256px
                path.AddArc(4, 4, r*2, r*2, 180, 90);
                path.AddArc(sz-4-r*2, 4, r*2, r*2, 270, 90);
                path.AddArc(sz-4-r*2, sz-4-r*2, r*2, r*2, 0, 90);
                path.AddArc(4, sz-4-r*2, r*2, r*2, 90, 90);
                path.CloseFigure();
                using (var bg = new LinearGradientBrush(new Rectangle(0,0,sz,sz), Color.FromArgb(80,40,180), Color.FromArgb(50,25,120), LinearGradientMode.Vertical))
                    g.FillPath(bg, path);
            }
            // Bold white "M" - FB-871d: scaled font from 18 to 144
            using (Font f = new Font("Segoe UI", 144f, FontStyle.Bold))
            using (var br = new SolidBrush(Color.White)) {
                SizeF ms = g.MeasureString("M", f);
                g.DrawString("M", f, br, (sz - ms.Width) / 2, (sz - ms.Height) / 2 - 16);
            }
            // Rainbow underline stripe - FB-871d: scaled proportionally
            Rectangle stripe = new Rectangle(40, sz - 56, sz - 80, 24);
            Color[] rc = { Color.Red, Color.Orange, Color.Yellow, Color.Lime, Color.Cyan, Color.DodgerBlue, Color.BlueViolet };
            using (var lb = new LinearGradientBrush(stripe, Color.Red, Color.BlueViolet, LinearGradientMode.Horizontal)) {
                ColorBlend cb = new ColorBlend(7);
                for (int i = 0; i < 7; i++) { cb.Colors[i] = rc[i]; cb.Positions[i] = i / 6f; }
                lb.InterpolationColors = cb;
                // FB-871d: Use rounded rectangle for stripe too
                using (var stripePath = new System.Drawing.Drawing2D.GraphicsPath()) {
                    float sr = 12;
                    stripePath.AddArc(stripe.X, stripe.Y, sr*2, sr*2, 180, 90);
                    stripePath.AddArc(stripe.Right - sr*2, stripe.Y, sr*2, sr*2, 270, 90);
                    stripePath.AddArc(stripe.Right - sr*2, stripe.Bottom - sr*2, sr*2, sr*2, 0, 90);
                    stripePath.AddArc(stripe.X, stripe.Bottom - sr*2, sr*2, sr*2, 90, 90);
                    stripePath.CloseFigure();
                    g.FillPath(lb, stripePath);
                }
            }
        }
        // FB-871d v2: Save multi-resolution ICO file for EXE embedding
        try {
            string icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            SaveMultiResIcon(_iconBmp, icoPath);
        } catch { /* Non-critical: app.ico update is best-effort */ }
        return Icon.FromHandle(_iconBmp.GetHicon());
    }

    /// <summary>FB-871d v2: Write a multi-resolution ICO file (16+32+48+256 PNG-encoded).</summary>
    static void SaveMultiResIcon(Bitmap source, string path) {
        int[] sizes = { 16, 32, 48, 256 };
        var pngData = new List<byte[]>();
        foreach (int sz in sizes) {
            using var scaled = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(scaled)) {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.DrawImage(source, 0, 0, sz, sz);
            }
            using var ms = new MemoryStream();
            scaled.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngData.Add(ms.ToArray());
        }
        using var fs = new FileStream(path, FileMode.Create);
        using var bw = new BinaryWriter(fs);
        // ICO header: reserved(2) + type=1(2) + count(2)
        bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
        // Directory entries offset starts after header(6) + entries(16*count)
        int dataOffset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++) {
            byte w = (byte)(sizes[i] >= 256 ? 0 : sizes[i]); // 0 = 256
            byte h = w;
            bw.Write(w); bw.Write(h);
            bw.Write((byte)0); bw.Write((byte)0); // colors, reserved
            bw.Write((short)1); bw.Write((short)32); // planes, bpp
            bw.Write(pngData[i].Length); bw.Write(dataOffset);
            dataOffset += pngData[i].Length;
        }
        foreach (var png in pngData) bw.Write(png);
    }

    public void Run() {
        // MUP-171852: Show splash screen during initialization
        long splashStartTick = Environment.TickCount64; // MUP-73d3: Track start for minimum display time
        SplashScreen splash = new SplashScreen();
        splash.Show();
        splash.SetProgress(20, "Initialisiere...");

        // FB-1452: Load saved settings before building UI
        LoadSettings();
        // FB-3792 v3: Save immediately after migration to persist new Ctrl+Shift defaults
        if (_needHotkeyMigrationSave) { SaveSettings(); _needHotkeyMigrationSave = false; }

        // MUP-b45e v3: Single overlay spanning entire virtual desktop
        // MUP-409e: Use physical (unscaled) coordinates to match WH_MOUSE_LL hook
        Rectangle virt = Win32.GetPhysicalVirtualScreen();
        ScreenOverlay ov = new ScreenOverlay(virt, _state);
        _overlays.Add(ov);
        ov.Show();
        splash.SetProgress(60, "Toolbar aufbauen...");

        _bar = new FloatingBar(_state, _overlays, this);
        _bar.Show();

        // MKP-133745 v2: Install global mouse hook with async queue drain
        MouseHookManager.Install();
        _drainTimer = new Timer { Interval = 16 }; // MUP-c55f: 60 FPS drain (was 33ms/30fps)
        _drainTimer.Tick += (s, e) => DrainMouseQueue();
        _drainTimer.Start();
        _rainbowTimer = new Timer { Interval = 150 }; // MUP-c55f: 150ms (was 100ms) - reduced CPU load
        _rainbowTimer.Tick += (s, e) => {
            // MUP-c55f: Skip full redraw if no overlay has animated rainbow shapes
            bool hasAnimated = false;
            foreach (var ov in _overlays) { if (ov.HasAnimatedRainbowShapes()) { hasAnimated = true; break; } }
            if (!hasAnimated) return;
            Shape.RainbowFrame = (Shape.RainbowFrame + 1) % 7;
            foreach (var ov in _overlays) { ov.RedrawAnimated(); ov.MarkFullDirtyPublic(); ov.Render(); }
        };
        _state.Changed += () => { if (_state.IsRainbow && _state.IsAnimatedRainbow) _rainbowTimer.Start(); else _rainbowTimer.Stop(); };
        _state.Changed += () => SaveSettings(); // FB-1452: Persist settings on every change

        // Tray - FB-a615: Keyboard shortcuts + ampersand accelerators
        ContextMenuStrip cm = new ContextMenuStrip();
        cm.BackColor=Color.FromArgb(30,30,46); cm.ForeColor=Color.White;
        cm.Renderer=new ToolStripProfessionalRenderer(new DarkColorTable());
        // FB-a295: Use FormatKey() for shortcut display (ShowShortcutKeys uses Keys.ToString() which shows "Oem7" instead of "Ä")
        var miShow = new ToolStripMenuItem("&Anzeigen\t" + DrawState.FormatKey(_state.KShow), null, (s,e) => Restore()) { ShowShortcutKeys=false };
        var miClear = new ToolStripMenuItem("Clear &all\t" + DrawState.FormatKey(_state.KClear), null, (s,e) => ClearAll()) { ShowShortcutKeys=false };
        var miQuit = new ToolStripMenuItem("&Quit\t" + DrawState.FormatKey(_state.KQuit), null, (s,e) => Application.Exit()) { ShowShortcutKeys=false };
        miShow.ForeColor=Color.White; miShow.BackColor=Color.FromArgb(30,30,46);
        miClear.ForeColor=Color.White; miClear.BackColor=Color.FromArgb(30,30,46);
        miQuit.ForeColor=Color.White; miQuit.BackColor=Color.FromArgb(30,30,46);
        cm.Items.Add(miShow); cm.Items.Add(miClear); cm.Items.Add(new ToolStripSeparator()); cm.Items.Add(miQuit);
        _tray = new NotifyIcon { Text="MarkUp", Icon=MakeAppIcon(), Visible=false, ContextMenuStrip=cm };
        _tray.DoubleClick += (s,e) => Restore();

        splash.SetProgress(100, "Bereit!");
        // MUP-73d3: Minimum 1.5s display time so rainbow animation is visible
        long splashElapsed = Environment.TickCount64 - splashStartTick;
        long remaining = 2000 - splashElapsed; // MUP-73d3: 2s minimum splash display
        if (remaining > 0) {
            var splashSw = System.Diagnostics.Stopwatch.StartNew();
            while (splashSw.ElapsedMilliseconds < remaining) {
                Application.DoEvents();
                System.Threading.Thread.Sleep(16);
            }
        }
        splash.Close();
        splash.Dispose();

        Application.ApplicationExit += (s,e) => { _drainTimer.Stop(); _rainbowTimer.Stop(); MouseHookManager.Uninstall(); _tray.Visible=false; _tray.Dispose(); };
        Application.Run(_bar);
    }

    // MKP-133745 v2: Drain async mouse event queue on UI thread
    // MUP-130727: Batch renders - silent moves + single Render() at end
    void DrainMouseQueue() {
        bool needsRender = false;
        while (MouseHookManager.TryDequeue(out var evt)) {
            switch (evt.msg) {
                case Win32.WM_LBUTTONDOWN: OnGlobalMouseDown(evt.pt); needsRender = true; break;
                case Win32.WM_MOUSEMOVE:   OnGlobalMouseMoveSilent(evt.pt); needsRender = true; break;
                case Win32.WM_LBUTTONUP:   OnGlobalMouseUp(evt.pt); needsRender = true; break;
                // MUP-234951: Right-click shows tool options on overlay
                case Win32.WM_RBUTTONDOWN: OnGlobalRightClick(evt.pt); break;
            }
        }
        if (needsRender && _activeOverlay != null) _activeOverlay.Render();
    }

    // MUP-b45e v3: Single VirtualScreen overlay - just check toolbar bounds
    ScreenOverlay FindOverlay(Point screenPt) {
        // Skip if click is on the toolbar
        if (_bar != null && _bar.Bounds.Contains(screenPt)) return null;
        // Single overlay covers entire virtual desktop
        if (_overlays.Count > 0 && _overlays[0].Visible) return _overlays[0];
        return null;
    }

    ScreenOverlay _activeOverlay;

    void OnGlobalMouseDown(Point screenPt) {
        if (_hidden || _state.PassThru) return;
        _activeOverlay = FindOverlay(screenPt);
        if (_activeOverlay != null) {
            // MUP-409e: Use physical origin for coordinate translation (WH_MOUSE_LL gives physical coords)
            Point local = new Point(screenPt.X - _activeOverlay._physLeft, screenPt.Y - _activeOverlay._physTop);
            _activeOverlay.OnHookDown(local);
        }
    }

    void OnGlobalMouseMove(Point screenPt) {
        if (_activeOverlay != null) {
            // MUP-409e: Use physical origin for coordinate translation
            Point local = new Point(screenPt.X - _activeOverlay._physLeft, screenPt.Y - _activeOverlay._physTop);
            _activeOverlay.OnHookMove(local);
        }
    }

    // MUP-130727: Silent variant - updates shape coords without triggering Render()
    void OnGlobalMouseMoveSilent(Point screenPt) {
        if (_activeOverlay != null) {
            // MUP-409e: Use physical origin for coordinate translation
            Point local = new Point(screenPt.X - _activeOverlay._physLeft, screenPt.Y - _activeOverlay._physTop);
            _activeOverlay.OnHookMoveSilent(local);
        }
    }

    void OnGlobalMouseUp(Point screenPt) {
        if (_activeOverlay != null) {
            // MUP-409e: Use physical origin for coordinate translation
            Point local = new Point(screenPt.X - _activeOverlay._physLeft, screenPt.Y - _activeOverlay._physTop);
            _activeOverlay.OnHookUp(local);
            _activeOverlay = null;
        }
    }

    // MUP-234951: Right-click on overlay shows tool-specific context menu
    void OnGlobalRightClick(Point screenPt) {
        if (_hidden || _state.PassThru) return;
        ScreenOverlay ov = FindOverlay(screenPt);
        if (ov != null && _bar != null) {
            _bar.ShowToolOptionsAtScreen(screenPt);
        }
    }

    public void SendToTray() {
        _hidden = true;
        _tray.Visible = true;
        _bar.Hide();
        foreach (ScreenOverlay ov in _overlays) ov.Hide();
        MouseHookManager.SwallowClicks = false;
        MouseHookManager.Active = false;
    }
    public void Restore() {
        _hidden = false;
        _tray.Visible = false;
        _bar.Show();
        foreach (ScreenOverlay ov in _overlays) { ov.Show(); ov.TopMost=true; }
        _bar.TopMost = true;
        _bar.Focus();
        if (!_state.PassThru) {
            MouseHookManager.SwallowClicks = true;
            MouseHookManager.Active = true;
        }
    }
    public void ClearAll() {
        foreach (ScreenOverlay ov in _overlays) ov.ClearAll();
    }
    public void UndoAll() {
        // Undo on whichever overlay was last drawn on
        foreach (ScreenOverlay ov in _overlays) { if (ov.HasShapes) { ov.Undo(); break; } }
    }

    // MUP-a295: Rebuild tray context menu to reflect updated shortcut keys
    public void RebuildTrayMenu() {
        if (_tray == null) return;
        ContextMenuStrip cm = new ContextMenuStrip();
        cm.BackColor=Color.FromArgb(30,30,46); cm.ForeColor=Color.White;
        cm.Renderer=new ToolStripProfessionalRenderer(new DarkColorTable());
        var miShow = new ToolStripMenuItem("&Anzeigen\t" + DrawState.FormatKey(_state.KShow), null, (s,e) => Restore()) { ShowShortcutKeys=false };
        var miClear = new ToolStripMenuItem("Clear &all\t" + DrawState.FormatKey(_state.KClear), null, (s,e) => ClearAll()) { ShowShortcutKeys=false };
        var miQuit = new ToolStripMenuItem("&Quit\t" + DrawState.FormatKey(_state.KQuit), null, (s,e) => Application.Exit()) { ShowShortcutKeys=false };
        miShow.ForeColor=Color.White; miShow.BackColor=Color.FromArgb(30,30,46);
        miClear.ForeColor=Color.White; miClear.BackColor=Color.FromArgb(30,30,46);
        miQuit.ForeColor=Color.White; miQuit.BackColor=Color.FromArgb(30,30,46);
        cm.Items.Add(miShow); cm.Items.Add(miClear); cm.Items.Add(new ToolStripSeparator()); cm.Items.Add(miQuit);
        _tray.ContextMenuStrip = cm;
    }

    // MUP-13df: Create .lnk shortcut via COM IShellLink
    public void CreateShortcut(string lnkPath) {
        try {
            string exePath = Application.ExecutablePath;
            IShellLink link = (IShellLink)new ShellLink();
            link.SetPath(exePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(exePath));
            link.SetDescription("MarkUp - Screen Annotation Tool");
            link.SetIconLocation(exePath, 0);
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(lnkPath, false);
            Marshal.ReleaseComObject(link);
            MessageBox.Show("Shortcut created:\n" + lnkPath, "MarkUp", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) {
            MessageBox.Show("Error: " + ex.Message, "MarkUp", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void CreateDesktopShortcut() {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        CreateShortcut(Path.Combine(desktop, "MarkUp.lnk"));
    }

    public void CreateStartMenuShortcut() {
        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        CreateShortcut(Path.Combine(startMenu, "MarkUp.lnk"));
    }

    public void ToggleAutostart() {
        try {
            string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string lnkPath = Path.Combine(startupDir, "MarkUp.lnk");
            if (File.Exists(lnkPath)) {
                File.Delete(lnkPath);
                MessageBox.Show("Removed from startup.", "MarkUp", MessageBoxButtons.OK, MessageBoxIcon.Information);
            } else {
                CreateShortcut(lnkPath);
            }
        } catch (Exception ex) {
            MessageBox.Show("Error: " + ex.Message, "MarkUp", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public bool IsAutostart() {
        string lnkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MarkUp.lnk");
        return File.Exists(lnkPath);
    }
}

// ── Per-Screen Overlay ────────────────────────────────────────────────────────
class ScreenOverlay : Form {
    DrawState    _st;
    Screen       _screen;

    List<Shape>  _shapes    = new List<Shape>();
    Bitmap       _committed;
    Bitmap       _display;
    // MUP-c55f v3: Static layer caches non-animated shapes to avoid redrawing ALL shapes every rainbow tick
    Bitmap       _staticLayer;
    bool         _staticDirty = true;

    bool   _down; Point _start; Shape _live;
    Point  _prevHookPt; // MUP-b72a: Previous mouse point for dirty-segment tracking
    bool   _txOn; Point _txPos; string _txBuf = "";

    // MUP-c55f: Performance - dirty-region tracking + idle skip + frame cap
    Rectangle _dirtyRect = Rectangle.Empty;
    bool _needsRender = false;
    readonly System.Diagnostics.Stopwatch _frameSw = System.Diagnostics.Stopwatch.StartNew();
    const int MIN_FRAME_MS = 16; // 60 FPS cap

    // MUP-409e: Physical (unscaled) origin for DPI-correct coordinate translation
    internal int _physLeft, _physTop, _physWidth, _physHeight;

    public bool HasShapes { get { return _shapes.Count > 0; } }
    // MUP-b45e: Expose screen reference for multi-monitor matching
    public Screen TargetScreen { get { return _screen; } }

    // Cut a hole in this overlay where the toolbar sits so it stays clickable
    public void UpdateBarHole(Rectangle barScreenRect) {
        if (!IsHandleCreated) return;
        // MUP-409e: Use physical dimensions for region (overlay positioned at physical coords)
        IntPtr full = Win32.CreateRectRgn(0, 0, _physWidth > 0 ? _physWidth : Width, _physHeight > 0 ? _physHeight : Height);
        // Translate bar rect to overlay-local coords using physical origin
        Rectangle local = new Rectangle(
            barScreenRect.X - _physLeft, barScreenRect.Y - _physTop,
            barScreenRect.Width, barScreenRect.Height);
        // Expand hole slightly for padding
        local.Inflate(4, 4);
        if (local.Width > 0 && local.Height > 0 &&
            local.IntersectsWith(new Rectangle(0, 0, _physWidth > 0 ? _physWidth : Width, _physHeight > 0 ? _physHeight : Height))) {
            IntPtr hole = Win32.CreateRectRgn(local.Left, local.Top, local.Right, local.Bottom);
            Win32.CombineRgn(full, full, hole, Win32.RGN_DIFF);
            Win32.DeleteObject(hole);
        }
        Win32.SetWindowRgn(Handle, full, true);
        // Note: SetWindowRgn takes ownership of region, do NOT DeleteObject(full)
    }

    // MUP-b45e v3: Rectangle overload for VirtualScreen spanning all monitors
    // MUP-409e: bounds are physical (unscaled) pixel coordinates
    public ScreenOverlay(Rectangle bounds, DrawState st) {
        _screen = Screen.PrimaryScreen; _st = st;
        // MUP-409e: Store physical coordinates for coordinate translation
        _physLeft = bounds.X; _physTop = bounds.Y;
        _physWidth = bounds.Width; _physHeight = bounds.Height;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true; ShowInTaskbar = false;
        SetStyle(ControlStyles.Opaque, true);
        // MUP-409e: Set initial WinForms Location/Size - will be corrected by SetWindowPos in Shown handler
        Location = bounds.Location;
        Size     = bounds.Size;
        InitBitmaps();

        KeyPress  += OnKeyChar;
        Shown     += (s,e) => {
            // MUP-409e: Force overlay to physical pixel position via Win32, bypassing DPI virtualization
            Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, _physLeft, _physTop, _physWidth, _physHeight,
                Win32.SWP_NOACTIVATE);
            MarkFullDirty();
            BeginInvoke((Action)Render);
        };

        _st.Changed += () => {
            if (!IsHandleCreated || !Visible) return;
            MarkFullDirty(); // MUP-c55f: State change requires full redraw
            Render();
        };
    }

    public ScreenOverlay(Screen sc, DrawState st) {
        _screen = sc; _st = st;
        // MUP-409e: Initialize physical coords from Screen bounds (fallback for non-v3 path)
        _physLeft = sc.Bounds.X; _physTop = sc.Bounds.Y;
        _physWidth = sc.Bounds.Width; _physHeight = sc.Bounds.Height;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true; ShowInTaskbar = false;
        SetStyle(ControlStyles.Opaque, true);
        Location = sc.Bounds.Location;
        Size     = sc.Bounds.Size;
        InitBitmaps();

        // MKP-133745: Mouse events now handled via global WH_MOUSE_LL hook
        // (OnHookDown/Move/Up called from MarkUpApp), not WinForms events.
        // WinForms MouseDown/Move/Up don't fire reliably when FloatingBar has focus.
        KeyPress  += OnKeyChar;
        Shown     += (s,e) => { MarkFullDirty(); BeginInvoke((Action)Render); }; // MUP-7ffd: deferred render

        _st.Changed += () => {
            if (!IsHandleCreated || !Visible) return;
            // MUP-130727: WS_EX_TRANSPARENT stays always on (set in CreateParams).
            // Click-through is controlled by MouseHookManager.SwallowClicks instead.
            MarkFullDirty(); // MUP-c55f: State change requires full redraw
            Render();
        };
    }

    protected override CreateParams CreateParams {
        get { CreateParams cp=base.CreateParams; cp.ExStyle|=Win32.WS_EX_LAYERED|Win32.WS_EX_TRANSPARENT; return cp; }
    }
    protected override void OnPaint(PaintEventArgs e) {}
    protected override void OnPaintBackground(PaintEventArgs e) {}

    void InitBitmaps() {
        if (_committed!=null) _committed.Dispose();
        if (_display  !=null) _display.Dispose();
        if (_staticLayer!=null) _staticLayer.Dispose();
        // MUP-409e: Use physical dimensions for bitmaps to match physical overlay size
        int w = _physWidth > 0 ? _physWidth : Math.Max(1, Width);
        int h = _physHeight > 0 ? _physHeight : Math.Max(1, Height);
        _committed   = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        _display     = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        _staticLayer = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        _staticDirty = true;
    }

    // MUP-c55f: Mark a region around a point as dirty (needs re-render)
    void MarkDirty(Point center, int radius = 100) {
        int bw = _physWidth > 0 ? _physWidth : Math.Max(1, Width);
        int bh = _physHeight > 0 ? _physHeight : Math.Max(1, Height);
        Rectangle r = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        // Clip to overlay bounds
        r.Intersect(new Rectangle(0, 0, bw, bh));
        if (r.Width <= 0 || r.Height <= 0) return;
        _dirtyRect = _dirtyRect.IsEmpty ? r : Rectangle.Union(_dirtyRect, r);
        _needsRender = true;
    }

    // MUP-b72a: Mark the bounding box of a line segment as dirty.
    // Fixes gaps when mouse moves fast (>200px between frames).
    void MarkDirtySegment(Point a, Point b, int pad = 100) {
        int bw = _physWidth > 0 ? _physWidth : Math.Max(1, Width);
        int bh = _physHeight > 0 ? _physHeight : Math.Max(1, Height);
        int x1 = Math.Min(a.X, b.X) - pad;
        int y1 = Math.Min(a.Y, b.Y) - pad;
        int x2 = Math.Max(a.X, b.X) + pad;
        int y2 = Math.Max(a.Y, b.Y) + pad;
        Rectangle r = Rectangle.FromLTRB(x1, y1, x2, y2);
        r.Intersect(new Rectangle(0, 0, bw, bh));
        if (r.Width <= 0 || r.Height <= 0) return;
        _dirtyRect = _dirtyRect.IsEmpty ? r : Rectangle.Union(_dirtyRect, r);
        _needsRender = true;
    }

    // MUP-c55f: Public accessor for external callers (e.g. rainbow timer in MarkUpApp)
    public void MarkFullDirtyPublic() { MarkFullDirty(); }

    // MUP-c55f: Mark entire overlay as dirty (for full redraws like clear/undo/help)
    void MarkFullDirty() {
        int bw = _physWidth > 0 ? _physWidth : Math.Max(1, Width);
        int bh = _physHeight > 0 ? _physHeight : Math.Max(1, Height);
        _dirtyRect = new Rectangle(0, 0, bw, bh);
        _needsRender = true;
    }

    // FB-3684: Force immediate render on clear - bypass Render() frame rate cap
    // so animated rainbow shapes don't linger on screen after first press.
    public void ClearAll() {
        _shapes.Clear(); _txOn=false; _txBuf="";
        RedrawCommitted();
        if (_display != null) {
            using (Graphics g = Graphics.FromImage(_display)) {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_committed != null) g.DrawImage(_committed, 0, 0);
            }
            Push();
            _dirtyRect = Rectangle.Empty;
            _needsRender = false;
            _frameSw.Restart();
        }
    }
    public void Undo()     { if (_shapes.Count>0) { _shapes.RemoveAt(_shapes.Count-1); RedrawCommitted(); MarkFullDirty(); Render(); } }

    // MUP-c55f v3: Helper - is this shape an animated rainbow?
    static bool IsAnimatedRainbow(Shape sh) {
        return (sh is FreehandShape fs && fs.AnimatedRainbow) ||
               (sh is ArrowShape ar && ar.AnimatedRainbow) ||
               (sh is RectShape rs && rs.AnimatedRainbow) ||
               (sh is EllipseShape es && es.AnimatedRainbow) ||
               (sh is CensorShape cs && cs.AnimatedRainbow) ||
               (sh is TextShape ts && ts.AnimatedRainbow) || // MUP-e0d2
               (sh is NumberedCircleShape nc && nc.AnimatedRainbow); // MUP-20260328-170558899-8e36: animated rainbow for numbered circles
    }

    // MUP-c55f v3: Rebuild static layer (non-animated shapes only)
    void RebuildStaticLayer() {
        if (_staticLayer == null) return;
        using (Graphics g = Graphics.FromImage(_staticLayer)) {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Shape sh in _shapes)
                if (!IsAnimatedRainbow(sh)) sh.Draw(g);
        }
        _staticDirty = false;
    }

    // MUP-66e2 v2: Region-clipped rainbow tick - only redraw animated shape bounds (not full screen)
    public void RedrawAnimated() {
        if (_committed == null) return;
        if (_staticDirty) RebuildStaticLayer();
        // Compute union bounds of all animated shapes
        Rectangle clip = Rectangle.Empty;
        foreach (Shape sh in _shapes)
            if (IsAnimatedRainbow(sh)) { Rectangle b=sh.GetBounds(); clip=clip.IsEmpty?b:Rectangle.Union(clip,b); }
        if (clip.IsEmpty) return;
        clip.Inflate(10, 10); // pad for shadow/AA
        int bw=_committed.Width, bh=_committed.Height;
        clip.Intersect(new Rectangle(0,0,bw,bh));
        if (clip.Width<=0||clip.Height<=0) return;
        using (Graphics g = Graphics.FromImage(_committed)) {
            g.SetClip(clip);
            g.Clear(Color.Transparent);
            if (_staticLayer != null) g.DrawImage(_staticLayer, clip, clip, GraphicsUnit.Pixel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Shape sh in _shapes)
                if (IsAnimatedRainbow(sh)) sh.Draw(g);
            g.ResetClip();
        }
    }

    // MUP-c55f: Check if any committed shape uses animated rainbow (avoids full redraw when none exist)
    public bool HasAnimatedRainbowShapes() {
        foreach (var s in _shapes) if (IsAnimatedRainbow(s)) return true;
        if (_live != null && IsAnimatedRainbow(_live)) return true;
        return false;
    }

    // Full redraw of all shapes (called on shape add/remove/undo)
    void RedrawCommitted() {
        if (_committed == null) return;
        using (Graphics g = Graphics.FromImage(_committed)) {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Shape sh in _shapes) sh.Draw(g);
        }
        _staticDirty = true; // Static layer needs rebuild on next animation tick
    }

    public void Render() {
        if (_display==null) return;
        // MUP-c55f: Skip render if nothing changed
        if (!_needsRender) return;
        // MUP-c55f: Frame rate cap - skip if too soon since last frame
        if (_frameSw.ElapsedMilliseconds < MIN_FRAME_MS) return;
        using (Graphics g=Graphics.FromImage(_display)) {
            // MUP-b72a: Always clear full bitmap - SetClip caused stale artifacts when shapes overlap
            g.Clear(Color.Transparent);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            if (_committed!=null) g.DrawImage(_committed,0,0);
            if (_live!=null) _live.Draw(g);
            if (_txOn) DrawTextCursor(g);
            if (_st.ShowHelp) DrawHelp(g);
        }
        Push();
        // MUP-c55f: Reset dirty state after successful push
        _dirtyRect = Rectangle.Empty;
        _needsRender = false;
        _frameSw.Restart();
    }

    void DrawTextCursor(Graphics g) {
        using (Font f=new Font("Segoe UI",_st.TextSize,FontStyle.Bold))
        using (SolidBrush sh=new SolidBrush(Color.FromArgb(160,0,0,0)))
        using (SolidBrush br=new SolidBrush(_st.Color)) {
            string d=_txBuf+"|";
            g.DrawString(d,f,sh,_txPos.X+2,_txPos.Y+2);
            g.DrawString(d,f,br,_txPos.X,  _txPos.Y);
        }
    }

    // MUP-3205 v2: Cached GDI handles - avoids 8MB+ alloc/free per frame
    IntPtr _cachedDc = IntPtr.Zero;
    IntPtr _cachedBmp = IntPtr.Zero;
    IntPtr _cachedOld = IntPtr.Zero;

    void FreeGdiCache() {
        if (_cachedDc != IntPtr.Zero) {
            if (_cachedBmp != IntPtr.Zero) { Win32.SelectObject(_cachedDc, _cachedOld); Win32.DeleteObject(_cachedBmp); _cachedBmp = IntPtr.Zero; }
            Win32.DeleteDC(_cachedDc);
            _cachedDc = IntPtr.Zero;
        }
    }

    // MUP-3205 v3: Cleaned up Push() - single HBITMAP lifecycle, no redundant deselect/delete
    void Push() {
        if (_display==null||!IsHandleCreated) return;
        // Ensure we have a compatible DC (reused across frames)
        if (_cachedDc == IntPtr.Zero) {
            IntPtr tmpDc = Win32.GetDC(IntPtr.Zero);
            _cachedDc = Win32.CreateCompatibleDC(tmpDc);
            Win32.ReleaseDC(IntPtr.Zero, tmpDc);
        }
        // Replace HBITMAP with fresh snapshot of _display
        if (_cachedBmp != IntPtr.Zero) {
            Win32.SelectObject(_cachedDc, _cachedOld);
            Win32.DeleteObject(_cachedBmp);
        }
        _cachedBmp = _display.GetHbitmap(Color.FromArgb(0));
        _cachedOld = Win32.SelectObject(_cachedDc, _cachedBmp);
        // Push to DWM
        IntPtr screenDc=Win32.GetDC(IntPtr.Zero);
        Win32.SIZE  sz  = new Win32.SIZE  { cx=_display.Width, cy=_display.Height };
        Win32.POINT src = new Win32.POINT { x=0, y=0 };
        // MUP-409e: Use physical origin for UpdateLayeredWindow (DWM uses physical coords)
        Win32.POINT dst = new Win32.POINT { x=_physLeft, y=_physTop };
        Win32.BLEND bf  = new Win32.BLEND { Op=Win32.AC_SRC_OVER, Flags=0, Alpha=255, Format=Win32.AC_SRC_ALPHA };
        Win32.UpdateLayeredWindow(Handle,screenDc,ref dst,ref sz,_cachedDc,ref src,0,ref bf,Win32.ULW_ALPHA);
        Win32.ReleaseDC(IntPtr.Zero,screenDc);
    }

    // ── Mouse (via WH_MOUSE_LL hook) ────────────────────────────────────────
    // MKP-133745: Public methods called from MarkUpApp via global mouse hook.
    // These receive overlay-local coordinates (already translated from screen coords).
    public void OnHookDown(Point loc) {
        // MUP-20260912-102455567-e140: EVERY click used to close the help
        // box -- you could not touch it without dismissing it.
        //
        // 🔴 This branch ends in `return` in EVERY case: nothing is ever
        // drawn underneath the open help box. Without that, a click inside
        // the box painted a line.
        if (_st.ShowHelp) {
            Rectangle hr = HelpRect();
            if (HelpCloseRect(hr).Contains(loc)) {
                _st.ShowHelp=false; _helpDrag=false; MarkFullDirty(); _st.Fire();
            } else if (HelpTitleRect(hr).Contains(loc)) {
                _helpDrag=true;
                _helpGrab=new Size(loc.X-hr.X, loc.Y-hr.Y);
            } else if (!hr.Contains(loc)) {
                // A click beside it still closes -- the familiar gesture.
                _st.ShowHelp=false; MarkFullDirty(); _st.Fire();
            }
            return;
        }
        if (_st.PassThru) return;
        if (_st.Tool==DrawTool.Text) {
            if (_txOn&&_txBuf.Length>0) CommitText();
            _txOn=true; _txPos=loc; _txBuf=""; MarkDirty(loc, 200); Render(); return;
        }
        // Stamp: single click placement, no drag
        if (_st.Tool==DrawTool.Stamp) {
            int stampRadius = (int)(_st.Width * 8f);
            if (_st.IsImageStamp) {
                Bitmap img = StampManager.Get(_st.StampImageKey);
                if (img != null) {
                    // MUP-234715 v4: Scale image stamps to same size as emoji stamps (Width*8f)
                    int targetSize = (int)(_st.Width * 8f);
                    int maxDim = Math.Max(img.Width, img.Height);
                    float scale = (float)targetSize / maxDim;
                    _shapes.Add(new ImageStampShape{Color=_st.Color,Width=_st.Width,Loc=loc,StampImage=img,Scale=scale});
                }
            } else {
                // MUP-234951: Pre-render color emoji via RichTextBox for native COLR/SVG colors
                int emojiSize = (int)(_st.Width * 8f);
                Bitmap emojiBmp = StampManager.RenderColorEmoji(_st.StampEmoji, emojiSize);
                if (emojiBmp != null) {
                    _shapes.Add(new ImageStampShape{Color=_st.Color,Width=_st.Width,Loc=loc,StampImage=emojiBmp,Scale=1.0f});
                } else {
                    // Fallback to monochrome if rendering fails
                    float sz = _st.Width * 5f;
                    _shapes.Add(new StampShape{Color=_st.Color,Width=_st.Width,Loc=loc,Emoji=_st.StampEmoji,Size=sz});
                }
            }
            MarkDirty(loc, stampRadius); RedrawCommitted(); Render(); return;
        }
        _down=true; _start=loc;
        _prevHookPt=loc; // MUP-b72a: Init prev point for segment tracking
        MarkDirty(loc);
        switch(_st.Tool) {
            case DrawTool.Pen:       FreehandShape fs=new FreehandShape{Color=_st.Color,Width=_st.Width,Dash=_st.PenDash,Cap=_st.PenCap,Rainbow=_st.IsRainbow,AnimatedRainbow=_st.IsAnimatedRainbow};fs.Points.Add(loc);_live=fs;break;
            case DrawTool.Arrow:     _live=new ArrowShape  {Color=_st.Color,Width=_st.Width,A=loc,B=loc,Head=_st.ArrowHeadStyle,Dual=_st.ArrowDual,Rainbow=_st.IsRainbow,AnimatedRainbow=_st.IsAnimatedRainbow};break;
            case DrawTool.Rect:      _live=new RectShape   {Color=_st.Color,Width=_st.Width,Rect=new Rectangle(loc,Size.Empty),Fill=_st.RectFill,CornerRadius=_st.RectRadius,Rainbow=_st.IsRainbow,AnimatedRainbow=_st.IsAnimatedRainbow};break;
            case DrawTool.Ellipse:   _live=new EllipseShape{Color=_st.Color,Width=_st.Width,Rect=new Rectangle(loc,Size.Empty),Rainbow=_st.IsRainbow,AnimatedRainbow=_st.IsAnimatedRainbow,Fill=_st.EllipseFill};break;
            case DrawTool.Highlight: _live=new RectShape   {Color=Color.FromArgb((int)_st.HighlightAlpha,_st.Color),Width=_st.Width,Rect=new Rectangle(loc,Size.Empty),Fill=true,Rainbow=_st.IsRainbow,AnimatedRainbow=_st.IsAnimatedRainbow};break;
            case DrawTool.Censor:    _live=new CensorShape {Color=_st.Color,Width=_st.Width,Rect=new Rectangle(loc,Size.Empty),Rainbow=_st.IsRainbow,AnimatedRainbow=_st.IsAnimatedRainbow,Method=_st.CensorMode,Intensity=_st.CensorLevel};break;
            case DrawTool.NumberedCircle: { // MUP-20260328-170558899-8e36: click-to-place, no drag
                int r=(int)(_st.Width*5f); if(r<6)r=6;
                var ncs=new NumberedCircleShape{Color=_st.Color,Width=_st.Width,Rect=new Rectangle(loc.X-r,loc.Y-r,r*2,r*2),Number=_st.NextNumCircle++,Fill=_st.NumCircleFill,Rainbow=_st.IsRainbow,AnimatedRainbow=_st.IsAnimatedRainbow};
                _shapes.Add(ncs); MarkDirty(loc,r+20); RedrawCommitted(); Render(); _down=false; return;
            }
        }
    }
    // MUP-20260912-102455567-e140: drag state.
    bool _helpDrag = false;
    Size _helpGrab = Size.Empty;

    public void OnHookMove(Point loc) {
        if (_helpDrag) {
            // 🔴 MarkFullDirty, not MarkDirty: `MarkDirty` works with
            // regions, and a moving box would leave smears behind.
            _st.HelpPos = HelpClamp(new Point(loc.X-_helpGrab.Width,
                                              loc.Y-_helpGrab.Height));
            MarkFullDirty(); Render(); return;
        }
        if (!_down||_live==null) return;
        // MUP-b72a: Mark segment between prev and current point to avoid gaps
        int pad = Math.Max(100, (int)(_st.Width * 3));
        MarkDirtySegment(_prevHookPt, loc, pad);
        _prevHookPt = loc;
        UpdateLiveShape(loc);
        Render();
    }
    // MUP-130727: Silent variant - updates shape coords without Render() for batch drain
    // MUP-c55f: Still marks dirty so next Render() knows what changed
    public void OnHookMoveSilent(Point loc) {
        if (!_down||_live==null) return;
        int pad = Math.Max(100, (int)(_st.Width * 3));
        MarkDirtySegment(_prevHookPt, loc, pad);
        _prevHookPt = loc;
        UpdateLiveShape(loc);
    }
    void UpdateLiveShape(Point loc) {
        switch(_st.Tool) {
            case DrawTool.Pen:                           ((FreehandShape)_live).Points.Add(loc);break;
            case DrawTool.Arrow:                         ((ArrowShape)_live).B=loc;break;
            case DrawTool.Rect:case DrawTool.Highlight:  ((RectShape)_live).Rect=MR(_start,loc);break;
            case DrawTool.Ellipse:                       ((EllipseShape)_live).Rect=MR(_start,loc);break;
            case DrawTool.Censor:                        ((CensorShape)_live).Rect=MR(_start,loc);break;
            // MUP-20260328-170558899-8e36: NumberedCircle is click-to-place, no live update needed
        }
    }
    public void OnHookUp(Point loc) {
        if (_helpDrag) {
            _helpDrag=false;
            // Fire() so the other overlays pick up the new position.
            _st.Fire();
            return;
        }
        if (!_down) return; _down=false;
        if (_live!=null) {
            // MUP-0aa8: Capture + pixelate screen region for censor pixelation
            if (_live is CensorShape cs && cs.Method == CensorMethod.Pixelate && cs.Rect.Width > 2 && cs.Rect.Height > 2) {
                try {
                    Rectangle screenRect = new Rectangle(cs.Rect.X + _physLeft, cs.Rect.Y + _physTop, cs.Rect.Width, cs.Rect.Height);
                    int blockSz = cs.Intensity == CensorIntensity.Light ? 14 : cs.Intensity == CensorIntensity.Heavy ? 6 : 10;
                    int sw = Math.Max(1, cs.Rect.Width / blockSz);
                    int sh = Math.Max(1, cs.Rect.Height / blockSz);
                    using (Bitmap capture = new Bitmap(cs.Rect.Width, cs.Rect.Height, PixelFormat.Format32bppArgb)) {
                        using (Graphics cg = Graphics.FromImage(capture))
                            cg.CopyFromScreen(screenRect.Location, Point.Empty, screenRect.Size);
                        // Downscale to block grid (pixel averaging)
                        Bitmap small = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
                        using (Graphics sg = Graphics.FromImage(small)) {
                            sg.InterpolationMode = InterpolationMode.Bilinear;
                            sg.DrawImage(capture, 0, 0, sw, sh);
                        }
                        cs.PixelCache = small;
                    }
                } catch { /* fallback to procedural pattern */ }
            }
            MarkDirty(loc); _shapes.Add(_live); _live=null; RedrawCommitted(); Render();
        }
    }
    static Rectangle MR(Point a,Point b) { return Rectangle.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)); }

    void OnKeyChar(object s, KeyPressEventArgs e) {
        if (_txOn&&!char.IsControl(e.KeyChar)) { _txBuf+=e.KeyChar; MarkDirty(_txPos, 400); Render(); }
    }
    void CommitText() {
        _shapes.Add(new TextShape{Color=_st.Color,Width=_st.Width,Loc=_txPos,Text=_txBuf,FontSize=_st.TextSize,Rainbow=_st.IsRainbow,AnimatedRainbow=_st.IsAnimatedRainbow});
        _txBuf=""; _txOn=false; MarkDirty(_txPos, 400); RedrawCommitted(); Render();
    }

    // ── Help ──────────────────────────────────────────────────────────────────
    // MUP-20260912-102455567-e140: ONE place computes the position.
    // Drawing and hit-testing must use the same one, otherwise they drift
    // apart and the close cross sits somewhere other than where it looks.
    //
    // MUP-20260912-102451348-e535: HilfeH 420 -> 448. The close hint sits at
    // ph-46 and the footer at ph-24; there was no room next to it.
    const int HilfeB = 500, HilfeH = 448, GriffH = 44, KreuzG = 28;

    Rectangle HelpRect() {
        // MUP-409e: Use physical dimensions for centering (bitmap is at physical size)
        int bw=_physWidth>0?_physWidth:Width, bh=_physHeight>0?_physHeight:Height;
        if (!_st.HelpPos.HasValue)
            return new Rectangle((bw-HilfeB)/2, (bh-HilfeH)/2, HilfeB, HilfeH);
        Point p = _st.HelpPos.Value;
        // 🔴 Resolution changed? A stored point may then lie outside
        // the screen. In that case fall back to the centre instead of
        // staying invisible.
        if (p.X > bw-60 || p.Y > bh-60 || p.X < -(HilfeB-60) || p.Y < 0) {
            _st.HelpPos = null;
            return new Rectangle((bw-HilfeB)/2, (bh-HilfeH)/2, HilfeB, HilfeH);
        }
        return new Rectangle(p.X, p.Y, HilfeB, HilfeH);
    }

    // The close cross at the top right, with the title bar beside it as the drag handle.
    Rectangle HelpCloseRect(Rectangle r) {
        return new Rectangle(r.Right-38, r.Y+10, KreuzG, KreuzG);
    }
    Rectangle HelpTitleRect(Rectangle r) {
        return new Rectangle(r.X, r.Y, r.Width-48, GriffH);
    }

    // 🔴 At least 60 px must stay visible horizontally, and never
    // above the top edge: otherwise you drag the box off-screen and can no
    // longer reach its title bar.
    Point HelpClamp(Point p) {
        int bw=_physWidth>0?_physWidth:Width, bh=_physHeight>0?_physHeight:Height;
        int x = Math.Max(-(HilfeB-60), Math.Min(bw-60, p.X));
        int y = Math.Max(0,            Math.Min(bh-60, p.Y));
        return new Point(x, y);
    }

    void DrawHelp(Graphics g) {
        Rectangle hr = HelpRect();
        int pw=hr.Width, ph=hr.Height, px=hr.X, py=hr.Y;
        using(GraphicsPath p=RR(px,py,pw,ph,16))
        using(SolidBrush bg=new SolidBrush(Color.FromArgb(235,12,12,24))) g.FillPath(bg,p);
        using(GraphicsPath p=RR(px,py,pw,ph,16))
        using(Pen border=new Pen(Color.FromArgb(70,255,255,255),1f)) g.DrawPath(border,p);

        int x=px+30,y=py+24;
        using(Font ft=new Font("Segoe UI",15f,FontStyle.Bold))
        using(SolidBrush w=new SolidBrush(Color.White)) g.DrawString("MarkUp  -  Help",ft,w,x,y);
        // MUP-20260912-102455567-e140: a dedicated way to close. Without
        // it, once the catch-all click was removed, the help box could only
        // be closed with the keyboard.
        Rectangle kr = HelpCloseRect(hr);
        using(SolidBrush kb=new SolidBrush(Color.FromArgb(40,255,255,255)))
        using(GraphicsPath kp=RR(kr.X,kr.Y,kr.Width,kr.Height,6))
            g.FillPath(kb,kp);
        using(Pen kx=new Pen(Color.FromArgb(190,255,255,255),1.6f)) {
            g.DrawLine(kx,kr.X+9,kr.Y+9,kr.Right-9,kr.Bottom-9);
            g.DrawLine(kx,kr.Right-9,kr.Y+9,kr.X+9,kr.Bottom-9);
        }
        y+=40;

        string[][] left={ new[]{"TOOLS",""}, new[]{DrawState.FormatKey(_st.KPen),"Freehand"}, new[]{DrawState.FormatKey(_st.KArrow),"Arrow"}, new[]{DrawState.FormatKey(_st.KRect),"Rectangle"}, new[]{DrawState.FormatKey(_st.KEll),"Ellipse"}, new[]{DrawState.FormatKey(_st.KText),"Text (Enter = done)"}, new[]{DrawState.FormatKey(_st.KHl),"Highlighter"}, new[]{DrawState.FormatKey(_st.KCen),"Redaction"}, new[]{DrawState.FormatKey(_st.KStamp),"Stamp (right-click = picker)"}, new[]{DrawState.FormatKey(_st.KNumCircle),"Numbering"} };
        // FB-a295: "Klick-Durchlass" renamed to "Markup on/off", Undo uses FormatKey
        string[][] right={ new[]{"ACTIONS",""}, new[]{"Ctrl+"+DrawState.FormatKey(_st.KUndo),"Undo"}, new[]{DrawState.FormatKey(_st.KClear),"Clear all"}, new[]{DrawState.FormatKey(_st.KPass),"Markup on/off"}, new[]{DrawState.FormatKey(_st.KTray),"To tray"}, new[]{DrawState.FormatKey(_st.KHelp),"This help"}, new[]{DrawState.FormatKey(_st.KQuit),"Quit"}, new[]{"",""},new[]{"MULTIPLE MONITORS",""},new[]{"Toolbar","Active on all screens at once"},new[]{"","Just draw on whichever"},new[]{"","screen you want"} };
        DrawCol(g,left, x,         y, 220);
        DrawCol(g,right,x+pw/2-10, y, 260);
        using(Font sm=new Font("Segoe UI",9f))
        using(SolidBrush dim=new SolidBrush(Color.FromArgb(80,255,255,255)))
            g.DrawString(DrawState.FormatKey(_st.KHelp)+" or click to close",sm,dim,px+pw-200,py+ph-46);
        // MUP-20260912-102451348-e535: version, publisher and the Twemoji
        // attribution -- permanently reachable via the help key, unlike the
        // splash screen.
        using(Pen trenn=new Pen(Color.FromArgb(40,255,255,255),1f))
            g.DrawLine(trenn,px+30,py+ph-32,px+pw-30,py+ph-32);
        using(Font sm2=new Font("Segoe UI",8.5f))
        using(SolidBrush dim2=new SolidBrush(Color.FromArgb(110,255,255,255)))
            g.DrawString(AppInfo.Fusszeile(),sm2,dim2,px+30,py+ph-24);
    }

    void DrawCol(Graphics g, string[][] rows, int x, int y, int w) {
        using(Font fs=new Font("Segoe UI",9f,FontStyle.Bold))
        using(Font fn=new Font("Segoe UI",10f))
        using(Font fk=new Font("Consolas",10f,FontStyle.Bold))
        using(SolidBrush acc=new SolidBrush(Color.FromArgb(255,160,0)))
        using(SolidBrush dim=new SolidBrush(Color.FromArgb(190,255,255,255)))
        using(SolidBrush wh =new SolidBrush(Color.White))
        using(SolidBrush kb =new SolidBrush(Color.FromArgb(55,255,255,255))) {
            foreach(string[] row in rows) {
                if(string.IsNullOrEmpty(row[0])&&string.IsNullOrEmpty(row[1])){y+=5;continue;}
                if(string.IsNullOrEmpty(row[1])){g.DrawString(row[0],fs,acc,x,y);y+=20;continue;}
                SizeF ksz=g.MeasureString(row[0],fk); float kw=Math.Max(ksz.Width+10,28);
                if(!string.IsNullOrEmpty(row[0])){using(GraphicsPath kp=RR(x,y,(int)kw,20,4))g.FillPath(kb,kp);g.DrawString(row[0],fk,wh,x+5,y+1);}
                g.DrawString(row[1],fn,dim,x+(string.IsNullOrEmpty(row[0])?0:kw+8),y+1);
                y+=24;
            }
        }
    }

    static GraphicsPath RR(int x,int y,int w,int h,int r) {
        GraphicsPath p=new GraphicsPath();
        p.AddArc(x,y,r*2,r*2,180,90); p.AddArc(x+w-r*2,y,r*2,r*2,270,90);
        p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90); p.AddArc(x,y+h-r*2,r*2,r*2,90,90);
        p.CloseFigure(); return p;
    }

    // MUP-e0d2: Expose text mode so FloatingBar can skip single-letter shortcuts
    public bool IsTextMode => _txOn;

    // MUP-e0d2: Accept typed character from FloatingBar's KeyPress
    public void AppendChar(char c) {
        if (_txOn && !char.IsControl(c)) { _txBuf += c; MarkDirty(_txPos, 400); Render(); }
    }

    // Text-Mode key handling forwarded from bar
    public bool HandleKey(Keys k) {
        if (_txOn) {
            if(k==Keys.Enter||k==Keys.Escape){if(_txBuf.Length>0)CommitText();else{_txOn=false;MarkDirty(_txPos,400);Render();}return true;}
            if(k==Keys.Back&&_txBuf.Length>0){_txBuf=_txBuf.Substring(0,_txBuf.Length-1);MarkDirty(_txPos,400);Render();return true;}
        }
        return false;
    }
}

// ── Hotkey Dialog ─────────────────────────────────────────────────────────────
// FB-3792: Free key capture - any key allowed, no ComboBox restriction
class HotkeyDialog : Form {
    DrawState _st;
    TextBox _cPen,_cArr,_cRct,_cEll,_cTxt,_cHl,_cCen,_cStamp,_cNumCircle,_cTray,_cPass,_cHelp,_cUndo,_cClear,_cShow,_cQuit;
    Label _warn;

    public HotkeyDialog(DrawState st) {
        _st=st; Text="Shortcuts"; FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=MinimizeBox=false; StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(22,22,36); ForeColor=Color.White; ClientSize=new Size(340,720); TopMost=true;
        Font=new Font("Segoe UI",10f); Build();
    }

    TextBox MRow(TableLayoutPanel t,int row,string lbl,Keys cur){
        t.Controls.Add(new Label{Text=lbl,ForeColor=Color.FromArgb(200,200,225),TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Fill},0,row);
        TextBox tb=new TextBox{ReadOnly=true,BackColor=Color.FromArgb(32,32,50),ForeColor=Color.White,
            BorderStyle=BorderStyle.FixedSingle,Dock=DockStyle.Fill,Margin=new Padding(0,2,0,2),
            TextAlign=HorizontalAlignment.Center,Text=DrawState.FormatKey(cur),Cursor=Cursors.Hand,ShortcutsEnabled=false};
        tb.Tag=cur;
        // MUP-c8ec: Ensure TextBox gets proper keyboard focus on click
        tb.MouseDown+=(s,e)=>{tb.Focus();};
        // MUP-c8ec: PreviewKeyDown marks all keys as regular input so KeyDown fires for arrows, Tab, etc.
        tb.PreviewKeyDown+=(s,e)=>{e.IsInputKey=true;};
        tb.KeyDown+=(s,e)=>{e.SuppressKeyPress=true;e.Handled=true;
            Keys k=e.KeyCode; if(k==Keys.ShiftKey||k==Keys.ControlKey||k==Keys.Menu)return;
            // FB-3792: Capture full combo including modifiers
            Keys combo=k; if(e.Shift)combo|=Keys.Shift; if(e.Control)combo|=Keys.Control; if(e.Alt)combo|=Keys.Alt;
            tb.Tag=combo;tb.Text=DrawState.FormatKey(combo);CheckDuplicates();};
        tb.GotFocus+=(s,e)=>{tb.BackColor=Color.FromArgb(50,50,80);};
        tb.LostFocus+=(s,e)=>{tb.BackColor=Color.FromArgb(32,32,50);};
        t.Controls.Add(tb,1,row);return tb;
    }

    void CheckDuplicates(){
        TextBox[] all={_cPen,_cArr,_cRct,_cEll,_cTxt,_cHl,_cCen,_cStamp,_cNumCircle,_cTray,_cPass,_cHelp,_cUndo,_cClear,_cShow,_cQuit};
        bool dup=false;
        foreach(var tb in all) if(tb!=null) tb.ForeColor=Color.White;
        for(int i=0;i<all.Length;i++) for(int j=i+1;j<all.Length;j++){
            if(all[i]!=null&&all[j]!=null&&(Keys)all[i].Tag==(Keys)all[j].Tag){
                all[i].ForeColor=Color.FromArgb(255,100,100);all[j].ForeColor=Color.FromArgb(255,100,100);dup=true;
            }
        }
        if(_warn!=null) _warn.Visible=dup;
    }

    void Build(){
        Label hdr=new Label{Text="Customise shortcuts",Font=new Font("Segoe UI",12f,FontStyle.Bold),ForeColor=Color.White,Dock=DockStyle.Top,Height=34,Padding=new Padding(14,6,0,0)};
        Label hint=new Label{Text="Click a field, then press any key",Font=new Font("Segoe UI",8.5f),ForeColor=Color.FromArgb(130,130,170),Dock=DockStyle.Top,Height=20,Padding=new Padding(14,0,0,0)};
        TableLayoutPanel tbl=new TableLayoutPanel{ColumnCount=2,Dock=DockStyle.Fill,Padding=new Padding(14,4,14,4),BackColor=Color.Transparent};
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,42));tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,58));
        // FB-a295: 19 rows = 9 tools + separator + 7 actions + warning + hint
        for(int i=0;i<19;i++)tbl.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
        _cPen =MRow(tbl,0,"Freehand",  _st.KPen);  _cArr =MRow(tbl,1,"Arrow",    _st.KArrow);
        _cRct =MRow(tbl,2,"Rectangle",  _st.KRect);  _cEll =MRow(tbl,3,"Circle",    _st.KEll);
        _cTxt =MRow(tbl,4,"Text",      _st.KText);  _cHl  =MRow(tbl,5,"Markieren",_st.KHl);
        _cCen =MRow(tbl,6,"Redaction",    _st.KCen);   _cStamp=MRow(tbl,7,"Stamp", _st.KStamp);
        _cNumCircle=MRow(tbl,8,"Numbering",_st.KNumCircle);
        tbl.Controls.Add(new Label{Text="\u2500\u2500 Actions \u2500\u2500",ForeColor=Color.FromArgb(100,100,140),TextAlign=ContentAlignment.MiddleLeft,Dock=DockStyle.Fill},0,9);
        tbl.Controls.Add(new Label{Dock=DockStyle.Fill},1,9);
        // FB-a295: "Durchlass" renamed to "Markup" (toggle markup mode)
        _cTray=MRow(tbl,10,"Tray",      _st.KTray);  _cPass=MRow(tbl,11,"Markup",_st.KPass); _cHelp=MRow(tbl,12,"Help",_st.KHelp);
        // FB-a295: 4 new configurable action shortcuts
        _cUndo=MRow(tbl,13,"Undo", _st.KUndo);  _cClear=MRow(tbl,14,"Clear all",_st.KClear);
        _cShow=MRow(tbl,15,"Anzeigen",   _st.KShow);  _cQuit=MRow(tbl,16,"Quit",_st.KQuit);
        _warn=new Label{Text="\u26A0 Duplicate key detected!",ForeColor=Color.FromArgb(255,100,100),Font=new Font("Segoe UI",8.5f,FontStyle.Bold),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Visible=false};
        tbl.Controls.Add(_warn,0,17); tbl.SetColumnSpan(_warn,2);
        tbl.Controls.Add(new Label{Text="Click a field, then press a key",ForeColor=Color.FromArgb(90,90,120),Font=new Font("Consolas",8f),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,18);
        tbl.SetColumnSpan(tbl.GetControlFromPosition(0,18),2);
        Panel bp=new Panel{Dock=DockStyle.Bottom,Height=46,BackColor=Color.FromArgb(18,18,30),Padding=new Padding(10,7,10,7)};
        Button ok=new Button{Text="Apply",Width=120,Height=28,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(40,110,55),ForeColor=Color.White,Dock=DockStyle.Right};
        Button can=new Button{Text="Cancel",Width=100,Height=28,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(60,35,35),ForeColor=Color.White,Dock=DockStyle.Right};
        // FB-3792: Reset to defaults button
        Button rst=new Button{Text="Standard",Width=90,Height=28,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(50,50,70),ForeColor=Color.FromArgb(180,180,210),Dock=DockStyle.Left};
        rst.FlatAppearance.BorderSize=0;
        rst.Click+=(s,e)=>ResetDefaults();
        ok.FlatAppearance.BorderSize=0;can.FlatAppearance.BorderSize=0;
        ok.Click+=(s,e)=>{Apply();Close();};can.Click+=(s,e)=>Close();
        bp.Controls.Add(ok);bp.Controls.Add(can);bp.Controls.Add(rst);
        Controls.Add(tbl);Controls.Add(hint);Controls.Add(hdr);Controls.Add(bp);
    }
    Keys K(TextBox tb){return(Keys)tb.Tag;}
    void SetTB(TextBox tb, Keys k){tb.Tag=k;tb.Text=DrawState.FormatKey(k);}
    // FB-a295: Apply now includes all 16 configurable shortcuts
    void Apply(){try{_st.KPen=K(_cPen);_st.KArrow=K(_cArr);_st.KRect=K(_cRct);_st.KEll=K(_cEll);_st.KText=K(_cTxt);_st.KHl=K(_cHl);_st.KCen=K(_cCen);_st.KStamp=K(_cStamp);_st.KNumCircle=K(_cNumCircle);_st.KTray=K(_cTray);_st.KPass=K(_cPass);_st.KHelp=K(_cHelp);_st.KUndo=K(_cUndo);_st.KClear=K(_cClear);_st.KShow=K(_cShow);_st.KQuit=K(_cQuit);_st.Fire();}catch(Exception ex){MessageBox.Show(ex.Message);}}
    // FB-3792 v5: Reset all fields to Ctrl+Shift+letter defaults (no F-keys, no Delete)
    void ResetDefaults(){
        SetTB(_cPen,Keys.Control|Keys.Shift|Keys.D); SetTB(_cArr,Keys.Control|Keys.Shift|Keys.A); SetTB(_cRct,Keys.Control|Keys.Shift|Keys.R);
        SetTB(_cEll,Keys.Control|Keys.Shift|Keys.C); SetTB(_cTxt,Keys.Control|Keys.Shift|Keys.T); SetTB(_cHl,Keys.Control|Keys.Shift|Keys.H);
        SetTB(_cCen,Keys.Control|Keys.Shift|Keys.B); SetTB(_cStamp,Keys.Control|Keys.Shift|Keys.S); SetTB(_cNumCircle,Keys.Control|Keys.Shift|Keys.N);
        SetTB(_cTray,Keys.Control|Keys.Shift|Keys.Y); SetTB(_cPass,Keys.Control|Keys.Shift|Keys.P); SetTB(_cHelp,Keys.Control|Keys.Shift|Keys.I);
        // FB-a295: Reset new action shortcuts
        SetTB(_cUndo,Keys.Z); SetTB(_cClear,Keys.Control|Keys.Shift|Keys.X); SetTB(_cShow,Keys.Control|Keys.Shift|Keys.M); SetTB(_cQuit,Keys.Control|Keys.Shift|Keys.Q);
        CheckDuplicates();
    }
    // FB-3792 v6: Capture ALL keys inside HotkeyDialog - prevent any leakage to FloatingBar/overlay
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        // Let the focused TextBox's KeyDown handler capture the key
        // Return false = don't eat key at form level, let it reach control KeyDown
        return false;
    }
}

// ── Floating Toolbar ──────────────────────────────────────────────────────────
class FloatingBar : Form {
    DrawState              _st;
    List<ScreenOverlay>    _overlays;
    MarkUpApp              _app;
    Dictionary<DrawTool,Button> _tbtns = new Dictionary<DrawTool,Button>();
    List<Button>           _cbtns = new List<Button>();
    Button                 _passBtn;
    // MUP-a295: Store action button refs for tooltip refresh after shortcut changes
    Button                 _undoBtn, _helpBtn, _trayBtn, _clearBtn, _quitBtn;
    ComboBox               _sizeCombo; // MUP-0fd0: Reference for Ctrl+Scroll
    bool _drag; Point _dpt;
    internal bool _dialogOpen; // FB-3792 v6: suppress shortcuts while HotkeyDialog is open
    ToolTip _tt;   // single shared tooltip with ShowAlways=true

    static readonly Color[]    COLS   = { Color.FromArgb(255,60,60),Color.FromArgb(255,140,0),Color.FromArgb(240,200,0),Color.FromArgb(30,200,90),Color.FromArgb(40,150,255),Color.FromArgb(255,255,255) };
    static readonly string[]   ICONS  = { "\u270F","\u2197","\u25AD","\u25EF","T","\u25AC","\u2591","\uD83D\uDCCE","\u2460" };
    static readonly DrawTool[] TOOLS  = { DrawTool.Pen,DrawTool.Arrow,DrawTool.Rect,DrawTool.Ellipse,DrawTool.Text,DrawTool.Highlight,DrawTool.Censor,DrawTool.Stamp,DrawTool.NumberedCircle };
    static readonly string[]   TNAMES = { "Freehand","Arrow","Rectangle","Circle","Text","Highlight","Redaction","Stamp","Numbering" };

    public FloatingBar(DrawState st, List<ScreenOverlay> overlays, MarkUpApp app) {
        _st=st; _overlays=overlays; _app=app;
        FormBorderStyle=FormBorderStyle.None; TopMost=true; ShowInTaskbar=true;
        Icon=MarkUpApp.MakeAppIcon(); // MUP-234728: Custom icon (smaller than default)
        BackColor=Color.FromArgb(18,18,28); Opacity=0.97; DoubleBuffered=true;
        StartPosition=FormStartPosition.Manual;
        AutoSize=true; AutoSizeMode=AutoSizeMode.GrowAndShrink;
        // Shared tooltip - ShowAlways so it works even when overlay has focus
        _tt = new ToolTip {
            ShowAlways   = true,
            AutoPopDelay = 8000,
            InitialDelay = 400,
            ReshowDelay  = 200,
        };
        // Place toolbar centered on primary screen
        Shown+=(s,e)=>Centre();
        Build();
        KeyDown+=OnKeyDown; KeyPress+=OnKeyPress;
    }

    void Centre() {
        Screen sc=Screen.PrimaryScreen;
        Location=new Point(sc.Bounds.X+(sc.Bounds.Width-Width)/2, sc.Bounds.Y+18);
        NotifyOverlaysOfBarPos();
    }

    void NotifyOverlaysOfBarPos() {
        Rectangle barRect = new Rectangle(Location, Size);
        // MUP-c5be: Keep mouse hook informed of toolbar position
        MouseHookManager.ToolbarRect = barRect;
        foreach (ScreenOverlay ov in _overlays)
            if (ov.IsHandleCreated) ov.UpdateBarHole(barRect);
    }

    // MUP-234728 v6: Reverted to original sizes (50% reduction caused icon clipping)
    void Build() {
        KeyPreview = true; // MUP-e0d2: Form sees KeyPress before child Buttons - required for text input
        FlowLayoutPanel fl=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Padding=new Padding(4,4,4,4),BackColor=Color.Transparent};

        Panel grip=new Panel{Size=new Size(8,22),BackColor=Color.Transparent,Cursor=Cursors.SizeAll};
        for(int i=0;i<3;i++){Panel d=new Panel{Size=new Size(3,3),BackColor=Color.FromArgb(80,255,255,255),Location=new Point(2,3+i*6)};grip.Controls.Add(d);}
        grip.MouseDown+=DS;grip.MouseMove+=DM;grip.MouseUp+=DU;
        fl.Controls.Add(grip); fl.Controls.Add(Div());

        for(int i=0;i<TOOLS.Length;i++){
            DrawTool t=TOOLS[i];int ii=i;
            Button b=TB(ICONS[i],TNAMES[ii]+" ("+_st.HotkeyLabel(t)+")"+(HasToolOptions(t)?"  (Rechtsklick=Optionen)":"")); // MUP-31bb: Shortcut in parentheses
            b.Click+=delegate{Pick(t);};
            // Right-click: tool-specific context menu or stamp picker
            DrawTool ct=t; Button cb2=b;
            b.MouseUp+=delegate(object sx,MouseEventArgs ex){if(ex.Button==MouseButtons.Right)ShowToolOptions(ct,cb2);};
            _tbtns[t]=b;fl.Controls.Add(b);
        }
        Mark(_st.Tool); fl.Controls.Add(Div()); // FB-1452: Sync to loaded tool

        for(int i=0;i<COLS.Length;i++){Color c=COLS[i];Button b=CB(c);Button rb=b;Color rc=c;b.Click+=delegate{_st.Color=rc;_st.IsRainbow=false;HiCol(rb);_st.Fire();};_cbtns.Add(b);fl.Controls.Add(b);}
        // Unicorn / Rainbow color button - MUP-e989: HotPink ForeColor for visibility
        Button ub2=new Button{Text="\uD83E\uDD84",Size=new Size(28,28),Margin=new Padding(1,1,1,1),FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand,Font=new Font("Segoe UI Emoji",12f),ForeColor=Color.FromArgb(255,105,180)}; // MUP-afae: 28x28+12f (was 22x22+10f, emoji clipped) + MUP-c9b9: uniform 28px height
        ub2.FlatAppearance.BorderSize=0;ub2.FlatAppearance.BorderColor=Color.White;ub2.BackColor=Color.FromArgb(42,42,62);
        _tt.SetToolTip(ub2,"Einhornfarbe (Regenbogen) - Rechtsklick: Statisch/Animiert");
        ub2.Click+=delegate{_st.IsRainbow=true;_st.IsAnimatedRainbow=false;HiCol(ub2);_st.Fire();};
        // MUP-e989: Right-click context menu for Static vs Animated rainbow
        ub2.MouseUp+=delegate(object sx,MouseEventArgs ex){
            if(ex.Button!=MouseButtons.Right) return;
            ContextMenuStrip cm=new ContextMenuStrip();
            cm.BackColor=Color.FromArgb(30,30,46);cm.ForeColor=Color.White;cm.Renderer=new ToolStripProfessionalRenderer(new DarkColorTable());
            ToolStripMenuItem mi1=new ToolStripMenuItem("Statisch (Regenbogen)");
            mi1.Click+=delegate{_st.IsRainbow=true;_st.IsAnimatedRainbow=false;HiCol(ub2);_st.Fire();};
            ToolStripMenuItem mi2=new ToolStripMenuItem("Animiert (Farbwechsel)");
            mi2.Click+=delegate{_st.IsRainbow=true;_st.IsAnimatedRainbow=true;HiCol(ub2);_st.Fire();};
            cm.Items.Add(mi1);cm.Items.Add(mi2);
            MouseHookManager.Active=false;
            cm.Closed+=(s2,e2)=>MouseHookManager.Active=true;
            cm.Show(ub2,new Point(0,ub2.Height));
        };
        _cbtns.Add(ub2);fl.Controls.Add(ub2);
        // FB-1452: Sync color highlight to loaded state
        {int ci=0;if(_st.IsRainbow){ci=_cbtns.Count-1;}else{for(int i=0;i<COLS.Length;i++)if(COLS[i].ToArgb()==_st.Color.ToArgb()){ci=i;break;}}HiCol(_cbtns[ci]);}
        fl.Controls.Add(Div());

        // MUP-0fd0: More size presets for finer control (was 4 presets, now 10)
        ComboBox cx=new ComboBox{Size=new Size(50,28),Margin=new Padding(1,1,1,1),DropDownStyle=ComboBoxStyle.DropDownList,BackColor=Color.FromArgb(30,30,46),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Font=new Font("Consolas",8f)};
        cx.Items.AddRange(new object[]{"1px","2px","3px","4px","5px","6px","8px","10px","14px","20px"});
        // FB-1452: Sync combo to loaded width
        {float[] _ws={1f,2f,3f,4f,5f,6f,8f,10f,14f,20f};int si=3;for(int i=0;i<_ws.Length;i++)if(Math.Abs(_ws[i]-_st.Width)<0.1f){si=i;break;}cx.SelectedIndex=si;}
        float[] ws={1f,2f,3f,4f,5f,6f,8f,10f,14f,20f};cx.SelectedIndexChanged+=delegate{_st.Width=ws[cx.SelectedIndex];_st.Fire();}; // MUP-e357: Fire() to propagate size change
        // MUP-e357: Disable mouse hook when dropdown opens (popup is outside ToolbarRect)
        cx.DropDown+=delegate{MouseHookManager.Active=false;};
        cx.DropDownClosed+=delegate{MouseHookManager.Active=true;};
        _sizeCombo = cx; // MUP-0fd0: Store ref for Ctrl+Scroll
        fl.Controls.Add(cx); fl.Controls.Add(Div());

        // FB-a295: Renamed "Durchlass" to "Markup" throughout
        _passBtn=TB("🖱","Markup ein/aus ("+DrawState.FormatKey(_st.KPass)+")");
        _passBtn.Size = new Size(28, 28);
        _passBtn.Click+=delegate{_st.PassThru=!_st.PassThru;MouseHookManager.SwallowClicks=!_st.PassThru;MouseHookManager.Active=!_st.PassThru;UpdatePassBtn();_st.Fire();};
        UpdatePassBtn(); // MUP-234921: Sync button state at startup
        fl.Controls.Add(_passBtn);

        _undoBtn=TB("↩","Undo (Ctrl+"+DrawState.FormatKey(_st.KUndo)+")");
        _undoBtn.Click+=delegate{_app.UndoAll();};
        fl.Controls.Add(_undoBtn);

        fl.Controls.Add(Div());
        Button hkb=TB("⌨","Customise shortcuts");
        // FB-7962: Disable mouse hook while HotkeyDialog is open (clicks outside ToolbarRect would be swallowed)
        // MUP-a295: After dialog closes, refresh tooltips + tray menu to show updated shortcuts
        hkb.Click+=delegate{_dialogOpen=true;bool wasActive=MouseHookManager.Active;MouseHookManager.Active=false;try{new HotkeyDialog(_st).ShowDialog();}finally{_dialogOpen=false;MouseHookManager.Active=wasActive;RefreshTooltips();_app.RebuildTrayMenu();}};
        fl.Controls.Add(hkb);

        _helpBtn=TB("?","Help ("+DrawState.FormatKey(_st.KHelp)+")");
        _helpBtn.Click+=delegate{_st.ShowHelp=!_st.ShowHelp;_st.Fire();};
        fl.Controls.Add(_helpBtn);

        // MUP-13df: Pin button - left-click=Desktop shortcut, right-click=more options
        Button pinBtn=TB("📌","Create shortcut (right-click = options)");
        pinBtn.Click+=delegate{_app.CreateDesktopShortcut();};
        pinBtn.MouseUp+=delegate(object sx,MouseEventArgs ex){
            if(ex.Button!=MouseButtons.Right) return;
            ContextMenuStrip pcm=new ContextMenuStrip();
            pcm.BackColor=Color.FromArgb(30,30,46);pcm.ForeColor=Color.White;pcm.Renderer=new ToolStripProfessionalRenderer(new DarkColorTable());
            pcm.Items.Add("Desktop shortcut",null,(s2,e2)=>{_app.CreateDesktopShortcut();});
            pcm.Items.Add("Start menu shortcut",null,(s2,e2)=>{_app.CreateStartMenuShortcut();});
            pcm.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem autoItem=new ToolStripMenuItem(_app.IsAutostart()?"✓ Remove from startup":"Run at logon");
            autoItem.Click+=delegate{_app.ToggleAutostart();};
            pcm.Items.Add(autoItem);
            MouseHookManager.Active=false;
            pcm.Closed+=(s2,e2)=>MouseHookManager.Active=true;
            pcm.Show(pinBtn,new Point(0,pinBtn.Height));
        };
        fl.Controls.Add(pinBtn);

        fl.Controls.Add(Div());

        // FB-a295: Consistent tooltip format using FormatKey()
        _trayBtn=TB("—","In Tray ("+DrawState.FormatKey(_st.KTray)+")");
        _trayBtn.Click+=delegate{_app.SendToTray();};
        fl.Controls.Add(_trayBtn);

        // FB-a295: Consistent tooltip format using FormatKey()
        _clearBtn=TB("🗑","Clear all ("+DrawState.FormatKey(_st.KClear)+")");
        _clearBtn.ForeColor=Color.FromArgb(210,70,70);
        _clearBtn.Click+=delegate{_app.ClearAll();};
        fl.Controls.Add(_clearBtn);

        // FB-a295: Consistent tooltip format using FormatKey()
        _quitBtn=TB("✕","Quit ("+DrawState.FormatKey(_st.KQuit)+")");
        _quitBtn.ForeColor=Color.FromArgb(210,70,70);
        _quitBtn.Click+=delegate{Application.Exit();};
        fl.Controls.Add(_quitBtn);

        Controls.Add(fl);
        fl.MouseDown+=DS;fl.MouseMove+=DM;fl.MouseUp+=DU;
        MouseDown+=DS;MouseMove+=DM;MouseUp+=DU;
    }

    public void Pick(DrawTool t) {
        _st.Tool=t; Mark(t);
        // MUP-235148: Sync color button highlight when switching tools
        if (_st.IsRainbow && _cbtns.Count > 0) HiCol(_cbtns[_cbtns.Count-1]); // rainbow btn is last
        _st.Fire();
    }

    protected override bool ProcessCmdKey(ref Message m, Keys k) {
        // FB-3792 v6: Don't intercept keys while a modal dialog (HotkeyDialog) is open
        if (_dialogOpen) return base.ProcessCmdKey(ref m, k);
        // Forward text-mode keys to active overlay first
        foreach(ScreenOverlay ov in _overlays) if(ov.HandleKey(k)) return true;

        // MUP-e0d2 + FB-3792: When in text mode, block letter/number shortcuts (bare AND Shift+letter for uppercase)
        bool anyTextMode = false;
        foreach(ScreenOverlay ov in _overlays) if(ov.IsTextMode) { anyTextMode = true; break; }
        if (anyTextMode) {
            Keys code = k & Keys.KeyCode;
            Keys mods = k & Keys.Modifiers;
            bool isLetter = code >= Keys.A && code <= Keys.Z;
            bool isDigit = (code >= Keys.D0 && code <= Keys.D9) || (code >= Keys.NumPad0 && code <= Keys.NumPad9);
            // Block bare letters/digits and Shift+letter/digit (needed for uppercase/symbols in text input)
            // Allow Ctrl combos (Ctrl+Z undo etc.), Fn keys, Escape, Delete through
            if ((isLetter || isDigit) && (mods == Keys.None || mods == Keys.Shift)) return false;
        }

        if(k==_st.KHelp)                    {_st.ShowHelp=!_st.ShowHelp;_st.Fire();return true;}
        if(k==(Keys.Control|_st.KUndo))     {_app.UndoAll();return true;}
        if(k==_st.KClear)                   {_app.ClearAll();return true;}
        if(k==_st.KPass)                    {_st.PassThru=!_st.PassThru;MouseHookManager.SwallowClicks=!_st.PassThru;MouseHookManager.Active=!_st.PassThru;UpdatePassBtn();_st.Fire();return true;}
        if(k==_st.KTray)                    {_app.SendToTray();return true;}
        // FB-a295 v3: Removed hardcoded ESC toggle - use configurable KPass instead
        // FB-a295: Use configurable KQuit + KShow instead of hardcoded Ctrl+Q
        if(k==_st.KQuit)                    {Application.Exit();return true;}
        if(k==_st.KShow)                    {_app.Restore();return true;}
        if(k==_st.KPen)    {Pick(DrawTool.Pen);      return true;}
        if(k==_st.KArrow)  {Pick(DrawTool.Arrow);    return true;}
        if(k==_st.KRect)   {Pick(DrawTool.Rect);     return true;}
        if(k==_st.KEll)    {Pick(DrawTool.Ellipse);  return true;}
        if(k==_st.KText)   {Pick(DrawTool.Text);     return true;}
        if(k==_st.KHl)     {Pick(DrawTool.Highlight);return true;}
        if(k==_st.KCen)    {Pick(DrawTool.Censor);   return true;}
        if(k==_st.KStamp)  {Pick(DrawTool.Stamp);    return true;}
        if(k==_st.KNumCircle){Pick(DrawTool.NumberedCircle);return true;}
        return base.ProcessCmdKey(ref m,k);
    }

    void OnKeyDown(object s,KeyEventArgs e){}
    void OnKeyPress(object s,KeyPressEventArgs e){
        // MUP-e0d2: Forward typed characters to overlay in text mode
        foreach(ScreenOverlay ov in _overlays) { if(ov.IsTextMode) { ov.AppendChar(e.KeyChar); break; } }
    }

    public void UpdatePassBtn(){
        if(_passBtn==null) return;
        // Green = markup active, Dark-blue = passthrough (exited markup)
        _passBtn.BackColor = _st.PassThru ? Color.FromArgb(42,42,62) : Color.FromArgb(40,110,55);
        _passBtn.ForeColor = Color.White;
        // FB-a295: Consistent tooltip format
        _tt.SetToolTip(_passBtn, _st.PassThru
            ? "Markup OFF - click to turn markup on ("+DrawState.FormatKey(_st.KPass)+")"
            : "Markup ON - click to turn markup off ("+DrawState.FormatKey(_st.KPass)+")");
    }

    // MUP-a295: Refresh all toolbar tooltips after shortcut changes in HotkeyDialog
    public void RefreshTooltips() {
        // Tool buttons - update shortcut label in tooltip
        for (int i = 0; i < TOOLS.Length; i++) {
            DrawTool t = TOOLS[i];
            if (_tbtns.ContainsKey(t))
                _tt.SetToolTip(_tbtns[t], TNAMES[i]+" ("+_st.HotkeyLabel(t)+")"+(HasToolOptions(t)?"  (Rechtsklick=Optionen)":""));
        }
        // Action buttons - update shortcut labels
        UpdatePassBtn(); // passBtn tooltip is updated inside UpdatePassBtn
        if (_undoBtn != null)  _tt.SetToolTip(_undoBtn,  "Undo (Ctrl+"+DrawState.FormatKey(_st.KUndo)+")");
        if (_helpBtn != null)  _tt.SetToolTip(_helpBtn,  "Help ("+DrawState.FormatKey(_st.KHelp)+")");
        if (_trayBtn != null)  _tt.SetToolTip(_trayBtn,  "In Tray ("+DrawState.FormatKey(_st.KTray)+")");
        if (_clearBtn != null) _tt.SetToolTip(_clearBtn, "Clear all ("+DrawState.FormatKey(_st.KClear)+")");
        if (_quitBtn != null)  _tt.SetToolTip(_quitBtn,  "Quit ("+DrawState.FormatKey(_st.KQuit)+")");
    }

    // MUP-234826: ALL tools now have right-click options
    bool HasToolOptions(DrawTool t) { return true; }

    // MUP-234951: Show tool options at screen coordinates (for right-click on overlay)
    public void ShowToolOptionsAtScreen(Point screenPt) {
        ContextMenuStrip cm = BuildToolOptionsMenu(_st.Tool);
        if (cm != null && cm.Items.Count > 0) {
            MouseHookManager.Active = false;
            cm.Closed += (s,e) => MouseHookManager.Active = true;
            cm.Show(screenPt);
        }
    }

    void ShowToolOptions(DrawTool tool, Button btn) {
        ContextMenuStrip cm = BuildToolOptionsMenu(tool);
        if (cm != null && cm.Items.Count > 0) {
            MouseHookManager.Active = false;
            cm.Closed += (s,e) => MouseHookManager.Active = true;
            cm.Show(btn, new Point(0, btn.Height));
        }
    }

    ContextMenuStrip BuildToolOptionsMenu(DrawTool tool) {
        ContextMenuStrip cm = new ContextMenuStrip();
        cm.BackColor=Color.FromArgb(30,30,48); cm.ForeColor=Color.White;
        cm.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());

        if (tool == DrawTool.Pen) {
            // Dash style options
            cm.Items.Add(new ToolStripLabel("Line style"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "Durchgezogen", _st.PenDash==DashStyle.Solid,   ()=>{_st.PenDash=DashStyle.Solid;});
            AddRadio(cm, "Gestrichelt",  _st.PenDash==DashStyle.Dash,    ()=>{_st.PenDash=DashStyle.Dash;});
            AddRadio(cm, "Gepunktet",    _st.PenDash==DashStyle.Dot,     ()=>{_st.PenDash=DashStyle.Dot;});
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(new ToolStripLabel("Line caps"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "Rund",   _st.PenCap==LineCap.Round,  ()=>{_st.PenCap=LineCap.Round;});
            AddRadio(cm, "Eckig",  _st.PenCap==LineCap.Square, ()=>{_st.PenCap=LineCap.Square;});
        }
        else if (tool == DrawTool.Arrow) {
            cm.Items.Add(new ToolStripLabel("Arrowhead"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "\u25B6 Filled",  _st.ArrowHeadStyle==ArrowHead.Filled, ()=>{_st.ArrowHeadStyle=ArrowHead.Filled;});
            AddRadio(cm, "\u25B7 Offen",      _st.ArrowHeadStyle==ArrowHead.Open,   ()=>{_st.ArrowHeadStyle=ArrowHead.Open;});
            AddRadio(cm, "\u25C6 Raute",      _st.ArrowHeadStyle==ArrowHead.Diamond,()=>{_st.ArrowHeadStyle=ArrowHead.Diamond;});
            AddRadio(cm, "\u25CF Punkt",      _st.ArrowHeadStyle==ArrowHead.Dot,    ()=>{_st.ArrowHeadStyle=ArrowHead.Dot;});
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(new ToolStripLabel("Richtung"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "\u2192 Einseitig",    !_st.ArrowDual, ()=>{_st.ArrowDual=false;});
            AddRadio(cm, "\u2194 Beidseitig",   _st.ArrowDual,  ()=>{_st.ArrowDual=true;});
        }
        else if (tool == DrawTool.Rect) {
            cm.Items.Add(new ToolStripLabel("Ecken"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "Eckig (0)",          _st.RectRadius==0,  ()=>{_st.RectRadius=0;});
            AddRadio(cm, "Abgerundet (12)",    _st.RectRadius==12, ()=>{_st.RectRadius=12;});
            AddRadio(cm, "Stark gerundet (24)",_st.RectRadius==24, ()=>{_st.RectRadius=24;});
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(new ToolStripLabel("Fill"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "Nur Rahmen",    !_st.RectFill, ()=>{_st.RectFill=false;});
            AddRadio(cm, "Halbtransparent", _st.RectFill, ()=>{_st.RectFill=true;});
        }
        else if (tool == DrawTool.Ellipse) {
            // MUP-234826: Ellipse fill option
            cm.Items.Add(new ToolStripLabel("Fill"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "Nur Rahmen",      !_st.EllipseFill, ()=>{_st.EllipseFill=false;});
            AddRadio(cm, "Halbtransparent",  _st.EllipseFill,  ()=>{_st.EllipseFill=true;});
        }
        else if (tool == DrawTool.Text) {
            // MUP-234826: Text size option
            cm.Items.Add(new ToolStripLabel("Font size"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "Klein (14pt)",  _st.TextSize==14f, ()=>{_st.TextSize=14f;});
            AddRadio(cm, "Normal (20pt)", _st.TextSize==20f, ()=>{_st.TextSize=20f;});
            AddRadio(cm, "Gross (32pt)",  _st.TextSize==32f, ()=>{_st.TextSize=32f;});
            AddRadio(cm, "Riesig (48pt)", _st.TextSize==48f, ()=>{_st.TextSize=48f;});
        }
        else if (tool == DrawTool.Highlight) {
            // MUP-234826: Highlight opacity option
            cm.Items.Add(new ToolStripLabel("Deckkraft"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            // MUP-133846: Correct percentage labels (alpha/255 = opacity%)
            AddRadio(cm, "10%",  _st.HighlightAlpha==25f,  ()=>{_st.HighlightAlpha=25f;});
            AddRadio(cm, "25%",  _st.HighlightAlpha==64f,  ()=>{_st.HighlightAlpha=64f;});
            AddRadio(cm, "50%",  _st.HighlightAlpha==128f, ()=>{_st.HighlightAlpha=128f;});
            AddRadio(cm, "75%",  _st.HighlightAlpha==191f, ()=>{_st.HighlightAlpha=191f;});
            AddRadio(cm, "100%", _st.HighlightAlpha==255f, ()=>{_st.HighlightAlpha=255f;});
        }
        else if (tool == DrawTool.Censor) {
            // MUP-234826: Censor methods + intensity
            cm.Items.Add(new ToolStripLabel("Methode"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "Schwarzer Balken", _st.CensorMode==CensorMethod.BlackBar, ()=>{_st.CensorMode=CensorMethod.BlackBar;});
            AddRadio(cm, "Pixeliert", _st.CensorMode==CensorMethod.Pixelate, ()=>{_st.CensorMode=CensorMethod.Pixelate;});
            AddRadio(cm, "Diagonal-Schraffur", _st.CensorMode==CensorMethod.DiagonalHatch, ()=>{_st.CensorMode=CensorMethod.DiagonalHatch;});
            AddRadio(cm, "Kreuz-Schraffur (rot)", _st.CensorMode==CensorMethod.CrossHatch, ()=>{_st.CensorMode=CensorMethod.CrossHatch;});
            AddRadio(cm, "Weichzeichner", _st.CensorMode==CensorMethod.Blur, ()=>{_st.CensorMode=CensorMethod.Blur;});
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(new ToolStripLabel("Intensity"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "Leicht", _st.CensorLevel==CensorIntensity.Light, ()=>{_st.CensorLevel=CensorIntensity.Light;});
            AddRadio(cm, "Normal", _st.CensorLevel==CensorIntensity.Medium, ()=>{_st.CensorLevel=CensorIntensity.Medium;});
            AddRadio(cm, "Stark", _st.CensorLevel==CensorIntensity.Heavy, ()=>{_st.CensorLevel=CensorIntensity.Heavy;});
        }
        else if (tool == DrawTool.Stamp) {
            // ── Image stamps section ──
            cm.Items.Add(new ToolStripLabel("Image stamps"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            StampManager.Init();
            for (int i=0;i<StampManager.ImageStampKeys.Length;i++) {
                string key=StampManager.ImageStampKeys[i]; string nm=StampManager.ImageStampNames[i];
                bool sel = _st.IsImageStamp && _st.StampImageKey==key;
                Image thumb = StampManager.GetThumbnail(key, 48); // MUP-f1b9: Larger stamp thumbnails for readability (was 33)
                ToolStripMenuItem mi = new ToolStripMenuItem(nm, thumb);
                mi.Checked = sel; mi.ForeColor=Color.White; mi.BackColor=Color.FromArgb(30,30,48);
                mi.Click+=delegate{_st.StampImageKey=key;_st.StampEmoji=null;Pick(DrawTool.Stamp);};
                cm.Items.Add(mi);
            }
            cm.Items.Add(new ToolStripSeparator());
            // ── Emoji stamps section ──
            cm.Items.Add(new ToolStripLabel("Emoji stamps"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            // MUP-234951: Expanded emoji set (20 emojis)
            string[] stamps = { "\u2714","\u274C","\u2757","\u2753","\u2B50","\uD83D\uDD25","\uD83D\uDC4D","\uD83D\uDC4E","\uD83D\uDCA1","\u26A0","\uD83D\uDD12","\u2764","\uD83D\uDE80","\uD83C\uDFAF","\uD83D\uDCDD","\uD83C\uDF89","\uD83D\uDED1","\uD83D\uDD04","\uD83D\uDCCC","\uD83E\uDD84","\uD83D\uDCA9","\uD83E\uDECF","\uD83D\uDD95" };
            string[] names  = { "Check mark","Kreuz","Ausrufezeichen","Fragezeichen","Stern","Feuer","Daumen hoch","Daumen runter","Light bulb","Warning","Schloss","Herz","Rakete","Zielscheibe","Notiz","Party","Stop","Refresh","Pin","Einhorn","Kacke","Esel","Mittelfinger" };
            for (int i=0;i<stamps.Length;i++) {
                string em=stamps[i]; string nm2=names[i]; bool sel2 = !_st.IsImageStamp && _st.StampEmoji==em;
                ToolStripMenuItem mi = new ToolStripMenuItem(em+"  "+nm2);
                mi.Checked = sel2; mi.ForeColor=Color.White; mi.BackColor=Color.FromArgb(30,30,48);
                mi.Click+=delegate{_st.StampEmoji=em;_st.StampImageKey=null;Pick(DrawTool.Stamp);};
                cm.Items.Add(mi);
            }
        }
        else if (tool == DrawTool.NumberedCircle) {
            // MUP-20260328-170558899-8e36: Numbered circle tool options
            cm.Items.Add(new ToolStripLabel("F\u00FCllung"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            AddRadio(cm, "Nur Rahmen",      !_st.NumCircleFill, ()=>{_st.NumCircleFill=false;});
            AddRadio(cm, "Halbtransparent",  _st.NumCircleFill,  ()=>{_st.NumCircleFill=true;});
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(new ToolStripLabel("Z\u00E4hler"){ ForeColor=Color.FromArgb(255,160,0), Font=new Font("Segoe UI",9f,FontStyle.Bold) });
            ToolStripMenuItem reset = new ToolStripMenuItem("Zur\u00FCcksetzen auf 1");
            reset.ForeColor=Color.White; reset.BackColor=Color.FromArgb(30,30,48);
            reset.Click+=delegate{_st.NextNumCircle=1;_st.Fire();};
            cm.Items.Add(reset);
            // FB-8e36 v3: Inline ToolStripTextBox instead of modal dialog - no overlay blocking
            ToolStripLabel setLbl = new ToolStripLabel("Setzen auf:"){ ForeColor=Color.FromArgb(200,200,220), Font=new Font("Segoe UI",9f) };
            cm.Items.Add(setLbl);
            ToolStripTextBox setTb = new ToolStripTextBox();
            setTb.Text=_st.NextNumCircle.ToString();
            setTb.BackColor=Color.FromArgb(42,42,62); setTb.ForeColor=Color.White;
            setTb.Font=new Font("Segoe UI",10f); setTb.Size=new Size(60,24);
            setTb.ToolTipText="Zahl eingeben + Enter";
            setTb.KeyDown+=(s2,e2)=>{
                if(e2.KeyCode==Keys.Enter){
                    if(int.TryParse(setTb.Text,out int v)&&v>=1&&v<=9999){_st.NextNumCircle=v;_st.Fire();}
                    cm.Close(); e2.Handled=true; e2.SuppressKeyPress=true;
                }
            };
            cm.Items.Add(setTb);
        }

        return cm;
    }

    void AddRadio(ContextMenuStrip cm, string text, bool chk, Action act) {
        ToolStripMenuItem mi = new ToolStripMenuItem(text);
        mi.Checked=chk; mi.ForeColor=Color.White; mi.BackColor=Color.FromArgb(30,30,48);
        mi.Click+=delegate{act();_st.Fire();};
        cm.Items.Add(mi);
    }

    void Mark(DrawTool active){foreach(KeyValuePair<DrawTool,Button> kv in _tbtns){kv.Value.BackColor=(kv.Key==active)?Color.FromArgb(40,110,55):Color.FromArgb(42,42,62);kv.Value.ForeColor=(kv.Key==active)?Color.White:Color.FromArgb(150,150,170);}}
    void HiCol(Button active){foreach(Button b in _cbtns){b.FlatAppearance.BorderSize=(b==active)?2:0;b.FlatAppearance.BorderColor=Color.White;}}
    // MUP-234728 v3: Height 28px + MiddleCenter alignment for HiDPI clarity
    Button TB(string lbl,string tip){Button b=new Button{Text=lbl,Size=new Size(28,28),Margin=new Padding(1),Padding=Padding.Empty,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(42,42,62),ForeColor=Color.FromArgb(150,150,170),Font=new Font("Segoe UI",10f),TextAlign=ContentAlignment.MiddleCenter,Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=0;_tt.SetToolTip(b,tip);Button rb=b;b.MouseEnter+=delegate{if(rb.BackColor.ToArgb()!=Color.FromArgb(40,110,55).ToArgb())rb.BackColor=Color.FromArgb(55,55,80);};b.MouseLeave+=delegate{if(rb.BackColor.ToArgb()!=Color.FromArgb(40,110,55).ToArgb())rb.BackColor=Color.FromArgb(42,42,62);};return b;}
    Button CB(Color c){Button b=new Button{Size=new Size(22,28),Margin=new Padding(1,1,1,1),FlatStyle=FlatStyle.Flat,BackColor=c,Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=0;b.FlatAppearance.BorderColor=Color.White;return b;} // MUP-c9b9: uniform 28px height
    Panel Div(){return new Panel{Size=new Size(1,28),Margin=new Padding(2,2,2,2),BackColor=Color.FromArgb(45,255,255,255)};}
    // MUP-0fd0: Ctrl+Scroll to change brush size
    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) != 0 && _sizeCombo != null) {
            int idx = _sizeCombo.SelectedIndex;
            if (e.Delta > 0 && idx < _sizeCombo.Items.Count - 1) _sizeCombo.SelectedIndex = idx + 1;
            else if (e.Delta < 0 && idx > 0) _sizeCombo.SelectedIndex = idx - 1;
        }
    }
    void DS(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left){_drag=true;_dpt=e.Location;}}
    void DM(object s,MouseEventArgs e){if(_drag){Location=new Point(Location.X+e.X-_dpt.X,Location.Y+e.Y-_dpt.Y);NotifyOverlaysOfBarPos();}}
    void DU(object s,MouseEventArgs e){_drag=false;NotifyOverlaysOfBarPos();}
}

// Dark theme for ContextMenuStrip
class DarkColorTable : ProfessionalColorTable {
    public override Color MenuBorder { get { return Color.FromArgb(60,60,80); } }
    public override Color MenuItemBorder { get { return Color.FromArgb(80,80,110); } }
    public override Color MenuItemSelected { get { return Color.FromArgb(55,55,80); } }
    public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(55,55,80); } }
    public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(55,55,80); } }
    public override Color MenuStripGradientBegin { get { return Color.FromArgb(30,30,48); } }
    public override Color MenuStripGradientEnd { get { return Color.FromArgb(30,30,48); } }
    public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(40,40,60); } }
    public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(40,40,60); } }
    public override Color ToolStripDropDownBackground { get { return Color.FromArgb(30,30,48); } }
    public override Color ImageMarginGradientBegin { get { return Color.FromArgb(30,30,48); } }
    public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(30,30,48); } }
    public override Color ImageMarginGradientEnd { get { return Color.FromArgb(30,30,48); } }
    public override Color SeparatorDark { get { return Color.FromArgb(60,60,80); } }
    public override Color SeparatorLight { get { return Color.FromArgb(45,45,65); } }
    public override Color CheckBackground { get { return Color.FromArgb(40,110,55); } }
    public override Color CheckSelectedBackground { get { return Color.FromArgb(50,130,65); } }
}
