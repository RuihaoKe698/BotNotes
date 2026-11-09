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
// 脑图里的一个节点。Key 是从中心主题到它的路径，重画后靠它找回同一个节点。
class MapNode {
 public string Text, Full, Key; public int Depth, Branch, Block=-1, Line=-1, Last=-1, Kind; public bool Heading, Folded; // Kind：1 标题、2 列表、3 文字、4 代码
 public double W, H, X, Y, Sub; public FrameworkElement El; public MapNode Parent;
 public readonly List<MapNode> Kids=new List<MapNode>();
}

class MapFold { public MapNode N; } // 折叠开关和“+N”徽标

partial class MainWindow {
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
  if(n.Depth==0) { tb.FontSize=Scaled(18); tb.FontWeight=FontWeights.SemiBold; tb.MaxWidth=260; tb.Foreground=Palette.Panel; el.Background=Palette.Accent; el.CornerRadius=new CornerRadius(12); el.Padding=new Thickness(18,10,18,11); }
  else if(n.Depth==1) { tb.FontSize=Scaled(15); tb.FontWeight=FontWeights.SemiBold; tb.MaxWidth=200; el.Background=new SolidColorBrush(Palette.Mix(surface,col,0.2)); el.BorderBrush=new SolidColorBrush(col); el.BorderThickness=new Thickness(1.5); el.CornerRadius=new CornerRadius(9); el.Padding=new Thickness(13,7,13,8); }
  else if(n.Heading) { // 深色背景上淡底不够分辨，加深一点并加一道淡的分支色描边
   tb.FontSize=Scaled(14); tb.FontWeight=FontWeights.SemiBold; tb.MaxWidth=200; el.Background=new SolidColorBrush(Palette.Mix(surface,col,light?0.1:0.16)); el.CornerRadius=new CornerRadius(7); el.Padding=new Thickness(10,5,10,6);
   if(!light) { el.BorderBrush=new SolidColorBrush(Color.FromArgb(0x8C,col.R,col.G,col.B)); el.BorderThickness=new Thickness(1); }
  }
  else { tb.FontSize=Scaled(13.5); tb.MaxWidth=task>=0?175:190; el.Background=Brushes.Transparent; el.Padding=new Thickness(6,3,6,4); }
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
    double badgeH=Scaled(22);
    var badge=new Border { Tag=new MapFold { N=n }, Cursor=Cursors.Hand, Height=badgeH, MinWidth=Scaled(26), CornerRadius=new CornerRadius(badgeH/2), Padding=new Thickness(Scaled(8),0,Scaled(8),0), Background=new SolidColorBrush(Palette.Mix(Palette.Surface.Color,bc,0.14)), BorderBrush=new SolidColorBrush(bc), BorderThickness=new Thickness(1.5), ToolTip="已折叠，点击展开",
     Child=new TextBlock { Text="+"+Descendants(n), FontSize=Scaled(13), FontWeight=FontWeights.SemiBold, FontFamily=FontFamily, Foreground=new SolidColorBrush(bc), VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Center } };
    Canvas.SetLeft(badge,n.X+n.W+8); Canvas.SetTop(badge,n.Y+n.H/2-badgeH/2); mapCanvas.Children.Add(badge);
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
  var box=Ui.Input(body); box.FontSize=Scaled(n.Depth==0?18:n.Depth==1?15:n.Heading?14:13.5); box.FontWeight=n.Heading || n.Depth<=1?FontWeights.SemiBold:FontWeights.Normal;
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
}
}
