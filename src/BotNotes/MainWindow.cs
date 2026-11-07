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
partial class MainWindow : Window {
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
 Grid dotLayer, dotBody; Border dotHit; BotVisual bot; MatrixTransform dotWarp;
 // 位置：面板左上角（屏幕坐标）和小圆点在面板里的位置。面板上沿为 0 时贴在屏幕顶边，显示成刘海。
 const double Snap=16; double DotTop { get { return 4+BotFloat(dotSize)+dotSize/2.0; } }
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
 Button autoOnBtn, autoOffBtn; int dotSize=22; Slider dotSizeSlider; TextBlock dotSizeText; BotVisual dotPreview;
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
  Title="BotNotes"; WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent; ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; Topmost=true; Width=184; Height=34; FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI");
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
  var notchLabel=Ui.IconLabel(G_Note,"BotNotes"); ((TextBlock)notchLabel.Children[0]).FontSize=12; ((TextBlock)notchLabel.Children[0]).Foreground=Palette.NotchMuted;
  notch=Ui.Button(notchLabel,"点击收起；按住拖动可移动面板",new CornerRadius(0,0,12,12)); notch.Foreground=Palette.NotchText; notch.FocusVisualStyle=null; notch.Height=34; notch.FontSize=12; notch.VerticalAlignment=VerticalAlignment.Top;
  hoverTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(Math.Max(1,hoverDelay)) };
  hoverTimer.Tick+=(s,e)=> { hoverTimer.Stop(); if(!expanded && hoverOn && dotHit.IsMouseOver) Expand(true); };
  notch.Click+=(s,e)=> { hoverTimer.Stop(); if(expanded) Collapse(); else Expand(false); };
  notchShape=new System.Windows.Shapes.Path { Fill=Palette.Notch, Height=34, VerticalAlignment=VerticalAlignment.Top, HorizontalAlignment=HorizontalAlignment.Center, IsHitTestVisible=false };
  notchShape.SizeChanged+=(s,e)=> { notchShape.Data=NotchGeometry(notchShape.ActualWidth,34,Ear,12); };
  // 刘海属于面板，随面板一起从小圆点处展开、收回。刘海这一行的空白处和刘海本身都能按住拖动面板。
  var grip=new Border { Background=Brushes.Transparent, VerticalAlignment=VerticalAlignment.Stretch }; Draggable(grip,false,false,null);
  root.Children.Add(grip); root.Children.Add(notchShape); root.Children.Add(notch); Draggable(notch,false,true,null);
  // 收起后只留一个小机器人；周围一块看不见的感应区负责悬停、点击、拖动和拖入（Alpha=1，肉眼不可见但能接住鼠标）。
  dotLayer=new Grid { HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top };
  dotHit=new Border { Width=72, Height=16, Background=new SolidColorBrush(Color.FromArgb(1,0,0,0)), Cursor=Cursors.Hand, ToolTip="BotNotes：点击或停留展开；按住拖动可移动" }; ToolTipService.SetInitialShowDelay(dotHit,1200);
  // 辉光、球体和眼睛一起随弹簧变形，变形原点始终在球心。
  dotBody=new Grid { Width=dotSize, Height=dotSize, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Top, Margin=new Thickness(0,4,0,0), IsHitTestVisible=false, RenderTransformOrigin=new Point(0.5,0.5), RenderTransform=dotWarp=new MatrixTransform() };
  bot=new BotVisual(dotSize,accent,Palette.Light); dotBody.Children.Add(bot);
  dotLayer.Children.Add(dotHit); dotLayer.Children.Add(dotBody); outerGrid.Children.Add(dotLayer);
  dotHit.MouseEnter+=(s,e)=> { if(!expanded && !dragArmed) BotHover(true,e.GetPosition(dotHit).X); if(expanded || !hoverOn || dragArmed) return; if(hoverDelay<=0) Expand(true); else { hoverTimer.Interval=TimeSpan.FromMilliseconds(hoverDelay); hoverTimer.Start(); } };
  
  dotHit.MouseLeave+=(s,e)=> { hoverTimer.Stop(); BotHover(false,0); };
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
  DragEnter+=(s,e)=> { if(!HasFiles(e)) return; FileDragEntered(); };
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

  tray=new Forms.NotifyIcon { Text="BotNotes", Icon=LoadAppIcon(), Visible=true };
  var menu=new Forms.ContextMenuStrip { ShowImageMargin=false, Font=new D.Font("Microsoft YaHei UI",9f), Padding=new Forms.Padding(0,4,0,4) }; var trayLook=new TrayMenuRenderer(); menu.Renderer=trayLook;
  Action<string,EventHandler> item=(t,h)=> { var m=new Forms.ToolStripMenuItem(t,null,h) { Padding=new Forms.Padding(8,5,28,5) }; menu.Items.Add(m); };
  item("打开笔记",(s,e)=>Expand(false)); item("设置…",(s,e)=> { Expand(false); OpenSettings(); }); menu.Items.Add(new Forms.ToolStripSeparator()); item("退出",(s,e)=>Quit());
  menu.Opening+=(s,e)=> { trayLook.Back=Gdi(Palette.Panel.Color); trayLook.Hover=Gdi(Palette.Mix(Palette.Panel.Color,Palette.Text.Color,0.10)); trayLook.Text=Gdi(Palette.Text.Color); trayLook.Line=Gdi(Palette.Rule.Color); };
  menu.Opened+=(s,e)=>RoundCorners(menu.Handle);
  tray.ContextMenuStrip=menu; tray.DoubleClick+=(s,e)=>Expand(false);

  // 全屏时窗口要比屏幕大出一圈阴影留白；Windows 默认把窗口限制在约屏幕大小，右边和下边会露出一条桌面，所以放宽上限。
  SourceInitialized+=(s,e)=> { var src=System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle); if(src!=null) src.AddHook(WndHook); };
  SystemParameters.StaticPropertyChanged+=(sender,args)=> { if(args.PropertyName=="ClientAreaAnimation") { Breathe(!expanded); PreviewBreathe(); } };
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
  try { var st=typeof(MainWindow).Assembly.GetManifestResourceStream("BotNotes.ico"); if(st!=null) using(st) return new D.Icon(st,Forms.SystemInformation.SmallIconSize); } catch {}
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
  bool strip=Docked; dotHit.Width=Math.Max(72,dotSize+16); dotHit.Height=Math.Max(16,dotSize+2*BotFloat(dotSize)+8);
  dotBody.Width=dotBody.Height=dotSize; double center=strip?DotTop:dotY;
  dotLayer.Margin=new Thickness(dotX-dotHit.Width/2,strip?0:center-dotHit.Height/2,0,0); dotBody.Margin=new Thickness(0,strip?4+BotFloat(dotSize):(dotHit.Height-dotSize)/2,0,0);
  ApplyChrome(expanded);
  if(!expanded) { panelClip.BeginAnimation(RectangleGeometry.RectProperty,null); panelClip.Rect=NoClip(); ResizeShadows(TuckClip(),TimeSpan.Zero,null); }
  else if(!panelClip.HasAnimatedProperties) { panelClip.Rect=FullClip(); ResizeShadows(FullClip(),TimeSpan.Zero,null); }
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
}
}
