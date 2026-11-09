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
partial class MainWindow {
 // ——— 设置 ———
 // 设置项的纯数据载体：解析和序列化不接触窗口字段，测试可以直接调用。
 internal sealed class SettingsData {
  public int HoverDelayMs=600;
  public double PanelLeft=double.NaN, PanelTop=0, DotX=double.NaN, DotY=8;
  public double PanelW=DefW, PanelH=DefH;
  public bool HoverEnabled=true, Pinned=false;
  public int BotSize=22;
  public int FontSize=2;
  public Color Accent=Palette.DefaultAccent, Background=Palette.DefaultBackground;
  public string SaveDir=null;
  public string LidRestore=null;
  public string CleanRestore=null;
  public string CleanBg="black";
  public int StorageVersion=0;
 }
 internal static SettingsData ParseSettings(string[] rawLines,string dataDir) {
  var s=new SettingsData(); string legacyArchive=null;
  foreach(var raw in rawLines) {
   int i=raw.IndexOf('='); if(i<=0) continue; string k=raw.Substring(0,i).Trim(), v=raw.Substring(i+1).Trim(); int n; double d;
   bool num=double.TryParse(v,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out d) && !double.IsInfinity(d);
   if(k=="hoverDelayMs" && int.TryParse(v,out n)) s.HoverDelayMs=Math.Max(0,Math.Min(3000,n));
   else if(k=="panelLeft" && num) s.PanelLeft=d;
   else if(k=="panelTop" && num) s.PanelTop=d;
   else if(k=="dotX" && num) s.DotX=d;
   else if(k=="dotY" && num) s.DotY=d;
   else if(k=="panelW" && num) s.PanelW=Math.Max(MinW,d);
   else if(k=="panelH" && num) s.PanelH=Math.Max(MinH,d);
   else if(k=="hoverEnabled") s.HoverEnabled=v!="0";
   else if(k=="pinned") s.Pinned=v=="1";
   else if(k=="botSize") s.BotSize=int.TryParse(v,out n) && n>=16 && n<=40?n:22;
   else if(k=="fontSize") s.FontSize=int.TryParse(v,out n) && n>=0 && n<=4?n:2;
   else if(k=="accent") { try { s.Accent=(Color)ColorConverter.ConvertFromString(v); } catch {} }
   else if(k=="background") { try { s.Background=(Color)ColorConverter.ConvertFromString(v); } catch {} }
   else if(k=="saveDir" && v.Length>0) s.SaveDir=v;
   else if(k=="archiveDir" && v.Length>0) legacyArchive=v;
   else if(k=="lidRestore") { LidRestoreRecord r; if(LidRestoreRecord.TryParse(v,out r)) s.LidRestore=r.ToString(); }
   else if(k=="cleanRestore") { CleanRestoreRecord r; if(CleanRestoreRecord.TryParse(v,out r)) s.CleanRestore=r.ToString(); }
   else if(k=="cleanBg" && (v=="black" || v=="white" || v=="keep")) s.CleanBg=v;
   else if(k=="storage" && int.TryParse(v,out n)) s.StorageVersion=n;
  }
  // 没有 saveDir 时沿用旧的 archiveDir（用户自定义过归档位置）；否则由 SaveDir() 给默认值。
  if(s.SaveDir==null && legacyArchive!=null) s.SaveDir=legacyArchive;
  s.SaveDir=RewriteArchiveDir(s.SaveDir,dataDir);
  return s;
 }
 internal static string[] SerializeSettings(SettingsData s) {
  Func<double,string> pos=v=>double.IsNaN(v)?"":Math.Round(v,1).ToString(System.Globalization.CultureInfo.InvariantCulture);
  var list=new System.Collections.Generic.List<string> { "hoverEnabled="+(s.HoverEnabled?"1":"0"), "hoverDelayMs="+s.HoverDelayMs, "saveDir="+(s.SaveDir??""), "pinned="+(s.Pinned?"1":"0"), "botSize="+s.BotSize, "fontSize="+s.FontSize, "accent="+Palette.ToHex(s.Accent), "background="+Palette.ToHex(s.Background),
   "panelLeft="+pos(s.PanelLeft), "panelTop="+pos(s.PanelTop), "dotX="+pos(s.DotX), "dotY="+pos(s.DotY), "panelW="+pos(s.PanelW), "panelH="+pos(s.PanelH) };
  if(!string.IsNullOrEmpty(s.LidRestore)) list.Add("lidRestore="+s.LidRestore);
  if(!string.IsNullOrEmpty(s.CleanRestore)) list.Add("cleanRestore="+s.CleanRestore);
  list.Add("cleanBg="+(s.CleanBg=="white" || s.CleanBg=="keep"?s.CleanBg:"black"));
  if(s.StorageVersion>=2) list.Add("storage=2");
  return list.ToArray();
 }
 internal static string SettingsPath(string dataDir) { return Path.Combine(dataDir,"settings.ini"); }
 internal static SettingsData ReadSettingsFile(string dataDir) {
  string f=SettingsPath(dataDir); return File.Exists(f)?ParseSettings(File.ReadAllLines(f),dataDir):null;
 }
 internal static void WriteSettingsFile(string dataDir,SettingsData s) {
  var text=SerializeSettings(s);
  string target=SettingsPath(dataDir), temp=Path.Combine(dataDir,"settings.tmp");
  Directory.CreateDirectory(dataDir);
  File.WriteAllLines(temp,text,System.Text.Encoding.UTF8); if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
 }
 void LoadSettings() {
  try {
   var s=ReadSettingsFile(dir); if(s==null) return;
   hoverDelay=s.HoverDelayMs; panelLeft=s.PanelLeft; panelTop=s.PanelTop; dotX=s.DotX; dotY=s.DotY; prefW=s.PanelW; prefH=s.PanelH; hoverOn=s.HoverEnabled; pinned=s.Pinned; dotSize=s.BotSize; fontStep=s.FontSize; accent=s.Accent; background=s.Background; saveDir=s.SaveDir; lidRestore=s.LidRestore; cleanRestore=s.CleanRestore; cleanBg=s.CleanBg; storageVersion=s.StorageVersion;
  } catch {}
 }
 bool SaveSettings(bool quiet=false) {
  try {
   double panelLeft=this.panelLeft, panelTop=this.panelTop, dotX=this.dotX, dotY=this.dotY; // 全屏时记的是全屏前的位置
   if(fullscreen && beforeFull!=null) { panelLeft=beforeFull[0]; panelTop=beforeFull[1]; dotX=beforeFull[4]; dotY=beforeFull[5]; }
   var s=new SettingsData { HoverEnabled=hoverOn, HoverDelayMs=hoverDelay, SaveDir=saveDir, StorageVersion=storageVersion, Pinned=pinned, BotSize=dotSize, FontSize=fontStep, Accent=accent, Background=background, PanelLeft=panelLeft, PanelTop=panelTop, DotX=dotX, DotY=dotY, PanelW=prefW, PanelH=prefH, LidRestore=lidRestore, CleanRestore=cleanRestore, CleanBg=cleanBg };
   WriteSettingsFile(dir,s);
   return true;
  } catch(Exception ex) { if(!quiet) Msg("设置保存失败："+ex.Message); return false; }
 }
 string SaveDir() { return string.IsNullOrEmpty(saveDir)?DefaultSaveDir():saveDir; }
 static bool SamePath(string a,string b) { try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\','/'),Path.GetFullPath(b).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase); } catch { return false; } }
 static StackPanel Section(string title,string hint) {
  var s=new StackPanel { Margin=new Thickness(0,0,0,22) };
  s.Children.Add(new TextBlock { Text=title, Foreground=Palette.Text, FontSize=13.5, FontWeight=FontWeights.SemiBold });
  if(!string.IsNullOrEmpty(hint)) s.Children.Add(new TextBlock { Text=hint, Foreground=Palette.Muted, FontSize=12, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,3,0,10), LineHeight=19 });
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

  var auto=Section("开机自动启动","开启后，登录 Windows 时自动打开 BotNotes，不用每次开机后手动打开。也可以在任务管理器的“启动应用”里关掉。");
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
  minusBtn=Secondary(LineIcon.Make(LineIcon.Minus,null,14),"缩短 0.1 秒"); minusBtn.Width=30; minusBtn.Padding=new Thickness(0); minusBtn.Margin=new Thickness(0); minusBtn.Click+=(s,e)=>StepHover(-100); hoverRow.Children.Add(minusBtn);
  hoverValue=new TextBlock { Foreground=Palette.Text, FontSize=13, Width=64, TextAlignment=TextAlignment.Center, VerticalAlignment=VerticalAlignment.Center }; hoverRow.Children.Add(hoverValue);
  plusBtn=Secondary(LineIcon.Make(LineIcon.Plus,null,14),"延长 0.1 秒"); plusBtn.Width=30; plusBtn.Padding=new Thickness(0); plusBtn.Margin=new Thickness(0); plusBtn.Click+=(s,e)=>StepHover(100); hoverRow.Children.Add(plusBtn);
  hover.Children.Add(hoverRow); list.Children.Add(hover);

  var place=Section("位置和大小","按住小圆点，或按住面板顶部的空白处和“BotNotes”标签，可以拖到别处。拖到屏幕顶边附近会自动贴住。按住面板的左、右、下边或下面两个角拖动，可以改大小；面板在顶部正中时左右一起缩放，保持居中。");
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
  // 干净桌面背景：三段切换，写法和上面的两段开关一致；开启期间改了，下次开启才生效。
  var cleanLabel=rowLabel("干净桌面背景",14); cleanBgName=cleanLabel; theme.Children.Add(cleanLabel);
  var cleanSeg=new Border { Background=Palette.Panel, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(8), Padding=new Thickness(2), HorizontalAlignment=HorizontalAlignment.Left };
  var cleanRow=new StackPanel { Orientation=Orientation.Horizontal }; cleanSeg.Child=cleanRow;
  cleanBlackBtn=Ui.Button("纯黑","收起桌面图标，背景换成纯黑",new CornerRadius(6)); cleanBlackBtn.Height=26; cleanBlackBtn.Click+=(s,e)=>SetCleanBg("black"); cleanRow.Children.Add(cleanBlackBtn);
  cleanWhiteBtn=Ui.Button("纯白","收起桌面图标，背景换成纯白",new CornerRadius(6)); cleanWhiteBtn.Height=26; cleanWhiteBtn.Click+=(s,e)=>SetCleanBg("white"); cleanRow.Children.Add(cleanWhiteBtn);
  cleanKeepBtn=Ui.Button("保持当前壁纸","只收起桌面图标，不改壁纸和背景色",new CornerRadius(6)); cleanKeepBtn.Height=26; cleanKeepBtn.Click+=(s,e)=>SetCleanBg("keep"); cleanRow.Children.Add(cleanKeepBtn);
  theme.Children.Add(cleanSeg);
  var sizeLine=new Grid { Margin=new Thickness(0,14,0,0), HorizontalAlignment=HorizontalAlignment.Left }; sizeLine.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); sizeLine.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
  var sizeLeft=new StackPanel { VerticalAlignment=VerticalAlignment.Center }; sizeLeft.Children.Add(new TextBlock { Text="小机器人大小", Foreground=Palette.Text, FontSize=12.5, Margin=new Thickness(0,0,0,6) });
  var sizeRow=new StackPanel { Orientation=Orientation.Horizontal }; sizeLeft.Children.Add(sizeRow); sizeLine.Children.Add(sizeLeft);
  dotSizeSlider=new Slider { Minimum=16, Maximum=40, TickFrequency=1, IsSnapToTickEnabled=true, SmallChange=1, LargeChange=1, Width=160, Height=28, Value=dotSize, Template=DotSliderTemplate(), VerticalAlignment=VerticalAlignment.Center };
  dotSizeText=new TextBlock { Text=dotSize+" px", Foreground=Palette.Text, FontSize=12, Width=48, Margin=new Thickness(12,0,0,0), VerticalAlignment=VerticalAlignment.Center };
  var previewBox=new Grid { Width=64, Height=64, VerticalAlignment=VerticalAlignment.Center, ClipToBounds=false }; dotPreview=new BotVisual(dotSize,accent,Palette.Light) { HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center }; previewBox.Children.Add(dotPreview); Grid.SetColumn(previewBox,1); sizeLine.Children.Add(previewBox);
  dotSizeSlider.ValueChanged+=(sender,args)=> { dotSize=(int)Math.Round(dotSizeSlider.Value); dotSizeText.Text=dotSize+" px"; bot.Resize(dotSize); dotPreview.StopWrite(); dotPreview.Resize(dotSize); ClampDot(); ApplyLayout(); Breathe(!expanded); PreviewBreathe(); };
  dotSizeSlider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((sender,args)=>SaveSettings()));
  dotSizeSlider.PreviewKeyUp+=(sender,args)=> { if(args.Key==Key.Left || args.Key==Key.Right) SaveSettings(); };
  dotSizeSlider.PreviewMouseLeftButtonUp+=(sender,args)=>SaveSettings();
  sizeRow.Children.Add(dotSizeSlider); sizeRow.Children.Add(dotSizeText); theme.Children.Add(sizeLine); PreviewBreathe();
  list.Children.Add(theme);

  var save=Section("保存位置",null);
  var pathBox=new Border { Background=Palette.Panel, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(7), Padding=new Thickness(10,7,10,7), Margin=new Thickness(0,8,0,8) };
  savePathText=new TextBlock { Foreground=Palette.Text, FontSize=12.5, TextTrimming=TextTrimming.CharacterEllipsis }; pathBox.Child=savePathText; save.Children.Add(pathBox);
  save.Children.Add(new TextBlock { Text="新建的笔记存在这里；已有的文件不会移动。", Foreground=Palette.Muted, FontSize=12, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,10), LineHeight=19 });
  var saveRow=new StackPanel { Orientation=Orientation.Horizontal };
  var change=Secondary(Ui.IconLabel(LineIcon.Folder,"更改…",16),"选择新的保存文件夹"); change.Click+=(s,e)=>PickSaveDir(); saveRow.Children.Add(change);
  var open=Secondary("打开文件夹","在资源管理器中打开"); open.Click+=(s,e)=>OpenSaveDir(); saveRow.Children.Add(open);
  resetSaveBtn=Ui.Button("恢复默认","改回“文档\\BotNotes”"); resetSaveBtn.Foreground=Palette.Muted; resetSaveBtn.Click+=(s,e)=> { saveDir=null; SaveSettings(); UpdateSettingsView(); }; saveRow.Children.Add(resetSaveBtn);
  save.Children.Add(saveRow); list.Children.Add(save);
  return view;
 }
 static string PresetName(string[][] list,Color c) { foreach(var p in list) if((Color)ColorConverter.ConvertFromString(p[0])==c) return p[1]; return "自定义"; }
 void StepHover(int ms) { hoverDelay=Math.Max(0,Math.Min(3000,hoverDelay+ms)); SaveSettings(); UpdateSettingsView(); }
 // 干净桌面的背景只写进设置；开关正开着时不去动桌面，下次开启再生效。
 void SetCleanBg(string value) { if(value!="black" && value!="white" && value!="keep") return; cleanBg=value; SaveSettings(); UpdateSettingsView(); }
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
 void ThemeChanged() { if(bot!=null) bot.Recolor(accent,Palette.Light); if(dotPreview!=null) dotPreview.Recolor(accent,Palette.Light); ThemeResources(); Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=DarkScrollBar(); Resources[typeof(ToolTip)]=TipStyle(); SaveSettings(); UpdateSettingsView(); if(mapping) RenderMap(false); }
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
  ((Run)cleanBgName.Tag).Text=" · "+(cleanBg=="white"?"纯白":cleanBg=="keep"?"保持当前壁纸":"纯黑");
  Choose(cleanBlackBtn,cleanBg=="black"); Choose(cleanWhiteBtn,cleanBg=="white"); Choose(cleanKeepBtn,cleanBg=="keep");
  ShowCleanDesktopState(); // 关闭状态的悬停提示跟着背景设置变
  Choose(hoverOnBtn,hoverOn); Choose(hoverOffBtn,!hoverOn);
  hoverValue.Text=hoverDelay==0?"立即":(hoverDelay/1000.0).ToString("0.0")+" 秒"; hoverValue.Opacity=hoverOn?1:0.4;
  minusBtn.IsEnabled=hoverOn && hoverDelay>0; plusBtn.IsEnabled=hoverOn && hoverDelay<3000;
  string a=SaveDir(); savePathText.Text=a; savePathText.ToolTip=a; resetSaveBtn.IsEnabled=!string.IsNullOrEmpty(saveDir);
  resetPlaceBtn.IsEnabled=!AtHome(); resetSizeBtn.IsEnabled=!DefaultSize();
  bool autoOn=AutoStartOn(); Choose(autoOnBtn,autoOn); Choose(autoOffBtn,!autoOn);
 }
}
}
