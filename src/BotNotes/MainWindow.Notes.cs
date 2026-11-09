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
    string dest=ArchiveDestination(archive,text,DateTime.Now);
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
 internal static string TitleOf(string text) {
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
 // 归档目标：标题 + 时间戳，重名时依次加 _2、_3…
 internal static string ArchiveDestination(string archive,string text,DateTime now) {
  string stem=TitleOf(text)+"_"+now.ToString("yyyy-MM-dd_HH-mm-ss",System.Globalization.CultureInfo.InvariantCulture); string dest=Path.Combine(archive,stem+".md");
  for(int k=2;File.Exists(dest);k++) dest=Path.Combine(archive,stem+"_"+k+".md");
  return dest;
 }
 bool SaveNote() { if(!noteReadable) { SetStatus("读取失败，无法保存",Palette.Warn); Msg("当前笔记未能读取，为保护原文件无法保存。请先复制编辑内容。"); return false; } try { string target=Path.Combine(dir,currentNote); string temp=target+".tmp"; File.WriteAllText(temp,editor.Text,System.Text.Encoding.UTF8); if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target); SetStatus("已自动保存",Palette.Accent); return true; } catch(Exception ex) { SetStatus("保存失败",Palette.Warn); Msg("笔记保存失败，请复制当前内容后检查磁盘：\n"+ex.Message); return false;} }

 // ——— 文件暂存 ———
 void AddFiles(string[] paths) { foreach(string path in paths) if(!files.Contains(path)) files.Add(path); SaveShelf(); RefreshShelf(); }
 void SaveShelf() { if(!shelfReadable) return; try { var list=new List<string>(); foreach(string f in files) list.Add(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(f))); string target=Path.Combine(dir,"shelf.txt"); string temp=Path.Combine(dir,"shelf.tmp"); File.WriteAllLines(temp,list.ToArray()); if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target); } catch(Exception ex) {Msg("暂存区保存失败："+ex.Message);} }
 static string GlyphFor(string path) {
  if(Directory.Exists(path)) return G_Folder;
  string ext=Path.GetExtension(path).ToLowerInvariant();
  return (ext==".png"||ext==".jpg"||ext==".jpeg"||ext==".gif"||ext==".webp"||ext==".bmp"||ext==".heic")?G_Image:G_File;
 }
 void RefreshShelf() {
  shelf.Children.Clear(); shelfTray=null; shelfHint=null; ShowDropZone(); UpdatePresenting();
  // 空状态只留一个 16px 的 Muted 线条托盘图标，文字提示和虚线框都不显示；提示文字放在整条的 ToolTip 上。
  if(files.Count==0) {
   shelfTray=LineIcon.Make(LineIcon.Tray,null,16); shelfTray.SetValue(TextElement.ForegroundProperty,Palette.Muted); shelfTray.Margin=new Thickness(12,0,12,0);
   shelf.Children.Add(shelfTray);
   shelfHint=new TextBlock { Foreground=Palette.Muted, FontSize=12.5, Margin=new Thickness(12,0,12,0), VerticalAlignment=VerticalAlignment.Center, Visibility=Visibility.Collapsed };
   shelf.Children.Add(shelfHint); UpdatePresenting(); return;
  }
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

}
}
