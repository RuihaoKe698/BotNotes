using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Documents;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using D = System.Drawing;

namespace NotchNotesWindows {
class App : Application {
 [STAThread] static void Main() {
  bool first;
  using(var mutex=new System.Threading.Mutex(true, @"Local\DotNotesSingleInstance", out first)) {
   if(!first) { NotchNotesWindows.MainWindow.Notice("DotNotes 已在运行，点屏幕顶部的小圆点或通知区域图标就能打开。"); return; }
   // 旧版 NotchNotes 还开着时不能搬数据文件夹，否则它之后保存的内容会写回旧文件夹
   if(NotchNotesWindows.MainWindow.OldVersionRunning()) { NotchNotesWindows.MainWindow.Notice("旧版 NotchNotes 还在运行。请先在通知区域右键它的图标选“退出”，再打开 DotNotes。笔记会自动搬过来。"); return; }
   NotchNotesWindows.MainWindow.MigrateFromNotchNotes(); NotchNotesWindows.MainWindow.FixAutoStartPath();
   var app = new App(); app.ShutdownMode=ShutdownMode.OnExplicitShutdown; app.Run(new MainWindow());
  }
 }
}

// 颜色角色：三层底色（面板 / 内容区 / 抬起），一种分隔线，两级文字，一组强调色。
static class Palette {
 // 刘海永远是黑色（模拟屏幕上的刘海），上面的字也固定用浅色，不跟背景色走。
 public static readonly Brush Notch=Make("#000000"), NotchText=Make("#ECECEF"), NotchMuted=Make("#9A9DA4");
 // 其余画刷都不冻结：换背景色、强调色时直接改颜色，界面上所有引用同一画刷的地方随之更新。
 public static readonly SolidColorBrush Panel=Live("#0F1013"), Surface=Live("#17181C"), Raised=Live("#25272C"), Line=Live("#2A2C31"), Text=Live("#ECECEF"), Muted=Live("#9A9DA4"), Warn=Live("#F0B48A"), Rule=Live("#3B3E45");
 public static readonly SolidColorBrush Scrim=Live("#D90F1013"), Thumb=Live("#3B3E45"), ThumbHover=Live("#5A5E66"), ThumbDrag=Live("#6C7078");
 public static readonly Color DefaultAccent=Color.FromRgb(0xA6,0xDD,0xB4), DefaultBackground=Color.FromRgb(0x0F,0x10,0x13);
 public static readonly SolidColorBrush Accent=new SolidColorBrush(DefaultAccent), AccentBg=new SolidColorBrush(Color.FromRgb(0x26,0x3A,0x2C));
 public static readonly LinearGradientBrush Edge=new LinearGradientBrush(Color.FromArgb(0x22,255,255,255),Color.FromArgb(0x0C,255,255,255),90);
 public static bool Light; static Color accentChoice=DefaultAccent;
 // 强调色要在当前背景上看得清：深色背景上太暗就逐步混入白色，浅色背景上太亮就逐步混入黑色。
 public static Color Readable(Color c) {
  if(Light) for(int i=0;i<16 && Luminance(c)>0.18;i++) c=Mix(c,Colors.Black,0.15);
  else for(int i=0;i<12 && Luminance(c)<0.33;i++) c=Mix(c,Colors.White,0.15);
  return c;
 }
 static double Channel(byte v) { double x=v/255.0; return x<=0.03928?x/12.92:Math.Pow((x+0.055)/1.055,2.4); }
 public static double Luminance(Color c) { return 0.2126*Channel(c.R)+0.7152*Channel(c.G)+0.0722*Channel(c.B); }
 public static Color Mix(Color a,Color b,double t) { return Color.FromRgb((byte)Math.Round(a.R+(b.R-a.R)*t),(byte)Math.Round(a.G+(b.G-a.G)*t),(byte)Math.Round(a.B+(b.B-a.B)*t)); }
 public static Color SetAccent(Color c) {
  accentChoice=c; c=Readable(c);
  Accent.Color=c; AccentBg.Color=Mix(Panel.Color,c,Light?0.16:0.17);
  return c;
 }
 // 背景色：亮度超过 0.4 按浅色背景处理（深色文字）；太灰的颜色会被推向更深或更浅，保证文字对比度。其余层次都从背景色推出来。
 public static Color SetBackground(Color c) {
  Light=Luminance(c)>0.4;
  if(Light) for(int i=0;i<12 && Luminance(c)<0.72;i++) c=Mix(c,Colors.White,0.15);
  else for(int i=0;i<12 && Luminance(c)>0.03;i++) c=Mix(c,Colors.Black,0.15);
  Color w=Colors.White, k=Colors.Black; Panel.Color=c;
  if(c==DefaultBackground) { Surface.Color=Hex("#17181C"); Raised.Color=Hex("#25272C"); Line.Color=Hex("#2A2C31"); Rule.Color=Hex("#3B3E45"); Thumb.Color=Rule.Color; ThumbHover.Color=Hex("#5A5E66"); ThumbDrag.Color=Hex("#6C7078"); }
  else if(!Light) { Surface.Color=Mix(c,w,0.035); Raised.Color=Mix(c,w,0.097); Line.Color=Mix(c,w,0.118); Rule.Color=Mix(c,w,0.195); Thumb.Color=Rule.Color; ThumbHover.Color=Mix(c,w,0.30); ThumbDrag.Color=Mix(c,w,0.36); }
  else { Surface.Color=Mix(c,w,0.6); Raised.Color=Mix(c,k,0.06); Line.Color=Mix(c,k,0.10); Rule.Color=Mix(c,k,0.2); Thumb.Color=Mix(c,k,0.2); ThumbHover.Color=Mix(c,k,0.3); ThumbDrag.Color=Mix(c,k,0.38); }
  Text.Color=Hex(Light?"#1D1E22":"#ECECEF"); Muted.Color=Hex(Light?"#62656C":"#9A9DA4"); Warn.Color=Hex(Light?"#B4561D":"#F0B48A");
  Scrim.Color=Color.FromArgb(0xD9,c.R,c.G,c.B);
  Edge.GradientStops[0].Color=Light?Color.FromArgb(0x26,0,0,0):Color.FromArgb(0x22,255,255,255); Edge.GradientStops[1].Color=Light?Color.FromArgb(0x14,0,0,0):Color.FromArgb(0x0C,255,255,255);
  SetAccent(accentChoice);
  return c;
 }
 // 脑图分支颜色：从强调色的色相出发，按固定间隔转开，明暗跟着背景走。强调色本身只留给中心主题，分支从 150 度开始。
 public static Color Branch(int i) {
  if(i<0) return Accent.Color;
  double h,s,l; ToHsl(Accent.Color,out h,out s,out l);
  double[] turn={150,215,40,290,95,330,180,250};
  return FromHsl((h+turn[i%turn.Length])%360,Math.Max(0.45,Math.Min(0.75,s)),Light?0.40:0.72);
 }
 static void ToHsl(Color c,out double h,out double s,out double l) {
  double r=c.R/255.0, g=c.G/255.0, b=c.B/255.0, max=Math.Max(r,Math.Max(g,b)), min=Math.Min(r,Math.Min(g,b)), d=max-min;
  l=(max+min)/2; s=d==0?0:d/(1-Math.Abs(2*l-1));
  if(d==0) h=0; else if(max==r) h=60*(((g-b)/d)%6); else if(max==g) h=60*((b-r)/d+2); else h=60*((r-g)/d+4);
  if(h<0) h+=360;
 }
 public static Color FromHsl(double h,double s,double l) {
  double c=(1-Math.Abs(2*l-1))*s, x=c*(1-Math.Abs((h/60)%2-1)), m=l-c/2, r=0,g=0,b=0;
  if(h<60) { r=c; g=x; } else if(h<120) { r=x; g=c; } else if(h<180) { g=c; b=x; } else if(h<240) { g=x; b=c; } else if(h<300) { r=x; b=c; } else { r=c; b=x; }
  return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
 }
 public static string ToHex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}",c.R,c.G,c.B); }
 static Color Hex(string c) { return (Color)ColorConverter.ConvertFromString(c); }
 static SolidColorBrush Live(string c) { return new SolidColorBrush(Hex(c)); }
 public static Brush Make(string c) { var b=(Brush)new BrushConverter().ConvertFromString(c); b.Freeze(); return b; }
}

// 脑图里的一个节点。Key 是从中心主题到它的路径，重画后靠它找回同一个节点。
class MapNode {
 public string Text, Full, Key; public int Depth, Branch, Block=-1, Line=-1, Last=-1, Kind; public bool Heading, Folded; // Kind：1 标题、2 列表、3 文字、4 代码
 public double W, H, X, Y, Sub; public FrameworkElement El; public MapNode Parent;
 public readonly List<MapNode> Kids=new List<MapNode>();
}

class MapFold { public MapNode N; } // 折叠开关和“+N”徽标

class Win32Owner : Forms.IWin32Window { readonly IntPtr h; public Win32Owner(IntPtr h) { this.h=h; } public IntPtr Handle { get { return h; } } }

static class Ui {
 public static readonly FontFamily IconFont=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
 public static readonly FontFamily Mono=new FontFamily("Cascadia Mono, Consolas");
 static Style focusRing;

 public static TextBlock Icon(string glyph,double size) {
  return new TextBlock { Text=glyph, FontFamily=IconFont, FontSize=size, VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Center };
 }
 public static StackPanel IconLabel(string glyph,string text) {
  var row=new StackPanel { Orientation=Orientation.Horizontal };
  var icon=Icon(glyph,13); icon.Margin=new Thickness(0,0,6,0); row.Children.Add(icon);
  row.Children.Add(new TextBlock { Text=text, VerticalAlignment=VerticalAlignment.Center });
  return row;
 }
 // 键盘切换焦点时才显示的绿色描边；鼠标点击不出现。
 static Style FocusRing() {
  if(focusRing!=null) return focusRing;
  var t=new ControlTemplate(); var ring=new FrameworkElementFactory(typeof(Border));
  ring.SetResourceReference(Border.BorderBrushProperty,"AccentBrush"); ring.SetValue(Border.BorderThicknessProperty,new Thickness(1.5)); ring.SetValue(Border.CornerRadiusProperty,new CornerRadius(8)); ring.SetValue(FrameworkElement.MarginProperty,new Thickness(-2));
  t.VisualTree=ring; focusRing=new Style(); focusRing.Setters.Add(new Setter(Control.TemplateProperty,t)); return focusRing;
 }
 public static Button Button(object content,string tip) { return Button(content,tip,new CornerRadius(7)); }
 public static Button Button(object content,string tip,CornerRadius radius) {
  var b=new Button { Content=content, ToolTip=tip, Background=Brushes.Transparent, Foreground=Palette.Text, BorderThickness=new Thickness(0), Padding=new Thickness(10,0,10,0), Height=30, FontSize=12.5, Cursor=Cursors.Hand, FocusVisualStyle=FocusRing(), VerticalAlignment=VerticalAlignment.Center };
  var template=new ControlTemplate(typeof(System.Windows.Controls.Button));
  var grid=new FrameworkElementFactory(typeof(Grid));
  var surface=new FrameworkElementFactory(typeof(Border)); surface.SetValue(Border.CornerRadiusProperty,radius); surface.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
  // 悬停与按下用一层半透明白色叠加，任何底色（含选中的绿色标签）都能看到反馈。
  var overlay=new FrameworkElementFactory(typeof(Border)); overlay.Name="overlay"; overlay.SetValue(Border.CornerRadiusProperty,radius); overlay.SetResourceReference(Border.BackgroundProperty,"HoverBrush"); overlay.SetValue(UIElement.OpacityProperty,0.0); overlay.SetValue(UIElement.IsHitTestVisibleProperty,false); // 叠加层：深色背景上是白色，浅色背景上是黑色
  var content2=new FrameworkElementFactory(typeof(ContentPresenter)); content2.Name="content"; content2.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center); content2.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center); content2.SetValue(ContentPresenter.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));
  grid.AppendChild(surface); grid.AppendChild(overlay); grid.AppendChild(content2); template.VisualTree=grid;
  // 悬停叠加层淡入淡出（110 / 160ms），不再一下子跳变；按下时短暂变浅。
  // 悬停：只有这一个触发器动叠加层（淡入 7% / 淡出 0）。按下：只动内容的不透明度，稍微变淡。
  // 一个属性只交给一个触发器：WPF 里多个触发器的动画抢同一个属性时，排在后面的触发器永远赢，
  // 之前“松开回到 7%”排在后面，压住了“移开回 0”，点过的按钮就一直留着一块灰底。
  var hover=new Trigger { Property=UIElement.IsMouseOverProperty, Value=true }; hover.EnterActions.Add(Fade("overlay",0.07,110)); hover.ExitActions.Add(Fade("overlay",0,160)); template.Triggers.Add(hover);
  var press=new Trigger { Property=System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value=true }; press.EnterActions.Add(Fade("content",0.7,50)); press.ExitActions.Add(Fade("content",1,140)); template.Triggers.Add(press);
  var disabled=new Trigger { Property=UIElement.IsEnabledProperty, Value=false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.4)); template.Triggers.Add(disabled);
  b.Template=template; return b;
 }
 static BeginStoryboard Fade(string target,double to,int ms) {
  var a=new DoubleAnimation(to,TimeSpan.FromMilliseconds(ms)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } };
  Storyboard.SetTargetName(a,target); Storyboard.SetTargetProperty(a,new PropertyPath(UIElement.OpacityProperty));
  var sb=new Storyboard(); sb.Children.Add(a); return new BeginStoryboard { Storyboard=sb };
 }
 // 输入框：圆角、细边框，聚焦时边框变强调色，不用系统默认的蓝框。
 public static TextBox Input(string text) {
  var t=new TextBox { Text=text, Background=Palette.Raised, Foreground=Palette.Text, CaretBrush=Palette.Text, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), Padding=new Thickness(8,5,8,5), FontSize=13, SelectionBrush=Palette.Accent, FocusVisualStyle=null };
  var tpl=new ControlTemplate(typeof(TextBox)); var bd=new FrameworkElementFactory(typeof(Border)); bd.Name="bd";
  bd.SetValue(Border.CornerRadiusProperty,new CornerRadius(7)); bd.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty)); bd.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty)); bd.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
  var host=new FrameworkElementFactory(typeof(ScrollViewer)); host.Name="PART_ContentHost"; host.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty)); host.SetValue(Control.PaddingProperty,new Thickness(0)); bd.AppendChild(host); tpl.VisualTree=bd;
  var focus=new Trigger { Property=UIElement.IsKeyboardFocusWithinProperty, Value=true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension("AccentBrush"),"bd")); tpl.Triggers.Add(focus); // 引用资源，不把会换色的画刷封进模板
  t.Template=tpl; return t;
 }
 public static void HorizontalWheel(ScrollViewer sv) {
  sv.PreviewMouseWheel+=(s,e)=> { if(sv.ScrollableWidth<=0) return; sv.ScrollToHorizontalOffset(sv.HorizontalOffset-e.Delta/2.0); e.Handled=true; };
 }
}

// 托盘右键菜单：WinForms 默认菜单是灰底蓝高亮，换成和面板同色的深色菜单；Windows 11 上再请系统画圆角。
class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer {
 public D.Color Back=D.Color.FromArgb(15,16,19), Hover=D.Color.FromArgb(37,39,44), Text=D.Color.FromArgb(236,236,239), Line=D.Color.FromArgb(59,62,69);
 public TrayMenuRenderer() : base(new Forms.ProfessionalColorTable()) { RoundedEdges=false; }
 protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) { using(var b=new D.SolidBrush(Back)) e.Graphics.FillRectangle(b,e.AffectedBounds); }
 protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e) { using(var p=new D.Pen(Line)) e.Graphics.DrawRectangle(p,0,0,e.AffectedBounds.Width-1,e.AffectedBounds.Height-1); }
 protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) {}
 protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e) {
  if(!e.Item.Selected || !e.Item.Enabled) return;
  e.Graphics.SmoothingMode=D.Drawing2D.SmoothingMode.AntiAlias;
  var r=new D.RectangleF(4,1,e.Item.Width-8,e.Item.Height-2); float d=8;
  using(var p=new D.Drawing2D.GraphicsPath()) { p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); using(var b=new D.SolidBrush(Hover)) e.Graphics.FillPath(b,p); }
 }
 protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e) { e.TextColor=Text; base.OnRenderItemText(e); }
 protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e) { int y=e.Item.Height/2; using(var p=new D.Pen(Line)) e.Graphics.DrawLine(p,10,y,e.Item.Width-10,y); }
}

class MainWindow : Window {
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int attr,ref int value,int size);
 static void RoundCorners(IntPtr h) { try { int round=3; DwmSetWindowAttribute(h,33,ref round,4); } catch {} } // DWMWA_WINDOW_CORNER_PREFERENCE = 小圆角；Windows 10 上没有，忽略
 static D.Color Gdi(Color c) { return D.Color.FromArgb(c.R,c.G,c.B); }
 [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
 [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
 [StructLayout(LayoutKind.Sequential)] struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
 static IntPtr WndHook(IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
  if(msg==0x0024) { // WM_GETMINMAXINFO
   var mm=(MINMAXINFO)Marshal.PtrToStructure(lp,typeof(MINMAXINFO)); var vs=Forms.SystemInformation.VirtualScreen;
   mm.ptMaxTrackSize.X=Math.Max(mm.ptMaxTrackSize.X,vs.Width+400); mm.ptMaxTrackSize.Y=Math.Max(mm.ptMaxTrackSize.Y,vs.Height+400);
   Marshal.StructureToPtr(mm,lp,false); // 不标记已处理：WPF 自己也按这个上限排版，要让它读到放宽后的值
  }
  return IntPtr.Zero;
 }
 [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
 const uint ES_CONTINUOUS=0x80000000, ES_SYSTEM_REQUIRED=1;
 const string G_Note="\uE70B", G_Close="\uE8BB", G_Add="\uE710", G_Minus="\uE738", G_Archive="\uE7B8", G_Sun="\uE706", G_File="\uE8A5", G_Folder="\uE8B7", G_Image="\uEB9F", G_Settings="\uE713", G_Pin="\uE718", G_Open="\uE8E5";
 const int K_Line=0, K_Blank=1, K_Code=2;
 const double BodySize=14.5, BodyLine=24;
 readonly string dir=DataDir();
 // 改名为 DotNotes 后数据放在 %LOCALAPPDATA%\DotNotes；第一次启动时把旧的 NotchNotesWindows 整个改名过来。改名失败（比如文件被占用）就继续用旧文件夹，笔记不会看起来“丢了”。
 internal static string LocalRoot=null, OldRunName="NotchNotes"; // 测试时换成临时位置和临时名字
 static string AppData(string name) { return Path.Combine(LocalRoot??Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),name); }
 static string DataDir() { string now=AppData("DotNotes"), old=AppData("NotchNotesWindows"); bool fresh=!Directory.Exists(now); try { if(!fresh) fresh=Directory.GetFileSystemEntries(now).Length==0; } catch {} return fresh && Directory.Exists(old)?old:now; }
 internal static bool OldVersionRunning() { try { using(System.Threading.Mutex.OpenExisting(@"Local\NotchNotesWindowsSingleInstance")) return true; } catch { return false; } }
 internal static void MigrateFromNotchNotes() {
  string now=AppData("DotNotes"), old=AppData("NotchNotesWindows");
  try {
   if(Directory.Exists(old) && Directory.Exists(now) && Directory.GetFileSystemEntries(now).Length==0) Directory.Delete(now); // 新文件夹是空的（没真正用过）就当不存在
   if(Directory.Exists(old) && !Directory.Exists(now)) Directory.Move(old,now);
  } catch {}
  // 开机启动项：旧名字的那条删掉；原来是开着的（没在任务管理器里禁用）就用新名字重新登记
  try {
   using(var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey,true)) {
    if(k==null || k.GetValue(OldRunName)==null) return;
    bool disabled=false;
    using(var a=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKey,true)) if(a!=null) { var v=a.GetValue(OldRunName) as byte[]; disabled=v!=null && v.Length>0 && (v[0]&1)==1; a.DeleteValue(OldRunName,false); }
    k.DeleteValue(OldRunName,false); if(!disabled) k.SetValue(RunName,RunCommand());
   }
  } catch {}
 }
 readonly List<string> files=new List<string>();
 readonly List<string> noteNames=new List<string>();
 TextBox editor; TextBlock saveStatus; System.Windows.Shapes.Ellipse statusDot; StackPanel shelf, toolbar, tabs; ScrollViewer tabsScroll; string currentNote="note.md", currentTitle="";
 Button editTab, previewTab, settingsButton; bool previewing=false; Border body, outerBox, shelfBorder; Button notch, sleepButton; Forms.NotifyIcon tray;
 bool expanded=false, awake=false, loading=true, quitting=false, noteReadable=true, shelfReadable=true;
 bool transient=false, dragInside=false, draggingOut=false; int modal=0;
 DispatcherTimer timer, hoverTimer, leaveTimer; Point dragStart;
 // 预览：按行渲染，点哪行就把哪行换成原文输入框。
 ScrollViewer previewScroll; StackPanel previewBlocks; readonly List<string> lines=new List<string>(); readonly List<int[]> blocks=new List<int[]>();
 TextBox lineBox; int editingBlock=-1, editStart=0, editCount=0, editOrigCount=0; bool rendering=false; string eol="\n";
 // 设置
 const double ShadowPad=24, Ear=8, NotchW=168;
 Grid dotLayer, dotBody; Border dotHit; System.Windows.Shapes.Ellipse dot; MatrixTransform dotWarp;
 // 位置：面板左上角（屏幕坐标）和小圆点在面板里的位置。面板上沿为 0 时贴在屏幕顶边，显示成刘海。
 const double Snap=16; double DotTop { get { return 4+dotSize/2.0; } }
 double pw=520, ph=590, panelLeft=double.NaN, panelTop=0, dotX=double.NaN, dotY=8;
 bool dragArmed=false, dragMoved=false, dragDot=false; Point dragFrom, dragOrigin; Grid shadowLayer;
 // 大小：用户拖边框定下的面板尺寸。屏幕放不下时临时缩小显示，记下的值不变。
 const double MinW=460, MinH=360, DefW=520, DefH=590, Edge=6, Corner=22;
 double prefW=DefW, prefH=DefH; bool sizeArmed=false, sizeSym=false; int sizeX=0, sizeY=0; Point sizeFrom; Rect sizeOrigin;
 // 标题折叠：每篇笔记记下折叠了哪些标题（级别 + 文字 + 第几个同名）。
 static readonly Regex HeadingRe=new Regex(@"^(#{1,6})\s+(.+)$");
 static readonly double[] HeadSize={21,18,16,15,14.5,14}, HeadLine={30,27,25,24,24,22};
 readonly Dictionary<string,HashSet<string>> folds=new Dictionary<string,HashSet<string>>();
 int[] headLevel=new int[0], foldEnd=new int[0], foldCount=new int[0]; string[] foldKey=new string[0]; bool[] blockHidden=new bool[0];
 RectangleGeometry panelClip; int animGen=0;
 bool pinned=false; Button pinButton; System.Windows.Shapes.Path notchShape; Border shadowNear, shadowFar; Grid outerGrid; System.Windows.Shapes.Rectangle dropZone; TextBlock shelfCount;
 int hoverDelay=600; bool hoverOn=true; string archiveDir=null; bool settingsOpen=false;
 Border closeView; TextBlock closeTitle; Button closeArchiveBtn; string closingNote; bool closeOpen=false;
 Color background=Palette.DefaultBackground; readonly List<KeyValuePair<Color,Border>> bgRings=new List<KeyValuePair<Color,Border>>(); Button customBgBtn;
 static readonly string[][] BgPresets={ new[]{"#0F1013","曜石黑"}, new[]{"#1C1D21","石墨灰"}, new[]{"#0E1522","午夜蓝"}, new[]{"#0E1A15","墨绿"}, new[]{"#1A1411","暖咖"}, new[]{"#F4F3EF","纸白（浅色）"} };
 Color accent=Palette.DefaultAccent; readonly List<KeyValuePair<Color,System.Windows.Shapes.Ellipse>> swatchRings=new List<KeyValuePair<Color,System.Windows.Shapes.Ellipse>>(); Button customColorBtn;
 static readonly string[][] Presets={ new[]{"#A6DDB4","薄荷绿"}, new[]{"#9CC7F5","天蓝"}, new[]{"#C3B4F0","薰衣草"}, new[]{"#F2C28C","杏橙"}, new[]{"#F2A9C4","樱粉"}, new[]{"#8ED8D2","青碧"} };
 Button autoOnBtn, autoOffBtn; Button[] breatheBtns=new Button[4]; int breatheRate=2, dotSize=8; Slider dotSizeSlider; TextBlock dotSizeText; System.Windows.Shapes.Ellipse dotPreview;
 // 全屏：面板铺满主显示器，方便演示；退出时回到原来的位置和大小。
 bool fullscreen=false; double[] beforeFull; Button fsButton;
 // 脑图：把标题和列表画成从左往右展开的树。
 Button mapTab; bool mapping=false, mapFromPreview=false, mapMoved=false, mapArmed=false, mapPanned=false; Grid mapView; Canvas mapCanvas; StackPanel mapEmpty; StackPanel mapBar;
 Button mapFitBtn, mapCopyBtn, mapSaveBtn, presentFit, presentBtn; Border presentBar; TextBlock mapHint;
 // 演示使用独立状态，退出后恢复原来的窗口全屏状态和作者界面。
 DockPanel headerRow, tabRowPanel; Border editCard, noteBar; StackPanel shelfCaption; TextBlock bgName, accentName;
 bool presenting=false, presentationWasFull=false; int presentCount=0; TextBlock presentProgress; readonly List<MapNode> presentNodes=new List<MapNode>(); readonly Dictionary<MapNode,UIElement> presentLines=new Dictionary<MapNode,UIElement>();
 bool Presenting { get { return presenting && fullscreen && mapping; } }
 ScaleTransform mapScale=new ScaleTransform(1,1); TranslateTransform mapPan=new TranslateTransform(); Point mapFrom, mapPanFrom; MapNode mapRoot, mapHit; readonly HashSet<string> mapFolds=new HashSet<string>();
 static readonly string G_Full=((char)0xE740).ToString(), G_Unfull=((char)0xE73F).ToString(), G_Map=((char)0xF158).ToString(), G_Fit=((char)0xE9A6).ToString(), G_Export=((char)0xE896).ToString(), G_Edit=((char)0xE70F).ToString();
 // 标签顺序：用户拖过之后记在 order.txt；没记过的笔记（比如新建的）排在最后。
 readonly List<string> tabOrder=new List<string>(); string tabDragName; bool tabDragMoved; double tabGrab;
 // 预览里的撤销 / 重做：每一步记下整篇原文。同一行连续打字（停顿不到 1.5 秒）算一步。
 readonly List<string> undoTexts=new List<string>(), redoTexts=new List<string>(); object histGroup; DateTime histAt; string leftPreviewText; static readonly object NoUndo=new object();
 // 预览里的选择：按住拖过几行就选中这些行，可以复制成纯文字或 Markdown 原文。
 Border previewPage; int selFrom=-1, selTo=-1, pressLine=-1, pressCaret=-1, pressAnchor=-1; bool pressArmed=false, selecting=false; Point pressAt;
 StackPanel selBar; Border selPill; TextBlock selInfo; static readonly string G_Copy=((char)0xE8C8).ToString(), G_Cut=((char)0xE8C6).ToString(), G_Delete=((char)0xE74D).ToString();
 TextBlock shelfHint; Border settingsView; Button hoverOnBtn, hoverOffBtn, minusBtn, plusBtn, resetArchiveBtn, resetPlaceBtn, resetSizeBtn; TextBlock hoverValue, archivePathText;

 public MainWindow() {
  Directory.CreateDirectory(dir);
  LoadSettings();
  Palette.SetBackground(background); Palette.SetAccent(accent);
  // 只放窗口级资源：应用级资源里的画刷会被冻结，之后无法换色
  ThemeResources();
  Title="DotNotes"; WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent; ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; Topmost=true; Width=184; Height=34; FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
  Left=(SystemParameters.PrimaryScreenWidth-Width)/2; Top=0;
  Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar),DarkScrollBar()); Resources.Add(typeof(ToolTip),TipStyle());
  var outer=new Grid(); Content=outer; outerGrid=outer;
  // 两层阴影：一层贴近、较实，一层远而淡，让面板从桌面上浮起来。阴影放在单独的底板上，不影响文字渲染。
  // 阴影层四周伸进留白；贴顶时裁掉屏幕顶边以上的部分，免得上方还有显示器时露出一道阴影。
  shadowLayer=new Grid { Margin=new Thickness(-ShadowPad) }; outer.Children.Add(shadowLayer);
  shadowFar=new Border { Background=Palette.Panel, CornerRadius=new CornerRadius(0,0,18,18), HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top, Visibility=Visibility.Collapsed, Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=28, ShadowDepth=8, Direction=270, Opacity=0.55, Color=Colors.Black } }; shadowLayer.Children.Add(shadowFar);
  shadowNear=new Border { Background=Palette.Panel, CornerRadius=new CornerRadius(0,0,18,18), HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top, Visibility=Visibility.Collapsed, Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=5, ShadowDepth=1.5, Direction=270, Opacity=0.5, Color=Colors.Black } }; shadowLayer.Children.Add(shadowNear);
  outerBox=new Border(); outer.Children.Add(outerBox);
  var root=new Grid(); outerBox.Child=root;
  root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(34) }); root.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });

  // 顶部入口：一块真正的“刘海”——两侧用内凹弧接到屏幕上边缘，下方圆角。收起和展开是同一个形状。
  var notchLabel=Ui.IconLabel(G_Note,"DotNotes"); ((TextBlock)notchLabel.Children[0]).FontSize=12; ((TextBlock)notchLabel.Children[0]).Foreground=Palette.NotchMuted;
  notch=Ui.Button(notchLabel,"点击收起；按住拖动可移动面板",new CornerRadius(0,0,12,12)); notch.Foreground=Palette.NotchText; notch.FocusVisualStyle=null; notch.Height=34; notch.FontSize=12; notch.VerticalAlignment=VerticalAlignment.Top;
  hoverTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(Math.Max(1,hoverDelay)) };
  hoverTimer.Tick+=(s,e)=> { hoverTimer.Stop(); if(!expanded && hoverOn && dotHit.IsMouseOver) Expand(true); };
  notch.Click+=(s,e)=> { hoverTimer.Stop(); if(expanded) Collapse(); else Expand(false); };
  notchShape=new System.Windows.Shapes.Path { Fill=Palette.Notch, Height=34, VerticalAlignment=VerticalAlignment.Top, HorizontalAlignment=HorizontalAlignment.Center, IsHitTestVisible=false };
  notchShape.SizeChanged+=(s,e)=> { notchShape.Data=NotchGeometry(notchShape.ActualWidth,34,Ear,12); };
  // 刘海属于面板，随面板一起从小圆点处展开、收回。刘海这一行的空白处和刘海本身都能按住拖动面板。
  var grip=new Border { Background=Brushes.Transparent, VerticalAlignment=VerticalAlignment.Stretch }; Draggable(grip,false,false,null);
  root.Children.Add(grip); root.Children.Add(notchShape); root.Children.Add(notch); Draggable(notch,false,true,null);
  // 收起后只留一个主题色的实心小圆点；周围一块看不见的感应区负责悬停、点击、拖动和拖入（Alpha=1，肉眼不可见但能接住鼠标）。
  dotLayer=new Grid { HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top };
  dotHit=new Border { Width=72, Height=16, Background=new SolidColorBrush(Color.FromArgb(1,0,0,0)), Cursor=Cursors.Hand, ToolTip="DotNotes：点击或停留展开；按住拖动可移动" }; ToolTipService.SetInitialShowDelay(dotHit,1200);
  // 圆点内部用同一把主题色画刷；拖动变形以圆心为原点。
  dotBody=new Grid { Width=dotSize, Height=dotSize, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Top, Margin=new Thickness(0,4,0,0), IsHitTestVisible=false, RenderTransformOrigin=new Point(0.5,0.5), RenderTransform=dotWarp=new MatrixTransform() };
  dot=new System.Windows.Shapes.Ellipse { Fill=Palette.Accent }; dotBody.Children.Add(dot);
  dotLayer.Children.Add(dotHit); dotLayer.Children.Add(dotBody); outerGrid.Children.Add(dotLayer);
  dotHit.MouseEnter+=(s,e)=> { if(expanded || !hoverOn || dragArmed) return; if(hoverDelay<=0) Expand(true); else { hoverTimer.Interval=TimeSpan.FromMilliseconds(hoverDelay); hoverTimer.Start(); } };
  dotHit.MouseLeave+=(s,e)=>hoverTimer.Stop();
  Draggable(dotHit,true,false,()=> { if(!expanded) Expand(false); });
  panelClip=new RectangleGeometry(Rect.Empty,18,18); outerBox.Clip=panelClip;

  body=new Border(); Grid.SetRow(body,1); root.Children.Add(body);
  var panel=new Grid(); body.Child=panel;
  panel.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
  panel.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
  panel.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
  panel.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
  panel.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });

  // 第一行：编辑 / 预览切换，右侧是设置、保持唤醒和收起。
  var header=headerRow=new DockPanel { LastChildFill=false, Margin=new Thickness(0,10,0,10), Background=Brushes.Transparent }; panel.Children.Add(header); Draggable(header,false,false,null);
  var close=Ui.Button(Ui.Icon(G_Close,10),"收起 (Esc)"); close.Width=30; close.Padding=new Thickness(0); close.Foreground=Palette.Muted; close.Click+=(s,e)=>Collapse(); DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
  pinButton=Ui.Button(Ui.Icon(G_Pin,14),""); pinButton.Width=30; pinButton.Padding=new Thickness(0); pinButton.Margin=new Thickness(0,0,2,0); pinButton.Click+=(s,e)=> { pinned=!pinned; if(pinned) transient=false; SaveSettings(); ShowPinState(); }; DockPanel.SetDock(pinButton,Dock.Right); header.Children.Add(pinButton); ShowPinState();
  settingsButton=Ui.Button(Ui.Icon(G_Settings,14),"设置"); settingsButton.Width=30; settingsButton.Padding=new Thickness(0); settingsButton.Margin=new Thickness(0,0,2,0); settingsButton.Foreground=Palette.Muted; settingsButton.Click+=(s,e)=> { if(settingsOpen) CloseSettings(); else OpenSettings(); }; DockPanel.SetDock(settingsButton,Dock.Right); header.Children.Add(settingsButton);
  fsButton=Ui.Button(Ui.Icon(G_Full,13),""); fsButton.Width=30; fsButton.Padding=new Thickness(0); fsButton.Margin=new Thickness(0,0,2,0); fsButton.Foreground=Palette.Muted; fsButton.Click+=(s,e)=>SetFullscreen(!fullscreen); DockPanel.SetDock(fsButton,Dock.Right); header.Children.Add(fsButton); ShowFullscreenState();
  sleepButton=Ui.Button(null,""); sleepButton.Margin=new Thickness(0,0,4,0); sleepButton.Click+=(s,e)=>ToggleSleep(); DockPanel.SetDock(sleepButton,Dock.Right); header.Children.Add(sleepButton); ShowSleepState();
  var seg=new Border { Background=Palette.Surface, CornerRadius=new CornerRadius(8), Padding=new Thickness(2) };
  var segRow=new StackPanel { Orientation=Orientation.Horizontal }; seg.Child=segRow;
  editTab=Ui.Button("编辑","编辑 Markdown 源码 (Ctrl+E)",new CornerRadius(6)); editTab.Height=26; editTab.Padding=new Thickness(14,0,14,0); editTab.Click+=(s,e)=> { CloseSettings(); SetPreview(false); }; segRow.Children.Add(editTab);
  previewTab=Ui.Button("预览","查看排版效果，点任意一行即可修改 (Ctrl+E)",new CornerRadius(6)); previewTab.Height=26; previewTab.Padding=new Thickness(14,0,14,0); previewTab.Click+=(s,e)=> { CloseSettings(); SetPreview(true); }; segRow.Children.Add(previewTab);
  mapTab=Ui.Button("脑图","把标题和列表画成脑图，方便演示和讲解 (Ctrl+M)",new CornerRadius(6)); mapTab.Height=26; mapTab.Padding=new Thickness(14,0,14,0); mapTab.Click+=(s,e)=> { CloseSettings(); SetMap(); }; segRow.Children.Add(mapTab);
  DockPanel.SetDock(seg,Dock.Left); header.Children.Add(seg);

  // 第二行：笔记标签可以横向滚动；新建、打开固定在最右边，标签再多也不会被挤出去。归档在关闭笔记时选。
  var tabRow=tabRowPanel=new DockPanel { Margin=new Thickness(0,0,0,10) }; Grid.SetRow(tabRow,1); panel.Children.Add(tabRow);
  var actions=new StackPanel { Orientation=Orientation.Horizontal }; DockPanel.SetDock(actions,Dock.Right); tabRow.Children.Add(actions);
  // 标签放不下时才出现分隔线，说明左边还能滚动。
  tabsEdge=new Border { Width=1, Height=16, Background=Palette.Line, Margin=new Thickness(4,0,4,0), VerticalAlignment=VerticalAlignment.Center, Visibility=Visibility.Collapsed }; actions.Children.Add(tabsEdge);
  var add=tabAdd=Ui.Button(Ui.Icon(G_Add,12),"新建笔记 (Ctrl+N)"); add.Height=28; add.Width=30; add.Padding=new Thickness(0); add.Foreground=Palette.Muted; add.Click+=(s,e)=> { CloseSettings(); NewNote(); }; actions.Children.Add(add);
  var open=tabOpen=Ui.Button(Ui.IconLabel(G_Open,"打开"),"打开文件（Ctrl+O）：从归档文件夹打开即取回这篇笔记；其他位置的文件只复制进来，原文件不动"); open.Height=28; open.Foreground=Palette.Muted; open.Margin=new Thickness(2,0,0,0); open.Click+=(s,e)=> { CloseSettings(); OpenFiles(); }; actions.Children.Add(open);
  // 标签放不下时两头出现 ‹ ›，点一下翻大半屏；被遮住的一侧边缘渐隐，提示那边还有。
  tabNext=Ui.Button(Ui.Icon("",10),"向右翻（也可以在标签上滚动滚轮）"); tabNext.Width=22; tabNext.Height=28; tabNext.Padding=new Thickness(0); tabNext.Foreground=Palette.Muted; tabNext.Focusable=false; tabNext.Visibility=Visibility.Collapsed; tabNext.Click+=(s,e)=>PageTabs(1); DockPanel.SetDock(tabNext,Dock.Right); tabRow.Children.Add(tabNext);
  tabPrev=Ui.Button(Ui.Icon("",10),"向左翻（也可以在标签上滚动滚轮）"); tabPrev.Width=22; tabPrev.Height=28; tabPrev.Padding=new Thickness(0); tabPrev.Foreground=Palette.Muted; tabPrev.Focusable=false; tabPrev.Visibility=Visibility.Collapsed; tabPrev.Margin=new Thickness(0,0,2,0); tabPrev.Click+=(s,e)=>PageTabs(-1); DockPanel.SetDock(tabPrev,Dock.Left); tabRow.Children.Add(tabPrev);
  tabsScroll=new ScrollViewer { HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden, VerticalScrollBarVisibility=ScrollBarVisibility.Disabled }; Ui.HorizontalWheel(tabsScroll); tabRow.Children.Add(tabsScroll);
  tabsScroll.ScrollChanged+=(s,e)=>UpdateTabEdges(); tabsScroll.SizeChanged+=(s,e)=>UpdateTabEdges(); tabRow.SizeChanged+=(s,e)=>UpdateTabEdges();
  tabs=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Left }; tabsScroll.Content=tabs; tabs.SizeChanged+=(s,e)=>UpdateTabEdges();

  // 编辑区：正文、格式按钮和保存状态放在同一张卡片里。
  var editBorder=editCard=new Border { BorderBrush=Palette.Line, BorderThickness=new Thickness(0), CornerRadius=new CornerRadius(0), Background=Brushes.Transparent, ClipToBounds=true }; Grid.SetRow(editBorder,2); panel.Children.Add(editBorder);
  var editGrid=new Grid(); editGrid.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) }); editGrid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); editBorder.Child=editGrid;
  editor=new TextBox { Background=Brushes.Transparent, Foreground=Palette.Text, BorderThickness=new Thickness(0), FontSize=15, Padding=new Thickness(10,6,10,10), AcceptsReturn=true, AcceptsTab=true, TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, CaretBrush=Palette.Text, SelectionBrush=Palette.Accent, SelectionOpacity=0.35, FocusVisualStyle=null };
  editor.SetValue(TextBlock.LineHeightProperty,25.0);
  editGrid.Children.Add(editor);
  previewBlocks=new StackPanel();
  previewScroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Background=Brushes.Transparent, Visibility=Visibility.Collapsed, FocusVisualStyle=null };
  previewPage=new Border { Background=Brushes.Transparent, Padding=new Thickness(12,6,12,16), Child=previewBlocks, Cursor=Cursors.IBeam };
  previewPage.SetBinding(FrameworkElement.MinHeightProperty,new Binding("ViewportHeight") { Source=previewScroll });
  previewPage.MouseLeftButtonDown+=(s,e)=> { Press(-2,0,e); e.Handled=true; }; // 点在最后一行下面：在末尾接着写
  previewPage.MouseMove+=(s,e)=>PressMove(e);
  previewPage.MouseLeftButtonUp+=(s,e)=>PressUp(e);
  previewPage.LostMouseCapture+=(s,e)=> { pressArmed=false; selecting=false; };
  previewScroll.Content=previewPage; editGrid.Children.Add(previewScroll);
  BuildMapView(editGrid);
  editor.TextChanged+=(s,e)=> { if(!loading) { SetStatus("正在保存…",Palette.Muted); timer.Stop(); timer.Start(); } };
  var bar=new DockPanel { Margin=new Thickness(2,4,2,4) }; var barBorder=noteBar=new Border { BorderBrush=Palette.Line, BorderThickness=new Thickness(0,1,0,0), Child=bar, MinHeight=38 }; Grid.SetRow(barBorder,1); editGrid.Children.Add(barBorder);
  var status=new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(12,0,0,0) }; DockPanel.SetDock(status,Dock.Left); headerRow.Children.Add(status);
  statusDot=new System.Windows.Shapes.Ellipse { Width=6, Height=6, Fill=Palette.Accent, Margin=new Thickness(0,1,7,0), VerticalAlignment=VerticalAlignment.Center }; status.Children.Add(statusDot);
  saveStatus=new TextBlock { Text="已自动保存", Foreground=Palette.Muted, FontSize=11.5, VerticalAlignment=VerticalAlignment.Center }; status.Children.Add(saveStatus);
  var tools=new Grid(); bar.Children.Add(tools);
  toolbar=new StackPanel { Orientation=Orientation.Horizontal }; tools.Children.Add(toolbar);
  // 预览里选中几行后，正文下方浮出一条操作条：复制、复制 Markdown、剪切、删除。
  // 浮在正文上面、不占布局：以前放在底栏里，底栏一出现正文就变矮，鼠标一下子落到了正文下边外，自动滚动把后面的行全选上了。
  selBar=new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center };
  selPill=new Border { Child=selBar, Background=Palette.Raised, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(10), Padding=new Thickness(4), HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Bottom, Margin=new Thickness(12,0,12,14), Visibility=Visibility.Collapsed,
   Effect=new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth=4, BlurRadius=18, Opacity=0.35, Color=Colors.Black, Direction=270 } };
  editGrid.Children.Add(selPill); Panel.SetZIndex(selPill,5);
  selInfo=new TextBlock { Foreground=Palette.Muted, FontSize=12, Margin=new Thickness(8,0,8,0), VerticalAlignment=VerticalAlignment.Center }; selBar.Children.Add(selInfo);
  Func<object,string,Action,Button> selAct=(content,tip,act)=> { var b=Ui.Button(content,tip); b.Height=28; b.Focusable=false; b.Foreground=Palette.Text; b.Margin=new Thickness(2,0,0,0); b.Click+=(s,e)=>act(); selBar.Children.Add(b); return b; };
  selAct(Ui.IconLabel(G_Copy,"复制"),"复制选中的内容，去掉 Markdown 符号（Ctrl+C）",()=>CopySelection(false));
  selAct("复制 Markdown","复制选中部分的 Markdown 原文（Ctrl+Shift+C）",()=>CopySelection(true)).Foreground=Palette.Muted;
  selAct(Ui.IconLabel(G_Cut,"剪切"),"剪切（Ctrl+X）：复制 Markdown 原文，再删掉这几行",CutSelection);
  selAct(Ui.IconLabel(G_Delete,"删除"),"删除选中的行（Delete），按 Ctrl+Z 可以撤销",DeleteSelection);
  // 脑图模式下，这里放适应窗口、复制图片、导出图片。
  mapBar=new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center, Visibility=Visibility.Collapsed }; tools.Children.Add(mapBar);
  // 三个按钮同一种写法：图标 + 文字，次要色。
  mapFitBtn=Ui.Button(Ui.IconLabel(G_Fit,"适应窗口"),"缩放到合适大小并居中 (Ctrl+0)"); mapFitBtn.Height=28; mapFitBtn.Focusable=false; mapFitBtn.Foreground=Palette.Muted; mapFitBtn.Click+=(s,e)=> { mapMoved=false; FitMap(); }; mapBar.Children.Add(mapFitBtn);
  mapCopyBtn=Ui.Button(Ui.IconLabel(G_Copy,"复制图片"),"把整张脑图复制成图片，可以直接粘贴到聊天、文档或幻灯片里"); mapCopyBtn.Height=28; mapCopyBtn.Focusable=false; mapCopyBtn.Foreground=Palette.Muted; mapCopyBtn.Margin=new Thickness(2,0,0,0); mapCopyBtn.Click+=(s,e)=>CopyMapImage(); mapBar.Children.Add(mapCopyBtn);
  mapSaveBtn=Ui.Button(Ui.IconLabel(G_Export,"导出图片…"),"把整张脑图保存成 PNG 图片"); mapSaveBtn.Height=28; mapSaveBtn.Focusable=false; mapSaveBtn.Foreground=Palette.Muted; mapSaveBtn.Margin=new Thickness(2,0,0,0); mapSaveBtn.Click+=(s,e)=>ExportMap(); mapBar.Children.Add(mapSaveBtn);
  { var head=new StackPanel { Orientation=Orientation.Horizontal }; head.Children.Add(new TextBlock { Text="H", FontSize=14.5, FontWeight=FontWeights.SemiBold, VerticalAlignment=VerticalAlignment.Center });
    var chev=Ui.Icon("\uE70D",7); chev.Margin=new Thickness(3,2,0,0); chev.VerticalAlignment=VerticalAlignment.Center; head.Children.Add(chev);
    var hb=Ui.Button(head,"标题：一级 Ctrl+1 · 二级 Ctrl+2 · 三级 Ctrl+3 · 正文 Ctrl+0"); hb.Width=38; hb.Padding=new Thickness(0); hb.Foreground=Palette.Muted; hb.Margin=new Thickness(0,0,2,0); hb.Focusable=false; hb.Click+=(s,e)=>ShowHeadMenu(hb); toolbar.Children.Add(hb); }
  AddTool(Ui.Icon("\uE8DD",14),"粗体 (Ctrl+B)","**","**",null); AddTool(Ui.Icon("\uE8DB",14),"斜体 (Ctrl+I)","*","*",null); AddTool(Ui.Icon("\uEDE0",14),"删除线","~~","~~",null); AddTool(Ui.Icon("\uE943",14),"行内代码","`","`",null); AddTool(Ui.Icon("\uE71B",14),"链接","[","](https://)",null);
  toolbar.Children.Add(new Border { Width=1, Height=16, Background=Palette.Line, Margin=new Thickness(5,0,7,0), VerticalAlignment=VerticalAlignment.Center });
  AddTool(Ui.Icon("\uE9B2",13),"引用（对选中的每一行生效）",null,null,"quote"); AddTool(Ui.Icon("\uE8FD",14),"无序列表（对选中的每一行生效）",null,null,"bullet"); AddTool(NumberedIcon(),"有序列表（对选中的每一行生效）",null,null,"number"); AddTool(Ui.Icon("\uE9D5",14),"待办事项（对选中的每一行生效）",null,null,"task");

  // 文件暂存
  var caption=shelfCaption=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(2,16,2,8) }; Grid.SetRow(caption,3); panel.Children.Add(caption);
  caption.Children.Add(new TextBlock { Text="文件暂存", Foreground=Palette.Muted, FontSize=12, FontWeight=FontWeights.SemiBold });
  shelfCount=new TextBlock { Foreground=Palette.Muted, Opacity=0.75, FontSize=12, Margin=new Thickness(6,0,0,0) }; caption.Children.Add(shelfCount);
  shelfBorder=new Border { Background=Brushes.Transparent, MinHeight=40, AllowDrop=true }; Grid.SetRow(shelfBorder,4); panel.Children.Add(shelfBorder);
  var shelfGrid=new Grid(); shelfBorder.Child=shelfGrid;
  dropZone=new System.Windows.Shapes.Rectangle { RadiusX=9, RadiusY=9, Stroke=Palette.Rule, StrokeThickness=1, StrokeDashArray=new DoubleCollection(new double[]{4,3}), Fill=Brushes.Transparent, SnapsToDevicePixels=true };
  shelfGrid.Children.Add(dropZone);
  var scroll=new ScrollViewer { HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden, VerticalScrollBarVisibility=ScrollBarVisibility.Disabled, VerticalAlignment=VerticalAlignment.Center }; Ui.HorizontalWheel(scroll);
  shelf=new StackPanel { Orientation=Orientation.Horizontal }; scroll.Content=shelf; shelfGrid.Children.Add(scroll);
  shelfBorder.DragEnter+=(s,e)=> { if(!HasFiles(e)) return; dropZone.Visibility=Visibility.Visible; dropZone.Stroke=Palette.Accent; dropZone.Fill=Palette.AccentBg; };
  shelfBorder.DragLeave+=(s,e)=>ShowDropZone();

  // 设置面板盖在标签、编辑区和暂存区上方。
  settingsView=BuildSettings(); Grid.SetRow(settingsView,1); Grid.SetRowSpan(settingsView,4); panel.Children.Add(settingsView);
  closeView=BuildCloseDialog(); Grid.SetRow(closeView,1); Grid.SetRowSpan(closeView,4); panel.Children.Add(closeView);
  BuildResizeGrips(root);

  // 整个窗口都可以接收文件；拖到收起的顶部条上会先展开。
  AllowDrop=true;
  DragEnter+=(s,e)=> { if(!HasFiles(e)) return; dragInside=true; leaveTimer.Stop(); if(!expanded) Expand(true); UpdatePresenting(); };
  DragOver+=(s,e)=> { bool ok=HasFiles(e); e.Effects=ok?DragDropEffects.Copy:DragDropEffects.None; if(ok) dragInside=true; e.Handled=true; };
  DragLeave+=(s,e)=> { dragInside=false; if(expanded && transient) leaveTimer.Start(); }; // 拖进来时露出的暂存区不在离开时收回，免得在子元素间移动时来回跳
  Drop+=(s,e)=> { dragInside=false; ShowDropZone(); UpdatePresenting(); if(HasFiles(e)) { transient=false; AddFiles((string[])e.Data.GetData(DataFormats.FileDrop)); e.Handled=true; } };

  timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) }; timer.Tick+=(s,e)=>{ timer.Stop(); if(SaveNote() && GetNoteTitle(currentNote)!=currentTitle) RefreshTabs(); };
  // 悬停或拖入展开的面板，在用户还没点进去时，鼠标离开就收回。
  leaveTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(450) }; leaveTimer.Tick+=(s,e)=> { leaveTimer.Stop(); if(expanded && transient && !pinned && !dragInside && !IsMouseOver) Collapse(); };

  try { string p=Path.Combine(dir,"note.md"); if(File.Exists(p)) editor.Text=File.ReadAllText(p); } catch(Exception ex) { noteReadable=false; SetStatus("读取失败，无法保存",Palette.Warn); Msg("笔记读取失败，自动保存已停用以保护原文件。请备份或检查：\n"+Path.Combine(dir,"note.md")+"\n"+ex.Message); }
  try { string f=Path.Combine(dir,"shelf.txt"); if(File.Exists(f)) foreach(var line in File.ReadAllLines(f)) try {files.Add(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(line)));} catch {} } catch(Exception ex) { shelfReadable=false; Msg("暂存区读取失败，自动保存已停用以保护原文件："+ex.Message); }
  LoadFolds(); LoadTabOrder();
  loading=false; RefreshShelf(); RefreshTabs(); SetPreview(false);

  tray=new Forms.NotifyIcon { Text="DotNotes", Icon=LoadAppIcon(), Visible=true };
  var menu=new Forms.ContextMenuStrip { ShowImageMargin=false, Font=new D.Font("Microsoft YaHei UI",9f), Padding=new Forms.Padding(0,4,0,4) }; var trayLook=new TrayMenuRenderer(); menu.Renderer=trayLook;
  Action<string,EventHandler> item=(t,h)=> { var m=new Forms.ToolStripMenuItem(t,null,h) { Padding=new Forms.Padding(8,5,28,5) }; menu.Items.Add(m); };
  item("打开笔记",(s,e)=>Expand(false)); item("设置…",(s,e)=> { Expand(false); OpenSettings(); }); menu.Items.Add(new Forms.ToolStripSeparator()); item("退出",(s,e)=>Quit());
  menu.Opening+=(s,e)=> { trayLook.Back=Gdi(Palette.Panel.Color); trayLook.Hover=Gdi(Palette.Mix(Palette.Panel.Color,Palette.Text.Color,0.10)); trayLook.Text=Gdi(Palette.Text.Color); trayLook.Line=Gdi(Palette.Rule.Color); };
  menu.Opened+=(s,e)=>RoundCorners(menu.Handle);
  tray.ContextMenuStrip=menu; tray.DoubleClick+=(s,e)=>Expand(false);

  // 全屏时窗口要比屏幕大出一圈阴影留白；Windows 默认把窗口限制在约屏幕大小，右边和下边会露出一条桌面，所以放宽上限。
  SourceInitialized+=(s,e)=> { var src=System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle); if(src!=null) src.AddHook(WndHook); };
  PreviewKeyDown+=OnKey;
  PreviewMouseDown+=(s,e)=> { if(expanded) transient=false; };
  MouseEnter+=(s,e)=>leaveTimer.Stop();
  MouseLeave+=(s,e)=> { if(expanded && transient) leaveTimer.Start(); };
  Deactivated+=(s,e)=> { if(expanded && !pinned && !fullscreen && modal==0 && !draggingOut) Collapse(); };
  Closing+=(s,e)=> {if(!quitting) {e.Cancel=true; Collapse();}};
  body.Visibility=Visibility.Collapsed; ApplyChrome(false); PlaceWindow();
  // 打开软件时直接展开，一眼就能看到面板在哪里；点别处、Esc 或 × 后再收成小圆点。
  Loaded+=(s,e)=>Dispatcher.BeginInvoke(new Action(()=> { if(!expanded) Expand(false); }),DispatcherPriority.ApplicationIdle);
  Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=(s,e)=>Dispatcher.BeginInvoke(new Action(PlaceWindow));
 }

 // ——— 外观辅助 ———
 // 深色细滚动条：只有拖动块，悬停时变亮。
 // 悬停提示：面板同色的圆角小卡片、细边框、柔和阴影，代替 WPF 默认的灰底方框。颜色写进样式，换色时整个重建。
 static Style TipStyle() {
  string xaml=@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ToolTip'>
  <Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='HasDropShadow' Value='False'/>
  <Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei UI'/><Setter Property='FontSize' Value='12'/><Setter Property='Foreground' Value='TEXT'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'>
   <Grid Margin='8,4,8,10'>
    <Border Background='BACK' CornerRadius='7'><Border.Effect><DropShadowEffect BlurRadius='12' ShadowDepth='3' Direction='270' Opacity='0.32' Color='Black'/></Border.Effect></Border>
    <Border Background='BACK' BorderBrush='LINE' BorderThickness='1' CornerRadius='7' Padding='9,5,9,6'>
     <ContentPresenter MaxWidth='300'><ContentPresenter.Resources><Style TargetType='TextBlock'><Setter Property='TextWrapping' Value='Wrap'/><Setter Property='LineHeight' Value='18'/></Style></ContentPresenter.Resources></ContentPresenter>
    </Border>
   </Grid>
  </ControlTemplate></Setter.Value></Setter></Style>";
  xaml=xaml.Replace("TEXT",Palette.ToHex(Palette.Text.Color)).Replace("BACK",Palette.ToHex(Palette.Light?Colors.White:Palette.Raised.Color)).Replace("LINE",Palette.ToHex(Palette.Rule.Color));
  return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
 }
 static Style DarkScrollBar() {
  string xaml=@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
  <Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
   <Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True'><Track.Thumb><Thumb>
    <Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='b' Background='THUMB' CornerRadius='3' Margin='3,2,3,2'/>
     <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='HOVER'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='b' Property='Background' Value='DRAG'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid>
  </ControlTemplate></Setter.Value></Setter></Style>";
  // 颜色直接写进模板（模板里引用的画刷会被冻结，没法改色）；换背景色时整个样式重建。
  xaml=xaml.Replace("THUMB",Palette.ToHex(Palette.Thumb.Color)).Replace("HOVER",Palette.ToHex(Palette.ThumbHover.Color)).Replace("DRAG",Palette.ToHex(Palette.ThumbDrag.Color));
  return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
 }
 static FrameworkElement NumberedIcon() {
  var g=new Grid { Width=16, Height=16 };
  Func<string,double,TextBlock> num=(t,top)=>new TextBlock { Text=t, FontSize=7.5, FontWeight=FontWeights.SemiBold, FontFamily=new FontFamily("Segoe UI"), Margin=new Thickness(0,top,0,0), VerticalAlignment=VerticalAlignment.Top };
  Func<double,System.Windows.Shapes.Rectangle> line=top=>new System.Windows.Shapes.Rectangle { Width=9, Height=1.2, Margin=new Thickness(7,top,0,0), HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top };
  g.Children.Add(num("1",-0.5)); g.Children.Add(num("2",7.5));
  foreach(var r in new[] { line(4.6), line(12.6) }) { r.SetBinding(System.Windows.Shapes.Shape.FillProperty,new Binding("Foreground") { RelativeSource=new RelativeSource(RelativeSourceMode.FindAncestor,typeof(Button),1) }); g.Children.Add(r); }
  return g;
 }
 static D.Icon LoadAppIcon() {
  try { var st=typeof(MainWindow).Assembly.GetManifestResourceStream("DotNotes.ico"); if(st!=null) using(st) return new D.Icon(st,Forms.SystemInformation.SmallIconSize); } catch {}
  return MakeTrayIcon();
 }
 static D.Icon MakeTrayIcon() {
  var bmp=new D.Bitmap(32,32);
  using(var g=D.Graphics.FromImage(bmp)) {
   g.SmoothingMode=D.Drawing2D.SmoothingMode.AntiAlias; g.Clear(D.Color.Transparent);
   using(var page=Rounded(new D.RectangleF(3,3,26,26),6)) using(var fill=new D.SolidBrush(D.Color.FromArgb(236,236,239))) g.FillPath(fill,page);
   using(var notchPath=Rounded(new D.RectangleF(10,3,12,6),3)) using(var dark=new D.SolidBrush(D.Color.Black)) { g.FillPath(dark,notchPath); g.FillRectangle(dark,10,3,12,3); }
   using(var grey=new D.SolidBrush(D.Color.FromArgb(90,93,100))) { g.FillRectangle(grey,8,13,16,2.4f); g.FillRectangle(grey,8,18,12,2.4f); }
   using(var green=new D.SolidBrush(D.Color.FromArgb(62,160,92))) g.FillRectangle(green,8,23,9,2.4f);
  }
  return D.Icon.FromHandle(bmp.GetHicon());
 }
 static D.Drawing2D.GraphicsPath Rounded(D.RectangleF r,float rad) {
  var p=new D.Drawing2D.GraphicsPath(); float d=rad*2;
  p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
 }
 // 收起和展开用同一套布局：刘海始终在面板顶部正中，只切换面板底色、描边和阴影。
 // 贴在屏幕顶边时面板上方两角是直角、没有上描边；拖离顶边后四角都是圆角。
 bool Docked { get { return panelTop<=0; } }
 void ApplyChrome(bool open) {
  notch.Background=Brushes.Transparent; outerBox.BorderBrush=Palette.Edge;
  var corners=fullscreen?new CornerRadius(0):Docked?new CornerRadius(0,0,18,18):new CornerRadius(18); outerBox.CornerRadius=shadowFar.CornerRadius=shadowNear.CornerRadius=corners;
  panelClip.RadiusX=panelClip.RadiusY=fullscreen?0:18;
  outerGrid.Margin=new Thickness(ShadowPad); outerBox.Padding=Presenting?new Thickness(0):new Thickness(16,0,16,16);
  shadowLayer.Clip=new RectangleGeometry(new Rect(0,Docked?ShadowPad:0,pw+2*ShadowPad,ph+2*ShadowPad));
  notch.Width=NotchW; notch.Margin=new Thickness(0); notch.HorizontalAlignment=HorizontalAlignment.Center; notchShape.Width=NotchW+2*Ear;
  SetPanelChrome(open);
 }
 // 窗口一直保持展开时的尺寸，不随收起/展开改变：改透明窗口尺寸时会有一帧旧画面错位，看起来像闪一下。
 // 窗口四周都留出阴影的位置；贴顶时上方留白伸出屏幕外。收起时除小圆点外全是透明像素，鼠标点击直接穿透到下面的窗口。
 void PlaceWindow() {
  var wa=SystemParameters.WorkArea;
  pw=Clamp(prefW,Math.Min(MinW,wa.Width),wa.Width); ph=Clamp(prefH,Math.Min(MinH,wa.Height),wa.Height);
  if(fullscreen) { pw=SystemParameters.PrimaryScreenWidth; ph=SystemParameters.PrimaryScreenHeight; dotX=pw/2; dotY=DotTop; }
  double w=pw+2*ShadowPad, h=ph+2*ShadowPad;
  if(Width!=w) Width=w; if(Height!=h) Height=h;
  if(double.IsNaN(panelLeft)) panelLeft=(SystemParameters.PrimaryScreenWidth-pw)/2;
  if(double.IsNaN(dotX)) dotX=pw/2;
  ClampPanel(); ClampDot(); ApplyLayout();
 }
 static double Clamp(double v,double lo,double hi) { return Math.Max(lo,Math.Min(hi,v)); }
 // 面板不出工作区；上沿离屏幕顶不到 16 就贴回顶边。
 void ClampPanel() {
  if(fullscreen) { panelLeft=0; panelTop=0; return; } // 全屏时盖住整个主显示器，包括任务栏
  var wa=SystemParameters.WorkArea;
  panelLeft=Clamp(panelLeft,wa.Left,wa.Right-pw); panelTop=Clamp(panelTop,0,wa.Bottom-ph); if(panelTop<Snap) panelTop=0;
 }
 void ClampDot() { dotX=Clamp(dotX,8,pw-8); dotY=Docked?DotTop:Clamp(dotY,8,ph-8); }
 // 窗口挪到 (left,top)：X、Y 用一次 SetWindowPos 一起改。分两次改 Left、Top 时窗口每帧要挪两下，斜着拖会一顿一顿。
 void MoveWindowTo(double left,double top) {
  var src=PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
  if(src==null) { if(Left!=left) Left=left; if(Top!=top) Top=top; return; }
  var m=src.CompositionTarget.TransformToDevice; int x=(int)Math.Round(left*m.M11), y=(int)Math.Round(top*m.M22);
  if(x==movedX && y==movedY && Math.Abs(Left-left)<1 && Math.Abs(Top-top)<1) return;
  movedX=x; movedY=y; SetWindowPos(src.Handle,IntPtr.Zero,x,y,0,0,0x0015); // SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE
 }
 int movedX=int.MinValue, movedY=int.MinValue;
 // 按当前位置摆窗口、小圆点和面板外形。
 void ApplyLayout() {
  MoveWindowTo(panelLeft-ShadowPad,panelTop-ShadowPad);
  if(shadowFar==null) return;
  // 感应区始终宽 72，四周给圆点留出足够空间。
  bool strip=Docked; dotHit.Width=72; dotHit.Height=Math.Max(16,dotSize+8);
  dotBody.Width=dotBody.Height=dotSize; double center=strip?4+dotSize/2.0:dotY;
  dotLayer.Margin=new Thickness(dotX-dotHit.Width/2,strip?0:center-dotHit.Height/2,0,0); dotBody.Margin=new Thickness(0,strip?4:(dotHit.Height-dotSize)/2,0,0);
  ApplyChrome(expanded);
  if(!expanded) { panelClip.BeginAnimation(RectangleGeometry.RectProperty,null); panelClip.Rect=NoClip(); ResizeShadows(TuckClip(),TimeSpan.Zero,null); }
  else if(!panelClip.HasAnimatedProperties) { panelClip.Rect=FullClip(); ResizeShadows(FullClip(),TimeSpan.Zero,null); }
 }
 // ——— 拖动 ———
 // 按下后移动超过 4px 才算拖动，否则当作点击。拖小圆点时它可以去屏幕任意位置，面板跟着它挪到放得下的地方；
 // 拖面板时小圆点回到面板顶部正中，收起时缩回那里。
 void Draggable(UIElement handle,bool movesDot,bool preview,Action click) {
  MouseButtonEventHandler down=(s,e)=> {
   if(fullscreen) return; // 全屏时不能拖动面板；点刘海仍是收起
   dragArmed=true; dragMoved=false; dragDot=movesDot; dragFrom=CursorDip(); hoverTimer.Stop();
   dragOrigin=movesDot?new Point(panelLeft+dotX,panelTop+dotY):new Point(panelLeft,panelTop);
   if(movesDot && !expanded) StartSpring(dragOrigin);
   handle.CaptureMouse(); if(!preview) e.Handled=true;
  };
  if(preview) handle.PreviewMouseLeftButtonDown+=down; else handle.MouseLeftButtonDown+=down;
  handle.PreviewMouseMove+=(s,e)=> {
   if(!dragArmed || e.LeftButton!=MouseButtonState.Pressed) return;
   var p=CursorDip(); double dx=p.X-dragFrom.X, dy=p.Y-dragFrom.Y;
   if(!dragMoved && Math.Abs(dx)<4 && Math.Abs(dy)<4) return;
   dragMoved=true; transient=false;
   if(dragDot && springOn) springGoal=DotTarget(new Point(dragOrigin.X+dx,dragOrigin.Y+dy)); // 只更新目标，圆点由弹簧带过去
   else if(dragDot) MoveDotTo(new Point(dragOrigin.X+dx,dragOrigin.Y+dy)); else MovePanelTo(new Point(dragOrigin.X+dx,dragOrigin.Y+dy));
  };
  handle.PreviewMouseLeftButtonUp+=(s,e)=> {
   if(!dragArmed) return; dragArmed=false;
   if(dragMoved) { e.Handled=true; handle.ReleaseMouseCapture(); if(!springOn) SaveSettings(); return; } // 按钮失去捕获后不会再触发点击；弹簧停稳后再存位置
   if(!preview) { handle.ReleaseMouseCapture(); e.Handled=true; if(click!=null) click(); }
  };
  handle.LostMouseCapture+=(s,e)=> { if(dragArmed && dragMoved) { dragArmed=false; if(!springOn) SaveSettings(); } else if(dragArmed && dragDot) dragArmed=false; };
 }
 Point CursorDip() {
  POINT p; GetCursorPos(out p); var src=PresentationSource.FromVisual(this);
  return src==null?new Point(p.X,p.Y):src.CompositionTarget.TransformFromDevice.Transform(new Point(p.X,p.Y));
 }
 void MoveDotTo(Point p) {
  p=DotTarget(p); double x=p.X, y=p.Y;
  panelLeft=x-pw/2; panelTop=y==DotTop?0:y-DotTop; ClampPanel();
  dotX=x-panelLeft; dotY=y-panelTop; ClampDot(); ApplyLayout();
 }
 // 圆点最终会落在哪：不出工作区，离顶边不到 16 就吸到顶上。
 Point DotTarget(Point p) {
  var wa=SystemParameters.WorkArea;
  double x=Clamp(p.X,wa.Left+8,wa.Right-8), y=Clamp(p.Y,DotTop,wa.Bottom-8); if(y<DotTop+Snap) y=DotTop;
  return new Point(x,y);
 }
 // 弹簧途中的位置：不吸顶（否则靠近顶边时会突然跳一下），只保证不出工作区。
 // 圆点在窗口里的位置和贴顶与否都没变时只挪窗口，不重排：重排会重建裁剪和阴影，整个透明窗口跟着重画，拖起来发卡。
 void PlaceDot(Point q) {
  var wa=SystemParameters.WorkArea; double x=Clamp(q.X,wa.Left+8,wa.Right-8), y=Clamp(q.Y,DotTop,wa.Bottom-8);
  bool wasDocked=Docked; double ox=dotX, oy=dotY;
  panelLeft=Clamp(x-pw/2,wa.Left,wa.Right-pw); panelTop=Clamp(y-DotTop,0,Math.Max(0,wa.Bottom-ph));
  dotX=x-panelLeft; dotY=y-panelTop; ClampDot();
  if(!expanded && wasDocked==Docked && Math.Abs(dotX-ox)<0.01 && Math.Abs(dotY-oy)<0.01) MoveWindowTo(panelLeft-ShadowPad,panelTop-ShadowPad);
  else ApplyLayout();
 }
 // ——— 圆点的弹簧跟随 ———
 // 拖动时圆点不直接跳到鼠标处，而是挂在一根弹簧上被拉过去：快拖会落后一点，松手或停下时冲过头一点再弹回，像有重量。
 // 弹簧刚度 1800、阻尼 64（阻尼比约 0.75，过冲约 3%）：紧跟鼠标，正常速度拖动只落后几像素、快甩约 30px，不会拖泥带水；每帧按 4ms 的小步积分，帧率不稳也不会抖。
 // 速度越快圆点沿运动方向拉得越长（最多 1.25 倍，垂直方向相应变扁，面积不变）；按住时放大到 1.18 倍，像被“拿起来”。
 // 系统关闭了界面动画时不用弹簧，照旧直接跟手。
 bool springOn; Point springPos, springGoal; Vector springVel; double pickScale=1; TimeSpan springLast;
 void StartSpring(Point at) {
  if(!SystemParameters.ClientAreaAnimation) return;
  if(!springOn) { springPos=at; springVel=new Vector(); springLast=TimeSpan.Zero; springOn=true; CompositionTarget.Rendering+=SpringTick; }
  springGoal=at; Breathe(false);
 }
 void SpringTick(object sender,EventArgs e) {
  var r=e as RenderingEventArgs; if(r==null) return;
  double dt=springLast==TimeSpan.Zero?1/60.0:(r.RenderingTime-springLast).TotalSeconds; if(r.RenderingTime==springLast) return; springLast=r.RenderingTime;
  dt=Math.Max(0.001,Math.Min(dt,0.05));
  const double k=1800, c=64; int n=(int)Math.Ceiling(dt/0.004); double h=dt/n;
  for(int i=0;i<n;i++) { var acc=k*(springGoal-springPos)-c*springVel; springVel+=acc*h; springPos+=springVel*h; }
  bool held=dragArmed && dragDot;
  // 目标每帧直接读光标位置：每帧回调的优先级高于鼠标输入，鼠标移动消息会被攒着、每 80–100ms 才处理一批，
  // 只靠它更新目标的话圆点是一顿一顿往前跳的（这就是拖动卡顿的原因）。
  if(held) {
   var cp=CursorDip(); double dx=cp.X-dragFrom.X, dy=cp.Y-dragFrom.Y;
   if(!dragMoved && (Math.Abs(dx)>=4 || Math.Abs(dy)>=4)) { dragMoved=true; transient=false; }
   if(dragMoved) springGoal=DotTarget(new Point(dragOrigin.X+dx,dragOrigin.Y+dy));
  }
  pickScale+=((held?1.18:1)-pickScale)*Math.Min(1,dt*16);
  if(dragMoved || (springPos-springGoal).Length>0.01) PlaceDot(springPos);
  double speed=springVel.Length, st=1+Math.Min(0.25,speed/5000), cs=speed>1?springVel.X/speed:1, sn=speed>1?springVel.Y/speed:0;
  double sx=st*pickScale, sy=pickScale/st;
  dotWarp.Matrix=new Matrix(sx*cs*cs+sy*sn*sn,(sx-sy)*cs*sn,(sx-sy)*cs*sn,sx*sn*sn+sy*cs*cs,0,0);
  if(!held && (springGoal-springPos).Length<0.3 && speed<6 && Math.Abs(pickScale-1)<0.004) StopSpring();
 }
 void StopSpring() {
  if(!springOn) return; springOn=false; CompositionTarget.Rendering-=SpringTick;
  dotWarp.Matrix=Matrix.Identity; pickScale=1;
  if(dragMoved) { MoveDotTo(springGoal); SaveSettings(); }
  if(!expanded) Breathe(true);
 }
 void MovePanelTo(Point p) { panelLeft=p.X; panelTop=p.Y; ClampPanel(); dotX=pw/2; dotY=DotTop; ApplyLayout(); }
 bool AtHome() { return Docked && Math.Abs(panelLeft-Clamp((SystemParameters.PrimaryScreenWidth-pw)/2,SystemParameters.WorkArea.Left,SystemParameters.WorkArea.Right-pw))<1 && Math.Abs(dotX-pw/2)<1; }
 void ResetPosition() { panelLeft=double.NaN; panelTop=0; dotX=double.NaN; dotY=DotTop; PlaceWindow(); SaveSettings(); UpdateSettingsView(); }
 // ——— 改大小 ———
 // 面板左、右、下三条边和两个下角可以按住拖动改大小（边 6px；角 22px，因为圆角外的部分被裁掉；透明度 1：看不见，但接得住鼠标）。
 // 手柄放在面板里面，收起时随面板一起被裁掉，不会挡住下面的窗口。上边是刘海和屏幕顶边，不参与。
 void BuildResizeGrips(Grid root) {
  var layer=new Grid { Margin=new Thickness(-17,0,-17,-17) }; Grid.SetRowSpan(layer,2); root.Children.Add(layer); // 伸过边框和内边距，贴到面板外沿
  Action<HorizontalAlignment,VerticalAlignment,double,double,Cursor,int,int> add=(h,v,w,hgt,cur,x,y)=> {
   var b=new Border { HorizontalAlignment=h, VerticalAlignment=v, Background=new SolidColorBrush(Color.FromArgb(1,0,0,0)), Cursor=cur };
   if(w>0) b.Width=w; if(hgt>0) b.Height=hgt;
   b.MouseLeftButtonDown+=(s,e)=> {
    if(!expanded || fullscreen) return;
    sizeArmed=true; sizeX=x; sizeY=y; sizeFrom=CursorDip(); sizeOrigin=new Rect(panelLeft,panelTop,pw,ph);
    sizeSym=x!=0 && AtHome(); transient=false; hoverTimer.Stop(); b.CaptureMouse(); e.Handled=true;
   };
   b.MouseMove+=(s,e)=> { if(sizeArmed && e.LeftButton==MouseButtonState.Pressed) ResizeTo(CursorDip()); };
   b.MouseLeftButtonUp+=(s,e)=> { if(!sizeArmed) return; sizeArmed=false; b.ReleaseMouseCapture(); SaveSettings(); UpdateSettingsView(); e.Handled=true; };
   b.LostMouseCapture+=(s,e)=> { if(sizeArmed) { sizeArmed=false; SaveSettings(); UpdateSettingsView(); } };
   layer.Children.Add(b);
  };
  add(HorizontalAlignment.Left,VerticalAlignment.Stretch,Edge,0,Cursors.SizeWE,-1,0);
  add(HorizontalAlignment.Right,VerticalAlignment.Stretch,Edge,0,Cursors.SizeWE,1,0);
  add(HorizontalAlignment.Stretch,VerticalAlignment.Bottom,0,Edge,Cursors.SizeNS,0,1);
  add(HorizontalAlignment.Left,VerticalAlignment.Bottom,Corner,Corner,Cursors.SizeNESW,-1,1);
  add(HorizontalAlignment.Right,VerticalAlignment.Bottom,Corner,Corner,Cursors.SizeNWSE,1,1);
 }
 // 拖右边时左边不动，拖左边时右边不动；面板在顶部正中时左右对称缩放，保持居中。不小于 460×360，不超出工作区。
 void ResizeTo(Point p) {
  var wa=SystemParameters.WorkArea; double dx=p.X-sizeFrom.X, dy=p.Y-sizeFrom.Y;
  double left=sizeOrigin.X, w=sizeOrigin.Width, h=sizeOrigin.Height;
  if(sizeSym) { double c=sizeOrigin.X+sizeOrigin.Width/2; w=Clamp(sizeOrigin.Width+2*dx*sizeX,MinW,2*Math.Min(c-wa.Left,wa.Right-c)); left=c-w/2; }
  else if(sizeX>0) w=Clamp(sizeOrigin.Width+dx,MinW,wa.Right-sizeOrigin.X);
  else if(sizeX<0) { w=Clamp(sizeOrigin.Width-dx,MinW,sizeOrigin.Right-wa.Left); left=sizeOrigin.Right-w; }
  if(sizeY>0) h=Clamp(sizeOrigin.Height+dy,MinH,wa.Bottom-sizeOrigin.Y);
  prefW=w; prefH=h; SetPanelSize(left,w,h);
 }
 // 一次把窗口位置和大小一起改掉，拖左边时右边不会抖。
 void SetPanelSize(double left,double w,double h) {
  bool centered=Math.Abs(dotX-pw/2)<1;
  pw=w; ph=h; panelLeft=left; if(centered) dotX=pw/2;
  ClampPanel(); ClampDot();
  var src=PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
  if(src!=null) { var m=src.CompositionTarget.TransformToDevice; SetWindowPos(src.Handle,IntPtr.Zero,(int)Math.Round((panelLeft-ShadowPad)*m.M11),(int)Math.Round((panelTop-ShadowPad)*m.M22),(int)Math.Round((pw+2*ShadowPad)*m.M11),(int)Math.Round((ph+2*ShadowPad)*m.M22),0x0014); } // SWP_NOZORDER|SWP_NOACTIVATE
  double ww=pw+2*ShadowPad, wh=ph+2*ShadowPad; if(Math.Abs(Width-ww)>0.01) Width=ww; if(Math.Abs(Height-wh)>0.01) Height=wh;
  ApplyLayout();
 }
 bool DefaultSize() { return Math.Abs(prefW-DefW)<1 && Math.Abs(prefH-DefH)<1; }
 void ResetSize() {
  var wa=SystemParameters.WorkArea; bool home=AtHome(); double c=panelLeft+pw/2;
  prefW=DefW; prefH=DefH; double w=Math.Min(DefW,wa.Width), h=Math.Min(DefH,wa.Height);
  SetPanelSize(home?c-w/2:panelLeft,w,h); SaveSettings(); UpdateSettingsView();
 }
 // ——— 全屏 ———
 // 面板铺满整个主显示器（连任务栏一起盖住），四角变直角、不拖不缩；点别处不收起。Esc、F11 或再点按钮退出，回到原来的位置和大小。
 void SetFullscreen(bool on) {
  if(on==fullscreen) return;
  bool leavingPresentation=!on && presenting; if(leavingPresentation) presenting=false;
  if(on) {
   if(!expanded) Expand(false);
   beforeFull=new[] { panelLeft,panelTop,pw,ph,dotX,dotY }; fullscreen=true; transient=false;
   double w=SystemParameters.PrimaryScreenWidth, h=SystemParameters.PrimaryScreenHeight;
   panelTop=0; SetPanelSize(0,w,h); dotX=w/2; dotY=DotTop;
  } else {
   fullscreen=false; var b=beforeFull; beforeFull=null;
   if(b!=null) { panelTop=b[1]; SetPanelSize(b[0],b[2],b[3]); dotX=b[4]; dotY=b[5]; } else PlaceWindow();
   ClampDot();
  }
  UpdatePresenting(); ApplyLayout(); ShowFullscreenState();
  if(leavingPresentation && mapping) RenderMap(false);
  if(mapping && !mapMoved) Dispatcher.BeginInvoke(new Action(FitMap),DispatcherPriority.Loaded);
 }
 // 演示时收起作者控件，右上角保留进度、适应窗口和退出演示。
 void UpdatePresenting() {
  if(presentBar==null || headerRow==null) return;
  bool p=Presenting; var vis=p?Visibility.Collapsed:Visibility.Visible;
  headerRow.Visibility=tabRowPanel.Visibility=vis;
  bool barNeeded=Writing || mapping || editingBlock>=0;
  noteBar.Visibility=p || !barNeeded?Visibility.Collapsed:Visibility.Visible;
  // 脑图里暂存区是空的就先收起，把高度让给脑图；拖文件进来或里面已有文件时照常显示。
  bool shelfOff=p || (mapping && files.Count==0 && !dragInside);
  bool slim=files.Count==0 && !dragInside;
  shelfCaption.Visibility=shelfOff || slim?Visibility.Collapsed:Visibility.Visible;
  shelfBorder.Visibility=shelfOff?Visibility.Collapsed:Visibility.Visible;
  shelfBorder.MinHeight=slim?30:40; shelfBorder.Margin=new Thickness(0,slim?10:0,0,0);
  if(shelfHint!=null) { shelfHint.Text=slim?"文件暂存 · 拖文件到这里，之后可以再拖到其他应用":"松开鼠标放进暂存区"; shelfHint.FontSize=slim?12:12.5; }
  mapHint.Visibility=p || mapRoot==null?Visibility.Collapsed:Visibility.Visible;
  editCard.BorderThickness=new Thickness(0); editCard.CornerRadius=new CornerRadius(0); editCard.Background=p?Palette.Panel:Brushes.Transparent;
  outerBox.Padding=p?new Thickness(0):new Thickness(16,0,16,16);
  presentFit.Visibility=p && mapRoot!=null?Visibility.Visible:Visibility.Collapsed; presentProgress.Visibility=p && mapRoot!=null?Visibility.Visible:Visibility.Collapsed;
  presentBtn.Content=Ui.IconLabel(p?G_Unfull:G_Full,p?"退出演示":"全屏演示");
  presentBtn.ToolTip=p?"退出演示（Esc 或 F5）":"全屏演示：一步步展示脑图（F5）";
  presentBar.Margin=p?new Thickness(0,8,16,0):new Thickness(0,8,8,0);
  presentBar.Opacity=p && !presentBar.IsMouseOver?0.45:1;
 }
 void ShowFullscreenState() {
  fsButton.Content=Ui.Icon(fullscreen?G_Unfull:G_Full,13);
  fsButton.ToolTip=fullscreen?"退出全屏（Esc 或 F11）":"全屏：铺满整个屏幕（F11）";
  fsButton.Background=fullscreen?Palette.AccentBg:Brushes.Transparent; fsButton.Foreground=fullscreen?Palette.Accent:Palette.Muted;
 }

 // 演示树独立于用户的折叠记录；步进只改变可见性，避免已有节点移动。
 void UnfoldPresentation(MapNode n) { n.Folded=false; foreach(var k in n.Kids) UnfoldPresentation(k); }
 void TogglePresentation() { if(Presenting) ExitPresentation(); else if(mapping && mapRoot!=null) EnterPresentation(); }
 void EnterPresentation() { if(!mapping || mapRoot==null || Presenting) return; if(mapBox!=null) EndMapEdit(true); presentationWasFull=fullscreen; if(!fullscreen) SetFullscreen(true); presenting=true; presentCount=1; RenderMap(false); UpdatePresenting(); mapMoved=false; Dispatcher.BeginInvoke(new Action(()=> { if(Presenting) FitMap(); }),DispatcherPriority.Loaded); mapView.Focus(); }
 void ExitPresentation() { if(!presenting) return; bool keep=presentationWasFull; presenting=false; if(!keep && fullscreen) SetFullscreen(false); UpdatePresenting(); if(mapping) { RenderMap(false); mapMoved=false; Dispatcher.BeginInvoke(new Action(FitMap),DispatcherPriority.Loaded); } }
 void ShowPresentation(int animateIndex,bool pan) {
  if(!Presenting) return;
  for(int i=0;i<presentNodes.Count;i++) { var n=presentNodes[i]; n.El.BeginAnimation(UIElement.OpacityProperty,null); n.El.RenderTransform=Transform.Identity; n.El.Visibility=i<presentCount?Visibility.Visible:Visibility.Hidden; UIElement line; if(presentLines.TryGetValue(n,out line)) line.Visibility=i<presentCount?Visibility.Visible:Visibility.Hidden; }
  presentProgress.Text=presentCount+" / "+presentNodes.Count;
  if(animateIndex>=0 && animateIndex<presentCount) { var n=presentNodes[animateIndex]; if(SystemParameters.ClientAreaAnimation) { var ease=new CubicEase { EasingMode=EasingMode.EaseOut }; n.El.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)) { EasingFunction=ease }); var slide=new TranslateTransform(); n.El.RenderTransform=slide; slide.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(-10,0,TimeSpan.FromMilliseconds(200)) { EasingFunction=ease }); } if(pan) ShowPresentationNode(n); }
 }
 void PresentationStep(int delta) { int old=presentCount; presentCount=Math.Max(1,Math.Min(presentNodes.Count,presentCount+delta)); if(old!=presentCount) ShowPresentation(delta>0?presentCount-1:-1,delta>0); }
 void StopMapPanAnimation() { double x=mapPan.X, y=mapPan.Y; mapPan.BeginAnimation(TranslateTransform.XProperty,null); mapPan.BeginAnimation(TranslateTransform.YProperty,null); mapPan.X=x; mapPan.Y=y; }
 void ShowPresentationNode(MapNode n) {
  StopMapPanAnimation();
  double z=mapScale.ScaleX, left=n.X*z+mapPan.X, top=n.Y*z+mapPan.Y, right=(n.X+n.W)*z+mapPan.X, bottom=(n.Y+n.H)*z+mapPan.Y, dx=0, dy=0, margin=48;
  if(left<margin) dx=margin-left; else if(right>mapView.ActualWidth-margin) dx=mapView.ActualWidth-margin-right;
  if(top<margin) dy=margin-top; else if(bottom>mapView.ActualHeight-margin) dy=mapView.ActualHeight-margin-bottom;
  if(dx==0 && dy==0) return; double x=mapPan.X+dx, y=mapPan.Y+dy;
  if(SystemParameters.ClientAreaAnimation) { var ease=new CubicEase { EasingMode=EasingMode.EaseOut }; double ox=mapPan.X, oy=mapPan.Y; mapPan.X=x; mapPan.Y=y; mapPan.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(ox,x,TimeSpan.FromMilliseconds(300)) { EasingFunction=ease, FillBehavior=FillBehavior.Stop }); mapPan.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(oy,y,TimeSpan.FromMilliseconds(300)) { EasingFunction=ease, FillBehavior=FillBehavior.Stop }); }
  else { mapPan.X=x; mapPan.Y=y; }
 }
 // ——— 脑图 ———
 // 第一行是标题时它就是中心主题；否则第一行文字当中心主题。之后的标题按级别挂成分支（和中心同级或更高的标题当一级分支），
 // 标题下的列表按缩进挂在标题下面，普通文字、引用、待办也作为叶子。代码块只放一个“代码：第一行”的节点，分隔线不画。
 void SetMap() {
  if(mapping) return;
  if(editingBlock>=0) FinishEdit();
  if(previewing) { leftPreviewText=editor.Text; ClearSelection(); }
  else { if(editor.Text!=leftPreviewText) ClearHistory(); leftPreviewText=editor.Text; } // 在“编辑”里改过，之前的撤销记录对不上了
  mapFromPreview=previewing; previewing=false; mapping=true;
  previewScroll.Visibility=Visibility.Collapsed; editor.Visibility=Visibility.Collapsed; mapView.Visibility=Visibility.Visible;
  Choose(editTab,false); Choose(previewTab,false); Choose(mapTab,true); UpdateToolbar();
  RenderMap(true); mapView.Focus(); FadeIn(mapView);
 }
 void BuildMapView(Grid host) {
  mapView=new Grid { Background=Brushes.Transparent, ClipToBounds=true, Visibility=Visibility.Collapsed, Focusable=true, FocusVisualStyle=null };
  mapCanvas=new Canvas { HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top };
  var group=new TransformGroup(); group.Children.Add(mapScale); group.Children.Add(mapPan); mapCanvas.RenderTransform=group;
  mapView.Children.Add(mapCanvas);
  // 空笔记：说明 + “去编辑”；这时底栏按钮禁用、操作提示隐藏。
  mapEmpty=new StackPanel { HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(24), Visibility=Visibility.Collapsed };
  mapEmpty.Children.Add(new TextBlock { Text="这篇笔记还是空的。写几个标题（#、##）和列表，这里就会画成脑图。", Foreground=Palette.Muted, FontSize=13, TextWrapping=TextWrapping.Wrap, TextAlignment=TextAlignment.Center });
  var goEdit=Ui.Button(Ui.IconLabel(G_Edit,"去编辑"),"切到编辑 (Ctrl+E)"); goEdit.Height=30; goEdit.Foreground=Palette.Accent; goEdit.HorizontalAlignment=HorizontalAlignment.Center; goEdit.Margin=new Thickness(0,12,0,0); goEdit.Click+=(s,e)=> { if(fullscreen) SetFullscreen(false); SetPreview(false); editor.Focus(); };
  mapEmpty.Children.Add(goEdit); mapView.Children.Add(mapEmpty);
  mapHint=new TextBlock { Text=MapHintIdle, Foreground=Palette.Muted, Opacity=0.7, FontSize=11, Margin=new Thickness(12,0,0,8), HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Bottom, IsHitTestVisible=false };
  mapView.Children.Add(mapHint);
  // 右上角：平时是“全屏演示”入口；演示时换成“适应窗口”和“退出全屏”。
  var presentRow=new StackPanel { Orientation=Orientation.Horizontal };
  presentProgress=new TextBlock { FontSize=12, Foreground=Palette.Muted, Margin=new Thickness(8,0,8,0), VerticalAlignment=VerticalAlignment.Center, Visibility=Visibility.Collapsed }; presentRow.Children.Add(presentProgress);
  presentFit=Ui.Button(Ui.IconLabel(G_Fit,"适应窗口"),"缩放到合适大小并居中 (Ctrl+0)"); presentFit.Height=28; presentFit.Focusable=false; presentFit.Foreground=Palette.Muted; presentFit.Visibility=Visibility.Collapsed; presentFit.Click+=(s,e)=> { mapMoved=false; FitMap(); }; presentRow.Children.Add(presentFit);
  presentBtn=Ui.Button(Ui.IconLabel(G_Full,"全屏演示"),""); presentBtn.Height=28; presentBtn.Focusable=false; presentBtn.Foreground=Palette.Muted; presentBtn.Click+=(s,e)=>TogglePresentation(); presentRow.Children.Add(presentBtn);
  presentBar=new Border { Child=presentRow, Background=Palette.Raised, CornerRadius=new CornerRadius(8), Padding=new Thickness(2), HorizontalAlignment=HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Top, Margin=new Thickness(0,8,8,0) };
  presentBar.MouseEnter+=(s,e)=>presentBar.Opacity=1; presentBar.MouseLeave+=(s,e)=>presentBar.Opacity=Presenting?0.45:1;
  mapView.Children.Add(presentBar);
  host.Children.Add(mapView);
  mapView.MouseLeftButtonDown+=(s,e)=> {
   var src=e.OriginalSource as DependencyObject; var fold=FoldOf(src); var hit=fold==null?NodeOf(src):null;
   if(e.ClickCount==2 && hit!=null && !Presenting) { mapArmed=false; mapView.Focus(); SelectMap(hit); BeginMapEdit(FindLine(mapRoot,mapSelLine)); e.Handled=true; return; }
   StopMapPanAnimation(); mapArmed=true; mapPanned=false; mapFrom=e.GetPosition(mapView); mapPanFrom=new Point(mapPan.X,mapPan.Y); mapHit=hit; mapHitFold=fold; mapView.CaptureMouse(); mapView.Focus(); e.Handled=true; };
  mapView.MouseMove+=(s,e)=> {
   if(!mapArmed || e.LeftButton!=MouseButtonState.Pressed) return;
   var p=e.GetPosition(mapView); double dx=p.X-mapFrom.X, dy=p.Y-mapFrom.Y;
   if(!mapPanned && dx*dx+dy*dy<16) return;
   mapPanned=true; mapMoved=true; mapView.Cursor=Cursors.SizeAll; mapPan.X=mapPanFrom.X+dx; mapPan.Y=mapPanFrom.Y+dy;
  };
  mapView.MouseLeftButtonUp+=(s,e)=> {
   if(!mapArmed) return; mapArmed=false; mapView.ReleaseMouseCapture(); mapView.Cursor=null;
   if(!mapPanned) { if(Presenting) PresentationStep(1); else if(mapHitFold!=null) ToggleMapNode(mapHitFold); else SelectMap(mapHit); }
   e.Handled=true; };
  mapView.MouseRightButtonUp+=(s,e)=> { if(Presenting) { PresentationStep(-1); e.Handled=true; } };
  mapView.PreviewKeyDown+=MapKey;
  mapView.LostMouseCapture+=(s,e)=> { mapArmed=false; mapView.Cursor=null; };
  mapView.PreviewMouseWheel+=(s,e)=> { ZoomMap(e.Delta>0?1.12:1/1.12,e.GetPosition(mapView)); e.Handled=true; };
  mapView.SizeChanged+=(s,e)=> { if(mapping && !mapMoved) FitMap(); };
 }
 MapNode FoldOf(DependencyObject d) {
  while(d!=null && d!=mapView) { var fe=d as FrameworkElement; if(fe!=null && fe.Tag is MapFold) return ((MapFold)fe.Tag).N; d=d is Visual?VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d); }
  return null;
 }
 MapNode NodeOf(DependencyObject d) {
  while(d!=null && d!=mapView) {
   var fe=d as FrameworkElement; if(fe!=null && fe.Tag is MapNode) return (MapNode)fe.Tag;
   d=d is Visual?VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d);
  }
  return null;
 }
 static string MapText(string md) { string t=PlainLine(md).Trim(); if(t.StartsWith("• ")) t=t.Substring(2); return t; }
 MapNode AddMapNode(MapNode parent,string text) {
  var n=new MapNode { Full=text, Text=text.Length>90?text.Substring(0,90)+"…":text, Parent=parent, Depth=parent.Depth+1 };
  n.Branch=n.Depth==1?parent.Kids.Count:parent.Branch; n.Key=parent.Key+"/"+parent.Kids.Count+":"+text;
  n.Folded=mapFolds.Contains(currentNote+"|"+n.Key);
  parent.Kids.Add(n); return n;
 }
 MapNode BuildMap() {
  LoadLines(); BuildBlocks(); FindFolds();
  var set=FoldsOf(currentNote,false);
  int first=-1; for(int b=0;b<blocks.Count;b++) if(blocks[b][2]!=K_Blank && !Regex.IsMatch(lines[blocks[b][0]],@"^\s*([-*_])(\s*\1){2,}\s*$")) { first=b; break; }
  if(first<0) return null;
  var root=new MapNode { Depth=0, Branch=-1, Key="" }; int rootLevel=0, from=first+1;
  if(headLevel[first]>0) { root.Full=MapText(lines[blocks[first][0]]); root.Heading=true; root.Block=first; rootLevel=headLevel[first]; root.Kind=1; root.Line=root.Last=blocks[first][0]; }
  else if(blocks[first][2]==K_Line) { root.Full=MapText(lines[blocks[first][0]]); root.Line=root.Last=blocks[first][0]; root.Kind=MapListRe.IsMatch(lines[root.Line])?2:3; }
  else { root.Full=TitleOf(editor.Text); from=first; }
  root.Text=root.Full.Length>60?root.Full.Substring(0,60)+"…":root.Full;
  var heads=new List<KeyValuePair<int,MapNode>>(); var lists=new List<KeyValuePair<int,MapNode>>();
  for(int b=from;b<blocks.Count;b++) {
   int kind=blocks[b][2]; string line=lines[blocks[b][0]];
   if(kind==K_Blank) continue;
   var under=heads.Count>0?heads[heads.Count-1].Value:root;
   if(headLevel[b]>0) {
    int lv=Math.Max(headLevel[b],rootLevel+1);
    while(heads.Count>0 && heads[heads.Count-1].Key>=lv) heads.RemoveAt(heads.Count-1);
    var n=AddMapNode(heads.Count>0?heads[heads.Count-1].Value:root,MapText(line)); n.Heading=true; n.Block=b; n.Kind=1; n.Line=n.Last=blocks[b][0];
    n.Folded=set!=null && set.Contains(foldKey[b]); // 和预览里的标题折叠是同一份记录
    heads.Add(new KeyValuePair<int,MapNode>(lv,n)); lists.Clear(); continue;
   }
   if(kind==K_Code) {
    string firstCode=blocks[b][1]>1?lines[blocks[b][0]+1].Trim():"";
    var cn=AddMapNode(under,"代码："+(firstCode.Length>0?firstCode:"…")); cn.Kind=4; cn.Line=blocks[b][0]; cn.Last=blocks[b][0]+blocks[b][1]-1; lists.Clear(); continue;
   }
   if(Regex.IsMatch(line,@"^\s*([-*_])(\s*\1){2,}\s*$")) continue;
   var lm=Regex.Match(line,@"^([ \t]*)([-*+]|\d{1,9}[.)])\s+");
   if(lm.Success) {
    int ind=lm.Groups[1].Value.Replace("\t","    ").Length;
    while(lists.Count>0 && lists[lists.Count-1].Key>=ind) lists.RemoveAt(lists.Count-1);
    var n=AddMapNode(lists.Count>0?lists[lists.Count-1].Value:under,MapText(line)); n.Kind=2; n.Line=n.Last=blocks[b][0];
    lists.Add(new KeyValuePair<int,MapNode>(ind,n));
   }
   else { var tn=AddMapNode(under,MapText(line)); tn.Kind=3; tn.Line=tn.Last=blocks[b][0]; lists.Clear(); }
  }
  return root;
 }
 static List<MapNode> Shown(MapNode n) { return n.Folded?new List<MapNode>():n.Kids; }
 static int Descendants(MapNode n) { int c=0; foreach(var k in n.Kids) c+=1+Descendants(k); return c; }
 // 节点外观：中心主题用强调色实底；一级分支用分支色描边和淡底；更深的标题淡底；列表和文字只有字，连线用分支色。
 void MakeMapElement(MapNode n) {
  Color col=Palette.Branch(n.Branch), surface=Palette.Surface.Color; bool light=Palette.Light;
  // 待办画成和预览一样的勾选框，不用 ☐ ☑ 字符。
  string text=n.Text; int task=-1;
  if(n.Depth>0 && !n.Heading && (text.StartsWith("☐ ") || text.StartsWith("☑ "))) { task=text[0]=='☑'?1:0; text=text.Substring(2); }
  var tb=new TextBlock { Text=text, FontFamily=FontFamily, TextWrapping=TextWrapping.Wrap, Foreground=task==1?Palette.Muted:Palette.Text };
  UIElement content=tb;
  if(task>=0) { var mark=CheckMark(task==1); mark.VerticalAlignment=VerticalAlignment.Top; mark.Margin=new Thickness(0,2,6,0); var row=new StackPanel { Orientation=Orientation.Horizontal }; row.Children.Add(mark); row.Children.Add(tb); content=row; }
  var el=new Border { Child=content, Tag=n };
  // 换行宽度收窄（叶子约 13 个字），长句往下长、不往右撑宽，适应窗口时字不会被缩得太小。
  if(n.Depth==0) { tb.FontSize=18; tb.FontWeight=FontWeights.SemiBold; tb.MaxWidth=260; tb.Foreground=Palette.Panel; el.Background=Palette.Accent; el.CornerRadius=new CornerRadius(12); el.Padding=new Thickness(18,10,18,11); }
  else if(n.Depth==1) { tb.FontSize=15; tb.FontWeight=FontWeights.SemiBold; tb.MaxWidth=200; el.Background=new SolidColorBrush(Palette.Mix(surface,col,0.2)); el.BorderBrush=new SolidColorBrush(col); el.BorderThickness=new Thickness(1.5); el.CornerRadius=new CornerRadius(9); el.Padding=new Thickness(13,7,13,8); }
  else if(n.Heading) { // 深色背景上淡底不够分辨，加深一点并加一道淡的分支色描边
   tb.FontSize=14; tb.FontWeight=FontWeights.SemiBold; tb.MaxWidth=200; el.Background=new SolidColorBrush(Palette.Mix(surface,col,light?0.1:0.16)); el.CornerRadius=new CornerRadius(7); el.Padding=new Thickness(10,5,10,6);
   if(!light) { el.BorderBrush=new SolidColorBrush(Color.FromArgb(0x8C,col.R,col.G,col.B)); el.BorderThickness=new Thickness(1); }
  }
  else { tb.FontSize=13.5; tb.MaxWidth=task>=0?175:190; el.Background=Brushes.Transparent; el.Padding=new Thickness(6,3,6,4); }
  if(n.Full!=n.Text) el.ToolTip=n.Full;
  if(n.Kids.Count>0) el.Cursor=Cursors.Hand;
  el.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity)); n.W=Math.Ceiling(el.DesiredSize.Width); n.H=Math.Ceiling(el.DesiredSize.Height); n.El=el;
  foreach(var k in Shown(n)) MakeMapElement(k);
 }
 static double MapGap(MapNode n) { return n.Depth==0?18:n.Depth==1?10:4; }
 static double MeasureSub(MapNode n) {
  var kids=Shown(n); double s=0; for(int i=0;i<kids.Count;i++) s+=MeasureSub(kids[i])+(i>0?MapGap(n):0);
  return n.Sub=Math.Max(n.H,s);
 }
 static void PlaceMap(MapNode n,double x,double top) {
  n.X=x; n.Y=top+(n.Sub-n.H)/2;
  var kids=Shown(n); double total=0; for(int i=0;i<kids.Count;i++) total+=kids[i].Sub+(i>0?MapGap(n):0);
  double y=top+(n.Sub-total)/2, cx=x+n.W+(n.Depth==0?56:n.Depth==1?40:28);
  foreach(var k in kids) { PlaceMap(k,cx,y); y+=k.Sub+MapGap(n); }
 }
 void RenderMap(bool fit) {
  if(fit) { mapBox=null; if(!Presenting) mapSelLine=NoSel; }
  mapCanvas.Children.Clear(); mapRoot=BuildMap();
  bool has=mapRoot!=null; mapEmpty.Visibility=has?Visibility.Collapsed:Visibility.Visible;
  mapFitBtn.IsEnabled=mapCopyBtn.IsEnabled=mapSaveBtn.IsEnabled=has; UpdatePresenting();
  if(!has) { mapCanvas.Width=mapCanvas.Height=0; return; }
  if(Presenting) UnfoldPresentation(mapRoot);
  MakeMapElement(mapRoot); MeasureSub(mapRoot); const double pad=32; PlaceMap(mapRoot,pad,pad);
  double maxX=0, maxY=0; var all=new List<MapNode>(); Collect(mapRoot,all);
  foreach(var n in all) { maxX=Math.Max(maxX,n.X+n.W+(n.Folded&&n.Kids.Count>0?56:0)); maxY=Math.Max(maxY,n.Y+n.H); }
  mapCanvas.Width=maxX+pad; mapCanvas.Height=maxY+pad;
  if(Presenting) { presentNodes.Clear(); presentNodes.AddRange(all); presentLines.Clear(); presentCount=Math.Max(1,Math.Min(presentCount,presentNodes.Count)); }
  // 先画连线，再画节点，节点盖在线头上。
  foreach(var n in all) foreach(var k in Shown(n)) {
   double x1=n.X+n.W, y1=n.Y+n.H/2, x2=k.X, y2=k.Y+k.H/2, mid=(x2-x1)*0.5;
   var fig=new PathFigure { StartPoint=new Point(x1,y1) }; fig.Segments.Add(new BezierSegment(new Point(x1+mid,y1),new Point(x2-mid,y2),new Point(x2,y2),true));
   var geo=new PathGeometry(); geo.Figures.Add(fig);
   var path=new System.Windows.Shapes.Path { Data=geo, Stroke=new SolidColorBrush(Palette.Branch(k.Branch)), StrokeThickness=n.Depth==0?2.4:1.6, Opacity=0.85, IsHitTestVisible=false }; mapCanvas.Children.Add(path); if(Presenting) presentLines[k]=path;
  }
  foreach(var n in all) {
   Canvas.SetLeft(n.El,n.X); Canvas.SetTop(n.El,n.Y); mapCanvas.Children.Add(n.El);
   if(!Presenting && n.Folded && n.Kids.Count>0) { // 折起来的节点右边挂一个“+N”，点它也能展开
    // 徽标字号和叶子一样大，用分支色，演示时一眼能看出“这里还收着几项”
    var bc=Palette.Branch(n.Branch);
    var badge=new Border { Tag=new MapFold { N=n }, Cursor=Cursors.Hand, Height=22, MinWidth=26, CornerRadius=new CornerRadius(11), Padding=new Thickness(8,0,8,0), Background=new SolidColorBrush(Palette.Mix(Palette.Surface.Color,bc,0.14)), BorderBrush=new SolidColorBrush(bc), BorderThickness=new Thickness(1.5), ToolTip="已折叠，点击展开",
     Child=new TextBlock { Text="+"+Descendants(n), FontSize=13, FontWeight=FontWeights.SemiBold, FontFamily=FontFamily, Foreground=new SolidColorBrush(bc), VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Center } };
    Canvas.SetLeft(badge,n.X+n.W+8); Canvas.SetTop(badge,n.Y+n.H/2-11); mapCanvas.Children.Add(badge);
   }
  }
  // 选中的节点：外面一圈强调色描边；有下级时右边挂一个折叠开关（折起后就是“+N”）。
  mapRing=null; var sel=Presenting?null:FindLine(mapRoot,mapSelLine); if(sel==null && !Presenting) mapSelLine=NoSel;
  if(sel!=null) {
   double r=sel.Depth==0?15:sel.Depth==1?12:sel.Heading?10:7;
   var ring=new System.Windows.Shapes.Rectangle { Width=sel.W+8, Height=sel.H+8, RadiusX=r, RadiusY=r, Stroke=Palette.Accent, StrokeThickness=2, IsHitTestVisible=false };
   Canvas.SetLeft(ring,sel.X-4); Canvas.SetTop(ring,sel.Y-4); mapCanvas.Children.Add(ring); mapRing=ring;
   if(sel.Kids.Count>0 && !sel.Folded) {
    var bc=sel.Depth==0?Palette.Accent.Color:Palette.Branch(sel.Branch);
    var tog=new Border { Tag=new MapFold { N=sel }, Width=18, Height=18, CornerRadius=new CornerRadius(9), Cursor=Cursors.Hand, ToolTip="折叠下级（空格）", Background=Palette.Panel, BorderBrush=new SolidColorBrush(bc), BorderThickness=new Thickness(1.5),
     Child=new System.Windows.Shapes.Rectangle { Width=8, Height=1.6, Fill=new SolidColorBrush(bc), HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center } };
    Canvas.SetLeft(tog,sel.X+sel.W+6); Canvas.SetTop(tog,sel.Y+sel.H/2-9); mapCanvas.Children.Add(tog);
   }
  }
  mapHint.Text=sel!=null?MapHintSel:MapHintIdle;
  if(Presenting) ShowPresentation(-1,false);
  if(fit) { mapMoved=false; Dispatcher.BeginInvoke(new Action(FitMap),DispatcherPriority.Loaded); }
 }
 static void Collect(MapNode n,List<MapNode> all) { all.Add(n); foreach(var k in Shown(n)) Collect(k,all); }
 static MapNode FindKey(MapNode n,string key) { if(n.Key==key) return n; foreach(var k in Shown(n)) { var f=FindKey(k,key); if(f!=null) return f; } return null; }
 // 整张图缩放到放得下并居中；小图最多放大到 1.6 倍，免得全屏时字大得夸张。
 // 整张图要能一眼看全，所以不为字号把图裁掉；只有特别大的图才停在 0.6 倍，从左上开始摆，拖动或滚轮看其余部分。
 // 字号靠收窄换行宽度保证（长句往下长，图就不会被撑宽、缩得太小）；演示用全屏，最多放大 1.6 倍。
 void FitMap() {
  if(!mapping || mapRoot==null) return; StopMapPanAnimation();
  double top=10, vw=mapView.ActualWidth, vh=mapView.ActualHeight-top-(Presenting?0:20); if(vw<20 || vh<20 || mapCanvas.Width<=0) return;
  double s=Math.Max(0.6,Math.Min(1.6,Math.Min(vw/mapCanvas.Width,vh/mapCanvas.Height)));
  double cw=mapCanvas.Width*s, ch=mapCanvas.Height*s;
  mapScale.ScaleX=mapScale.ScaleY=s; mapPan.X=cw<=vw?(vw-cw)/2:0; mapPan.Y=top+(ch<=vh?(vh-ch)/2:0);
 }
 void ZoomMap(double factor,Point? at) {
  if(!mapping) return; StopMapPanAnimation();
  var q=at ?? new Point(mapView.ActualWidth/2,mapView.ActualHeight/2);
  double s=mapScale.ScaleX, ns=Clamp(s*factor,0.15,4); if(Math.Abs(ns-s)<1e-6) return;
  mapPan.X=q.X-(q.X-mapPan.X)*ns/s; mapPan.Y=q.Y-(q.Y-mapPan.Y)*ns/s; mapScale.ScaleX=mapScale.ScaleY=ns; mapMoved=true;
 }
 // 点节点折叠 / 展开它下面的分支。标题节点和预览共用折叠记录；列表节点只在这次打开期间记住。被点的节点保持在原来的屏幕位置。
 void ToggleMapNode(MapNode n) {
  if(n.Kids.Count==0) return;
  double sx=n.X*mapScale.ScaleX+mapPan.X, sy=n.Y*mapScale.ScaleY+mapPan.Y; string key=n.Key;
  if(n.Heading && n.Block>=0 && n.Block<foldKey.Length) { var set=FoldsOf(currentNote,true); if(!set.Remove(foldKey[n.Block])) set.Add(foldKey[n.Block]); SaveFolds(); }
  else { string k=currentNote+"|"+n.Key; if(!mapFolds.Remove(k)) mapFolds.Add(k); }
  RenderMap(false);
  var m=mapRoot==null?null:FindKey(mapRoot,key);
  if(m!=null) { mapPan.X+=sx-(m.X*mapScale.ScaleX+mapPan.X); mapPan.Y+=sy-(m.Y*mapScale.ScaleY+mapPan.Y); }
 }
 // ——— 脑图编辑：每个节点对应笔记里的一行，改动直接写回 Markdown，“编辑”“预览”里看到的是同一份 ———
 const int NoSel=-3, RootSel=-2; const string NewTopic="新主题";
 const string MapHintIdle="点节点选中 · 双击改字 · 滚轮缩放 · 拖动空白处平移", MapHintSel="Tab 加下级 · Enter 加同级 · F2 改字 · Delete 删除 · 空格折叠";
 int mapSelLine=-3, mapBoxLine=-1; TextBox mapBox; UIElement mapRing; string mapBoxPre="", mapBoxOld=""; MapNode mapHitFold;
 static readonly Regex MapListRe=new Regex(@"^([ \t]*)([-*+]|\d{1,9}[.)])(\s+)(\[[ xX]\]\s+)?(.*)$");
 // 拆成“结构前缀 + 可改的文字”：改字只换后半段，标题符号、列表符号、缩进和待办框都留着。
 static void SplitLine(string l,out string pre,out string body) {
  var m=HeadingRe.Match(l); if(m.Success) { pre=l.Substring(0,m.Groups[2].Index); body=m.Groups[2].Value; return; }
  m=MapListRe.Match(l); if(m.Success) { pre=l.Substring(0,m.Groups[5].Index); body=m.Groups[5].Value; return; }
  m=Regex.Match(l,@"^(\s*>\s?)(.*)$"); if(m.Success) { pre=m.Groups[1].Value; body=m.Groups[2].Value; return; }
  int i=0; while(i<l.Length && (l[i]==' ' || l[i]=='\t')) i++; pre=l.Substring(0,i); body=l.Substring(i);
 }
 static int KeyOf(MapNode n) { return n.Depth==0?RootSel:n.Line; }
 static MapNode FindLine(MapNode n,int line) {
  if(n==null || line==NoSel) return null; if(line==RootSel) return n.Depth==0?n:null;
  if(n.Line==line && n.Depth>0) return n; foreach(var k in Shown(n)) { var f=FindLine(k,line); if(f!=null) return f; } return null;
 }
 MapNode MapSel() { return Presenting?null:FindLine(mapRoot,mapSelLine); }
 void SelectMap(MapNode n) { if(mapBox!=null) EndMapEdit(true); mapSelLine=n==null?NoSel:KeyOf(n); RenderMap(false); var m=MapSel(); if(m!=null) ShowMapNode(m); }
 // 选中或新建的节点跑到可视区外时，平移把它挪进来。
 void ShowMapNode(MapNode n) {
  double s=mapScale.ScaleX, x=n.X*s+mapPan.X, y=n.Y*s+mapPan.Y, w=n.W*s, h=n.H*s, vw=mapView.ActualWidth, vh=mapView.ActualHeight, m=24;
  if(x<m) mapPan.X+=m-x; else if(x+w>vw-m) mapPan.X-=Math.Min(x-m,x+w-(vw-m));
  if(y<m) mapPan.Y+=m-y; else if(y+h>vh-m) mapPan.Y-=Math.Min(y-m,y+h-(vh-m));
 }
 // 这个节点连同下级在原文里占到哪一行为止（不含）。
 int MapEnd(MapNode n) {
  if(n.Depth==0) return lines.Count;
  if(n.Kind==1 && n.Block>=0 && n.Block<foldEnd.Length) { int e=foldEnd[n.Block]; return e<blocks.Count?blocks[e][0]:lines.Count; }
  int last=n.Last; foreach(var k in n.Kids) last=Math.Max(last,MapEnd(k)-1); return last+1;
 }
 int TrimBack(int at,int floor) { while(at>Math.Max(0,floor) && lines[at-1].Trim().Length==0) at--; return at; }
 string HashesOf(MapNode n) { var m=HeadingRe.Match(lines[n.Line]); return m.Success?m.Groups[1].Value:"##"; }
 // 插入一行新节点并马上进入改字；标题前后各留一个空行。
 void MapInsert(int at,string line,bool spaced) {
  var add=new List<string>(); if(spaced && at>0 && lines[at-1].Trim().Length>0) add.Add(""); int idx=add.Count; add.Add(line);
  if(spaced && at<lines.Count && lines[at].Trim().Length>0) add.Add("");
  lines.InsertRange(at,add); WriteLines(); mapSelLine=at+idx; RenderMap(false); Refit();
  var n=MapSel(); if(n!=null) { ShowMapNode(n); BeginMapEdit(n); }
 }
 void Refit() { if(!mapMoved) FitMap(); var m=MapSel(); if(m!=null) ShowMapNode(m); }
 void Unfold(MapNode n) { if(n.Folded) ToggleMapNode(n); }
 void MapAddChild(MapNode n) {
  if(n==null) return; if(n.Kind==4) { SetStatus("代码块下面不能加节点",Palette.Muted); return; }
  Unfold(n); n=FindLine(mapRoot,KeyOf(n)) ?? n; LoadLines();
  if(n.Depth==0 || n.Kind==1) {
   MapNode head=null, plain=null; foreach(var k in n.Kids) { if(k.Kind==1) head=k; else if(head==null) plain=k; }
   if(head!=null) { MapInsert(TrimBack(MapEnd(n),n.Line+1),HashesOf(head)+" "+NewTopic,true); return; }
   int at=plain!=null?MapEnd(plain):(n.Line>=0?n.Line+1:TrimBack(lines.Count,0));
   MapInsert(at,"- "+NewTopic,false); return;
  }
  string pre, body; SplitLine(lines[n.Line],out pre,out body);
  if(n.Kind==2) {
   var m=MapListRe.Match(lines[n.Line]); string ind=m.Groups[1].Value+new string(' ',m.Groups[2].Length+m.Groups[3].Length);
   MapInsert(MapEnd(n),ind+"- "+(m.Groups[4].Length>0?"[ ] ":"")+NewTopic,false); return;
  }
  if(pre.Trim().Length>0) { SetStatus("引用下面不能加节点",Palette.Muted); return; }
  // 普通文字没法带下级：先把它变成列表项，再在下面缩进一项
  lines[n.Line]=pre+"- "+body; MapInsert(n.Line+1,pre+"  - "+NewTopic,false);
 }
 void MapAddSibling(MapNode n) {
  if(n==null) return; if(n.Depth==0) { MapAddChild(n); return; }
  LoadLines(); int at=MapEnd(n);
  if(n.Kind==1) { MapInsert(TrimBack(at,n.Line+1),HashesOf(n)+" "+NewTopic,true); return; }
  if(n.Kind==2) {
   var m=MapListRe.Match(lines[n.Line]); string mk=m.Groups[2].Value; var num=Regex.Match(mk,@"^(\d+)([.)])$");
   if(num.Success) { int v; if(int.TryParse(num.Groups[1].Value,out v)) mk=(v+1)+num.Groups[2].Value; }
   MapInsert(at,m.Groups[1].Value+mk+" "+(m.Groups[4].Length>0?"[ ] ":"")+NewTopic,false); return;
  }
  string pre, body; SplitLine(lines[n.Line],out pre,out body);
  MapInsert(at,n.Kind==4?NewTopic:pre+NewTopic,false);
 }
 void MapDelete(MapNode n) {
  if(n==null) return; if(n.Depth==0) { SetStatus("中心主题不能删除，可以双击改字",Palette.Muted); return; }
  LoadLines(); int from=n.Line, to=MapEnd(n); if(n.Kind==1) to=TrimBack(to,from+1);
  var sib=n.Parent.Kids; int i=sib.IndexOf(n); MapNode next=i>0?sib[i-1]:(i+1<sib.Count?sib[i+1]:n.Parent);
  int key=KeyOf(next); if(key>=to) key-=to-from;
  lines.RemoveRange(from,to-from);
  if(from>0 && from<lines.Count && lines[from-1].Trim().Length==0 && lines[from].Trim().Length==0) { lines.RemoveAt(from); if(key>from) key--; }
  WriteLines(); mapSelLine=key; RenderMap(false); Refit();
 }
 void BeginMapEdit(MapNode n) {
  if(n==null || Presenting) return;
  if(n.Line<0 || n.Kind==4) { SetStatus("这个节点请切到“编辑”里改",Palette.Muted); return; }
  LoadLines(); string pre, body; SplitLine(lines[n.Line],out pre,out body);
  var box=Ui.Input(body); box.FontSize=n.Depth==0?18:n.Depth==1?15:n.Heading?14:13.5; box.FontWeight=n.Heading || n.Depth<=1?FontWeights.SemiBold:FontWeights.Normal;
  box.MinWidth=Math.Max(n.W+8,140); box.MaxWidth=360; box.Padding=new Thickness(6,3,6,4); box.BorderBrush=Palette.Accent;
  Canvas.SetLeft(box,n.X-4); Canvas.SetTop(box,n.Y+n.H/2-(box.FontSize*1.33+9)/2); Panel.SetZIndex(box,10); mapCanvas.Children.Add(box);
  mapBox=box; mapBoxLine=n.Line; mapBoxPre=pre; mapBoxOld=body; if(mapRing!=null) mapRing.Visibility=Visibility.Collapsed; // 输入框自己有强调色描边
  box.LostKeyboardFocus+=(s,e)=> { if(mapBox==box) EndMapEdit(true); };
  Dispatcher.BeginInvoke(new Action(()=> { if(mapBox==box) { box.Focus(); box.SelectAll(); } }),DispatcherPriority.Input);
 }
 void EndMapEdit(bool keep) {
  var box=mapBox; if(box==null) return; mapBox=null;
  string t=box.Text.Replace("\r"," ").Replace("\n"," ").Trim();
  if(keep && t.Length>0 && t!=mapBoxOld) { LoadLines(); if(mapBoxLine>=0 && mapBoxLine<lines.Count) { lines[mapBoxLine]=mapBoxPre+t; WriteLines(); } }
  RenderMap(false); Refit(); if(mapping) mapView.Focus();
 }
 // 方向键在节点间走：左回上级，右进下级（折着的先展开），上下换同级。
 void MapMove(MapNode n,Key key) {
  MapNode to=null;
  if(key==Key.Left) to=n.Parent;
  else if(key==Key.Right) { if(n.Folded) { ToggleMapNode(n); return; } if(n.Kids.Count>0) to=n.Kids[0]; }
  else if(n.Parent!=null) { var sib=n.Parent.Kids; int i=sib.IndexOf(n)+(key==Key.Up?-1:1); if(i>=0 && i<sib.Count) to=sib[i]; }
  if(to!=null) SelectMap(to);
 }
 void MapUndo(bool redo) {
  var from=redo?redoTexts:undoTexts; var to=redo?undoTexts:redoTexts; if(from.Count==0) return;
  if(mapBox!=null) EndMapEdit(false);
  to.Add(editor.Text); string prev=from[from.Count-1]; from.RemoveAt(from.Count-1); histGroup=null;
  editor.Text=prev; leftPreviewText=prev; RenderMap(false); Refit();
 }
 void MapKey(object sender,KeyEventArgs e) {
  if(!mapping || settingsOpen || closeOpen) return;
  var key=e.Key==Key.System?e.SystemKey:e.Key; var mods=Keyboard.Modifiers;
  if(mapBox!=null) {
   if(key==Key.Enter && mods==ModifierKeys.None) { EndMapEdit(true); e.Handled=true; }
   else if(key==Key.Tab && mods==ModifierKeys.None) { EndMapEdit(true); MapAddChild(MapSel()); e.Handled=true; }
   return;
  }
  if(Presenting && mods==ModifierKeys.None) {
   if(key==Key.Space || key==Key.Right || key==Key.Down || key==Key.Enter || key==Key.PageDown) PresentationStep(1);
   else if(key==Key.Left || key==Key.Up || key==Key.Back || key==Key.PageUp) PresentationStep(-1);
   else if(key==Key.Home) { presentCount=1; ShowPresentation(-1,false); }
   else if(key==Key.End) { presentCount=presentNodes.Count; ShowPresentation(-1,false); }
   else return; e.Handled=true; return;
  }
  if(Presenting || (mods&(ModifierKeys.Control|ModifierKeys.Alt))!=0) return;
  var n=MapSel();
  if(n==null) { if(mapRoot!=null && (key==Key.Enter || key==Key.Tab || key==Key.F2 || key==Key.Right || key==Key.Left || key==Key.Up || key==Key.Down)) { SelectMap(mapRoot); e.Handled=true; } return; }
  if(key==Key.Tab || key==Key.Insert) MapAddChild(n);
  else if(key==Key.Enter) MapAddSibling(n);
  else if(key==Key.F2) BeginMapEdit(n);
  else if(key==Key.Delete || key==Key.Back) MapDelete(n);
  else if(key==Key.Space) { if(n.Kids.Count>0) ToggleMapNode(n); }
  else if(key==Key.Left || key==Key.Right || key==Key.Up || key==Key.Down) MapMove(n,key);
  else return;
  e.Handled=true;
 }
 // 导出用：不带缩放平移，按 2 倍清晰度画整张图（太大时降低倍数，最多约 2500 万像素），底色用编辑区的底色。
 BitmapSource MapBitmap() {
  double w=mapCanvas.Width, h=mapCanvas.Height; double k=Math.Min(2,Math.Sqrt(25e6/(w*h)));
  int savedCount=presentCount; if(Presenting) { presentCount=presentNodes.Count; ShowPresentation(-1,false); }
  var old=mapCanvas.RenderTransform; mapCanvas.RenderTransform=Transform.Identity; mapCanvas.Background=Palette.Surface;
  try {
   mapCanvas.UpdateLayout();
   var rtb=new RenderTargetBitmap((int)Math.Ceiling(w*k),(int)Math.Ceiling(h*k),96*k,96*k,PixelFormats.Pbgra32); rtb.Render(mapCanvas); rtb.Freeze(); return rtb;
  } finally { mapCanvas.RenderTransform=old; mapCanvas.Background=null; if(Presenting) { presentCount=savedCount; ShowPresentation(-1,false); } }
 }
 void CopyMapImage() {
  if(mapRoot==null) return;
  try { Clipboard.SetImage(MapBitmap()); SetStatus("脑图已复制成图片",Palette.Accent); } catch(Exception ex) { Msg("复制图片失败："+ex.Message); }
 }
 void ExportMap() {
  if(mapRoot==null) return;
  modal++;
  try {
   using(var d=new Forms.SaveFileDialog { Title="导出脑图图片", Filter="PNG 图片 (*.png)|*.png", FileName=TitleOf(editor.Text)+"_脑图.png", InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.Desktop) }) {
    if(d.ShowDialog(new Win32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle))!=Forms.DialogResult.OK) return;
    var enc=new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(MapBitmap()));
    using(var f=File.Create(d.FileName)) enc.Save(f);
    SetStatus("已导出脑图图片",Palette.Accent);
   }
  } catch(Exception ex) { Msg("导出失败："+ex.Message); }
  finally { modal--; }
 }
 void SetPanelChrome(bool on) {
  outerBox.Background=on?Palette.Panel:Brushes.Transparent; outerBox.BorderThickness=on && !fullscreen?(Docked?new Thickness(1,0,1,1):new Thickness(1)):new Thickness(0);
  shadowFar.Visibility=shadowNear.Visibility=on?Visibility.Visible:Visibility.Collapsed;
 }
 // 刘海外形：顶边整宽贴住屏幕，两侧以半径 ear 的内凹弧收进来，底部圆角 r。
 static Geometry NotchGeometry(double w,double h,double ear,double r) {
  if(w<=2*(ear+r)) return Geometry.Empty;
  var inv=System.Globalization.CultureInfo.InvariantCulture;
  string d=string.Format(inv,"M0,0 L{0},0 A{1},{1} 0 0 0 {2},{1} L{2},{3} A{4},{4} 0 0 1 {5},{6} L{7},{6} A{4},{4} 0 0 1 {1},{3} L{1},{1} A{1},{1} 0 0 0 0,0 Z",w,ear,w-ear,h-r,r,w-ear-r,h,ear+r);
  var g=Geometry.Parse(d); g.Freeze(); return g;
 }
 void ShowPinState() {
  pinButton.Background=pinned?Palette.AccentBg:Brushes.Transparent; pinButton.Foreground=pinned?Palette.Accent:Palette.Muted;
  pinButton.ToolTip=pinned?"已固定：点别处不会收起。点击取消固定":"固定窗口：点别处不收起，一直显示在最前";
 }
 void ShowDropZone() {
  if(dropZone==null) return; bool empty=files.Count==0;
  dropZone.Visibility=empty?Visibility.Visible:Visibility.Collapsed; dropZone.Stroke=Palette.Rule; dropZone.Fill=Brushes.Transparent;
  shelfCount.Text=empty?"":files.Count.ToString();
 }
 void SetStatus(string text,Brush tone) { if(saveStatus==null) return; saveStatus.Text=text; saveStatus.Foreground=tone==Palette.Warn?Palette.Warn:Palette.Muted; statusDot.Fill=tone; }
 void ShowSleepState() {
  sleepButton.Content=Ui.IconLabel(G_Sun,awake?"唤醒中":"保持唤醒");
  sleepButton.Background=awake?Palette.AccentBg:Brushes.Transparent; sleepButton.Foreground=awake?Palette.Accent:Palette.Muted;
  sleepButton.ToolTip=awake?"正在阻止系统自动睡眠；点击恢复":"阻止系统自动睡眠";
 }
 static void Choose(Button b,bool on) { b.Background=on?Palette.Raised:Brushes.Transparent; b.Foreground=on?Palette.Text:Palette.Muted; }
 void Msg(string text) { modal++; try { Dialog(IsLoaded?this:null,null,text,"知道了",null,false,null,null); } finally { modal--; } }
 bool Ask(string text,string title,string ok,bool warn) { modal++; try { return Dialog(IsLoaded?this:null,title,text,ok,"取消",warn,null,null); } finally { modal--; } }
 internal static void Notice(string text) { Dialog(null,null,text,"知道了",null,false,null,null); }
 // ——— 对话框 ———
 // 和面板同一套样式的小窗口，代替系统 MessageBox：面板色圆角卡片、柔和阴影、右下角按钮（主按钮强调色，危险操作用警示色）。
 // Enter 等于点主按钮，Esc 关闭。返回 true 表示点了主按钮；canOk 返回 false 时不关闭。
 static bool Dialog(Window owner,string title,object body,string ok,string cancel,bool warn,Func<bool> canOk,UIElement focus) {
  var w=new Window { WindowStyle=WindowStyle.None, AllowsTransparency=true, Background=Brushes.Transparent, ResizeMode=ResizeMode.NoResize, ShowInTaskbar=owner==null, Topmost=true, SizeToContent=SizeToContent.WidthAndHeight, FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI"), Title=title??"DotNotes" };
  if(owner!=null) { w.Owner=owner; w.WindowStartupLocation=WindowStartupLocation.CenterOwner; foreach(var key in new object[] { "HoverBrush","AccentBrush",typeof(ToolTip) }) if(owner.Resources.Contains(key)) w.Resources[key]=owner.Resources[key]; }
  else { w.WindowStartupLocation=WindowStartupLocation.CenterScreen; w.Resources["HoverBrush"]=new SolidColorBrush(Palette.Light?Colors.Black:Colors.White); w.Resources["AccentBrush"]=new SolidColorBrush(Palette.Accent.Color); }
  var root=new Grid { Margin=new Thickness(28) }; w.Content=root;
  root.Children.Add(new Border { Background=Palette.Panel, CornerRadius=new CornerRadius(12), Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=28, ShadowDepth=8, Direction=270, Opacity=0.45, Color=Colors.Black } });
  var card=new Border { Background=Palette.Panel, BorderBrush=Palette.Edge, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(12), Padding=new Thickness(20,18,20,16) }; root.Children.Add(card);
  var stack=new StackPanel { Width=340 }; card.Child=stack;
  if(!string.IsNullOrEmpty(title)) stack.Children.Add(new TextBlock { Text=title, Foreground=Palette.Text, FontSize=14.5, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,8) });
  if(body is string) stack.Children.Add(new TextBlock { Text=(string)body, Foreground=string.IsNullOrEmpty(title)?Palette.Text:Palette.Muted, FontSize=13, TextWrapping=TextWrapping.Wrap, LineHeight=20 });
  else if(body!=null) stack.Children.Add((UIElement)body);
  var row=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,18,0,0) }; stack.Children.Add(row);
  bool result=false;
  if(cancel!=null) { var c=Ui.Button(cancel,null); c.Background=Palette.Raised; c.Padding=new Thickness(16,0,16,0); c.Margin=new Thickness(0,0,8,0); c.Click+=(s,e)=>w.Close(); row.Children.Add(c); }
  var okBtn=Ui.Button(ok,null); okBtn.Padding=new Thickness(16,0,16,0); okBtn.FontWeight=FontWeights.SemiBold;
  if(warn) { okBtn.Background=new SolidColorBrush(Palette.Mix(Palette.Panel.Color,Palette.Warn.Color,0.18)); okBtn.Foreground=Palette.Warn; } else { okBtn.Background=Palette.AccentBg; okBtn.Foreground=Palette.Accent; }
  okBtn.Click+=(s,e)=> { if(canOk!=null && !canOk()) return; result=true; w.Close(); }; row.Children.Add(okBtn);
  w.PreviewKeyDown+=(s,e)=> { if(e.Key==Key.Escape) { w.Close(); e.Handled=true; } else if(e.Key==Key.Enter) { okBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); e.Handled=true; } };
  w.Loaded+=(s,e)=> { var f=focus??okBtn; f.Focus(); var tb=f as TextBox; if(tb!=null) tb.SelectAll(); };
  w.ShowDialog(); return result;
 }
 static bool HasFiles(DragEventArgs e) { return e.Data.GetDataPresent(DataFormats.FileDrop); }

 // ——— 快捷键 ———
 void OnKey(object sender,KeyEventArgs e) {
  var key=e.Key==Key.System?e.SystemKey:e.Key; bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0, shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
  if(expanded) transient=false;
  if(closeOpen) {
   if(key==Key.Escape) { HideCloseDialog(); e.Handled=true; return; }
   if(key==Key.Enter) { ConfirmClose("archive"); e.Handled=true; return; }
  }
  if(key==Key.F5 && expanded && mapping && !settingsOpen && !closeOpen) { TogglePresentation(); e.Handled=true; return; }
  if(key==Key.F11 && expanded) { SetFullscreen(!fullscreen); e.Handled=true; return; }
  if(key==Key.Escape) {
   if(Presenting) ExitPresentation();
   else if(settingsOpen) CloseSettings();
   else if(mapBox!=null) EndMapEdit(false);
   else if(mapping && !Presenting && MapSel()!=null) { mapSelLine=NoSel; RenderMap(false); }
   else if(previewing && selFrom>=0) ClearSelection();
   else if(fullscreen && editingBlock<0) SetFullscreen(false);
   else if(editingBlock>=0) { FinishEdit(); RenderPreview(); }
   else Collapse();
   e.Handled=true; return;
  }
  if(!ctrl && expanded && previewing && selFrom>=0 && editingBlock<0 && !settingsOpen && !closeOpen && (key==Key.Delete || key==Key.Back)) { DeleteSelection(); e.Handled=true; return; }
  if(!ctrl || !expanded) return;
  if(!mapping && !settingsOpen && !closeOpen && !shift) { // Ctrl+1/2/3 标题，Ctrl+0 正文（脑图里 Ctrl+0 是适应窗口）
   int lv=key==Key.D1 || key==Key.NumPad1?1:key==Key.D2 || key==Key.NumPad2?2:key==Key.D3 || key==Key.NumPad3?3:key==Key.D0 || key==Key.NumPad0?0:-1;
   if(lv>=0) { ApplyHeading(lv); e.Handled=true; return; }
  }
  // 预览里的撤销、重做、全选、复制；正在改的那一行里有选中文字时，Ctrl+C 仍按输入框自己的复制。
  if(key==Key.M) { CloseSettings(); if(mapping) SetPreview(mapFromPreview); else SetMap(); e.Handled=true; return; }
  if(mapping && !settingsOpen) {
   if(key==Key.D0 || key==Key.NumPad0) { mapMoved=false; FitMap(); e.Handled=true; return; }
   if(key==Key.OemPlus || key==Key.Add) { ZoomMap(1.2,null); e.Handled=true; return; }
   if(key==Key.OemMinus || key==Key.Subtract) { ZoomMap(1/1.2,null); e.Handled=true; return; }
  }
  if(mapping && mapBox==null && !settingsOpen && !closeOpen && (key==Key.Z || key==Key.Y)) { MapUndo(key==Key.Y || shift); e.Handled=true; return; }
  if(previewing && !settingsOpen && !closeOpen) {
   if(key==Key.Z || key==Key.Y) { PreviewUndo(key==Key.Y || shift); e.Handled=true; return; }
   if(key==Key.A && editingBlock<0) { SelectAllBlocks(); e.Handled=true; return; }
   if(key==Key.C && selFrom>=0) { CopySelection(shift); e.Handled=true; return; }
   if(key==Key.X && selFrom>=0) { CutSelection(); e.Handled=true; return; }
  }
  var target=ActiveBox();
  if(key==Key.B && target!=null) { Wrap(target,"**","**"); e.Handled=true; }
  else if(key==Key.I && target!=null) { Wrap(target,"*","*"); e.Handled=true; }
  else if(key==Key.N) { NewNote(); e.Handled=true; }
  else if(key==Key.E) { CloseSettings(); SetPreview(!previewing); e.Handled=true; }
  else if(key==Key.Tab) { CycleNote(shift?-1:1); e.Handled=true; }
  else if(key==Key.W) { CloseNote(currentNote); e.Handled=true; }
  else if(key==Key.O) { CloseSettings(); OpenFiles(); e.Handled=true; }
 }

 // ——— 设置 ———
 void LoadSettings() {
  try {
   string f=Path.Combine(dir,"settings.ini"); if(!File.Exists(f)) return; bool hasRate=false; int legacyRate=2;
   foreach(var raw in File.ReadAllLines(f)) {
    int i=raw.IndexOf('='); if(i<=0) continue; string k=raw.Substring(0,i).Trim(), v=raw.Substring(i+1).Trim(); int n; double d;
    bool num=double.TryParse(v,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out d) && !double.IsInfinity(d);
    if(k=="hoverDelayMs" && int.TryParse(v,out n)) hoverDelay=Math.Max(0,Math.Min(3000,n));
    else if(k=="panelLeft" && num) panelLeft=d;
    else if(k=="panelTop" && num) panelTop=d;
    else if(k=="dotX" && num) dotX=d;
    else if(k=="dotY" && num) dotY=d;
    else if(k=="panelW" && num) prefW=Math.Max(MinW,d);
    else if(k=="panelH" && num) prefH=Math.Max(MinH,d);
    else if(k=="hoverEnabled") hoverOn=v!="0";
    else if(k=="pinned") pinned=v=="1";
    else if(k=="breathe") legacyRate=v=="0"?0:2;
    else if(k=="breatheRate") { hasRate=true; breatheRate=int.TryParse(v,out n) && n>=0 && n<=3?n:2; }
    else if(k=="dotSize") dotSize=int.TryParse(v,out n) && n>=5 && n<=16?n:8;
    else if(k=="accent") { try { accent=(Color)ColorConverter.ConvertFromString(v); } catch {} }
    else if(k=="background") { try { background=(Color)ColorConverter.ConvertFromString(v); } catch {} }
    else if(k=="archiveDir" && v.Length>0) archiveDir=v;
   }
   if(!hasRate) breatheRate=legacyRate;
   string oldRoot=AppData("NotchNotesWindows"); // 旧数据文件夹搬走后，指向它里面的归档位置跟着改
   if(archiveDir!=null && !Directory.Exists(oldRoot) && archiveDir.StartsWith(oldRoot,StringComparison.OrdinalIgnoreCase)) archiveDir=dir+archiveDir.Substring(oldRoot.Length);
  } catch {}
 }
 void SaveSettings() {
  try {
   Func<double,string> pos=v=>double.IsNaN(v)?"":Math.Round(v,1).ToString(System.Globalization.CultureInfo.InvariantCulture);
   double panelLeft=this.panelLeft, panelTop=this.panelTop, dotX=this.dotX, dotY=this.dotY; // 全屏时记的是全屏前的位置
   if(fullscreen && beforeFull!=null) { panelLeft=beforeFull[0]; panelTop=beforeFull[1]; dotX=beforeFull[4]; dotY=beforeFull[5]; }
   var text=new[] { "hoverEnabled="+(hoverOn?"1":"0"), "hoverDelayMs="+hoverDelay, "archiveDir="+(archiveDir??""), "pinned="+(pinned?"1":"0"), "breatheRate="+breatheRate, "dotSize="+dotSize, "accent="+Palette.ToHex(accent), "background="+Palette.ToHex(background),
    "panelLeft="+pos(panelLeft), "panelTop="+pos(panelTop), "dotX="+pos(dotX), "dotY="+pos(dotY), "panelW="+pos(prefW), "panelH="+pos(prefH) };
   string target=Path.Combine(dir,"settings.ini"), temp=Path.Combine(dir,"settings.tmp");
   File.WriteAllLines(temp,text,System.Text.Encoding.UTF8); if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
  } catch(Exception ex) { Msg("设置保存失败："+ex.Message); }
 }
 string ArchiveDir() { return string.IsNullOrEmpty(archiveDir)?Path.Combine(dir,"archive"):archiveDir; }
 static bool SamePath(string a,string b) { try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\','/'),Path.GetFullPath(b).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase); } catch { return false; } }
 static StackPanel Section(string title,string hint) {
  var s=new StackPanel { Margin=new Thickness(0,0,0,22) };
  s.Children.Add(new TextBlock { Text=title, Foreground=Palette.Text, FontSize=13.5, FontWeight=FontWeights.SemiBold });
  s.Children.Add(new TextBlock { Text=hint, Foreground=Palette.Muted, FontSize=12, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,3,0,10), LineHeight=19 });
  return s;
 }
 static Button Secondary(object content,string tip) { var b=Ui.Button(content,tip); b.Background=Palette.Raised; b.Margin=new Thickness(0,0,6,0); return b; }
 ControlTemplate DotSliderTemplate() {
  // 动态资源让模板里的画刷随深浅主题一起变化。
  string x=@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'><Grid><Track x:Name='PART_Track'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='{DynamicResource AccentBrush}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Height='4' CornerRadius='2' Background='{DynamicResource DotTrackBrush}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton><Track.Thumb><Thumb Width='14' Height='14'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Ellipse x:Name='dot' Fill='{DynamicResource DotThumbBrush}'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='dot' Property='Opacity' Value='0.72'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='dot' Property='Opacity' Value='0.72'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate>";
  return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(x);
 }
 Border BuildSettings() {
  var view=new Border { Background=Palette.Panel, Visibility=Visibility.Collapsed };
  var card=new Border { Background=Palette.Surface, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(10), Padding=new Thickness(18,16,18,14) }; view.Child=card;
  var dock=new DockPanel(); card.Child=dock;
  var footer=new DockPanel { LastChildFill=false, Margin=new Thickness(0,12,0,0) }; DockPanel.SetDock(footer,Dock.Bottom); dock.Children.Add(footer);
  var done=Secondary("完成","关闭设置 (Esc)"); done.Margin=new Thickness(0); done.Padding=new Thickness(18,0,18,0); done.Click+=(s,e)=>CloseSettings(); DockPanel.SetDock(done,Dock.Right); footer.Children.Add(done);
  footer.Children.Add(new TextBlock { Text="改动会立即生效", Foreground=Palette.Muted, FontSize=12, VerticalAlignment=VerticalAlignment.Center });
  var list=new StackPanel(); dock.Children.Add(new ScrollViewer { Content=list, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, FocusVisualStyle=null });
  list.Children.Add(new TextBlock { Text="设置", Foreground=Palette.Text, FontSize=16, FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,0,0,18) });

  var auto=Section("开机自动启动","开启后，登录 Windows 时自动打开 DotNotes，不用每次开机后手动打开。也可以在任务管理器的“启动应用”里关掉。");
  var autoSeg=new Border { Background=Palette.Panel, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(8), Padding=new Thickness(2), HorizontalAlignment=HorizontalAlignment.Left };
  var autoRow=new StackPanel { Orientation=Orientation.Horizontal }; autoSeg.Child=autoRow;
  autoOnBtn=Ui.Button("开启","登录 Windows 时自动打开",new CornerRadius(6)); autoOnBtn.Height=26; autoOnBtn.Click+=(s,e)=>SetAutoStart(true); autoRow.Children.Add(autoOnBtn);
  autoOffBtn=Ui.Button("关闭","需要时手动打开",new CornerRadius(6)); autoOffBtn.Height=26; autoOffBtn.Click+=(s,e)=>SetAutoStart(false); autoRow.Children.Add(autoOffBtn);
  auto.Children.Add(autoSeg); list.Children.Add(auto);

  var hover=Section("悬停展开","鼠标移到小圆点上，停留多久后自动展开。设为“立即”时，一碰到就展开。");
  var hoverRow=new StackPanel { Orientation=Orientation.Horizontal };
  var seg=new Border { Background=Palette.Panel, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(8), Padding=new Thickness(2), Margin=new Thickness(0,0,12,0) };
  var segRow=new StackPanel { Orientation=Orientation.Horizontal }; seg.Child=segRow;
  hoverOnBtn=Ui.Button("开启","鼠标停留时自动展开",new CornerRadius(6)); hoverOnBtn.Height=26; hoverOnBtn.Click+=(s,e)=> { hoverOn=true; SaveSettings(); UpdateSettingsView(); }; segRow.Children.Add(hoverOnBtn);
  hoverOffBtn=Ui.Button("关闭","只在点击时展开",new CornerRadius(6)); hoverOffBtn.Height=26; hoverOffBtn.Click+=(s,e)=> { hoverOn=false; SaveSettings(); UpdateSettingsView(); }; segRow.Children.Add(hoverOffBtn);
  hoverRow.Children.Add(seg);
  minusBtn=Secondary(Ui.Icon(G_Minus,11),"缩短 0.1 秒"); minusBtn.Width=30; minusBtn.Padding=new Thickness(0); minusBtn.Margin=new Thickness(0); minusBtn.Click+=(s,e)=>StepHover(-100); hoverRow.Children.Add(minusBtn);
  hoverValue=new TextBlock { Foreground=Palette.Text, FontSize=13, Width=64, TextAlignment=TextAlignment.Center, VerticalAlignment=VerticalAlignment.Center }; hoverRow.Children.Add(hoverValue);
  plusBtn=Secondary(Ui.Icon(G_Add,11),"延长 0.1 秒"); plusBtn.Width=30; plusBtn.Padding=new Thickness(0); plusBtn.Margin=new Thickness(0); plusBtn.Click+=(s,e)=>StepHover(100); hoverRow.Children.Add(plusBtn);
  hover.Children.Add(hoverRow); list.Children.Add(hover);

  var place=Section("位置和大小","按住小圆点，或按住面板顶部的空白处和“DotNotes”标签，可以拖到别处。拖到屏幕顶边附近会自动贴住。按住面板的左、右、下边或下面两个角拖动，可以改大小；面板在顶部正中时左右一起缩放，保持居中。");
  var placeRow=new StackPanel { Orientation=Orientation.Horizontal };
  resetPlaceBtn=Secondary("回到顶部正中","把面板和小圆点放回屏幕顶部正中"); resetPlaceBtn.Click+=(s,e)=>ResetPosition(); placeRow.Children.Add(resetPlaceBtn);
  resetSizeBtn=Secondary("恢复默认大小","改回 520 × 590"); resetSizeBtn.Click+=(s,e)=>ResetSize(); placeRow.Children.Add(resetSizeBtn);
  place.Children.Add(placeRow); list.Children.Add(place);

  var theme=Section("外观","选浅色背景时文字会换成深色。强调色用于当前标签、开关状态、链接、勾选框和脑图。颜色太深或太浅时会自动调整，保证看得清。");
  // 标签后面写出当前选的是哪一个：几个深色方块放在一起不容易分清。
  Func<string,double,TextBlock> rowLabel=(t,top)=> { var l=new TextBlock { Foreground=Palette.Text, FontSize=12.5, Margin=new Thickness(0,top,0,6) }; l.Inlines.Add(new Run(t)); var name=new Run { Foreground=Palette.Muted }; l.Inlines.Add(name); l.Tag=name; return l; };
  var bgLabel=rowLabel("背景色",0); bgName=bgLabel; theme.Children.Add(bgLabel);
  var bgRow=new WrapPanel { Orientation=Orientation.Horizontal };
  foreach(var preset in BgPresets) {
   var c=(Color)ColorConverter.ConvertFromString(preset[0]);
   var g=new Grid { Width=26, Height=26 };
   var ring=new Border { Width=26, Height=26, CornerRadius=new CornerRadius(8), BorderBrush=Palette.Text, BorderThickness=new Thickness(1.6), Visibility=Visibility.Hidden }; // 选中外圈和强调色那排一样用文字色
   g.Children.Add(ring); g.Children.Add(new Border { Width=18, Height=18, CornerRadius=new CornerRadius(5), Background=new SolidColorBrush(c), BorderBrush=Palette.Rule, BorderThickness=new Thickness(1) });
   var b=Ui.Button(g,preset[1]); b.Width=32; b.Height=32; b.Padding=new Thickness(0); b.Margin=new Thickness(0,0,4,0);
   var chosen=c; b.Click+=(s,e)=>ApplyBackground(chosen); bgRow.Children.Add(b);
   bgRings.Add(new KeyValuePair<Color,Border>(c,ring));
  }
  customBgBtn=Secondary("自定义…","选择任意背景色"); customBgBtn.Margin=new Thickness(8,0,0,0); customBgBtn.Click+=(s,e)=>PickBackground(); bgRow.Children.Add(customBgBtn);
  theme.Children.Add(bgRow);
  var acLabel=rowLabel("强调色",14); accentName=acLabel; theme.Children.Add(acLabel);
  var themeRow=new WrapPanel { Orientation=Orientation.Horizontal };
  foreach(var preset in Presets) {
   var c=(Color)ColorConverter.ConvertFromString(preset[0]);
   var g=new Grid { Width=24, Height=24 };
   var ring=new System.Windows.Shapes.Ellipse { Width=24, Height=24, Stroke=Palette.Text, StrokeThickness=1.6, Visibility=Visibility.Hidden };
   g.Children.Add(ring); g.Children.Add(new System.Windows.Shapes.Ellipse { Width=16, Height=16, Fill=new SolidColorBrush(c) });
   var b=Ui.Button(g,preset[1]); b.Width=32; b.Height=32; b.Padding=new Thickness(0); b.Margin=new Thickness(0,0,4,0);
   var chosen=c; b.Click+=(s,e)=>ApplyAccent(chosen); themeRow.Children.Add(b);
   swatchRings.Add(new KeyValuePair<Color,System.Windows.Shapes.Ellipse>(c,ring));
  }
  customColorBtn=Secondary("自定义…","选择任意颜色"); customColorBtn.Margin=new Thickness(8,0,0,0); customColorBtn.Click+=(s,e)=>PickAccent(); themeRow.Children.Add(customColorBtn);
  theme.Children.Add(themeRow);
  theme.Children.Add(new TextBlock { Text="小圆点呼吸灯", Foreground=Palette.Text, FontSize=12.5, Margin=new Thickness(0,14,0,6) });
  var bSeg=new Border { Background=Palette.Panel, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(8), Padding=new Thickness(2), HorizontalAlignment=HorizontalAlignment.Left };
  var bRow=new StackPanel { Orientation=Orientation.Horizontal }; bSeg.Child=bRow;
  string[] rateNames={"关闭","低","中","高"}, rateTips={"小圆点保持常亮","约每分钟 8.5 次","约每分钟 12 次","约每分钟 20 次"};
  for(int i=0;i<4;i++) { int rate=i; var button=Ui.Button(rateNames[i],rateTips[i],new CornerRadius(6)); button.Height=26; button.Click+=(sender,args)=> { breatheRate=rate; Breathe(!expanded); PreviewBreathe(); SaveSettings(); UpdateSettingsView(); }; breatheBtns[i]=button; bRow.Children.Add(button); }
  theme.Children.Add(bSeg);
  theme.Children.Add(new TextBlock { Text="小圆点大小", Foreground=Palette.Text, FontSize=12.5, Margin=new Thickness(0,14,0,6) });
  var sizeRow=new StackPanel { Orientation=Orientation.Horizontal };
  dotSizeSlider=new Slider { Minimum=5, Maximum=16, TickFrequency=1, IsSnapToTickEnabled=true, SmallChange=1, LargeChange=1, Width=160, Height=28, Value=dotSize, Template=DotSliderTemplate(), VerticalAlignment=VerticalAlignment.Center };
  dotSizeText=new TextBlock { Text=dotSize+" px", Foreground=Palette.Text, FontSize=12, Width=48, Margin=new Thickness(12,0,0,0), VerticalAlignment=VerticalAlignment.Center };
  dotPreview=new System.Windows.Shapes.Ellipse { Width=dotSize, Height=dotSize, Fill=Palette.Accent, VerticalAlignment=VerticalAlignment.Center };
  dotSizeSlider.ValueChanged+=(sender,args)=> { dotSize=(int)Math.Round(dotSizeSlider.Value); dotSizeText.Text=dotSize+" px"; dotPreview.Width=dotPreview.Height=dotSize; ClampDot(); ApplyLayout(); };
  dotSizeSlider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((sender,args)=>SaveSettings()));
  dotSizeSlider.PreviewKeyUp+=(sender,args)=> { if(args.Key==Key.Left || args.Key==Key.Right) SaveSettings(); };
  dotSizeSlider.PreviewMouseLeftButtonUp+=(sender,args)=>SaveSettings();
  sizeRow.Children.Add(dotSizeSlider); sizeRow.Children.Add(dotSizeText); sizeRow.Children.Add(dotPreview); theme.Children.Add(sizeRow); PreviewBreathe();
  list.Children.Add(theme);

  var arch=Section("归档位置","关闭笔记时选“归档”，笔记会移到这个文件夹；需要时用“打开”取回。更改位置后，之前已归档的笔记留在原处，不会跟着移动。");
  var pathBox=new Border { Background=Palette.Panel, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(7), Padding=new Thickness(10,7,10,7), Margin=new Thickness(0,0,0,8) };
  archivePathText=new TextBlock { Foreground=Palette.Text, FontSize=12.5, TextTrimming=TextTrimming.CharacterEllipsis }; pathBox.Child=archivePathText; arch.Children.Add(pathBox);
  var archRow=new StackPanel { Orientation=Orientation.Horizontal };
  var change=Secondary(Ui.IconLabel(G_Folder,"更改…"),"选择新的归档文件夹"); change.Click+=(s,e)=>PickArchive(); archRow.Children.Add(change);
  var open=Secondary("打开文件夹","在资源管理器中打开"); open.Click+=(s,e)=>OpenArchive(); archRow.Children.Add(open);
  resetArchiveBtn=Ui.Button("恢复默认","改回笔记文件夹里的 archive"); resetArchiveBtn.Foreground=Palette.Muted; resetArchiveBtn.Click+=(s,e)=> { archiveDir=null; SaveSettings(); UpdateSettingsView(); }; archRow.Children.Add(resetArchiveBtn);
  arch.Children.Add(archRow); list.Children.Add(arch);
  return view;
 }
 static string PresetName(string[][] list,Color c) { foreach(var p in list) if((Color)ColorConverter.ConvertFromString(p[0])==c) return p[1]; return "自定义"; }
 void StepHover(int ms) { hoverDelay=Math.Max(0,Math.Min(3000,hoverDelay+ms)); SaveSettings(); UpdateSettingsView(); }
 static StackPanel CustomSwatch(Color c) {
  var row=new StackPanel { Orientation=Orientation.Horizontal };
  row.Children.Add(new System.Windows.Shapes.Ellipse { Width=12, Height=12, Fill=new SolidColorBrush(c), Stroke=Palette.Text, StrokeThickness=1.2, Margin=new Thickness(0,0,7,0), VerticalAlignment=VerticalAlignment.Center });
  row.Children.Add(new TextBlock { Text="自定义 "+Palette.ToHex(c), VerticalAlignment=VerticalAlignment.Center });
  return row;
 }
 // 记下用户选的颜色本身；显示用的颜色由 Palette 按背景调整，换背景后会重新调整。
 void ApplyAccent(Color c) { accent=c; Palette.SetAccent(c); ThemeChanged(); }
 void ApplyBackground(Color c) { background=c; Palette.SetBackground(c); ThemeChanged(); }
 // 模板里通过资源引用的画刷会被 WPF 冻结，不能改色，所以每次换色都放一个新的画刷进去。
 void ThemeResources() { Resources["DotTrackBrush"]=new SolidColorBrush(Palette.Line.Color); Resources["DotThumbBrush"]=new SolidColorBrush(Palette.Text.Color); Resources["AccentBrush"]=new SolidColorBrush(Palette.Accent.Color); Resources["HoverBrush"]=new SolidColorBrush(Palette.Light?Colors.Black:Colors.White); }
 void ThemeChanged() { ThemeResources(); Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=DarkScrollBar(); Resources[typeof(ToolTip)]=TipStyle(); SaveSettings(); UpdateSettingsView(); if(mapping) RenderMap(false); }
 void PickAccent() { Color c; if(PickColor(accent,false,out c)) ApplyAccent(c); }
 void PickBackground() { Color c; if(PickColor(background,true,out c)) ApplyBackground(c); }
 // 自定义颜色：一组挑好的色块（强调色是明亮的颜色，背景色是几档深色加一排浅色）加一个颜色代码输入框，代替 Windows 老式取色对话框。
 bool PickColor(Color from,bool bg,out Color picked) {
  Color chosen=from; picked=from;
  var body=new StackPanel();
  var grid=new System.Windows.Controls.Primitives.UniformGrid { Columns=8, Margin=new Thickness(-4,0,-4,0) }; body.Children.Add(grid);
  double[] ls=bg?new[]{0.06,0.10,0.15,0.93}:new[]{0.82,0.73,0.64,0.55}, ss=bg?new[]{0.30,0.26,0.22,0.20}:new[]{0.70,0.62,0.55,0.50};
  var rings=new List<KeyValuePair<Color,FrameworkElement>>();
  var preview=new Border { Width=30, Height=30, CornerRadius=new CornerRadius(bg?7:15), BorderBrush=Palette.Rule, BorderThickness=new Thickness(1), Background=new SolidColorBrush(from) };
  var hex=Ui.Input(Palette.ToHex(from)); hex.FontFamily=Ui.Mono; hex.Width=104; hex.Margin=new Thickness(10,0,0,0); hex.VerticalAlignment=VerticalAlignment.Center;
  Action show=()=> { preview.Background=new SolidColorBrush(chosen); foreach(var r in rings) r.Value.Visibility=r.Key==chosen?Visibility.Visible:Visibility.Hidden; };
  for(int r=0;r<4;r++) for(int c=0;c<8;c++) {
   // 背景色第一列是中性灰，其余每 45 度一个色相；强调色每 45 度一个色相
   var col=bg && c==0?Palette.FromHsl(0,0,ls[r]):Palette.FromHsl(((bg?c-1:c)*(bg?360.0/7:45)+(bg?200:0))%360,ss[r],ls[r]);
   var g=new Grid { Width=26, Height=26 };
   FrameworkElement ring=bg?(FrameworkElement)new Border { CornerRadius=new CornerRadius(8), BorderBrush=Palette.Text, BorderThickness=new Thickness(1.6) }:new System.Windows.Shapes.Ellipse { Stroke=Palette.Text, StrokeThickness=1.6 };
   ring.Visibility=Visibility.Hidden; g.Children.Add(ring);
   g.Children.Add(bg?(UIElement)new Border { Width=18, Height=18, CornerRadius=new CornerRadius(5), Background=new SolidColorBrush(col), BorderBrush=Palette.Rule, BorderThickness=new Thickness(1) }:new System.Windows.Shapes.Ellipse { Width=18, Height=18, Fill=new SolidColorBrush(col) });
   var b=Ui.Button(g,Palette.ToHex(col)); b.Width=40; b.Height=34; b.Padding=new Thickness(0); var cc=col;
   b.Click+=(s,e)=> { chosen=cc; hex.Text=Palette.ToHex(cc); show(); }; grid.Children.Add(b); rings.Add(new KeyValuePair<Color,FrameworkElement>(col,ring));
  }
  body.Children.Add(new TextBlock { Text="或输入颜色代码", Foreground=Palette.Muted, FontSize=12, Margin=new Thickness(0,14,0,6) });
  var line=new StackPanel { Orientation=Orientation.Horizontal }; line.Children.Add(preview); line.Children.Add(hex); body.Children.Add(line);
  var bad=new TextBlock { Text="请输入 6 位颜色代码，例如 #A6DDB4", Foreground=Palette.Warn, FontSize=12, Margin=new Thickness(0,6,0,0), Visibility=Visibility.Collapsed }; body.Children.Add(bad);
  Func<bool> valid=()=> { var m=Regex.Match(hex.Text.Trim(),"^#?([0-9A-Fa-f]{6})$"); if(!m.Success) return false; chosen=(Color)ColorConverter.ConvertFromString("#"+m.Groups[1].Value); return true; };
  hex.TextChanged+=(s,e)=> { bool ok=valid(); bad.Visibility=ok?Visibility.Collapsed:Visibility.Visible; if(ok) show(); };
  show();
  bool done;
  modal++; try { done=Dialog(this,bg?"自定义背景色":"自定义强调色",body,"使用这个颜色","取消",false,()=> { bool ok=valid(); bad.Visibility=ok?Visibility.Collapsed:Visibility.Visible; return ok; },hex); } finally { modal--; }
  if(done) picked=chosen; return done;
 }
 void UpdateSettingsView() {
  bool preset=false;
  foreach(var pair in swatchRings) { bool on=pair.Key==accent; if(on) preset=true; pair.Value.Visibility=on?Visibility.Visible:Visibility.Hidden; }
  customColorBtn.Content=preset?(object)"自定义…":CustomSwatch(accent);
  bool bgPreset=false;
  foreach(var pair in bgRings) { bool on=pair.Key==background; if(on) bgPreset=true; pair.Value.Visibility=on?Visibility.Visible:Visibility.Hidden; }
  customBgBtn.Content=bgPreset?(object)"自定义…":CustomSwatch(background);
  ((Run)bgName.Tag).Text=" · "+PresetName(BgPresets,background); ((Run)accentName.Tag).Text=" · "+PresetName(Presets,accent);
  Choose(hoverOnBtn,hoverOn); Choose(hoverOffBtn,!hoverOn);
  hoverValue.Text=hoverDelay==0?"立即":(hoverDelay/1000.0).ToString("0.0")+" 秒"; hoverValue.Opacity=hoverOn?1:0.4;
  minusBtn.IsEnabled=hoverOn && hoverDelay>0; plusBtn.IsEnabled=hoverOn && hoverDelay<3000;
  string a=ArchiveDir(); archivePathText.Text=a; archivePathText.ToolTip=a; resetArchiveBtn.IsEnabled=!string.IsNullOrEmpty(archiveDir);
  resetPlaceBtn.IsEnabled=!AtHome(); resetSizeBtn.IsEnabled=!DefaultSize();
  bool autoOn=AutoStartOn(); Choose(autoOnBtn,autoOn); Choose(autoOffBtn,!autoOn); for(int i=0;i<4;i++) Choose(breatheBtns[i],i==breatheRate);
 }
 // ——— 开机自动启动 ———
 // 写在当前用户的启动项（HKCU\...\Run）里，不需要管理员权限。任务管理器“启动应用”里禁用的，记在 StartupApproved 下，这里也认。
 internal static string RunName="DotNotes";
 const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run", ApprovedKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
 static string RunCommand() { return "\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\""; }
 static bool AutoStartOn() {
  try {
   using(var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) if(k==null || k.GetValue(RunName)==null) return false;
   using(var a=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKey)) { var v=a==null?null:a.GetValue(RunName) as byte[]; if(v!=null && v.Length>0 && (v[0]&1)==1) return false; } // 单数表示已在任务管理器里禁用
   return true;
  } catch { return false; }
 }
 void SetAutoStart(bool on) {
  try {
   using(var k=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) { if(on) k.SetValue(RunName,RunCommand()); else k.DeleteValue(RunName,false); }
   using(var a=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKey,true)) if(a!=null) a.DeleteValue(RunName,false); // 清掉任务管理器里的“已禁用”，免得开了也不生效
  } catch(Exception ex) { Msg("无法更改开机自动启动："+ex.Message); }
  UpdateSettingsView();
 }
 // 程序文件挪了位置后，启动项跟着指向新位置。只在真正启动程序时调用。
 internal static bool AutoStartPathChanged(string stored,string current) { return !SamePath((stored??"").Trim().Trim('"'),(current??"").Trim().Trim('"')); }
 internal static void FixAutoStartPath() {
  if(!AutoStartOn()) return;
  try { using(var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey,true)) { if(k==null) return; var v=k.GetValue(RunName) as string; if(v!=null && AutoStartPathChanged(v,RunCommand())) k.SetValue(RunName,RunCommand()); } } catch {}
 }
 void OpenSettings() { if(editingBlock>=0) { FinishEdit(); RenderPreview(); } settingsOpen=true; UpdateSettingsView(); settingsView.Visibility=Visibility.Visible; PreviewBreathe(); settingsButton.Background=Palette.Raised; settingsButton.Foreground=Palette.Text; }
 void CloseSettings() { if(!settingsOpen) return; settingsOpen=false; PreviewBreathe(); settingsView.Visibility=Visibility.Collapsed; settingsButton.Background=Brushes.Transparent; settingsButton.Foreground=Palette.Muted; if(expanded && Writing) editor.Focus(); }
 void PickArchive() {
  modal++;
  try {
   using(var d=new Forms.FolderBrowserDialog()) {
    d.Description="选择归档文件夹"; d.ShowNewFolderButton=true;
    try { if(Directory.Exists(ArchiveDir())) d.SelectedPath=ArchiveDir(); } catch {}
    if(d.ShowDialog(new Win32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle))!=Forms.DialogResult.OK || string.IsNullOrEmpty(d.SelectedPath)) return;
    if(SamePath(d.SelectedPath,dir)) { Msg("不能选笔记所在的文件夹，否则归档的笔记会重新出现在标签里。请换一个文件夹。"); return; }
    archiveDir=SamePath(d.SelectedPath,Path.Combine(dir,"archive"))?null:d.SelectedPath; SaveSettings(); UpdateSettingsView();
   }
  } finally { modal--; }
 }
 void OpenArchive() { try { string p=ArchiveDir(); Directory.CreateDirectory(p); System.Diagnostics.Process.Start("explorer.exe","\""+p+"\""); } catch(Exception ex) { Msg("无法打开归档文件夹："+ex.Message); } }

 // ——— 笔记 ———
 string GetNoteTitle(string name) { string title=""; try { string content=name==currentNote?editor.Text:File.ReadAllText(Path.Combine(dir,name)); foreach(string line in content.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) { title=Regex.Replace(line.Trim(),@"^#{1,6}\s*","").Trim(); if(title.Length>0) break; } } catch {} if(title.Length==0) title=name=="note.md"?"未命名笔记":"未命名 "+name.Substring(5,name.Length-8); if(title.Length>12) title=title.Substring(0,12)+"…"; return title; }
 void RefreshTabs() {
  tabs.Children.Clear(); noteNames.Clear(); noteNames.Add("note.md");
  try { foreach(var p in Directory.GetFiles(dir,"note_*.md")) noteNames.Add(Path.GetFileName(p)); } catch(Exception ex) {Msg("无法读取笔记列表："+ex.Message);}
  noteNames.Sort(StringComparer.OrdinalIgnoreCase); ApplyTabOrder();
  Button active=null;
  foreach(string name in noteNames) {
   string n=name; string title=GetNoteTitle(n); bool on=n==currentNote; if(on) currentTitle=title;
   var label=new StackPanel { Orientation=Orientation.Horizontal }; label.Children.Add(new TextBlock { Text=title, VerticalAlignment=VerticalAlignment.Center });
   var x=Ui.Button(Ui.Icon(G_Close,7),"关闭这篇笔记，可选择归档（Ctrl+W）"); x.Width=18; x.Height=18; x.Padding=new Thickness(0); x.Margin=new Thickness(6,0,-3,0); x.Focusable=false; x.Opacity=on?0.8:0; x.IsHitTestVisible=on;
   x.Click+=(s,e)=> { e.Handled=true; CloseNote(n); }; label.Children.Add(x);
   var b=Ui.Button(label,"切换笔记："+title+"（Ctrl+Tab；中键点击关闭）"); b.Height=28; b.Margin=new Thickness(0,0,4,0); b.Padding=new Thickness(10,0,6,0);
   x.Foreground=on?Palette.Accent:Palette.Muted;
   b.MouseEnter+=(s,e)=> { x.Opacity=0.8; x.IsHitTestVisible=true; }; b.MouseLeave+=(s,e)=> { if(!on) { x.Opacity=0; x.IsHitTestVisible=false; } };
   b.MouseUp+=(s,e)=> { if(e.ChangedButton==MouseButton.Middle) { e.Handled=true; CloseNote(n); } };
   b.Background=on?Palette.AccentBg:Brushes.Transparent; b.Foreground=on?Palette.Accent:Palette.Muted; if(on) b.FontWeight=FontWeights.SemiBold;
   b.Click+=(s,e)=> { CloseSettings(); SwitchNote(n); }; TabDrag(b,n); tabs.Children.Add(b); if(on) active=b;
  }
  if(active!=null) { var a=active; Dispatcher.BeginInvoke(new Action(()=>a.BringIntoView()),DispatcherPriority.Loaded); }
 }
 // ——— 标签翻页 ———
 Button tabPrev, tabNext, tabAdd, tabOpen; Border tabsEdge;
 static readonly DependencyProperty TabScrollProperty=DependencyProperty.Register("TabScroll",typeof(double),typeof(MainWindow),new PropertyMetadata(0.0,(d,e)=>((MainWindow)d).tabsScroll.ScrollToHorizontalOffset((double)e.NewValue)));
 void PageTabs(int dir) {
  double from=tabsScroll.HorizontalOffset, to=Clamp(from+dir*Math.Max(80,tabsScroll.ViewportWidth*0.75),0,tabsScroll.ScrollableWidth);
  var a=new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?240:1)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } };
  BeginAnimation(TabScrollProperty,a);
 }
 void UpdateTabEdges() {
  if(tabPrev==null) return;
  // 放不放得下只看“没有箭头和分隔线时”标签能占多宽：整行减去新建、打开两个按钮。
  // 不能用 ViewportWidth 加回箭头宽度——分隔线的 9px 没算进去，宽度正好卡在这 9px 里时箭头会一出一收来回跳。
  double room=tabRowPanel.ActualWidth-tabAdd.ActualWidth-tabAdd.Margin.Left-tabOpen.ActualWidth-tabOpen.Margin.Left;
  if(room<=0) return;
  bool over=tabs.DesiredSize.Width>room+0.5; var v=over?Visibility.Visible:Visibility.Collapsed; // 用内容本来的宽度；ActualWidth 会被拉伸到可视宽度，还可能晚一拍
  if(tabPrev.Visibility!=v) { tabPrev.Visibility=tabNext.Visibility=tabsEdge.Visibility=v; }
  double off=tabsScroll.HorizontalOffset, max=tabsScroll.ScrollableWidth; bool left=over && off>0.5, right=over && off<max-0.5;
  tabPrev.IsEnabled=left; tabNext.IsEnabled=right;
  double w=Math.Max(1,tabsScroll.ActualWidth), f=Math.Min(0.4,22/w);
  var mask=new LinearGradientBrush { StartPoint=new Point(0,0), EndPoint=new Point(1,0) };
  mask.GradientStops.Add(new GradientStop(left?Colors.Transparent:Colors.Black,0)); mask.GradientStops.Add(new GradientStop(Colors.Black,f));
  mask.GradientStops.Add(new GradientStop(Colors.Black,1-f)); mask.GradientStops.Add(new GradientStop(right?Colors.Transparent:Colors.Black,1));
  tabsScroll.OpacityMask=over?mask:null;
 }
 // ——— 拖动标签换位 ———
 // 按住标签左右拖动超过 6px 开始拖；标签跟着鼠标走，越过相邻标签的中线就和它换位，松开后记住顺序。
 // 只挪别的标签，被拖的那个一直留在原位置的可视树里，鼠标捕获不会丢；拖过之后不算点击。
 void TabDrag(Button b,string name) {
  b.PreviewMouseLeftButtonDown+=(s,e)=> { tabDragName=name; tabDragMoved=false; tabGrab=e.GetPosition(b).X; };
  b.PreviewMouseMove+=(s,e)=> {
   if(tabDragName!=name || e.LeftButton!=MouseButtonState.Pressed) return;
   double x=e.GetPosition(tabs).X, left=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(b).X;
   if(!tabDragMoved) { if(Math.Abs(x-tabGrab-left)<6) return; tabDragMoved=true; b.CaptureMouse(); Panel.SetZIndex(b,1); b.RenderTransform=new TranslateTransform(); }
   int i=tabs.Children.IndexOf(b);
   while(i+1<tabs.Children.Count && x>TabMid(i+1)) { var o=tabs.Children[i+1]; tabs.Children.RemoveAt(i+1); tabs.Children.Insert(i,o); SwapNames(i,i+1); i++; tabs.UpdateLayout(); }
   while(i>0 && x<TabMid(i-1)) { var o=tabs.Children[i-1]; tabs.Children.RemoveAt(i-1); tabs.Children.Insert(i,o); SwapNames(i-1,i); i--; tabs.UpdateLayout(); }
   left=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(b).X;
   ((TranslateTransform)b.RenderTransform).X=Clamp(x-tabGrab-left,-left,Math.Max(0,tabs.ActualWidth-left-b.ActualWidth));
   double v=e.GetPosition(tabsScroll).X; // 拖到标签区两头时自动滚动
   if(v<24) tabsScroll.ScrollToHorizontalOffset(tabsScroll.HorizontalOffset-8); else if(v>tabsScroll.ActualWidth-24) tabsScroll.ScrollToHorizontalOffset(tabsScroll.HorizontalOffset+8);
  };
  b.PreviewMouseLeftButtonUp+=(s,e)=> { if(tabDragName!=name) return; bool moved=tabDragMoved; tabDragName=null; if(!moved) return; e.Handled=true; EndTabDrag(b); b.ReleaseMouseCapture(); };
  b.LostMouseCapture+=(s,e)=> { if(tabDragName==name && tabDragMoved) { tabDragName=null; EndTabDrag(b); } };
 }
 double TabMid(int i) { var r=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot((FrameworkElement)tabs.Children[i]); return r.X+r.Width/2; }
 void SwapNames(int a,int b) { string t=noteNames[a]; noteNames[a]=noteNames[b]; noteNames[b]=t; }
 void EndTabDrag(Button b) { b.RenderTransform=Transform.Identity; Panel.SetZIndex(b,0); tabOrder.Clear(); tabOrder.AddRange(noteNames); SaveTabOrder(); }
 void ApplyTabOrder() {
  if(tabOrder.Count==0) return;
  var exist=new HashSet<string>(noteNames,StringComparer.OrdinalIgnoreCase);
  if(tabOrder.RemoveAll(n=>!exist.Contains(n))>0) SaveTabOrder(); // 关掉或删掉的笔记不留在顺序里，免得以后同名新笔记插回旧位置
  var listed=new HashSet<string>(tabOrder,StringComparer.OrdinalIgnoreCase);
  var rest=noteNames.FindAll(n=>!listed.Contains(n)); noteNames.Clear(); noteNames.AddRange(tabOrder); noteNames.AddRange(rest);
 }
 void LoadTabOrder() {
  try {
   string f=Path.Combine(dir,"order.txt"); if(!File.Exists(f)) return;
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var raw in File.ReadAllLines(f)) { string n=raw.Trim(); if(Regex.IsMatch(n,@"^note(_\d+)?\.md$",RegexOptions.IgnoreCase) && seen.Add(n)) tabOrder.Add(n); }
  } catch {}
 }
 void SaveTabOrder() {
  try {
   string target=Path.Combine(dir,"order.txt"), temp=Path.Combine(dir,"order.tmp");
   File.WriteAllLines(temp,tabOrder.ToArray()); if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
  } catch {} // 顺序丢了只是回到默认排序，不打扰用户
 }
 void CycleNote(int step) { if(noteNames.Count<2) return; int i=noteNames.IndexOf(currentNote); if(i<0) i=0; SwitchNote(noteNames[(i+step+noteNames.Count)%noteNames.Count]); }
 void SwitchNote(string name) { if(name==currentNote) return; if(Presenting) ExitPresentation(); if(editingBlock>=0) FinishEdit(); timer.Stop(); if(!SaveNote()) return; string text; try { string p=Path.Combine(dir,name); text=File.Exists(p)?File.ReadAllText(p):""; } catch(Exception ex) { Msg("无法打开笔记："+ex.Message); return; } loading=true; currentNote=name; noteReadable=true; editor.Text=text; loading=false; ClearHistory(); RefreshTabs(); if(previewing) RenderPreview(); else if(mapping) RenderMap(true); else editor.Focus(); FadeIn(previewing?(UIElement)previewScroll:mapping?(UIElement)mapView:editor); }
 void NewNote() { timer.Stop(); if(!SaveNote()) return; int i=2; while(File.Exists(Path.Combine(dir,"note_"+i+".md"))) i++; string name="note_"+i+".md"; try { using(var f=new FileStream(Path.Combine(dir,name),FileMode.CreateNew)) {} } catch(Exception ex) {Msg("无法创建笔记："+ex.Message);return;} SwitchNote(name); if(mapping) SetPreview(false); else if(previewing) EditAtLine(0,0,false); }
 // ——— 打开文件 ———
 // 把选中文件的内容放进笔记；当前笔记是空的就直接填进去，免得多出一篇空白标签。
 // 归档文件夹里的文件算“取回”：放回笔记后，归档里那份移到回收站，同一篇不会存两份。其他位置的文件只复制，原文件不动。
 void OpenFiles() {
  string[] picked;
  modal++;
  try {
   using(var d=new Forms.OpenFileDialog { Title="打开文件或取回归档的笔记", Multiselect=true, Filter="Markdown 和文本 (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|所有文件 (*.*)|*.*" }) {
    try { if(Directory.Exists(ArchiveDir())) d.InitialDirectory=ArchiveDir(); } catch {}
    if(d.ShowDialog(new Win32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle))!=Forms.DialogResult.OK) return;
    picked=d.FileNames;
   }
  } finally { modal--; }
  OpenPaths(picked);
 }
 void OpenPaths(string[] picked) {
  if(editingBlock>=0) FinishEdit(); timer.Stop(); if(!SaveNote()) return;
  string last=null; var skipped=new List<string>();
  foreach(string path in picked) {
   string text, why=ReadTextFile(path,out text); if(why!=null) { skipped.Add(Path.GetFileName(path)+"："+why); continue; }
   string name;
   if(last==null && editor.Text.Trim().Length==0) name=currentNote;
   else { int i=2; while(File.Exists(Path.Combine(dir,"note_"+i+".md"))) i++; name="note_"+i+".md"; }
   try { File.WriteAllText(Path.Combine(dir,name),text,System.Text.Encoding.UTF8); } catch(Exception ex) { skipped.Add(Path.GetFileName(path)+"："+ex.Message); continue; }
   if(name==currentNote) { loading=true; editor.Text=text; loading=false; ClearHistory(); }
   last=name;
   if(InArchive(path)) try { Discard(path); } catch(Exception ex) { skipped.Add(Path.GetFileName(path)+"：已取回，但归档里的原文件没能移走（"+ex.Message+"）"); }
  }
  if(last!=null) { if(last==currentNote) { RefreshTabs(); if(previewing) RenderPreview(); else if(mapping) RenderMap(true); } else SwitchNote(last); }
  if(skipped.Count>0) Msg("以下文件有问题：\n"+string.Join("\n",skipped.ToArray()));
 }
 // 当前归档文件夹或默认的 archive 文件夹里的文件。
 bool InArchive(string path) { string parent=Path.GetDirectoryName(path); return SamePath(parent,ArchiveDir()) || SamePath(parent,Path.Combine(dir,"archive")); }
 // 读成文字：有 BOM 按 BOM；否则先按 UTF-8，不是合法 UTF-8 就按系统默认编码（中文 Windows 为 GBK）。能打开返回 null，否则返回原因。
 static string ReadTextFile(string path,out string text) {
  text=null;
  try {
   if(new FileInfo(path).Length>5*1024*1024) return "文件超过 5 MB";
   byte[] bytes=File.ReadAllBytes(path);
   try { using(var r=new StreamReader(new MemoryStream(bytes),new System.Text.UTF8Encoding(false,true),true)) text=r.ReadToEnd(); }
   catch(System.Text.DecoderFallbackException) { text=System.Text.Encoding.Default.GetString(bytes); }
   if(text.IndexOf('\0')>=0) { text=null; return "看起来不是文本文件"; }
   return null;
  } catch(Exception ex) { return ex.Message; }
 }
 // ——— 关闭笔记 ———
 // 空笔记直接关掉；有内容时问一句：归档，还是不要了（移到回收站）。
 void CloseNote(string name) {
  CloseSettings(); if(editingBlock>=0) { FinishEdit(); if(previewing) RenderPreview(); }
  timer.Stop(); if(!SaveNote()) return;
  string text;
  try { string path=Path.Combine(dir,name); text=name==currentNote?editor.Text:(File.Exists(path)?File.ReadAllText(path):""); }
  catch(Exception ex) { Msg("无法读取这篇笔记："+ex.Message); return; }
  if(text.Trim().Length==0) { RemoveNote(name,"empty"); return; }
  closingNote=name; closeTitle.Text="关闭“"+GetNoteTitle(name)+"”？"; closeOpen=true; closeView.Visibility=Visibility.Visible;
  Dispatcher.BeginInvoke(new Action(()=>closeArchiveBtn.Focus()),DispatcherPriority.Input);
 }
 void HideCloseDialog() { if(!closeOpen) return; closeOpen=false; closingNote=null; closeView.Visibility=Visibility.Collapsed; if(expanded && Writing) editor.Focus(); }
 void ConfirmClose(string mode) { string name=closingNote; HideCloseDialog(); if(name!=null) RemoveNote(name,mode); }
 Border BuildCloseDialog() {
  var view=new Border { Background=Palette.Scrim, Visibility=Visibility.Collapsed };
  view.MouseLeftButtonDown+=(s,e)=> { if(e.OriginalSource==view) HideCloseDialog(); };
  var card=new Border { Background=Palette.Surface, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(12), Padding=new Thickness(20,18,20,16), Margin=new Thickness(10,0,10,40), VerticalAlignment=VerticalAlignment.Center };
  view.Child=card; var list=new StackPanel(); card.Child=list;
  closeTitle=new TextBlock { Foreground=Palette.Text, FontSize=15.5, FontWeight=FontWeights.SemiBold, TextTrimming=TextTrimming.CharacterEllipsis }; list.Children.Add(closeTitle);
  list.Children.Add(new TextBlock { Text="关闭后它不会再出现在标签里。要归档保留，还是不要了？", Foreground=Palette.Muted, FontSize=12.5, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,0), LineHeight=20 });
  list.Children.Add(new TextBlock { Text="选“不保留”会把它移到回收站，需要时还能找回。", Foreground=Palette.Muted, Opacity=0.75, FontSize=11.5, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,4,0,0) });
  var row=new DockPanel { LastChildFill=false, Margin=new Thickness(0,18,0,0) }; list.Children.Add(row);
  closeArchiveBtn=Ui.Button(Ui.IconLabel(G_Archive,"归档"),"放进归档文件夹，需要时用“打开”取回（Enter）"); closeArchiveBtn.Background=Palette.AccentBg; closeArchiveBtn.Foreground=Palette.Accent; closeArchiveBtn.FontWeight=FontWeights.SemiBold; closeArchiveBtn.Padding=new Thickness(16,0,16,0);
  closeArchiveBtn.Click+=(s,e)=>ConfirmClose("archive"); DockPanel.SetDock(closeArchiveBtn,Dock.Right); row.Children.Add(closeArchiveBtn);
  var discard=Ui.Button("不保留","移到回收站"); discard.Background=Palette.Raised; discard.Foreground=Palette.Warn; discard.Padding=new Thickness(14,0,14,0); discard.Margin=new Thickness(0,0,8,0);
  discard.Click+=(s,e)=>ConfirmClose("discard"); DockPanel.SetDock(discard,Dock.Right); row.Children.Add(discard);
  var cancel=Ui.Button("取消","不关闭（Esc）"); cancel.Foreground=Palette.Muted; cancel.Click+=(s,e)=>HideCloseDialog(); DockPanel.SetDock(cancel,Dock.Left); row.Children.Add(cancel);
  return view;
 }
 static void Discard(string path) { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); }
 // mode：archive 归档 / discard 移到回收站 / empty 空笔记直接删除。
 // 关掉第一篇（note.md）时，后面那篇顶上来成为第一篇；只剩它一篇时变成一篇空白笔记。
 void RemoveNote(string name,string mode) {
  string path=Path.Combine(dir,name);
  var order=new List<string>(noteNames); int idx=order.IndexOf(name);
  string neighbor=null; if(idx>=0 && order.Count>1) neighbor=idx+1<order.Count?order[idx+1]:order[idx-1];
  modal++;
  try {
   if(mode=="archive") {
    string text=File.Exists(path)?File.ReadAllText(path):""; string archive=ArchiveDir(); Directory.CreateDirectory(archive);
    string stem=TitleOf(text)+"_"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss",System.Globalization.CultureInfo.InvariantCulture); string dest=Path.Combine(archive,stem+".md");
    for(int k=2;File.Exists(dest);k++) dest=Path.Combine(archive,stem+"_"+k+".md");
    if(File.Exists(path)) File.Move(path,dest);
   }
   else if(mode=="discard") { if(File.Exists(path)) Discard(path); }
   else { if(File.Exists(path)) File.Delete(path); }
  }
  catch(Exception ex) { modal--; Msg((mode=="archive"?"归档失败：":"关闭失败：")+ex.Message+(mode=="archive"?"\n\n可以在设置里换一个归档位置。":"")); return; }
  modal--;
  string promoted=null;
  if(name=="note.md") {
   try {
    string next=null; foreach(var n in order) if(n!="note.md" && File.Exists(Path.Combine(dir,n))) { next=n; break; }
    if(next!=null) { File.Move(Path.Combine(dir,next),path); promoted=next; }
    else using(new FileStream(path,FileMode.Create)) {}
   } catch(Exception ex) { Msg("整理笔记顺序失败："+ex.Message); }
  }
  // 折叠记录跟着笔记走：关掉的删掉，顶上来的那篇改记到 note.md 名下。
  folds.Remove(name); HashSet<string> moved; if(promoted!=null && folds.TryGetValue(promoted,out moved)) { folds.Remove(promoted); folds["note.md"]=moved; }
  SaveFolds();
  // 标签顺序同理：关掉的移出；顶上来的那篇在原位置改叫 note.md。
  if(tabOrder.Count>0) { tabOrder.Remove(name); if(promoted!=null) { int at=tabOrder.IndexOf(promoted); if(at>=0) tabOrder[at]="note.md"; } SaveTabOrder(); }
  ClearHistory();
  string target=name==currentNote?(neighbor??"note.md"):currentNote;
  if(target==promoted) target="note.md";
  string text2="";
  try { string tp=Path.Combine(dir,target); if(File.Exists(tp)) text2=File.ReadAllText(tp); else target="note.md"; } catch(Exception ex) { Msg("无法打开笔记："+ex.Message); }
  loading=true; currentNote=target; noteReadable=true; editor.Text=text2; loading=false;
  RefreshTabs(); if(previewing) RenderPreview(); else if(mapping) RenderMap(true); else if(expanded) editor.Focus();
  SetStatus("已自动保存",Palette.Accent);
 }
 static string TitleOf(string text) {
  string t="";
  foreach(string raw in text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
   t=raw.Trim();
   t=Regex.Replace(t,@"^#{1,6}\s*",""); t=Regex.Replace(t,@"^(>\s*)+","");
   t=Regex.Replace(t,@"^([-*+]\s+(\[[ xX]\]\s+)?|\d{1,9}[.)]\s+)","");
   t=Regex.Replace(t,@"\[([^\]]+)\]\([^)]*\)","$1"); t=Regex.Replace(t,@"(\*\*|__|~~|`|\*)","");
   t=t.Trim(); if(t.Length>0) break;
  }
  foreach(char c in Path.GetInvalidFileNameChars()) t=t.Replace(c,' ');
  t=Regex.Replace(t,@"\s+"," ").Trim().TrimEnd('.',' ');
  if(t.Length>60) t=t.Substring(0,60).TrimEnd('.',' ');
  return t.Length==0?"未命名笔记":t;
 }
 bool SaveNote() { if(!noteReadable) { SetStatus("读取失败，无法保存",Palette.Warn); Msg("当前笔记未能读取，为保护原文件无法保存。请先复制编辑内容。"); return false; } try { string target=Path.Combine(dir,currentNote); string temp=target+".tmp"; File.WriteAllText(temp,editor.Text,System.Text.Encoding.UTF8); if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target); SetStatus("已自动保存",Palette.Accent); return true; } catch(Exception ex) { SetStatus("保存失败",Palette.Warn); Msg("笔记保存失败，请复制当前内容后检查磁盘：\n"+ex.Message); return false;} }

 // ——— 格式按钮 ———
 void AddTool(FrameworkElement icon,string tip,string before,string after,string lineKind) {
  var b=Ui.Button(icon,tip); b.Width=32; b.Padding=new Thickness(0); b.Foreground=Palette.Muted; b.Margin=new Thickness(0,0,2,0);
  b.Focusable=false; // 点按钮不抢走输入框的焦点，选区保持不变
  b.Click+=(s,e)=> { var t=ActiveBox(); if(t==null) return; if(lineKind!=null) ToggleLinePrefix(t,lineKind); else Wrap(t,before,after); };
  toolbar.Children.Add(b);
 }
 TextBox ActiveBox() { return previewing?lineBox:(mapping?mapBox:editor); }
 bool Writing { get { return !previewing && !mapping; } } // 在“编辑”模式（源码输入框可见）
 static void Wrap(TextBox t,string before,string after) { int start=t.SelectionStart; string selected=t.SelectedText; t.SelectedText=before+selected+after; t.Focus(); t.Select(start+before.Length,selected.Length); }
 const string ListPrefix=@"(?:[-*+] \[[ xX]\] |[-*+] |\d{1,9}[.)] )";
 static Regex PrefixOf(string kind) {
  if(kind=="quote") return new Regex(@"^(\s*)> ?");
  if(kind=="task") return new Regex(@"^(\s*)[-*+] \[[ xX]\] ");
  if(kind=="bullet") return new Regex(@"^(\s*)[-*+] (?!\[[ xX]\] )");
  return new Regex(@"^(\s*)\d{1,9}[.)] ");
 }
 // 引用 / 列表 / 有序 / 待办：作用于选区覆盖的每一行；选中的行都已是这种格式时再点一次取消。
 static void ToggleLinePrefix(TextBox t,string kind) {
  string text=t.Text; int s=t.SelectionStart, e=s+t.SelectionLength; bool collapsed=t.SelectionLength==0;
  if(e>s && text[e-1]=='\n') e--;
  int ls=s==0?0:text.LastIndexOf('\n',s-1)+1;
  int le=text.IndexOf('\n',e); if(le<0) le=text.Length;
  if(le>ls && text[le-1]=='\r') le--; // 不把 \r\n 拆开选，否则替换后会留下孤立的 \r
  string[] parts=text.Substring(ls,le-ls).Split('\n');
  var has=PrefixOf(kind); bool any=false, all=true;
  foreach(string p in parts) { string core=p.TrimEnd('\r'); if(core.Trim().Length==0) continue; any=true; if(!has.IsMatch(core)) all=false; }
  if(!any) all=false;
  int n=1;
  for(int i=0;i<parts.Length;i++) {
   string core=parts[i].TrimEnd('\r'), cr=parts[i].EndsWith("\r")?"\r":"";
   if(any && core.Trim().Length==0) continue;
   if(all) core=has.Replace(core,"$1",1);
   else if(kind=="quote") { if(!has.IsMatch(core)) { var m=Regex.Match(core,@"^(\s*)(.*)$"); core=m.Groups[1].Value+"> "+m.Groups[2].Value; } }
   else {
    var m=Regex.Match(core,@"^(\s*)"+ListPrefix+"?(.*)$");
    string prefix=kind=="bullet"?"- ":(kind=="task"?"- [ ] ":(n++)+". ");
    core=m.Groups[1].Value+prefix+m.Groups[2].Value;
   }
   parts[i]=core+cr;
  }
  string result=string.Join("\n",parts);
  t.Select(ls,le-ls); t.SelectedText=result; t.Focus();
  if(collapsed) t.Select(ls+result.TrimEnd('\r').Length,0); else t.Select(ls,result.Length);
 }

 // ——— 编辑与预览 ———
 // 切换编辑 / 预览 / 脑图、换笔记时，新内容 150ms 淡入，不再一下子跳过去。
 static void FadeIn(UIElement e) { if(!SystemParameters.ClientAreaAnimation) return; e.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0.3,1,TimeSpan.FromMilliseconds(150)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } }); }
 void SetPreview(bool on) {
  if(Presenting) ExitPresentation();
  if(mapBox!=null) EndMapEdit(true);
  if(on!=previewing || mapping) FadeIn(on?(UIElement)previewScroll:editor);
  if(editingBlock>=0) FinishEdit();
  // 在“编辑”里改过内容再回到预览，之前记下的撤销步骤就对不上了，清掉。
  if(on && !previewing && editor.Text!=leftPreviewText) ClearHistory();
  if(!on && previewing) { leftPreviewText=editor.Text; ClearSelection(); }
  mapping=false; mapView.Visibility=Visibility.Collapsed; Choose(mapTab,false);
  previewing=on; if(on) RenderPreview();
  previewScroll.Visibility=on?Visibility.Visible:Visibility.Collapsed; editor.Visibility=on?Visibility.Collapsed:Visibility.Visible;
  Choose(editTab,!on); Choose(previewTab,on); UpdateToolbar();
  if(!on && expanded) editor.Focus();
 }
 void UpdateToolbar() { toolbar.Visibility=(Writing || (previewing && editingBlock>=0))?Visibility.Visible:Visibility.Collapsed; mapBar.Visibility=mapping?Visibility.Visible:Visibility.Collapsed; UpdatePresenting(); }
 void LoadLines() {
  string text=editor.Text; eol=text.Contains("\r\n")?"\r\n":"\n";
  lines.Clear(); lines.AddRange(text.Replace("\r\n","\n").Replace('\r','\n').Split('\n'));
 }
 void WriteLines() { WriteLines(null); }
 // group：同一个对象连续改动可以合成一步撤销；null 表示单独一步；NoUndo 不记（比如点末尾时补的空行）。
 void WriteLines(object group) {
  string next=string.Join(eol,lines); if(next==editor.Text) return;
  if((previewing || mapping) && group!=NoUndo) Remember(group);
  editor.Text=next; if(mapping) leftPreviewText=next; // 回到预览时撤销记录还接得上
 }
 // ——— 预览里的撤销 / 重做 ———
 void Remember(object group) {
  var now=DateTime.Now;
  if(group==null || group!=histGroup || (now-histAt).TotalMilliseconds>1500) { undoTexts.Add(editor.Text); if(undoTexts.Count>200) undoTexts.RemoveAt(0); }
  histGroup=group; histAt=now; redoTexts.Clear();
 }
 void ClearHistory() { undoTexts.Clear(); redoTexts.Clear(); histGroup=null; }
 // 换回上一版原文，并在改动的地方打开输入框，光标放在改动处。
 void PreviewUndo(bool redo) {
  var from=redo?redoTexts:undoTexts; var to=redo?undoTexts:redoTexts;
  if(from.Count==0) return;
  if(editingBlock>=0) FinishEdit();
  string cur=editor.Text, prev=from[from.Count-1]; from.RemoveAt(from.Count-1); to.Add(cur); histGroup=null;
  editor.Text=prev;
  string a=cur.Replace("\r\n","\n"), b=prev.Replace("\r\n","\n");
  int p=0; while(p<a.Length && p<b.Length && a[p]==b[p]) p++;
  int s=0; while(s<a.Length-p && s<b.Length-p && a[a.Length-1-s]==b[b.Length-1-s]) s++;
  int at=Math.Max(p,b.Length-s), line=0, ls=0; for(int i=0;i<at;i++) if(b[i]=='\n') { line++; ls=i+1; }
  EditAtLine(line,at-ls,false);
 }
 // ——— 预览里的选择和复制 ———
 // 按下时先不进入编辑：松开时没拖动才算点击（进入这一行编辑）；拖过 5px 就变成按行选择。
 void Press(int line,int caret,MouseButtonEventArgs e) {
  if((Keyboard.Modifiers&ModifierKeys.Shift)!=0 && (selFrom>=0 || editingBlock>=0)) { ExtendSelection(e); return; }
  ClearSelection(); pressArmed=true; selecting=false; pressLine=line; pressCaret=caret; pressAt=e.GetPosition(previewBlocks);
  previewPage.CaptureMouse();
 }
 void PressMove(MouseEventArgs e) {
  if(!pressArmed || e.LeftButton!=MouseButtonState.Pressed) return;
  var p=e.GetPosition(previewBlocks);
  if(!selecting) {
   if(Math.Abs(p.Y-pressAt.Y)<5 && Math.Abs(p.X-pressAt.X)<5) return;
   selecting=true; if(editingBlock>=0) { FinishEdit(); RenderPreview(); } // 先把正在改的那行收起来，块号才和界面对得上
   pressAnchor=BlockAt(pressAt.Y);
  }
  int cur=BlockAt(p.Y); if(pressAnchor<0 || cur<0) return;
  selFrom=Math.Min(pressAnchor,cur); selTo=Math.Max(pressAnchor,cur); ShowSelection();
  double y=e.GetPosition(previewScroll).Y; // 拖到上下边外时跟着滚动
  if(y<0) previewScroll.ScrollToVerticalOffset(previewScroll.VerticalOffset-14); else if(y>previewScroll.ActualHeight) previewScroll.ScrollToVerticalOffset(previewScroll.VerticalOffset+14);
 }
 void PressUp(MouseButtonEventArgs e) {
  if(!pressArmed) return; bool sel=selecting; pressArmed=false; selecting=false; previewPage.ReleaseMouseCapture(); e.Handled=true;
  if(sel) { previewScroll.Focus(); return; } // 焦点放在预览上，Ctrl+C 才能收到
  if(pressLine==-2) EditAtEnd(); else EditAtLine(pressLine,pressCaret,true);
 }
 // Shift+点击：从已选的起点（或正在改的那一行）选到点的这一行。
 void ExtendSelection(MouseButtonEventArgs e) {
  int anchor=selFrom>=0?(pressAnchor>=selFrom && pressAnchor<=selTo?pressAnchor:selFrom):-1;
  if(editingBlock>=0) { int ln=blocks[editingBlock][0]; FinishEdit(); RenderPreview(); previewBlocks.UpdateLayout(); anchor=BlockForLine(Math.Min(ln,lines.Count-1)); }
  int cur=BlockAt(e.GetPosition(previewBlocks).Y); if(cur<0) return;
  if(anchor<0) anchor=selFrom>=0?selFrom:cur;
  pressAnchor=anchor; selFrom=Math.Min(anchor,cur); selTo=Math.Max(anchor,cur); ShowSelection(); previewScroll.Focus();
 }
 int BlockAt(double y) {
  int last=-1;
  for(int b=0;b<previewBlocks.Children.Count;b++) {
   var el=(FrameworkElement)previewBlocks.Children[b]; if(el.Visibility!=Visibility.Visible) continue;
   double top=el.TranslatePoint(new Point(0,0),previewBlocks).Y;
   if(y<top+el.ActualHeight) return b;
   last=b;
  }
  return last;
 }
 // 选中折起来的标题时，连同折起的内容一起算。
 int SelEnd() { int to=selTo; if(to>=0 && to<headLevel.Length && IsFolded(to)) to=foldEnd[to]-1; return to; }
 void ShowSelection() {
  int to=SelEnd(), n=0;
  for(int b=0;b<previewBlocks.Children.Count;b++) { var host=previewBlocks.Children[b] as Border; if(host==null) continue; bool on=b>=selFrom && b<=selTo; host.Background=on?Palette.AccentBg:Brushes.Transparent; // 选中的几行连成一整条：首行留上圆角，末行留下圆角
   double top=!on || b==selFrom?4:0, bottom=!on || b==selTo?4:0; host.CornerRadius=new CornerRadius(top,top,bottom,bottom); }
  for(int b=selFrom;b<=to && b<blocks.Count;b++) for(int k=blocks[b][0];k<blocks[b][0]+blocks[b][1];k++) if(lines[k].Trim().Length>0) n++;
  selInfo.Text="已选 "+n+" 行"; if(selPill.Visibility!=Visibility.Visible) { selPill.Visibility=Visibility.Visible; FadeIn(selPill); }
 }
 void ClearSelection() {
  if(selFrom<0 && selPill.Visibility!=Visibility.Visible) return;
  for(int b=0;b<previewBlocks.Children.Count;b++) { var host=previewBlocks.Children[b] as Border; if(host!=null && b>=selFrom && b<=selTo) { host.Background=Brushes.Transparent; host.CornerRadius=new CornerRadius(4); } }
  selFrom=selTo=-1; selPill.Visibility=Visibility.Collapsed; UpdateToolbar();
 }
 void SelectAllBlocks() { if(blocks.Count==0) return; if(editingBlock>=0) { FinishEdit(); RenderPreview(); } selFrom=0; selTo=blocks.Count-1; ShowSelection(); previewScroll.Focus(); }
 // markdown=false 时去掉 Markdown 符号：标题的 #、引用的 >、粗斜体、代码的反引号、代码块的 ```；列表换成 •，待办换成 ☐ / ☑，链接写成“文字 (网址)”。
 bool CopySelection(bool markdown) {
  if(selFrom<0) return false;
  var outLines=new List<string>(); int to=SelEnd();
  for(int b=selFrom;b<=to && b<blocks.Count;b++) {
   int s=blocks[b][0], c=blocks[b][1]; bool code=blocks[b][2]==K_Code;
   for(int k=s;k<s+c;k++) {
    string l=lines[k];
    if(!markdown && code && (k==s || k==s+c-1) && l.TrimStart().StartsWith("```")) continue;
    outLines.Add(markdown||code?l:PlainLine(l));
   }
  }
  while(outLines.Count>0 && outLines[0].Trim().Length==0) outLines.RemoveAt(0);
  while(outLines.Count>0 && outLines[outLines.Count-1].Trim().Length==0) outLines.RemoveAt(outLines.Count-1);
  string text=string.Join("\r\n",outLines.ToArray()); if(text.Length==0) return false;
  for(int tries=0;;tries++) { try { Clipboard.SetText(text); break; } catch(Exception ex) { if(tries>=2) { Msg("复制失败，剪贴板可能正被其他程序占用："+ex.Message); return false; } System.Threading.Thread.Sleep(60); } }
  selInfo.Text=markdown?"已复制 Markdown 原文":"已复制"; return true;
 }
 void CutSelection() { if(selFrom<0) return; if(CopySelection(true)) DeleteSelection(); }
 // 删掉选中的行（折起的标题连同折起的内容）；接缝处不留连续两个空行。Ctrl+Z 撤销。
 void DeleteSelection() {
  if(selFrom<0 || selFrom>=blocks.Count) return;
  int to=Math.Min(SelEnd(),blocks.Count-1), from=blocks[selFrom][0], end=blocks[to][0]+blocks[to][1];
  lines.RemoveRange(from,end-from);
  while(from>0 && from<lines.Count && lines[from-1].Trim().Length==0 && lines[from].Trim().Length==0) lines.RemoveAt(from);
  if(from==0) while(lines.Count>1 && lines[0].Trim().Length==0) lines.RemoveAt(0);
  if(lines.Count==0) lines.Add("");
  WriteLines(); RenderPreview(); previewScroll.Focus();
 }
 // 一级 / 二级 / 三级标题；level=0 恢复成正文。对选区覆盖的每一行生效，已经都是这一级时再按一次恢复正文。
 static readonly Regex HeadPrefixRe=new Regex(@"^\s{0,3}(#{1,6})\s+");
 static int HeadLevelOf(string l) { var m=HeadPrefixRe.Match(l); return m.Success?m.Groups[1].Length:0; }
 static string[] Headings(string[] parts,int level) {
  bool any=false, all=true;
  foreach(string p in parts) { string c=p.TrimEnd('\r'); if(c.Trim().Length==0) continue; any=true; if(HeadLevelOf(c)!=level) all=false; }
  int target=any && all?0:level; var res=new string[parts.Length];
  for(int i=0;i<parts.Length;i++) {
   string c=parts[i].TrimEnd('\r'), cr=parts[i].EndsWith("\r")?"\r":"";
   if(c.Trim().Length==0) { res[i]=parts.Length==1 && target>0?new string('#',target)+" "+cr:parts[i]; continue; }
   var m=HeadPrefixRe.Match(c); string body=m.Success?c.Substring(m.Length):c;
   if(target>0 && !m.Success) body=Regex.Replace(Regex.Replace(body,@"^\s*>\s?",""),@"^\s*"+ListPrefix,"").TrimStart(); // 列表、引用变标题时去掉它们的符号
   res[i]=(target>0?new string('#',target)+" ":"")+body+cr;
  }
  return res;
 }
 static void ToggleHeading(TextBox t,int level) {
  string text=t.Text; int s=t.SelectionStart, e=s+t.SelectionLength; bool collapsed=t.SelectionLength==0;
  if(e>s && text[e-1]=='\n') e--;
  int ls=s==0?0:text.LastIndexOf('\n',s-1)+1;
  int le=text.IndexOf('\n',e); if(le<0) le=text.Length;
  if(le>ls && text[le-1]=='\r') le--;
  string result=string.Join("\n",Headings(text.Substring(ls,le-ls).Split('\n'),level));
  t.Select(ls,le-ls); t.SelectedText=result; t.Focus();
  if(collapsed) t.Select(ls+result.TrimEnd('\r').Length,0); else t.Select(ls,result.Length);
 }
 // 预览里选中几行时直接改原文，改完保持选中，方便接着按。
 void HeadingSelection(int level) {
  int from=selFrom, to=selTo, end=SelEnd(); var idx=new List<int>();
  for(int b=from;b<=end && b<blocks.Count;b++) if(blocks[b][2]!=K_Code) idx.Add(blocks[b][0]);
  if(idx.Count==0) return;
  var parts=new string[idx.Count]; for(int i=0;i<idx.Count;i++) parts[i]=lines[idx[i]];
  var res=Headings(parts,level); for(int i=0;i<idx.Count;i++) lines[idx[i]]=res[i];
  WriteLines(); RenderPreview();
  if(from<blocks.Count) { selFrom=from; selTo=Math.Min(to,blocks.Count-1); ShowSelection(); previewScroll.Focus(); }
 }
 void ApplyHeading(int level) {
  if(mapping) return;
  if(previewing && editingBlock<0) { if(selFrom>=0) HeadingSelection(level); return; }
  var t=ActiveBox(); if(t!=null) ToggleHeading(t,level);
 }
 Popup headMenu;
 void ShowHeadMenu(Button anchor) {
  if(headMenu==null) {
   var list=new StackPanel { Margin=new Thickness(4) };
   var names=new[]{"一级标题","二级标题","三级标题","正文"}; var sizes=new[]{17.0,15.5,14.0,13.0}; var keys=new[]{"Ctrl+1","Ctrl+2","Ctrl+3","Ctrl+0"}; var levels=new[]{1,2,3,0};
   for(int i=0;i<4;i++) {
    var row=new DockPanel { Width=176, LastChildFill=true };
    var k=new TextBlock { Text=keys[i], Foreground=Palette.Muted, FontSize=11.5, VerticalAlignment=VerticalAlignment.Center }; DockPanel.SetDock(k,Dock.Right); row.Children.Add(k);
    row.Children.Add(new TextBlock { Text=names[i], Foreground=Palette.Text, FontSize=sizes[i], FontWeight=levels[i]>0?FontWeights.SemiBold:FontWeights.Normal, VerticalAlignment=VerticalAlignment.Center });
    var b=Ui.Button(row,null); b.Height=34; b.Padding=new Thickness(10,0,10,0); b.Focusable=false; b.HorizontalContentAlignment=HorizontalAlignment.Stretch; int lv=levels[i];
    b.Click+=(s,e)=> { headMenu.IsOpen=false; ApplyHeading(lv); }; list.Children.Add(b);
   }
   var card=new Border { Child=list, Background=Palette.Raised, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(10), Margin=new Thickness(12),
    Effect=new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth=4, BlurRadius=18, Opacity=0.35, Color=Colors.Black, Direction=270 } };
   headMenu=new Popup { Child=card, AllowsTransparency=true, StaysOpen=false, Placement=PlacementMode.Top, PopupAnimation=PopupAnimation.Fade, VerticalOffset=10, HorizontalOffset=-12 };
  }
  headMenu.PlacementTarget=anchor; headMenu.IsOpen=!headMenu.IsOpen;
 }
 static string PlainLine(string l) {
  if(Regex.IsMatch(l,@"^\s*([-*_])(\s*\1){2,}\s*$")) return "———";
  var m=HeadingRe.Match(l); if(m.Success) l=m.Groups[2].Value;
  l=Regex.Replace(l,@"^(\s*)>\s?","$1");
  l=Regex.Replace(l,@"^(\s*)[-*+]\s+\[([ xX])\]\s*",x=>x.Groups[1].Value+(x.Groups[2].Value==" "?"☐ ":"☑ "));
  l=Regex.Replace(l,@"^(\s*)[-*+]\s+","$1• ");
  l=Regex.Replace(l,@"\[([^\]]+)\]\(([^)]+)\)","$1 ($2)");
  l=Regex.Replace(l,@"\*\*([^*]+)\*\*","$1"); l=Regex.Replace(l,@"~~([^~]+)~~","$1"); l=Regex.Replace(l,@"`([^`]+)`","$1"); l=Regex.Replace(l,@"\*([^*]+)\*","$1");
  return l;
 }
 void BuildBlocks() {
  blocks.Clear(); int i=0;
  while(i<lines.Count) {
   if(lines[i].TrimStart().StartsWith("```")) { int j=i+1; while(j<lines.Count && !lines[j].TrimStart().StartsWith("```")) j++; int end=Math.Min(j,lines.Count-1); blocks.Add(new[]{i,end-i+1,K_Code}); i=end+1; }
   else { blocks.Add(new[]{i,1,lines[i].Trim().Length==0?K_Blank:K_Line}); i++; }
  }
 }
 int BlockForLine(int line) { for(int b=0;b<blocks.Count;b++) if(line>=blocks[b][0] && line<blocks[b][0]+blocks[b][1]) return b; return blocks.Count-1; }
 void RenderPreview() {
  rendering=true;
  editingBlock=-1; lineBox=null; selFrom=selTo=-1; selPill.Visibility=Visibility.Collapsed;
  LoadLines(); BuildBlocks(); FindFolds(); PruneFolds(); previewBlocks.Children.Clear();
  bool empty=editor.Text.Trim().Length==0;
  // 折起来的块照样生成、只是隐藏，这样块号和界面上的位置一一对应，点哪行改哪行的逻辑不用变。
  for(int b=0;b<blocks.Count;b++) { var el=empty && b==0 ? Placeholder() : RenderBlock(b); if(blockHidden[b]) el.Visibility=Visibility.Collapsed; previewBlocks.Children.Add(el); }
  rendering=false; UpdateToolbar();
 }
 // ——— 标题折叠 ———
 // 一个标题管到下一个同级或更高级标题之前；折起后这一段（含下级标题）都隐藏。代码块里的 # 不算标题。
 void FindFolds() {
  int n=blocks.Count; headLevel=new int[n]; foldEnd=new int[n]; foldCount=new int[n]; foldKey=new string[n]; blockHidden=new bool[n];
  var seen=new Dictionary<string,int>(); var set=FoldsOf(currentNote,false);
  for(int b=0;b<n;b++) {
   if(blocks[b][2]!=K_Line) continue; var m=HeadingRe.Match(lines[blocks[b][0]]); if(!m.Success) continue;
   headLevel[b]=m.Groups[1].Length; string k=headLevel[b]+" "+m.Groups[2].Value.Trim(); int c; seen.TryGetValue(k,out c); seen[k]=c+1; foldKey[b]=k+" "+c;
  }
  for(int b=0;b<n;b++) {
   if(headLevel[b]==0) continue; int e=b+1;
   while(e<n && (headLevel[e]==0 || headLevel[e]>headLevel[b])) { if(blocks[e][2]!=K_Blank) foldCount[b]+=blocks[e][1]; e++; }
   foldEnd[b]=e;
  }
  int until=-1;
  for(int b=0;b<n;b++) { if(b<until) { blockHidden[b]=true; continue; } if(IsFolded(b)) until=foldEnd[b]; }
 }
 bool IsFolded(int b) { var set=FoldsOf(currentNote,false); return headLevel[b]>0 && foldCount[b]>0 && set!=null && set.Contains(foldKey[b]); }
 HashSet<string> FoldsOf(string note,bool create) { HashSet<string> set; if(!folds.TryGetValue(note,out set) && create) { set=new HashSet<string>(); folds[note]=set; } return set; }
 // 标题改名或删掉后，旧的折叠记录就没用了。
 void PruneFolds() {
  var set=FoldsOf(currentNote,false); if(set==null || set.Count==0) return;
  var keys=new HashSet<string>(); foreach(var k in foldKey) if(k!=null) keys.Add(k);
  if(set.RemoveWhere(k=>!keys.Contains(k))>0) SaveFolds();
 }
 void ToggleFold(int line) {
  line=Settle(line,true); LoadLines(); BuildBlocks(); FindFolds();
  int b=BlockForLine(line); if(b<0 || headLevel[b]==0) { RenderPreview(); return; }
  var set=FoldsOf(currentNote,true); if(!set.Remove(foldKey[b])) set.Add(foldKey[b]);
  SaveFolds(); RenderPreview();
 }
 // 要编辑的行藏在折叠里时（新起一行、合并到上一行），把包住它的标题展开。
 bool Reveal(int bi) {
  var set=FoldsOf(currentNote,false); bool changed=false;
  for(int b=0;b<bi;b++) if(IsFolded(b) && foldEnd[b]>bi) { set.Remove(foldKey[b]); changed=true; }
  if(changed) SaveFolds(); return changed;
 }
 void LoadFolds() {
  try {
   string f=Path.Combine(dir,"folds.txt"); if(!File.Exists(f)) return;
   foreach(var raw in File.ReadAllLines(f)) { int t=raw.IndexOf('\t'); if(t<=0) continue; try { FoldsOf(raw.Substring(0,t),true).Add(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(raw.Substring(t+1)))); } catch {} }
  } catch {}
 }
 void SaveFolds() {
  try {
   var list=new List<string>(); foreach(var pair in folds) foreach(var k in pair.Value) list.Add(pair.Key+"\t"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(k)));
   string target=Path.Combine(dir,"folds.txt"), temp=Path.Combine(dir,"folds.tmp");
   File.WriteAllLines(temp,list.ToArray()); if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
  } catch {} // 折叠状态丢了不影响笔记本身，不打扰用户
 }
 FrameworkElement Placeholder() {
  var t=new TextBlock { Text="这篇笔记还是空的，点这里开始写。", Foreground=Palette.Muted, FontSize=BodySize };
  return Host(t,new Thickness(0,2,0,2),0,null);
 }
 TextBlock Body() { return new TextBlock { FontSize=BodySize, LineHeight=BodyLine, Foreground=Palette.Text, TextWrapping=TextWrapping.Wrap }; }
 // 每个块外包一层透明底，块之间的空隙也能点；点文字时把光标放到对应的原文位置。
 FrameworkElement Host(FrameworkElement content,Thickness pad,int line,TextBlock caretSource) {
  // 左右各伸出 6px，选中时的底色不贴着文字。
  var host=new Border { Background=Brushes.Transparent, Padding=new Thickness(pad.Left+6,pad.Top,pad.Right+6,pad.Bottom), Margin=new Thickness(-6,0,-6,0), CornerRadius=new CornerRadius(4), Child=content };
  host.MouseLeftButtonDown+=(s,e)=> {
   int caret=-1;
   if(caretSource!=null) { var pos=caretSource.GetPositionFromPoint(e.GetPosition(caretSource),true); if(pos!=null) { var run=pos.Parent as Run; if(run!=null && run.Tag is int) caret=(int)run.Tag+run.ContentStart.GetOffsetToPosition(pos); } }
   Press(line,caret,e); e.Handled=true;
  };
  return host;
 }
 FrameworkElement RenderBlock(int bi) {
  int start=blocks[bi][0], count=blocks[bi][1], kind=blocks[bi][2];
  if(kind==K_Blank) return Host(new Border { Height=10 },new Thickness(0),start,null);
  if(kind==K_Code) {
   var inner=new List<string>(); for(int k=start+1;k<start+count;k++) if(!(k==start+count-1 && lines[k].TrimStart().StartsWith("```"))) inner.Add(lines[k]);
   var code=new TextBlock { Text=string.Join("\n",inner), FontFamily=Ui.Mono, FontSize=13, LineHeight=20, Foreground=Palette.Text, TextWrapping=TextWrapping.Wrap };
   var box=new Border { Background=Palette.Raised, CornerRadius=new CornerRadius(6), Padding=new Thickness(12,8,12,8), Child=code, MinHeight=20 };
   return Host(box,new Thickness(0,2,0,6),count>1?start+1:start,null);
  }
  string line=lines[start]; Match m; var tb=Body();
  int prev=bi-1; while(prev>=0 && blockHidden[prev]) prev--; // 跟在折起的段落后面时，按看得见的上一块算间距
  bool afterText=prev>=0 && blocks[prev][2]!=K_Blank;
  if((m=HeadingRe.Match(line)).Success) {
   // 六级标题各有字号；五、六级用次要文字色，和正文拉开但不抢一到四级。
   int lv=m.Groups[1].Length; tb.FontWeight=FontWeights.SemiBold; tb.FontSize=HeadSize[lv-1]; tb.LineHeight=HeadLine[lv-1]; if(lv>=5) tb.Foreground=Palette.Muted;
   AddInline(tb.Inlines,m.Groups[2].Value,m.Groups[2].Index);
   var pad=new Thickness(0,afterText?(lv<=2?12:8):0,0,4);
   if(foldCount[bi]==0) return Host(tb,pad,start,tb);
   bool folded=IsFolded(bi); int ln=start;
   // 折叠开关挂在左侧留白里，不推动标题文字；展开时鼠标移到标题上才出现，折起后一直显示。
   var arrow=Ui.Icon(folded?"":"",9); arrow.Foreground=Palette.Muted;
   var toggle=new Border { Width=16, Height=tb.LineHeight, Background=Brushes.Transparent, Cursor=Cursors.Hand, Child=arrow, ToolTip=folded?"展开这一节":"折叠这一节", Opacity=folded?0.9:0 };
   toggle.MouseLeftButtonDown+=(s,e)=> { ToggleFold(ln); e.Handled=true; };
   var gutter=new Canvas { HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top }; Canvas.SetLeft(toggle,-16); gutter.Children.Add(toggle);
   if(folded) {
    var badge=new Border { Background=Palette.Raised, CornerRadius=new CornerRadius(5), Padding=new Thickness(7,1,7,2), Margin=new Thickness(8,0,0,0), Cursor=Cursors.Hand, ToolTip="已折叠，点击展开",
     Child=new TextBlock { Text="… "+foldCount[bi]+" 行", FontSize=11.5, LineHeight=16, FontWeight=FontWeights.Normal, Foreground=Palette.Muted } }; // 行高不跟标题走，否则小标签被撑高
    badge.MouseLeftButtonDown+=(s,e)=> { ToggleFold(ln); e.Handled=true; };
    tb.Inlines.Add(new InlineUIContainer(badge) { BaselineAlignment=BaselineAlignment.Center });
   }
   var row=new Grid(); row.Children.Add(tb); row.Children.Add(gutter);
   if(!folded) { row.MouseEnter+=(s,e)=>toggle.Opacity=0.8; row.MouseLeave+=(s,e)=>toggle.Opacity=0; }
   return Host(row,pad,start,tb);
  }
  if((m=Regex.Match(line,@"^\s*>\s?(.*)$")).Success) {
   tb.Foreground=Palette.Muted; AddInline(tb.Inlines,m.Groups[1].Value,m.Groups[1].Index);
   return Host(new Border { BorderBrush=Palette.Rule, BorderThickness=new Thickness(3,0,0,0), Padding=new Thickness(12,0,0,0), Child=tb },new Thickness(0,2,0,4),start,tb);
  }
  if((m=Regex.Match(line,@"^\s*[-*+]\s+\[([ xX])\]\s*(.*)$")).Success) {
   bool done=m.Groups[1].Value!=" "; if(done) tb.Foreground=Palette.Muted; AddInline(tb.Inlines,m.Groups[2].Value,m.Groups[2].Index);
   var mark=new Border { Background=Brushes.Transparent, Cursor=Cursors.Hand, Child=CheckMark(done), ToolTip=done?"标记为未完成":"标记为完成", VerticalAlignment=VerticalAlignment.Top, Padding=new Thickness(0,4.5,6,4) };
   int ln=start; mark.MouseLeftButtonDown+=(s,e)=> { ToggleTask(ln); e.Handled=true; };
   return Host(ListRow(mark,tb),new Thickness(0,1,0,1),start,tb);
  }
  if((m=Regex.Match(line,@"^\s*[-*+]\s+(.*)$")).Success) {
   AddInline(tb.Inlines,m.Groups[1].Value,m.Groups[1].Index);
   return Host(ListRow(new System.Windows.Shapes.Ellipse { Width=5, Height=5, Fill=Palette.Muted, Margin=new Thickness(6,9.5,0,0), HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top },tb),new Thickness(0,1,0,1),start,tb);
  }
  if((m=Regex.Match(line,@"^\s*(\d+)[.)]\s+(.*)$")).Success) {
   AddInline(tb.Inlines,m.Groups[2].Value,m.Groups[2].Index);
   return Host(ListRow(new TextBlock { Text=m.Groups[1].Value+".", Foreground=Palette.Muted, FontSize=13.5, LineHeight=BodyLine, VerticalAlignment=VerticalAlignment.Top },tb),new Thickness(0,1,0,1),start,tb);
  }
  if(Regex.IsMatch(line,@"^\s*([-*_])(\s*\1){2,}\s*$")) return Host(new Border { Height=1, Background=Palette.Line },new Thickness(0,11,0,11),start,null);
  AddInline(tb.Inlines,line,0);
  return Host(tb,new Thickness(0,1,0,3),start,tb);
 }
 static Grid ListRow(FrameworkElement marker,TextBlock text) {
  var g=new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(24) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
  g.Children.Add(marker); Grid.SetColumn(text,1); g.Children.Add(text); return g;
 }
 static FrameworkElement CheckMark(bool done) {
  var box=new Border { Width=15, Height=15, CornerRadius=new CornerRadius(4), HorizontalAlignment=HorizontalAlignment.Left, BorderThickness=new Thickness(done?0:1.4), BorderBrush=Palette.Muted, Background=done?Palette.Accent:Brushes.Transparent };
  if(done) box.Child=new System.Windows.Shapes.Path { Data=Geometry.Parse("M3.6,7.6 L6.3,10.2 L11.4,4.8"), Stroke=Palette.Panel, StrokeThickness=1.8, StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round, StrokeLineJoin=PenLineJoin.Round };
  return box;
 }
 // 行内格式。每段文字记下它在原文里的起点，点击时据此放光标。
 void AddInline(InlineCollection target,string line,int baseOffset) {
  var re=new Regex(@"(\*\*[^*]+\*\*|~~[^~]+~~|`[^`]+`|\*[^*]+\*|\[[^\]]+\]\([^)]+\))"); int pos=0;
  foreach(Match m in re.Matches(line)) {
   if(m.Index>pos) target.Add(new Run(line.Substring(pos,m.Index-pos)) { Tag=baseOffset+pos });
   string x=m.Value; Run r;
   if(x.StartsWith("**")) { r=new Run(x.Substring(2,x.Length-4)) { Tag=baseOffset+m.Index+2 }; r.FontWeight=FontWeights.SemiBold; }
   else if(x.StartsWith("~~")) { r=new Run(x.Substring(2,x.Length-4)) { Tag=baseOffset+m.Index+2 }; r.TextDecorations=TextDecorations.Strikethrough; r.Foreground=Palette.Muted; }
   else if(x.StartsWith("`")) { r=new Run(" "+x.Substring(1,x.Length-2)+" ") { Tag=baseOffset+m.Index }; r.FontFamily=Ui.Mono; r.FontSize=13; r.Background=Palette.Raised; }
   else if(x.StartsWith("*")) { r=new Run(x.Substring(1,x.Length-2)) { Tag=baseOffset+m.Index+1 }; r.FontStyle=FontStyles.Italic; }
   else {
    int end=x.IndexOf("]("); string url=x.Substring(end+2,x.Length-end-3);
    r=new Run(x.Substring(1,end-1)) { Tag=baseOffset+m.Index+1 }; r.Foreground=Palette.Accent; r.TextDecorations=TextDecorations.Underline; r.ToolTip=url+"\nCtrl+点击打开"; r.Cursor=Cursors.Hand;
    r.MouseLeftButtonDown+=(s,e)=> { if((Keyboard.Modifiers&ModifierKeys.Control)==0) return; e.Handled=true; if(Regex.IsMatch(url,@"^(https?://|mailto:)",RegexOptions.IgnoreCase)) { try { System.Diagnostics.Process.Start(url); } catch(Exception ex) { Msg("无法打开链接："+ex.Message); } } };
   }
   target.Add(r); pos=m.Index+m.Length;
  }
  if(pos<line.Length) target.Add(new Run(line.Substring(pos)) { Tag=baseOffset+pos });
 }
 void ToggleTask(int line) {
  line=Settle(line,true); if(line<0 || line>=lines.Count) return;
  lines[line]=Regex.Replace(lines[line],@"^(\s*[-*+]\s+\[)([ xX])(\])",m=>m.Groups[1].Value+(m.Groups[2].Value==" "?"x":" ")+m.Groups[3].Value);
  WriteLines(); RenderPreview();
 }
 // 正在编辑某行时，其他块上记的行号可能因为增删行而过时，这里换算成当前行号。
 int Settle(int line,bool stale) {
  if(editingBlock>=0) { if(stale && line>=editStart+editOrigCount) line+=editCount-editOrigCount; FinishEdit(); }
  return line;
 }
 void FinishEdit() { editingBlock=-1; lineBox=null; UpdateToolbar(); }
 void EditAtEnd() {
  Settle(0,false); LoadLines();
  int last=lines.Count-1;
  if(lines[last].Trim().Length>0) { lines.Add(""); WriteLines(NoUndo); last++; }
  EditAtLine(last,0,false);
 }
 // caret 为 -1 时放到行尾。
 void EditAtLine(int line,int caret,bool stale) {
  line=Settle(line,stale); RenderPreview();
  line=Math.Max(0,Math.Min(line,lines.Count-1));
  int bi=BlockForLine(line); if(blockHidden[bi] && Reveal(bi)) RenderPreview();
  int off=0;
  for(int k=blocks[bi][0];k<line;k++) off+=lines[k].Length+1;
  int len=lines[line].Length; off+=(caret<0||caret>len)?len:caret;
  BeginEdit(bi,off);
 }
 void BeginEdit(int bi,int caret) {
  int start=blocks[bi][0], count=blocks[bi][1], kind=blocks[bi][2];
  editingBlock=bi; editStart=start; editCount=count; editOrigCount=count;
  var box=new TextBox { Text=string.Join("\n",lines.GetRange(start,count)), Background=Brushes.Transparent, Foreground=Palette.Text, CaretBrush=Palette.Text, BorderThickness=new Thickness(0), Padding=new Thickness(0), AcceptsReturn=true, AcceptsTab=true, TextWrapping=TextWrapping.Wrap, FontSize=kind==K_Code?13:BodySize, FontFamily=kind==K_Code?Ui.Mono:FontFamily, SelectionBrush=Palette.Accent, SelectionOpacity=0.35, FocusVisualStyle=null, VerticalContentAlignment=VerticalAlignment.Center, MinHeight=BodyLine };
  lineBox=box; UpdateToolbar(); Dispatcher.BeginInvoke(new Action(()=> { if(lineBox==box) box.BringIntoView(); }),DispatcherPriority.Loaded);
  var frame=new Border { Background=Palette.Raised, CornerRadius=new CornerRadius(6), Margin=new Thickness(-8,1,-8,3), Padding=new Thickness(6,2,6,2), Child=box, Cursor=Cursors.IBeam };
  frame.MouseLeftButtonDown+=(s,e)=> { box.Focus(); e.Handled=true; };
  rendering=true; previewBlocks.Children.RemoveAt(bi); previewBlocks.Children.Insert(bi,frame); rendering=false;
  box.TextChanged+=(s,e)=> {
   if(rendering || box!=lineBox) return;
   var nl=box.Text.Replace("\r\n","\n").Split('\n');
   lines.RemoveRange(editStart,editCount); lines.InsertRange(editStart,nl); editCount=nl.Length; WriteLines(box);
  };
  box.PreviewKeyDown+=LineKey;
  box.LostFocus+=(s,e)=> { if(rendering || box!=lineBox) return; Dispatcher.BeginInvoke(new Action(()=> { if(box==lineBox && !box.IsKeyboardFocusWithin) { FinishEdit(); RenderPreview(); } })); };
  UpdateToolbar();
  Dispatcher.BeginInvoke(new Action(()=> { if(box!=lineBox) return; box.Focus(); box.CaretIndex=Math.Min(caret,box.Text.Length); box.BringIntoView(); }),DispatcherPriority.Input);
 }
 void LineKey(object sender,KeyEventArgs e) {
  var t=lineBox; if(t==null || editingBlock<0) return;
  int kind=blocks[editingBlock][2]; bool shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0, mods=Keyboard.Modifiers!=ModifierKeys.None;
  if(e.Key==Key.Enter && !mods && kind!=K_Code) { SplitLine(); e.Handled=true; }
  else if(e.Key==Key.Back && !mods && kind!=K_Code && t.SelectionLength==0 && t.CaretIndex==0 && editStart>0 && blocks[BlockForLine(editStart-1)][2]!=K_Code) {
   int prev=editStart-1; string joined=lines[prev]; int at=joined.Length;
   string cur=t.Text.Replace("\r\n","\n"); lines.RemoveRange(editStart,editCount); lines[prev]=joined+cur; WriteLines(); FinishEdit(); EditAtLine(prev,at,false); e.Handled=true;
  }
  // 上下移动时跳过折起来的段落，停在看得见的行上。
  else if(e.Key==Key.Up && !shift && t.GetLineIndexFromCharacterIndex(t.CaretIndex)<=0 && editStart>0) {
   int b=editingBlock-1; while(b>0 && blockHidden[b]) b--;
   EditAtLine(blocks[b][0]+blocks[b][1]-1,-1,false); e.Handled=true;
  }
  else if(e.Key==Key.Down && !shift && t.GetLineIndexFromCharacterIndex(t.CaretIndex)>=t.LineCount-1 && editStart+editCount<lines.Count) {
   int b=editingBlock+1; while(b<blocks.Count && blockHidden[b]) b++;
   if(b<blocks.Count) EditAtLine(blocks[b][0]+editCount-editOrigCount,0,false); e.Handled=true; // 正在编辑的块增减了行，后面的行号跟着挪
  }
 }
 // 回车拆成两行；列表、待办、有序、引用会续上同样的符号，空的列表项再按回车则结束列表。
 void SplitLine() {
  var t=lineBox; if(t.SelectionLength>0) t.SelectedText="";
  int c=t.CaretIndex; string before=t.Text.Substring(0,c).Replace("\r\n","\n"), after=t.Text.Substring(c).Replace("\r\n","\n");
  string lastBefore=before.Substring(before.LastIndexOf('\n')+1);
  var m=Regex.Match(lastBefore,@"^(\s*)(?:([-*+] )\[[ xX]\] |([-*+] )|(\d{1,9})([.)]) |(> ?))");
  string cont="";
  if(m.Success) {
   if(lastBefore==m.Value && after.Trim().Length==0 && before.IndexOf('\n')<0) {
    int at=editStart; lines.RemoveRange(editStart,editCount); lines.Insert(at,""); WriteLines(); FinishEdit(); EditAtLine(at,0,false); return;
   }
   string ind=m.Groups[1].Value;
   if(m.Groups[2].Success) cont=ind+m.Groups[2].Value+"[ ] ";
   else if(m.Groups[3].Success) cont=ind+m.Groups[3].Value;
   else if(m.Groups[4].Success) cont=ind+(long.Parse(m.Groups[4].Value)+1)+m.Groups[5].Value+" ";
   else cont=ind+m.Groups[6].Value;
  }
  var nl=(before+"\n"+cont+after).Split('\n'); int newLine=editStart+before.Split('\n').Length;
  lines.RemoveRange(editStart,editCount); lines.InsertRange(editStart,nl); WriteLines(); FinishEdit(); EditAtLine(newLine,cont.Length,false);
 }

 // ——— 文件暂存 ———
 void AddFiles(string[] paths) { foreach(string path in paths) if(!files.Contains(path)) files.Add(path); SaveShelf(); RefreshShelf(); }
 void SaveShelf() { if(!shelfReadable) return; try { var list=new List<string>(); foreach(string f in files) list.Add(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(f))); string target=Path.Combine(dir,"shelf.txt"); string temp=Path.Combine(dir,"shelf.tmp"); File.WriteAllLines(temp,list.ToArray()); if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target); } catch(Exception ex) {Msg("暂存区保存失败："+ex.Message);} }
 static string GlyphFor(string path) {
  if(Directory.Exists(path)) return G_Folder;
  string ext=Path.GetExtension(path).ToLowerInvariant();
  return (ext==".png"||ext==".jpg"||ext==".jpeg"||ext==".gif"||ext==".webp"||ext==".bmp"||ext==".heic")?G_Image:G_File;
 }
 void RefreshShelf() {
  shelf.Children.Clear(); ShowDropZone(); UpdatePresenting();
  if(files.Count==0) { shelfHint=new TextBlock { Foreground=Palette.Muted, FontSize=12, Margin=new Thickness(12,0,12,0), VerticalAlignment=VerticalAlignment.Center }; shelf.Children.Add(shelfHint); UpdatePresenting(); return; }
  shelfHint=null;
  foreach(string path in files) {
   string p=path; bool exists=File.Exists(p)||Directory.Exists(p);
   var card=new Border { Background=Palette.Raised, CornerRadius=new CornerRadius(7), Padding=new Thickness(9,0,2,0), Height=30, Margin=new Thickness(0,0,6,0), ToolTip=exists?p+"\n双击打开，拖出到其他应用":"文件不存在或已移动：\n"+p, Opacity=exists?1:0.55, Cursor=Cursors.Hand };
   var row=new StackPanel { Orientation=Orientation.Horizontal };
   var icon=Ui.Icon(exists?GlyphFor(p):"\uE7BA",13); icon.Foreground=exists?Palette.Muted:Palette.Warn; icon.Margin=new Thickness(0,0,7,0); row.Children.Add(icon);
   row.Children.Add(new TextBlock { Text=Path.GetFileName(p.TrimEnd('\\','/')), Foreground=Palette.Text, FontSize=12.5, MaxWidth=140, TextTrimming=TextTrimming.CharacterEllipsis, VerticalAlignment=VerticalAlignment.Center });
   var remove=Ui.Button(Ui.Icon(G_Close,8),"从暂存区移除（不删除文件）"); remove.Width=24; remove.Height=24; remove.Padding=new Thickness(0); remove.Margin=new Thickness(4,0,0,0); remove.Foreground=Palette.Muted;
   remove.Click+=(s,e)=> {files.Remove(p); SaveShelf(); RefreshShelf();}; row.Children.Add(remove);
   card.Child=row;
   card.MouseLeftButtonDown+=(s,e)=> {dragStart=e.GetPosition(card); if(e.ClickCount==2) {try { System.Diagnostics.Process.Start(p); } catch(Exception ex) {Msg(ex.Message);} }};
   card.MouseMove+=(s,e)=> {var q=e.GetPosition(card); if(e.LeftButton==MouseButtonState.Pressed && (Math.Abs(q.X-dragStart.X)>5 || Math.Abs(q.Y-dragStart.Y)>5)) { if(File.Exists(p)||Directory.Exists(p)) { draggingOut=true; try { DragDrop.DoDragDrop(card,new DataObject(DataFormats.FileDrop,new string[]{p}),DragDropEffects.Copy); } finally { draggingOut=false; } } else Msg("文件不存在或已移动：\n"+p); }};
   shelf.Children.Add(card);
  }
 }

 // ——— 展开与收起 ———
 void Expand() { Expand(false); }
 // 面板露出的范围：贴顶时上沿多留 18 让顶部两角保持直角，底部两角与面板同为 18 的圆角；浮起时四角都是圆角。
 Rect FullClip() { return Docked?new Rect(0,-18,pw,ph+18):new Rect(0,0,pw,ph); }
 Rect TuckClip() { double c=Docked?4+dotSize/2.0:dotY, w=dotSize-2; return new Rect(dotX-w/2,c-w/2,w,w); } // 收进圆点中心
 Rect NoClip() { return new Rect(dotX,Docked?4+dotSize/2.0:dotY,0,0); } // 收起后什么都不露
 // 展开：刘海不动，面板连同内容从刘海下面展开（320ms，先快后慢）；内容在 90ms 后淡入下滑，阴影在快展满时淡入。
 void Expand(bool transientOpen) {
  if(expanded) { if(!transientOpen) transient=false; Activate(); return; }
  bool wasVisible=body.Visibility==Visibility.Visible; double fromOpacity=wasVisible?body.Opacity:0;
  if(!wasVisible) PlaceWindow(); // 收起动画还没走完时不重摆，免得打断它
  Rect from=wasVisible?panelClip.Rect:TuckClip();
  expanded=true; transient=transientOpen && !pinned; leaveTimer.Stop(); hoverTimer.Stop(); Breathe(false);
  int gen=++animGen;
  dotLayer.IsHitTestVisible=false; // 展开后感应区不再接鼠标，免得挡住刘海
  ApplyChrome(true);
  body.Visibility=Visibility.Visible; Activate();
  if(Writing && !settingsOpen) editor.Focus();
  var slide=Slide();
  if(!SystemParameters.ClientAreaAnimation) { StopMotion(); panelClip.Rect=FullClip(); ResizeShadows(FullClip(),TimeSpan.Zero,null); body.Opacity=1; slide.Y=0; return; }
  var open=TimeSpan.FromMilliseconds(320); var settle=new PowerEase { Power=4, EasingMode=EasingMode.EaseOut };
  var clip=new RectAnimation(from,FullClip(),open) { EasingFunction=settle }; Timeline.SetDesiredFrameRate(clip,120);
  clip.Completed+=(s,e)=> { if(gen!=animGen) return; panelClip.BeginAnimation(RectangleGeometry.RectProperty,null); panelClip.Rect=FullClip(); ResizeShadows(FullClip(),TimeSpan.Zero,null); };
  panelClip.BeginAnimation(RectangleGeometry.RectProperty,clip); ResizeShadows(FullClip(),open,settle);
  var soft=new CubicEase { EasingMode=EasingMode.EaseOut }; var delay=TimeSpan.FromMilliseconds(fromOpacity>0.3?0:90);
  body.BeginAnimation(OpacityProperty,null); body.Opacity=fromOpacity;
  var fade=Anim(fromOpacity,1,TimeSpan.FromMilliseconds(220),soft); fade.BeginTime=delay;
  slide.BeginAnimation(TranslateTransform.YProperty,null); double fromY=fromOpacity>0.3?slide.Y:-6; slide.Y=fromY;
  var move=Anim(fromY,0,TimeSpan.FromMilliseconds(240),soft); move.BeginTime=delay;
  body.BeginAnimation(OpacityProperty,fade); slide.BeginAnimation(TranslateTransform.YProperty,move);
  FadeShadows(wasVisible?shadowFar.Opacity:0,1,160,wasVisible?0:200);
  FadeDot(0,120,0);
 }
 // 收起：面板连同内容收回刘海背后（300ms，起步慢、中间快、最后柔和收住）；内容和阴影在前段同时淡出，不再有“先消失再缩”的停顿。
 void Collapse() {
  if(!expanded) return;
  if(fullscreen) SetFullscreen(false); // 收起前先回到原来的大小，小圆点才在原位置
  HideCloseDialog();
  if(editingBlock>=0) { FinishEdit(); RenderPreview(); }
  if(!loading) {timer.Stop(); if(!SaveNote()) return;}
  expanded=false; transient=false; leaveTimer.Stop(); hoverTimer.Stop();
  int gen=++animGen;
  Action finish=()=> { if(gen!=animGen) return; Breathe(true); dotLayer.IsHitTestVisible=true; panelClip.BeginAnimation(RectangleGeometry.RectProperty,null); panelClip.Rect=NoClip(); ResizeShadows(TuckClip(),TimeSpan.Zero,null); body.Visibility=Visibility.Collapsed; ApplyChrome(false); };
  var slide=Slide();
  if(!SystemParameters.ClientAreaAnimation) { StopMotion(); body.Opacity=0; finish(); return; }
  var shut=TimeSpan.FromMilliseconds(300); var glide=new PowerEase { Power=3, EasingMode=EasingMode.EaseInOut };
  var clip=new RectAnimation(panelClip.Rect,TuckClip(),shut) { EasingFunction=glide }; Timeline.SetDesiredFrameRate(clip,120);
  clip.Completed+=(s,e)=>finish();
  panelClip.BeginAnimation(RectangleGeometry.RectProperty,clip); ResizeShadows(TuckClip(),shut,glide);
  var ease=new CubicEase { EasingMode=EasingMode.EaseIn };
  body.BeginAnimation(OpacityProperty,Anim(body.Opacity,0,TimeSpan.FromMilliseconds(160),ease));
  slide.BeginAnimation(TranslateTransform.YProperty,Anim(slide.Y,-6,TimeSpan.FromMilliseconds(200),ease));
  FadeShadows(shadowFar.Opacity,0,260,0);
  FadeDot(1,160,200);
 }
 static DoubleAnimation Anim(double from,double to,TimeSpan duration,IEasingFunction ease) {
  var a=new DoubleAnimation(from,to,duration) { EasingFunction=ease };
  Timeline.SetDesiredFrameRate(a,120); // 高刷新率屏幕上不卡在 60 帧
  return a;
 }
 TranslateTransform Slide() { var t=body.RenderTransform as TranslateTransform; if(t==null) { t=new TranslateTransform(); body.RenderTransform=t; } return t; }
 // 阴影底板（实心、带阴影特效）始终和面板露出的范围一样大，裁剪时不会从边上露出来。
 void ResizeShadows(Rect target,TimeSpan d,IEasingFunction ease) {
  double top=Docked?0:target.Y; double tw=target.Width, th=target.Y+target.Height-top;
  var at=new Thickness(ShadowPad+target.X,ShadowPad+top,0,0); // 阴影层四周比面板大出 ShadowPad
  foreach(var sh in new[]{shadowFar,shadowNear}) {
   double w=sh.Width, h=sh.Height;
   if(d==TimeSpan.Zero || double.IsNaN(w) || double.IsNaN(h)) { sh.BeginAnimation(WidthProperty,null); sh.BeginAnimation(HeightProperty,null); sh.BeginAnimation(MarginProperty,null); sh.Width=tw; sh.Height=th; sh.Margin=at; continue; }
   sh.BeginAnimation(WidthProperty,Anim(w,tw,d,ease)); sh.BeginAnimation(HeightProperty,Anim(h,th,d,ease));
   var move=new ThicknessAnimation(sh.Margin,at,d) { EasingFunction=ease }; Timeline.SetDesiredFrameRate(move,120); sh.BeginAnimation(MarginProperty,move);
  }
 }
 void FadeShadows(double from,double to,int ms,int delayMs) {
  foreach(var sh in new[]{shadowFar,shadowNear}) { sh.BeginAnimation(OpacityProperty,null); sh.Opacity=from; var a=Anim(from,to,TimeSpan.FromMilliseconds(ms),null); a.BeginTime=TimeSpan.FromMilliseconds(delayMs); sh.BeginAnimation(OpacityProperty,a); }
 }
 // 呼吸只改变透明度，按 exp(sin) 曲线从最亮开始，暗时仍可找到圆点。
 DoubleAnimationUsingKeyFrames BreathAnimation() {
  double seconds=breatheRate==1?7:breatheRate==3?3:5, low=Math.Exp(-1), span=Math.Exp(1)-low;
  var a=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(seconds), RepeatBehavior=RepeatBehavior.Forever };
  for(int i=0;i<=48;i++) { double t=(double)i/48, value=0.28+0.72*(Math.Exp(Math.Sin(2*Math.PI*t+Math.PI/2))-low)/span; a.KeyFrames.Add(new LinearDoubleKeyFrame(value,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds*t)))); }
  Timeline.SetDesiredFrameRate(a,30); return a;
 }
 void AnimateBreath(UIElement el,bool on) { if(el==null) return; el.BeginAnimation(OpacityProperty,null); el.Opacity=1; if(on && breatheRate>0 && SystemParameters.ClientAreaAnimation) el.BeginAnimation(OpacityProperty,BreathAnimation()); }
 void PreviewBreathe() { AnimateBreath(dotPreview,settingsOpen); }
 void Breathe(bool on) { AnimateBreath(dot,on && !springOn); }
 void FadeDot(double to,int ms,int delayMs) { var a=Anim(dotLayer.Opacity,to,TimeSpan.FromMilliseconds(ms),null); a.BeginTime=TimeSpan.FromMilliseconds(delayMs); dotLayer.BeginAnimation(OpacityProperty,a); }
 void StopMotion() {
  if(springOn) StopSpring();
  dotLayer.BeginAnimation(OpacityProperty,null); dotLayer.Opacity=expanded?0:1;
  panelClip.BeginAnimation(RectangleGeometry.RectProperty,null);
  body.BeginAnimation(OpacityProperty,null); Slide().BeginAnimation(TranslateTransform.YProperty,null);
  shadowFar.BeginAnimation(OpacityProperty,null); shadowNear.BeginAnimation(OpacityProperty,null); shadowFar.Opacity=shadowNear.Opacity=1;
 }
 void ToggleSleep() { if(!awake) {if(SetThreadExecutionState(ES_CONTINUOUS|ES_SYSTEM_REQUIRED)==0) {Msg("无法阻止系统睡眠。"); return;} awake=true;} else {SetThreadExecutionState(ES_CONTINUOUS); awake=false;} ShowSleepState(); }
 void Quit() {timer.Stop(); if(!SaveNote()) {Expand(false); if(!Ask("当前内容没能保存。如果需要，请先把要保留的文字复制出来。","放弃未保存的更改并退出？","放弃并退出",true)) return;} SetThreadExecutionState(ES_CONTINUOUS); tray.Visible=false; tray.Dispose(); quitting=true; Close(); Application.Current.Shutdown();}
}
}
