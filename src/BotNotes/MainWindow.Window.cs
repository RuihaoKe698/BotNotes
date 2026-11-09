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
   MarkDragMoved();
   if(dragDot && springOn) springGoal=DotTarget(new Point(dragOrigin.X+dx,dragOrigin.Y+dy)); // 只更新目标，圆点由弹簧带过去
   else if(dragDot) MoveDotTo(new Point(dragOrigin.X+dx,dragOrigin.Y+dy)); else MovePanelTo(new Point(dragOrigin.X+dx,dragOrigin.Y+dy));
  };
  handle.PreviewMouseLeftButtonUp+=(s,e)=> {
   if(!dragArmed) return; dragArmed=false;
   if(dragMoved) { e.Handled=true; if(dragDot && !expanded) EndDragFace(); handle.ReleaseMouseCapture(); if(!springOn) { SaveSettings(); if(dragDot && !expanded) Breathe(true); } return; } // 按钮失去捕获后不会再触发点击；弹簧停稳后再存位置
   if(!preview) { handle.ReleaseMouseCapture(); e.Handled=true; if(click!=null) click(); }
  };
  handle.LostMouseCapture+=(s,e)=> { if(dragArmed && dragMoved) { dragArmed=false; if(dragDot && !expanded) EndDragFace(); if(!springOn) SaveSettings(); } else if(dragArmed && dragDot) dragArmed=false; };
 }
 // 真正开始拖动的那一刻（移动超过 4px）：鼠标消息和每帧弹簧谁先发现都走这里，拖小机器人时换成 >< 表情。
 void MarkDragMoved() { if(dragMoved) return; dragMoved=true; transient=false; if(dragDot && !expanded) BeginDragFace(); }
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
 // 弹簧刚度 420、阻尼 28（阻尼比约 0.68，过冲约 5%）：拖动时明显落在鼠标后面（每秒 1000px 的速度约落后 65px），停手后追上来、轻轻冲过头再弹回，惯性感更强；每帧按 4ms 的小步积分，帧率不稳也不会抖。
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
  const double k=420, c=28; int n=(int)Math.Ceiling(dt/0.004); double h=dt/n;
  for(int i=0;i<n;i++) { var acc=k*(springGoal-springPos)-c*springVel; springVel+=acc*h; springPos+=springVel*h; }
  bool held=dragArmed && dragDot;
  // 目标每帧直接读光标位置：每帧回调的优先级高于鼠标输入，鼠标移动消息会被攒着、每 80–100ms 才处理一批，
  // 只靠它更新目标的话圆点是一顿一顿往前跳的（这就是拖动卡顿的原因）。
  if(held) {
   var cp=CursorDip(); double dx=cp.X-dragFrom.X, dy=cp.Y-dragFrom.Y;
   if(!dragMoved && (Math.Abs(dx)>=4 || Math.Abs(dy)>=4)) MarkDragMoved();
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
  mapHint.Visibility=p || mapRoot==null?Visibility.Collapsed:Visibility.Visible;
  editCard.BorderThickness=new Thickness(0); editCard.CornerRadius=new CornerRadius(0); editCard.Background=p?Palette.Panel:Brushes.Transparent;
  outerBox.Padding=p?new Thickness(0):new Thickness(16,0,16,16);
  presentFit.Visibility=p && mapRoot!=null?Visibility.Visible:Visibility.Collapsed; presentProgress.Visibility=p && mapRoot!=null?Visibility.Visible:Visibility.Collapsed;
  presentBtn.Content=Ui.IconLabel(p?LineIcon.ExitPresent:LineIcon.Present,p?"退出演示":"全屏演示",14);
  presentBtn.ToolTip=p?"退出演示（Esc 或 F5）":"全屏演示：一步步展示脑图（F5）";
  presentBar.Margin=p?new Thickness(0,8,16,0):new Thickness(0,8,8,0);
  presentBar.Opacity=p && !presentBar.IsMouseOver?0.45:1;
 }
 void ShowFullscreenState() {
  fsButton.Content=LineIcon.Make(fullscreen?LineIcon.ExitFullscreen:LineIcon.Fullscreen);
  fsButton.ToolTip=fullscreen?"退出全屏（Esc 或 F11）":"全屏：铺满整个屏幕（F11）";
  fsButton.Background=fullscreen?Palette.AccentBg:Brushes.Transparent; fsButton.Foreground=fullscreen?Palette.Accent:Palette.Muted;
 }

 // 演示树独立于用户的折叠记录；步进只改变可见性，避免已有节点移动。
 // ——— 展开与收起 ———
 void Expand() { Expand(false); }
 // 面板露出的范围：贴顶时上沿多留 18 让顶部两角保持直角，底部两角与面板同为 18 的圆角；浮起时四角都是圆角。
 Rect FullClip() { return Docked?new Rect(0,-18,pw,ph+18):new Rect(0,0,pw,ph); }
 Rect TuckClip() { double c=Docked?DotTop:dotY, w=dotSize*0.3; return new Rect(dotX-w/2,c-w/2,w,w); } // 收进圆点中心
 Rect NoClip() { return new Rect(dotX,Docked?DotTop:dotY,0,0); } // 收起后什么都不露
 // 展开：刘海不动，面板连同内容从刘海下面展开（320ms，先快后慢）；内容在 90ms 后淡入下滑，阴影在快展满时淡入。
 void Expand(bool transientOpen) {
  if(expanded) { if(!transientOpen) transient=false; Activate(); return; }
  StopWriting(); if(writeDelay!=null) writeDelay.Stop();
  CheckExternalForCurrent(); expandedText=editor.Text;
  bool wasVisible=body.Visibility==Visibility.Visible; double fromOpacity=wasVisible?body.Opacity:0;
  if(!wasVisible) PlaceWindow(); // 收起动画还没走完时不重摆，免得打断它
  Rect from=wasVisible?panelClip.Rect:TuckClip();
  expanded=true; transient=transientOpen && !pinned; leaveTimer.Stop(); hoverTimer.Stop(); Breathe(false); if(settingsOpen) PreviewBreathe();
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
  CloseHistory();
  if(fullscreen) SetFullscreen(false); // 收起前先回到原来的大小，小圆点才在原位置
  HideConfirm();
  if(editingBlock>=0) { FinishEdit(); RenderPreview(); }
  if(!loading) {timer.Stop(); if(!SaveNote()) return; RenameCurrentAtOpportunity(); SnapshotCurrentNote();}
  bool changed=expandedText!=null && editor.Text!=expandedText; expandedText=null;
  expanded=false; transient=false; leaveTimer.Stop(); hoverTimer.Stop(); if(settingsOpen) PreviewBreathe();
  int gen=++animGen;
  Action finish=()=> { if(gen!=animGen) return; Breathe(true); dotLayer.IsHitTestVisible=true; panelClip.BeginAnimation(RectangleGeometry.RectProperty,null); panelClip.Rect=NoClip(); ResizeShadows(TuckClip(),TimeSpan.Zero,null); body.Visibility=Visibility.Collapsed; ApplyChrome(false); if(changed) DelayWrite(); };
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
 // 缓慢的悬浮和呼吸用 16 帧控制待机开销（幅度只有一两个像素，看不出差别）；眨眼、环顾、表情和写字 60 帧；目光跟随每帧更新，最高 60 帧。
 static double BotFloat(double d) { return Math.Max(1,0.06*d); }
 DispatcherTimer botTimer; readonly Random botRandom=new Random(); DateTime botEpoch; double nextBlink, nextLook, nextWrite, nextReturn=double.PositiveInfinity; bool mainBotOn, previewBotOn, botHovering; double idleX,idleY; bool gazeTracking, gazeFrameOn, writing; TimeSpan gazeFrameAt; DispatcherTimer gazeTimer; DateTime gazeLast, gazeSeen, gazeResume; double gazeX,gazeY; Func<Point> gazeReader=null; string expandedText; DispatcherTimer writeDelay,writeLift;
 DoubleAnimationUsingKeyFrames BreathAnimation(double low,double high) {
  double seconds=5, bottom=Math.Exp(-1), span=Math.Exp(1)-bottom;
  var a=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(seconds), RepeatBehavior=RepeatBehavior.Forever };
  for(int i=0;i<=48;i++) { double t=(double)i/48, value=low+(high-low)*(Math.Exp(Math.Sin(2*Math.PI*t+Math.PI/2))-bottom)/span; a.KeyFrames.Add(new LinearDoubleKeyFrame(value,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds*t)))); }
  Timeline.SetDesiredFrameRate(a,16); return a;
 }
 DoubleAnimationUsingKeyFrames FloatAnimation(double d) {
  var a=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(3.6), RepeatBehavior=RepeatBehavior.Forever }; double amp=BotFloat(d);
  for(int i=0;i<=48;i++) { double t=i/48.0; a.KeyFrames.Add(new LinearDoubleKeyFrame(-amp*Math.Sin(t*2*Math.PI),KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.6*t)))); } Timeline.SetDesiredFrameRate(a,16); return a;
 }
 void AnimateBot(BotVisual b,bool on) {
  if(b==null) return; b.Reset(); if(!on || !SystemParameters.ClientAreaAnimation) return;
  b.floating.BeginAnimation(TranslateTransform.YProperty,FloatAnimation(b.Diameter));
  { b.ballScale.BeginAnimation(ScaleTransform.ScaleXProperty,BreathAnimation(1,1.035)); b.ballScale.BeginAnimation(ScaleTransform.ScaleYProperty,BreathAnimation(1,1.035)); b.glow.BeginAnimation(UIElement.OpacityProperty,BreathAnimation(0.35,0.6)); }
 }
 void Breathe(bool on) { if(!on) StopWriting(); mainBotOn=on && !springOn && !dragArmed && !expanded && SystemParameters.ClientAreaAnimation; botHovering=false; AnimateBot(bot,mainBotOn); SyncBotTimer(); SetGazePolling(mainBotOn); }
 void PreviewBreathe() { if((!settingsOpen || !expanded) && dotPreview!=null) dotPreview.StopWrite(); previewBotOn=settingsOpen && expanded && SystemParameters.ClientAreaAnimation; AnimateBot(dotPreview,previewBotOn); SyncBotTimer(); }
 void SyncBotTimer() {
  bool active=mainBotOn || previewBotOn;
  if(botTimer==null) { botTimer=new DispatcherTimer(); botTimer.Tick+=(sender,args)=> { botTimer.Stop(); BotTick((DateTime.UtcNow-botEpoch).TotalSeconds); ScheduleBotTimer(); }; }
  if(!active) { botTimer.Stop(); return; }
  if(!botTimer.IsEnabled) { botEpoch=DateTime.UtcNow; idleX=idleY=0; nextBlink=1+botRandom.NextDouble()*4; nextLook=2+botRandom.NextDouble()*2; nextWrite=45+botRandom.NextDouble()*45; nextReturn=double.PositiveInfinity; ScheduleBotTimer(); }
 }
 void ScheduleBotTimer() { if(!mainBotOn && !previewBotOn) return; double now=(DateTime.UtcNow-botEpoch).TotalSeconds, target=Math.Min(nextWrite,Math.Min(nextBlink,Math.Min(nextLook,nextReturn))); botTimer.Interval=TimeSpan.FromSeconds(Math.Max(0.01,target-now)); botTimer.Start(); }
 void BotTick(double now) {
  while(Math.Min(nextWrite,Math.Min(nextBlink,Math.Min(nextLook,nextReturn)))<=now) {
   if(nextWrite<=nextBlink && nextWrite<=nextLook && nextWrite<=nextReturn) { double when=nextWrite; nextWrite=when+45+botRandom.NextDouble()*45; if(mainBotOn) StartWriting(); if(previewBotOn) StartPreviewWriting(); }
   else if(nextBlink<=nextLook && nextBlink<=nextReturn) { double when=nextBlink; bool twice=botRandom.NextDouble()<0.2; if(mainBotOn && !writing) BlinkBot(bot,twice,botHovering); if(previewBotOn) BlinkBot(dotPreview,twice,false); nextBlink=when+1+botRandom.NextDouble()*4; }
   else if(nextLook<=nextReturn && mainBotOn && (gazeTracking || writing || DateTime.UtcNow<gazeResume) && !previewBotOn) { nextLook+=1; nextReturn=double.PositiveInfinity; }
   else if(nextLook<=nextReturn) { double when=nextLook; idleX=(botRandom.NextDouble()*2-1)*0.08; idleY=(botRandom.NextDouble()*2-1)*0.08; ConstrainGaze(ref idleX,ref idleY); if(mainBotOn && !botHovering && !gazeTracking && !writing) LookBot(bot,idleX,idleY,250); if(previewBotOn) LookBot(dotPreview,idleX,idleY,250); nextReturn=botRandom.NextDouble()<0.5?when+0.6+botRandom.NextDouble()*0.9:double.PositiveInfinity; nextLook=when+2+botRandom.NextDouble()*2; }
   else { idleX=idleY=0; if(mainBotOn && !botHovering && !gazeTracking && !writing) LookBot(bot,0,0,250); if(previewBotOn) LookBot(dotPreview,0,0,250); nextReturn=double.PositiveInfinity; }
  }
 }
 static void ConstrainGaze(ref double x,ref double y) { // 默认视线保留对标位置，同时保证眼睛中心和外缘都留在球内。
  double k=1; for(int i=0;i<24;i++) { double lx=-0.21+x*k, rx=0.08+x*k, ey=-0.245+y*k, left=Math.Abs(lx)+0.0625, right=Math.Abs(rx)+0.0625, top=Math.Abs(ey)+0.125; if(lx*lx+ey*ey<=0.42*0.42 && rx*rx+ey*ey<=0.42*0.42 && left*left+top*top<=0.25 && right*right+top*top<=0.25) break; k*=0.8; } x*=k; y*=k;
 }
 static void LookBot(BotVisual b,double x,double y,int ms) { if(b==null) return; var ease=new CubicEase { EasingMode=EasingMode.EaseOut }; double xx=x*b.Diameter, yy=y*b.Diameter; var ax=new DoubleAnimation(b.gaze.X,xx,TimeSpan.FromMilliseconds(ms)) { EasingFunction=ease, FillBehavior=FillBehavior.Stop }; var ay=new DoubleAnimation(b.gaze.Y,yy,TimeSpan.FromMilliseconds(ms)) { EasingFunction=ease, FillBehavior=FillBehavior.Stop }; Timeline.SetDesiredFrameRate(ax,60); Timeline.SetDesiredFrameRate(ay,60); b.gaze.X=xx; b.gaze.Y=yy; b.gaze.BeginAnimation(TranslateTransform.XProperty,ax); b.gaze.BeginAnimation(TranslateTransform.YProperty,ay); }
 static void BlinkBot(BotVisual b,bool twice,bool hovering) {
  if(b==null) return; double open=hovering?1.12:1; var a=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromMilliseconds(twice?400:140), FillBehavior=FillBehavior.Stop };
  a.KeyFrames.Add(new LinearDoubleKeyFrame(open,KeyTime.FromTimeSpan(TimeSpan.Zero))); a.KeyFrames.Add(new LinearDoubleKeyFrame(0.1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70)))); a.KeyFrames.Add(new LinearDoubleKeyFrame(open,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
  if(twice) { a.KeyFrames.Add(new LinearDoubleKeyFrame(open,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)))); a.KeyFrames.Add(new LinearDoubleKeyFrame(0.1,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)))); a.KeyFrames.Add(new LinearDoubleKeyFrame(open,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400)))); }
  Timeline.SetDesiredFrameRate(a,60); b.leftScale.ScaleY=b.rightScale.ScaleY=open; b.leftScale.BeginAnimation(ScaleTransform.ScaleYProperty,a); b.rightScale.BeginAnimation(ScaleTransform.ScaleYProperty,a);
 }
 void BotHover(bool on,double x) { if(!mainBotOn || !SystemParameters.ClientAreaAnimation) return; botHovering=on; if(on && writing) StopWriting(); double scale=on?1.12:1; foreach(var eye in new[]{bot.leftScale,bot.rightScale}) { var a=new DoubleAnimation(eye.ScaleY,scale,TimeSpan.FromMilliseconds(150)) { FillBehavior=FillBehavior.Stop }; Timeline.SetDesiredFrameRate(a,60); eye.ScaleY=scale; eye.BeginAnimation(ScaleTransform.ScaleYProperty,a); } }
 // 鼠标在透明穿透区时也能看见位置；屏幕坐标先通过窗口的 DPI 矩阵换算为 DIP。
 void SetGazePolling(bool on) { if(gazeTimer==null) { gazeTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) }; gazeTimer.Tick+=(sender,args)=>PollGaze(); } if(on && !expanded) { gazeLast=DateTime.UtcNow; gazeTimer.Interval=TimeSpan.FromMilliseconds(100); gazeTimer.Start(); } else { gazeTimer.Stop(); StopGazeFrames(); gazeTracking=false; gazeX=gazeY=0; } }
 void StopGazeFrames() { if(gazeFrameOn) { CompositionTarget.Rendering-=GazeFrame; gazeFrameOn=false; } }
 void GazeFrame(object sender,EventArgs args) { var frame=args as RenderingEventArgs; if(frame==null) return; if(gazeFrameAt==TimeSpan.Zero) gazeFrameAt=frame.RenderingTime; if(frame.RenderingTime<gazeFrameAt) return; gazeFrameAt+=TimeSpan.FromTicks(166667); if(gazeFrameAt<frame.RenderingTime) gazeFrameAt=frame.RenderingTime+TimeSpan.FromTicks(166667); PollGaze(); }
 static Point SmoothGaze(Point current,Point target,double seconds) { double weight=1-Math.Exp(-seconds/0.052); return new Point(current.X+(target.X-current.X)*weight,current.Y+(target.Y-current.Y)*weight); }
 static Point GazeTarget(Point cursor,Point center,double d) { double x=cursor.X-center.X,y=cursor.Y-center.Y, dist=Math.Sqrt(x*x+y*y); if(dist<0.3*d) return new Point(); double m=Math.Min(1,(dist-0.3*d)/(1.7*d)); double dx=x/dist*0.11*m, dy=y/dist*0.09*m; ConstrainGaze(ref dx,ref dy); return new Point(dx,dy); }
 void PollGaze() { if(!mainBotOn || expanded || dragArmed || writing || DateTime.UtcNow<gazeResume) return; var now=DateTime.UtcNow; var cursor=gazeReader==null?CursorDip():gazeReader(); var center=new Point(panelLeft+dotX,panelTop+dotY); double dx=cursor.X-center.X,dy=cursor.Y-center.Y; bool near=dx*dx+dy*dy<=Math.Pow(Math.Max(160,6*dotSize),2); double dt=Math.Max(0.001,Math.Min(0.15,(now-gazeLast).TotalSeconds)); gazeLast=now;
  if(near) { if(!gazeTracking) { gazeTracking=true; gazeTimer.Stop(); if(!gazeFrameOn) { gazeFrameAt=TimeSpan.Zero; gazeFrameOn=true; CompositionTarget.Rendering+=GazeFrame; } gazeX=bot.gaze.X/dotSize; gazeY=bot.gaze.Y/dotSize; bot.gaze.BeginAnimation(TranslateTransform.XProperty,null); bot.gaze.BeginAnimation(TranslateTransform.YProperty,null); bot.gaze.X=gazeX*dotSize; bot.gaze.Y=gazeY*dotSize; } gazeSeen=now; var target=GazeTarget(cursor,center,dotSize); var smoothed=SmoothGaze(new Point(gazeX,gazeY),target,dt); gazeX=smoothed.X; gazeY=smoothed.Y; bot.gaze.X=gazeX*dotSize; bot.gaze.Y=gazeY*dotSize; }
  else if(gazeTracking && (now-gazeSeen).TotalSeconds>=0.5) { gazeTracking=false; gazeX=gazeY=0; LookBot(bot,0,0,400); gazeResume=now.AddMilliseconds(400); StopGazeFrames(); gazeTimer.Interval=TimeSpan.FromMilliseconds(100); gazeTimer.Start(); }
 }
 void BeginDragFace() { if(writing) StopWriting(); SetGazePolling(false); bot.SetDragFace(true,SystemParameters.ClientAreaAnimation,null); }
 void EndDragFace() { gazeResume=DateTime.UtcNow.AddMilliseconds(300); bot.SetDragFace(false,SystemParameters.ClientAreaAnimation,()=> { if(!expanded && SystemParameters.ClientAreaAnimation) BlinkBot(bot,false,false); }); }
 void DelayWrite() { if(writeDelay==null) { writeDelay=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(400) }; writeDelay.Tick+=(sender,args)=> { writeDelay.Stop(); StartWriting(); }; } writeDelay.Stop(); writeDelay.Start(); }
 bool StartWriting() {
  if(expanded || dragArmed || springOn || botHovering || dotHit.IsMouseOver || !mainBotOn || !SystemParameters.ClientAreaAnimation || writing) return false;
  writing=true; gazeTracking=false; StopGazeFrames(); if(gazeTimer!=null) { gazeTimer.Interval=TimeSpan.FromMilliseconds(100); gazeTimer.Start(); } double x=-0.08,y=0.08; ConstrainGaze(ref x,ref y); LookBot(bot,x,y,220);
  bot.StartWrite(()=> { writing=false; gazeResume=DateTime.UtcNow.AddMilliseconds(300); });
  ArmWriteLift(); return true;
 }
 void StartPreviewWriting() { if(!previewBotOn || dotPreview==null || dotPreview.writeClocks.Count>0) return; LookBot(dotPreview,-0.08,0.08,220); dotPreview.StartWrite(null); ArmWriteLift(); }
 void ArmWriteLift() { if(writeLift==null) { writeLift=new DispatcherTimer { Interval=TimeSpan.FromSeconds(3.3) }; writeLift.Tick+=(sender,args)=> { writeLift.Stop(); if(writing) { LookBot(bot,0,0,300); BlinkBot(bot,false,false); } if(previewBotOn && dotPreview.writeClocks.Count>0) { LookBot(dotPreview,0,0,300); BlinkBot(dotPreview,false,false); } }; } writeLift.Stop(); writeLift.Start(); }

 void StopWriting() { if(writeDelay!=null) writeDelay.Stop(); if(writeLift!=null) writeLift.Stop(); if(bot!=null) { bot.StopWrite(); ResetWriteEyes(bot); } if(dotPreview!=null) { bool previewWriting=dotPreview.writeClocks.Count>0; dotPreview.StopWrite(); if(previewWriting) { ResetWriteEyes(dotPreview); ResetWriteGaze(dotPreview); } } if(writing && bot!=null) ResetWriteGaze(bot); writing=false; }
 static void ResetWriteEyes(BotVisual b) { b.leftScale.BeginAnimation(ScaleTransform.ScaleYProperty,null); b.rightScale.BeginAnimation(ScaleTransform.ScaleYProperty,null); b.leftScale.ScaleY=b.rightScale.ScaleY=1; }
 static void ResetWriteGaze(BotVisual b) { b.gaze.BeginAnimation(TranslateTransform.XProperty,null); b.gaze.BeginAnimation(TranslateTransform.YProperty,null); b.gaze.X=b.gaze.Y=0; }
 void FadeDot(double to,int ms,int delayMs) { var a=Anim(dotLayer.Opacity,to,TimeSpan.FromMilliseconds(ms),null); a.BeginTime=TimeSpan.FromMilliseconds(delayMs); dotLayer.BeginAnimation(OpacityProperty,a); }
 void StopMotion() {
  if(springOn) StopSpring();
  dotLayer.BeginAnimation(OpacityProperty,null); dotLayer.Opacity=expanded?0:1;
  panelClip.BeginAnimation(RectangleGeometry.RectProperty,null);
  body.BeginAnimation(OpacityProperty,null); Slide().BeginAnimation(TranslateTransform.YProperty,null);
  shadowFar.BeginAnimation(OpacityProperty,null); shadowNear.BeginAnimation(OpacityProperty,null); shadowFar.Opacity=shadowNear.Opacity=1;
 }
 // 咖啡开启：照旧阻止系统自动睡眠，再尽力把当前电源方案的合盖动作改成“不采取任何操作”。
 // 合盖设置读不到 / 写不进 / settings 存不下时都静默跳过，不影响咖啡本身。
 void ToggleSleep() {
  if(!awake) {
   if(SetThreadExecutionState(ES_CONTINUOUS|ES_SYSTEM_REQUIRED)==0) {Msg("无法阻止系统睡眠。"); return;}
   awake=true;
   if(keepAwake!=null) keepAwake.Enable();
  } else StopKeepAwake();
  ShowSleepState();
 }
 // 关掉咖啡：恢复记录里的合盖设置，再允许系统按自己的设置睡眠。
 void StopKeepAwake() {
  SetThreadExecutionState(ES_CONTINUOUS); awake=false;
  if(keepAwake!=null) keepAwake.Restore();
 }
 // 记录或清除 lidRestore，立即写盘；失败时把内存里的值还原，避免和文件不一致。
 bool SetLidRestore(string value) {
  string old=lidRestore; lidRestore=value;
  if(SaveSettings(true)) return true;
  lidRestore=old; return false;
 }
 // 出错退出前收起通知区域图标，免得留下一个点了没反应的图标。
 internal void HideTray() { if(tray!=null) { tray.Visible=false; tray.Dispose(); } }
 void Quit() {
  timer.Stop();
  if(!SaveNote()) {
   Expand(false);
   if(confirmOpen) return;
   if(!Ask("当前内容没能保存。如果需要，请先把要保留的文字复制出来。","放弃未保存的更改并退出？","放弃并退出",true)) return;
  }
  RenameAllAtOpportunity(); SnapshotCurrentNote(); SaveTabOrder(); SaveFolds();
  StopKeepAwake(); tray.Visible=false; tray.Dispose(); quitting=true; Close(); Application.Current.Shutdown();
 }
}
}
