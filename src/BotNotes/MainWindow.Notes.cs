using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
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
 // ——— 标签 ———
 string NoteTextOf(string path) {
  if(path==currentNote) return editor.Text;
  NoteFileState st;
  if(noteStates.TryGetValue(path,out st)) return st.LastSavedText;
  try { string text, error=ReadTextFile(path,out text,out var encoding,out var bom); if(error==null) return text; } catch {}
  return "";
 }
 string GetNoteTitle(string name) {
  string title=TitleOf(name==DraftKey?"":NoteTextOf(name));
  if(title.Length>12) title=title.Substring(0,12)+"…";
  return title;
 }
 void RefreshTabs() {
  tabs.Children.Clear(); Button active=null;
  foreach(string n in notePaths) {
   string name=n, title=GetNoteTitle(name); bool on=name==currentNote, missing=missingNotes.Contains(name); if(on) currentTitle=title;
   var text=new TextBlock { Text=title, VerticalAlignment=VerticalAlignment.Center };
   if(missing) { text.Foreground=Palette.Muted; text.TextDecorations=TextDecorations.Strikethrough; }
   var label=new StackPanel { Orientation=Orientation.Horizontal }; label.Children.Add(text);
   var x=Ui.Button(Ui.Icon(G_Close,7),"关闭这篇笔记（Ctrl+W）"); x.Width=18; x.Height=18; x.Padding=new Thickness(0); x.Margin=new Thickness(6,0,-3,0); x.Focusable=false; x.Opacity=on?0.8:0; x.IsHitTestVisible=on;
   x.Click+=(s,e)=> { e.Handled=true; CloseNote(name); }; label.Children.Add(x);
   var b=Ui.Button(label,"切换笔记："+title+"（Ctrl+Tab；中键点击关闭）"); b.Height=28; b.Margin=new Thickness(0,0,4,0); b.Padding=new Thickness(10,0,6,0);
   x.Foreground=on?Palette.Accent:Palette.Muted;
   b.MouseEnter+=(s,e)=> { x.Opacity=0.8; x.IsHitTestVisible=true; }; b.MouseLeave+=(s,e)=> { if(!on) { x.Opacity=0; x.IsHitTestVisible=false; } };
   b.MouseUp+=(s,e)=> { if(e.ChangedButton==MouseButton.Middle) { e.Handled=true; CloseNote(name); } };
   b.Background=on?Palette.AccentBg:Brushes.Transparent; b.Foreground=missing?Palette.Muted:(on?Palette.Accent:Palette.Muted); if(on && !missing) b.FontWeight=FontWeights.SemiBold;
   b.Click+=(s,e)=> { CloseSettings(); SwitchNote(name); }; TabDrag(b,name); tabs.Children.Add(b); if(on) active=b;
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
  double room=tabRowPanel.ActualWidth-tabAdd.ActualWidth-tabAdd.Margin.Left-tabOpen.ActualWidth-tabOpen.Margin.Left;
  if(room<=0) return;
  bool over=tabs.DesiredSize.Width>room+0.5; var v=over?Visibility.Visible:Visibility.Collapsed;
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
 void TabDrag(Button b,string name) {
  b.PreviewMouseLeftButtonDown+=(s,e)=> { tabDragName=name; tabDragging=true; tabDragMoved=false; tabGrab=e.GetPosition(b).X; };
  b.PreviewMouseMove+=(s,e)=> {
   if(!tabDragging || tabDragName!=name || e.LeftButton!=MouseButtonState.Pressed) return;
   double x=e.GetPosition(tabs).X, left=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(b).X;
   if(!tabDragMoved) { if(Math.Abs(x-tabGrab-left)<6) return; tabDragMoved=true; b.CaptureMouse(); Panel.SetZIndex(b,1); b.RenderTransform=new TranslateTransform(); }
   int i=tabs.Children.IndexOf(b);
   while(i+1<tabs.Children.Count && x>TabMid(i+1)) { var o=tabs.Children[i+1]; tabs.Children.RemoveAt(i+1); tabs.Children.Insert(i,o); SwapNotes(i,i+1); i++; tabs.UpdateLayout(); }
   while(i>0 && x<TabMid(i-1)) { var o=tabs.Children[i-1]; tabs.Children.RemoveAt(i-1); tabs.Children.Insert(i,o); SwapNotes(i-1,i); i--; tabs.UpdateLayout(); }
   left=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(b).X;
   ((TranslateTransform)b.RenderTransform).X=Clamp(x-tabGrab-left,-left,Math.Max(0,tabs.ActualWidth-left-b.ActualWidth));
   double v=e.GetPosition(tabsScroll).X;
   if(v<24) tabsScroll.ScrollToHorizontalOffset(tabsScroll.HorizontalOffset-8); else if(v>tabsScroll.ActualWidth-24) tabsScroll.ScrollToHorizontalOffset(tabsScroll.HorizontalOffset+8);
  };
  b.PreviewMouseLeftButtonUp+=(s,e)=> { if(!tabDragging || tabDragName!=name) return; bool moved=tabDragMoved; tabDragging=false; tabDragName=null; if(!moved) return; e.Handled=true; EndTabDrag(b); b.ReleaseMouseCapture(); };
  b.LostMouseCapture+=(s,e)=> { if(tabDragging && tabDragName==name && tabDragMoved) { tabDragging=false; tabDragName=null; EndTabDrag(b); } };
 }
 double TabMid(int i) { var r=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot((FrameworkElement)tabs.Children[i]); return r.X+r.Width/2; }
 void SwapNotes(int a,int b) { string t=notePaths[a]; notePaths[a]=notePaths[b]; notePaths[b]=t; }
 void EndTabDrag(Button b) { b.RenderTransform=Transform.Identity; Panel.SetZIndex(b,0); SaveTabOrder(); }
 int IndexOfPath(string path) { for(int i=0;i<notePaths.Count;i++) if(SamePath(notePaths[i],path)) return i; return -1; }
 void SaveTabOrder() { SaveOrderFile(dir,SerializeOrderPaths(notePaths)); }
 void CycleNote(int step) { if(notePaths.Count<2) return; int i=IndexOfPath(currentNote); if(i<0) i=0; SwitchNote(notePaths[(i+step+notePaths.Count)%notePaths.Count]); }

 // ——— 启动时恢复标签 ———
 void LoadInitialTabs() {
  notePaths.Clear(); noteStates.Clear(); missingNotes.Clear();
  var missing=new List<string>(); var bad=new List<string>();
  var paths=new List<string>();
  try {
   string f=Path.Combine(dir,"order.txt");
   if(File.Exists(f)) paths.AddRange(ParseOrderPaths(File.ReadAllLines(f),dir));
  } catch {}
  // 迁移中止或还没有 storage=2 时，旧的 note*.md 仍按普通路径打开。
  if(storageVersion<2) {
   try { foreach(string name in LegacyNoteFiles(dir)) { string p=Path.GetFullPath(Path.Combine(dir,name)); bool has=false; foreach(string q in paths) if(SamePath(q,p)) {has=true;break;} if(!has) paths.Add(p); } } catch {}
  }
  foreach(string path in paths) {
   if(!File.Exists(path)) { missing.Add(path); continue; }
   string text,error=ReadTextFile(path,out text,out var encoding,out var bom);
   if(error!=null) { bad.Add(path+"："+error); continue; }
   var fi=new FileInfo(path);
   noteStates[path]=new NoteFileState { Path=path, Encoding=encoding, Bom=bom, LastWriteUtc=fi.LastWriteTimeUtc, Length=fi.Length, LastSavedText=text, NamedTitle=TitleOf(text) };
   notePaths.Add(path);
  }
  if(notePaths.Count==0) notePaths.Add(DraftKey);
  currentNote=notePaths[0]; savedText="";
  NoteFileState first;
  if(currentNote!=DraftKey && noteStates.TryGetValue(currentNote,out first)) { editor.Text=first.LastSavedText; savedText=first.LastSavedText; }
  else editor.Text="";
  noteReadable=true;
  if(missing.Count>0 || bad.Count>0) SaveTabOrder();
  if(missing.Count>0) {
   var copy=new List<string>(missing);
   Dispatcher.BeginInvoke(new Action(()=>Msg("以下文件找不到了，已从标签里移除：\n"+string.Join("\n",copy.ToArray()))),DispatcherPriority.ApplicationIdle);
  }
  if(bad.Count>0) {
   var copy=new List<string>(bad);
   Dispatcher.BeginInvoke(new Action(()=>Msg("以下文件打不开，已从标签里移除：\n"+string.Join("\n",copy.ToArray()))),DispatcherPriority.ApplicationIdle);
  }
 }

 // ——— 切换 / 新建 ———
 void SwitchNote(string name) {
  if(name==currentNote) return;
  if(Presenting) ExitPresentation(); if(editingBlock>=0) FinishEdit(); timer.Stop();
  if(!SaveNote()) return;
  RenameCurrentAtOpportunity();
  string text; NoteFileState st;
  if(!LoadTabText(name,out text,out st)) return;
  loading=true; currentNote=name; noteReadable=(name==DraftKey)||st==null||st.Readable; savedText=text; editor.Text=text; loading=false; ClearHistory();
  RefreshTabs(); if(previewing) RenderPreview(); else if(mapping) RenderMap(true); else editor.Focus();
  FadeIn(previewing?(UIElement)previewScroll:mapping?(UIElement)mapView:editor);
  if(!noteReadable) SetStatus("读取失败，无法保存",Palette.Warn);
  if(missingNotes.Contains(name)) { SetStatus("文件已被移走或删除",Palette.Warn); ShowMissingCard(name); }
 }
 bool LoadTabText(string path,out string text,out NoteFileState state) {
  text=""; state=null;
  if(path==DraftKey) return true;
  noteStates.TryGetValue(path,out state);
  if(File.Exists(path)) {
   string error=ReadTextFile(path,out text,out var encoding,out var bom);
   if(error!=null) {
    if(state!=null) { state.Readable=false; text=state.LastSavedText; return true; }
    Msg("无法打开笔记："+error); return false;
   }
   var fi=new FileInfo(path);
   state=new NoteFileState { Path=path, Encoding=encoding, Bom=bom, LastWriteUtc=fi.LastWriteTimeUtc, Length=fi.Length, LastSavedText=text, NamedTitle=state!=null?state.NamedTitle:TitleOf(text) };
   noteStates[path]=state; missingNotes.Remove(path); return true;
  }
  missingNotes.Add(path);
  if(state==null) state=new NoteFileState { Path=path, LastSavedText="", NamedTitle=Path.GetFileNameWithoutExtension(path), Encoding=new UTF8Encoding(true), Bom=true };
  text=state.LastSavedText;
  return true;
 }
 void ShowBlankDraft() {
  if(!notePaths.Contains(DraftKey)) notePaths.Add(DraftKey);
  currentNote=DraftKey; currentTitle="未命名"; savedText=""; noteReadable=true; draftCreateWarned=false;
  loading=true; editor.Text=""; loading=false; ClearHistory(); RefreshTabs(); SaveTabOrder();
  if(previewing) RenderPreview(); else if(mapping) RenderMap(true); else if(expanded) editor.Focus();
  SetStatus("已自动保存",Palette.Accent);
 }
 void NewNote() {
  timer.Stop(); if(!SaveNote()) return; RenameCurrentAtOpportunity();
  if(notePaths.Contains(DraftKey)) { if(currentNote!=DraftKey) SwitchNote(DraftKey); else if(expanded) editor.Focus(); }
  else ShowBlankDraft();
  if(mapping) SetPreview(false); else if(previewing) EditAtLine(0,0,false);
 }
 // 写第一个字时把空白草稿落成“未命名.md”；失败时保留草稿，下次输入再试。
 bool EnsureDraftFile() {
  if(currentNote!=DraftKey) return true;
  try {
   string folder=SaveDir(); Directory.CreateDirectory(folder);
   string path=NewDraftPath(folder);
   var encoding=new UTF8Encoding(true);
   WriteTextFile(path,editor.Text,encoding,true);
   var fi=new FileInfo(path);
   var st=new NoteFileState { Path=path, Encoding=encoding, Bom=true, LastWriteUtc=fi.LastWriteTimeUtc, Length=fi.Length, LastSavedText=editor.Text, NamedTitle="未命名" };
   noteStates[path]=st; missingNotes.Remove(path);
   int i=notePaths.IndexOf(DraftKey); if(i>=0) notePaths[i]=path; else notePaths.Add(path);
   currentNote=path; noteReadable=true; savedText=editor.Text; draftCreateWarned=false;
   SaveTabOrder(); RefreshTabs();
   return true;
  } catch(Exception ex) {
   if(!draftCreateWarned) { draftCreateWarned=true; SetStatus("新建文件失败",Palette.Warn); Msg("无法新建笔记："+ex.Message); }
   return false;
  }
 }

 // ——— 打开文件 ———
 void OpenFiles() {
  string[] picked;
  modal++;
  try {
   using(var d=new Forms.OpenFileDialog { Title="打开文件", Multiselect=true, Filter="Markdown 和文本 (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|所有文件 (*.*)|*.*" }) {
    try { string s=SaveDir(); Directory.CreateDirectory(s); d.InitialDirectory=s; } catch {}
    if(d.ShowDialog(new Win32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle))!=Forms.DialogResult.OK) return;
    picked=d.FileNames;
   }
  } finally { modal--; }
  OpenPaths(picked);
 }
 void OpenPaths(string[] picked) {
  if(editingBlock>=0) FinishEdit(); timer.Stop(); if(!SaveNote()) return; RenameCurrentAtOpportunity();
  string last=null; var skipped=new List<string>(); bool blankDraft=currentNote==DraftKey && editor.Text.Trim().Length==0;
  foreach(string raw in picked??new string[0]) {
   string path; try { path=Path.GetFullPath(raw); } catch { continue; }
   if(IndexOfPath(path)>=0) { last=path; continue; }
   string text, error=ReadTextFile(path,out text,out var encoding,out var bom);
   if(error!=null) { skipped.Add(Path.GetFileName(path)+"："+error); continue; }
   var fi=new FileInfo(path);
   var st=new NoteFileState { Path=path, Encoding=encoding, Bom=bom, LastWriteUtc=fi.LastWriteTimeUtc, Length=fi.Length, LastSavedText=text, NamedTitle=TitleOf(text) };
   noteStates[path]=st; missingNotes.Remove(path);
   if(blankDraft && currentNote==DraftKey) {
    int i=notePaths.IndexOf(DraftKey); if(i>=0) notePaths[i]=path; else notePaths.Add(path);
    currentNote=path; noteReadable=true; savedText=text;
    loading=true; editor.Text=text; loading=false; ClearHistory(); blankDraft=false;
   } else notePaths.Add(path);
   last=path;
  }
  SaveTabOrder();
  if(last!=null) {
   if(last==currentNote) { RefreshTabs(); if(previewing) RenderPreview(); else if(mapping) RenderMap(true); else if(expanded) editor.Focus(); }
   else SwitchNote(last);
  }
  if(skipped.Count>0) Msg("以下文件有问题：\n"+string.Join("\n",skipped.ToArray()));
 }

 // ——— 保存 ———
 NoteFileState StateOf(string path) {
  NoteFileState st;
  if(!noteStates.TryGetValue(path,out st)) { st=new NoteFileState { Path=path, Encoding=new UTF8Encoding(true), Bom=true }; noteStates[path]=st; }
  return st;
 }
 void RefreshStamp(NoteFileState st,string text) {
  try { var fi=new FileInfo(st.Path); st.LastWriteUtc=fi.LastWriteTimeUtc; st.Length=fi.Length; } catch { st.LastWriteUtc=DateTime.MinValue; st.Length=-1; }
  st.LastSavedText=text??""; st.Readable=true;
 }
 bool SaveNote() { return SaveNote(false); }
 bool SaveNote(bool force) {
  if(!noteReadable) { SetStatus("读取失败，无法保存",Palette.Warn); Msg("当前笔记未能读取，为保护原文件无法保存。请先复制编辑内容。"); return false; }
  if(currentNote!=DraftKey) {
   NoteFileState cur;
   if(noteStates.TryGetValue(currentNote,out cur) && !cur.Readable) { SetStatus("读取失败，无法保存",Palette.Warn); Msg("当前笔记未能读取，为保护原文件无法保存。请先复制编辑内容。"); return false; }
  }
  if(currentNote==DraftKey) {
   if(editor.Text.Length==0) { SetStatus("已自动保存",Palette.Accent); return true; }
   if(!EnsureDraftFile()) return false;
  }
  var st=StateOf(currentNote);
  if(!force && !CheckExternalForCurrent()) return false;
  try {
   Encoding writeEncoding=st.Encoding; bool writeBom=st.Bom;
   EnsureWritableEncoding(editor.Text,ref writeEncoding,ref writeBom);
   WriteTextFile(currentNote,editor.Text,writeEncoding,writeBom);
   st.Encoding=writeEncoding; st.Bom=writeBom;
   RefreshStamp(st,editor.Text); savedText=editor.Text; missingNotes.Remove(currentNote); noteReadable=true;
   SetStatus("已自动保存",Palette.Accent); return true;
  } catch(Exception ex) {
   SetStatus("保存失败",Palette.Warn); Msg("笔记保存失败，请复制当前内容后检查磁盘：\n"+ex.Message); return false;
  }
 }
 // 面板展开、切换回来、保存之前都检查磁盘上的修改时间和大小。
 bool CheckExternalForCurrent() {
  if(loading || currentNote==DraftKey) return true;
  var st=StateOf(currentNote);
  if(!File.Exists(currentNote)) { MarkMissing(currentNote); ShowMissingCard(currentNote); return false; }
  FileInfo fi;
  try { fi=new FileInfo(currentNote); } catch { return true; }
  if(fi.LastWriteTimeUtc==st.LastWriteUtc && fi.Length==st.Length) { missingNotes.Remove(currentNote); return true; }
  string text, error=ReadTextFile(currentNote,out text,out var encoding,out var bom);
  if(error!=null) { SetStatus("读取失败",Palette.Warn); Msg("无法读取磁盘上的这篇笔记："+error); return false; }
  if(editor.Text==st.LastSavedText) {
   ReloadFromDisk(text,encoding,bom,true);
   SetStatus("已重新读取",Palette.Muted);
   return true;
  }
  ShowConflictCard();
  return false;
 }
 void ReloadFromDisk(string text,Encoding encoding,bool bom,bool silent) {
  if(editingBlock>=0) { FinishEdit(); RenderPreview(); }
  timer.Stop();
  int at=editor.CaretIndex, len=editor.SelectionLength;
  loading=true; editor.Text=text; loading=false;
  try { editor.CaretIndex=Math.Min(at,editor.Text.Length); if(len>0) editor.Select(Math.Min(at,editor.Text.Length),Math.Min(len,editor.Text.Length-Math.Min(at,editor.Text.Length))); } catch {}
  var st=StateOf(currentNote); st.Encoding=encoding; st.Bom=bom; RefreshStamp(st,text); savedText=text; missingNotes.Remove(currentNote); noteReadable=true; ClearHistory();
  RefreshTabs(); if(previewing) RenderPreview(); else if(mapping) RenderMap(true);
  if(!silent) SetStatus("已重新读取",Palette.Muted);
 }
 void MarkMissing(string path) {
  missingNotes.Add(path); if(path==currentNote) SetStatus("文件已被移走或删除",Palette.Warn); RefreshTabs();
 }
 // ——— 关闭 ———
 void CloseNote(string name) {
  CloseSettings(); if(editingBlock>=0) { FinishEdit(); if(previewing) RenderPreview(); }
  timer.Stop();
  if(name==currentNote) { if(!SaveNote()) return; name=RenameNoteAtOpportunity(name); }
  else name=RenameNoteAtOpportunity(name);
  string text;
  if(name==currentNote) text=editor.Text;
  else {
   NoteFileState st;
   text=noteStates.TryGetValue(name,out st)?st.LastSavedText:"";
   if(!File.Exists(name) && !noteStates.ContainsKey(name)) text="";
  }
  if(text.Trim().Length==0) { RemoveNote(name,"empty"); return; }
  if(!File.Exists(name)) { RemoveNote(name,"close"); return; }
  closeTarget=name;
  ShowConfirm("关闭「"+GetNoteTitle(name)+"」？","关闭后文件仍保留在原位置，之后可以用“打开”找回。","选“删除文件”会把它移到回收站，需要时还能找回。");
  AddConfirmButton("仅关闭","从标签里关掉，文件保留（Enter）",true,false,true,()=>ConfirmClose("close"));
  AddConfirmButton("删除文件","把文件移到回收站",false,true,true,()=>ConfirmClose("delete"));
  AddConfirmButton("取消","不关闭（Esc）",false,false,false,HideConfirm);
  confirmPrimaryBtn.Focus();
 }
 void ConfirmClose(string mode) { string name=closeTarget; HideConfirm(); if(name!=null) RemoveNote(name,mode); }
 // mode：close 只关标签 / delete 移到回收站 / empty 空内容（保存位置里的文件移到回收站，别处不动）。
 void RemoveNote(string name,string mode) {
  int idx=IndexOfPath(name);
  modal++;
  try {
   if(mode=="delete") { if(File.Exists(name)) Discard(name); }
   else if(mode=="empty") { string parent=name==DraftKey?null:Path.GetDirectoryName(name); if(name!=DraftKey && SamePath(parent,SaveDir()) && File.Exists(name)) Discard(name); }
  } catch(Exception ex) { modal--; Msg("关闭失败："+ex.Message); return; }
  modal--;
  bool wasCurrent=name==currentNote;
  if(idx>=0) notePaths.RemoveAt(idx);
  noteStates.Remove(name); missingNotes.Remove(name); folds.Remove(name);
  if(notePaths.Count==0) { SaveFolds(); ShowBlankDraft(); return; }
  string target=wasCurrent?notePaths[Math.Min(idx<0?0:idx,notePaths.Count-1)]:currentNote;
  SaveTabOrder(); SaveFolds();
  if(wasCurrent) {
   string text; NoteFileState st;
   if(LoadTabText(target,out text,out st)) {
    loading=true; currentNote=target; noteReadable=(target==DraftKey)||st==null||st.Readable; savedText=text; editor.Text=text; loading=false; ClearHistory();
   } else {
    loading=true; currentNote=target; noteReadable=false; savedText=""; editor.Text=""; loading=false; ClearHistory(); SetStatus("读取失败，无法保存",Palette.Warn);
   }
  }
  RefreshTabs(); if(previewing) RenderPreview(); else if(mapping) RenderMap(true); else if(expanded) editor.Focus();
  if(wasCurrent && missingNotes.Contains(currentNote)) { SetStatus("文件已被移走或删除",Palette.Warn); ShowMissingCard(currentNote); }
 }
 static void Discard(string path) { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); }

 // ——— 改名 ———
 string RenameNoteAtOpportunity(string path) {
  if(string.IsNullOrEmpty(path) || path==DraftKey) return path;
  NoteFileState st=StateOf(path);
  string text=path==currentNote?editor.Text:st.LastSavedText;
  string title=TitleOf(text);
  if(!ShouldRename(path,title,st.NamedTitle,SaveDir())) return path;
  if(!File.Exists(path)) return path;
  string extension=Path.GetExtension(path); if(extension.Length==0) extension=".md";
  string target=UniqueFile(SaveDir(),NoteFileName(title,extension),path);
  if(SamePath(target,path)) { st.NamedTitle=title; return path; }
  try { File.Move(path,target); } catch { return path; }
  noteStates.Remove(path); st.Path=target; st.NamedTitle=title; noteStates[target]=st;
  missingNotes.Remove(path);
  int i=IndexOfPath(path); if(i>=0) notePaths[i]=target;
  HashSet<string> set;
  if(folds.TryGetValue(path,out set)) { folds.Remove(path); folds[target]=set; SaveFolds(); }
  if(currentNote==path) currentNote=target;
  SaveTabOrder(); RefreshTabs();
  return target;
 }
 void RenameCurrentAtOpportunity() { if(currentNote!=DraftKey) RenameNoteAtOpportunity(currentNote); }
 void RenameAllAtOpportunity() { foreach(string path in new List<string>(notePaths)) RenameNoteAtOpportunity(path); }

 // ——— 外部修改和文件丢失的卡片 ———
 void ShowConflictCard() {
  if(confirmOpen) return;
  ShowConfirm("「"+GetNoteTitle(currentNote)+"」在别处被修改了",null,null);
  AddConfirmButton("保留我的版本",null,true,false,true,KeepMyVersion);
  AddConfirmButton("用文件里的版本","用磁盘上现在的内容替换编辑区",false,false,true,UseFileVersion);
  confirmPrimaryBtn.Focus();
 }
 void UseFileVersion() {
  HideConfirm();
  string text, error=ReadTextFile(currentNote,out text,out var encoding,out var bom);
  if(error!=null) { Msg("无法读取磁盘上的这篇笔记："+error); return; }
  ReloadFromDisk(text,encoding,bom,false);
 }
 void KeepMyVersion() {
  HideConfirm();
  if(!SaveNote(true)) return;
  SetStatus("已自动保存",Palette.Accent);
 }
 void ShowMissingCard(string path) {
  if(confirmOpen || path!=currentNote) return;
  ShowConfirm("「"+GetNoteTitle(path)+"」的文件已被移走或删除",null,null);
  AddConfirmButton("重新保存到原位置",null,true,false,true,()=>ResaveMissing(path));
  AddConfirmButton("关闭标签",null,false,false,true,()=> { HideConfirm(); RemoveNote(path,"close"); });
  confirmPrimaryBtn.Focus();
 }
 void ResaveMissing(string path) {
  HideConfirm();
  try {
   Directory.CreateDirectory(Path.GetDirectoryName(path));
   if(!SaveNote(true)) return;
   missingNotes.Remove(path); RefreshTabs();
  } catch(Exception ex) { Msg("重新保存失败："+ex.Message); }
 }

 // ——— 卡片外壳 ———
 Border BuildConfirmCard() {
  var view=new Border { Background=Palette.Scrim, Visibility=Visibility.Collapsed };
  view.MouseLeftButtonDown+=(s,e)=> { if(e.OriginalSource==view) HideConfirm(); };
  var card=new Border { Background=Palette.Surface, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(12), Padding=new Thickness(20,18,20,16), Margin=new Thickness(10,0,10,40), VerticalAlignment=VerticalAlignment.Center };
  view.Child=card; var list=new StackPanel(); card.Child=list;
  confirmTitle=new TextBlock { Foreground=Palette.Text, FontSize=15.5, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap, TextTrimming=TextTrimming.CharacterEllipsis }; list.Children.Add(confirmTitle);
  confirmBody=new TextBlock { Foreground=Palette.Muted, FontSize=12.5, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,0), LineHeight=20, Visibility=Visibility.Collapsed }; list.Children.Add(confirmBody);
  confirmHint=new TextBlock { Foreground=Palette.Muted, Opacity=0.75, FontSize=11.5, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,4,0,0), Visibility=Visibility.Collapsed }; list.Children.Add(confirmHint);
  confirmButtons=new DockPanel { LastChildFill=false, Margin=new Thickness(0,18,0,0) }; list.Children.Add(confirmButtons);
  return view;
 }
 void ShowConfirm(string title,string body,string hint) {
  confirmTitle.Text=title; confirmBody.Text=body??""; confirmHint.Text=hint??"";
  confirmBody.Visibility=string.IsNullOrEmpty(body)?Visibility.Collapsed:Visibility.Visible;
  confirmHint.Visibility=string.IsNullOrEmpty(hint)?Visibility.Collapsed:Visibility.Visible;
  confirmButtons.Children.Clear(); confirmView.Visibility=Visibility.Visible; confirmOpen=true;
 }
 void AddConfirmButton(string text,string tip,bool primary,bool warn,bool dockRight,Action action) {
  var b=Ui.Button(text,tip); b.Padding=new Thickness(16,0,16,0); b.Margin=new Thickness(0,0,8,0);
  if(warn) { b.Background=Palette.Raised; b.Foreground=Palette.Warn; }
  else if(primary) { b.Background=Palette.AccentBg; b.Foreground=Palette.Accent; b.FontWeight=FontWeights.SemiBold; }
  else { b.Background=Palette.Raised; b.Foreground=Palette.Text; }
  b.Click+=(s,e)=>action();
  if(dockRight) DockPanel.SetDock(b,Dock.Right); else DockPanel.SetDock(b,Dock.Left);
  confirmButtons.Children.Add(b);
  if(primary) confirmPrimaryBtn=b;
 }
 void HideConfirm() {
  if(!confirmOpen) return;
  confirmOpen=false; closeTarget=null; confirmView.Visibility=Visibility.Collapsed;
  if(expanded && Writing) editor.Focus();
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
  shelf.Children.Clear(); shelfTray=null; shelfHint=null; ShowDropZone(); UpdatePresenting();
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
