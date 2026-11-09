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
   foreach(var pair in ParseFoldLines(File.ReadAllLines(f),dir)) FoldsOf(pair.Key,true).Add(pair.Value);
  } catch {}
 }
 void SaveFolds() {
  try {
   var list=new List<KeyValuePair<string,string>>(); foreach(var pair in folds) foreach(var k in pair.Value) list.Add(new KeyValuePair<string,string>(pair.Key,k));
   SaveFoldFile(dir,list);
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
   var code=new TextBlock { Text=string.Join("\n",inner), FontFamily=Ui.Mono, FontSize=Scaled(13), LineHeight=Scaled(20), Foreground=Palette.Text, TextWrapping=TextWrapping.Wrap };
   var box=new Border { Background=Palette.Raised, CornerRadius=new CornerRadius(6), Padding=new Thickness(12,8,12,8), Child=code, MinHeight=20 };
   return Host(box,new Thickness(0,2,0,6),count>1?start+1:start,null);
  }
  string line=lines[start]; Match m; var tb=Body();
  int prev=bi-1; while(prev>=0 && blockHidden[prev]) prev--; // 跟在折起的段落后面时，按看得见的上一块算间距
  bool afterText=prev>=0 && blocks[prev][2]!=K_Blank;
  if((m=HeadingRe.Match(line)).Success) {
   // 六级标题各有字号；五、六级用次要文字色，和正文拉开但不抢一到四级。
   int lv=m.Groups[1].Length; tb.FontWeight=FontWeights.SemiBold; tb.FontSize=Scaled(HeadSize[lv-1]); tb.LineHeight=Scaled(HeadLine[lv-1]); if(lv>=5) tb.Foreground=Palette.Muted;
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
   var mark=new Border { Background=Brushes.Transparent, Cursor=Cursors.Hand, Child=CheckMark(done), ToolTip=done?"标记为未完成":"标记为完成", VerticalAlignment=VerticalAlignment.Top, Padding=new Thickness(0,(BodyLine-15)/2,6,4) };
   int ln=start; mark.MouseLeftButtonDown+=(s,e)=> { ToggleTask(ln); e.Handled=true; };
   return Host(ListRow(mark,tb),new Thickness(0,1,0,1),start,tb);
  }
  if((m=Regex.Match(line,@"^\s*[-*+]\s+(.*)$")).Success) {
   AddInline(tb.Inlines,m.Groups[1].Value,m.Groups[1].Index);
   return Host(ListRow(new System.Windows.Shapes.Ellipse { Width=5, Height=5, Fill=Palette.Muted, Margin=new Thickness(6,(BodyLine-5)/2,0,0), HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top },tb),new Thickness(0,1,0,1),start,tb);
  }
  if((m=Regex.Match(line,@"^\s*(\d+)[.)]\s+(.*)$")).Success) {
   AddInline(tb.Inlines,m.Groups[2].Value,m.Groups[2].Index);
   return Host(ListRow(new TextBlock { Text=m.Groups[1].Value+".", Foreground=Palette.Muted, FontSize=Scaled(13.5), LineHeight=BodyLine, VerticalAlignment=VerticalAlignment.Top },tb),new Thickness(0,1,0,1),start,tb);
  }
  if(Regex.IsMatch(line,@"^\s*([-*_])(\s*\1){2,}\s*$")) return Host(new Border { Height=1, Background=Palette.Line },new Thickness(0,11,0,11),start,null);
  AddInline(tb.Inlines,line,0);
  return Host(tb,new Thickness(0,1,0,3),start,tb);
 }
 Grid ListRow(FrameworkElement marker,TextBlock text) {
  var g=new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(Scaled(24)) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
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
   else if(x.StartsWith("`")) { r=new Run(" "+x.Substring(1,x.Length-2)+" ") { Tag=baseOffset+m.Index }; r.FontFamily=Ui.Mono; r.FontSize=Scaled(13); r.Background=Palette.Raised; }
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
  var box=new TextBox { Text=string.Join("\n",lines.GetRange(start,count)), Background=Brushes.Transparent, Foreground=Palette.Text, CaretBrush=Palette.Text, BorderThickness=new Thickness(0), Padding=new Thickness(0), AcceptsReturn=true, AcceptsTab=true, TextWrapping=TextWrapping.Wrap, FontSize=kind==K_Code?Scaled(13):BodySize, FontFamily=kind==K_Code?Ui.Mono:FontFamily, SelectionBrush=Palette.Accent, SelectionOpacity=0.35, FocusVisualStyle=null, VerticalContentAlignment=VerticalAlignment.Center, MinHeight=BodyLine };
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

}
}
