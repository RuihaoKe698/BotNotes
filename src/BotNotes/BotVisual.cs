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
// 球体和眼睛分层（不画暗色外环，只留一层淡淡的同色辉光，显得更轻）：呼吸缩放球体时眼睛仍保持原来的形状。
class BotVisual : Canvas {
 public readonly System.Windows.Shapes.Ellipse glow, ball;
 public readonly System.Windows.Shapes.Rectangle leftEye, rightEye;
 public readonly ScaleTransform ballScale=new ScaleTransform(1,1), leftScale=new ScaleTransform(1,1), rightScale=new ScaleTransform(1,1);
 public readonly TranslateTransform floating=new TranslateTransform(), gaze=new TranslateTransform();
 readonly Canvas eyes=new Canvas();
 public readonly System.Windows.Shapes.Path leftDrag=new System.Windows.Shapes.Path(), rightDrag=new System.Windows.Shapes.Path(); public readonly ScaleTransform leftDragScale=new ScaleTransform(1,1), rightDragScale=new ScaleTransform(1,1); int faceGeneration;
 public double Diameter { get; private set; }
 public Canvas writeRearLeft, writeRearRight, writeFrontLeft, writeFrontRight; public readonly List<AnimationClock> writeClocks=new List<AnimationClock>(); readonly List<IAnimatable> writeTargets=new List<IAnimatable>(); readonly List<DependencyProperty> writeProperties=new List<DependencyProperty>(); readonly List<System.Windows.Shapes.Ellipse> writeHands=new List<System.Windows.Shapes.Ellipse>(); bool writeSeeking;
 public BotVisual(double d,Color accent,bool light) {
  IsHitTestVisible=false; ClipToBounds=false; RenderTransform=floating;
  // 这些图层只改变缩放或透明度，缓存可避免待机时每帧重算径向渐变。
  glow=new System.Windows.Shapes.Ellipse { CacheMode=new BitmapCache() }; Children.Add(glow);
  ball=new System.Windows.Shapes.Ellipse { CacheMode=new BitmapCache(), RenderTransformOrigin=new Point(0.5,0.5), RenderTransform=ballScale }; Children.Add(ball);
  eyes.RenderTransform=gaze; Children.Add(eyes);
  leftEye=Eye(leftScale); rightEye=Eye(rightScale); eyes.Children.Add(leftEye); eyes.Children.Add(rightEye);
  foreach(var pair in new[]{new KeyValuePair<System.Windows.Shapes.Path,ScaleTransform>(leftDrag,leftDragScale),new KeyValuePair<System.Windows.Shapes.Path,ScaleTransform>(rightDrag,rightDragScale)}) { pair.Key.Stroke=Brushes.White; pair.Key.StrokeStartLineCap=PenLineCap.Round; pair.Key.StrokeEndLineCap=PenLineCap.Round; pair.Key.StrokeLineJoin=PenLineJoin.Round; pair.Key.Visibility=Visibility.Collapsed; pair.Key.RenderTransformOrigin=new Point(0.5,0.5); pair.Key.RenderTransform=pair.Value; eyes.Children.Add(pair.Key); }
  Resize(d); Recolor(accent,light);
 }
 static System.Windows.Shapes.Rectangle Eye(ScaleTransform transform) { return new System.Windows.Shapes.Rectangle { Fill=Brushes.White, RenderTransformOrigin=new Point(0.5,0.5), RenderTransform=transform }; }
 public void Resize(double d) {
  Diameter=d; Width=Height=d; eyes.Width=eyes.Height=d;
  SizeAt(glow,d*1.6,-d*0.3);
  SizeAt(ball,d,0);
  double w=Math.Max(2,d*0.10), h=Math.Max(4,d*0.21); leftEye.Width=rightEye.Width=w; leftEye.Height=rightEye.Height=h; leftEye.RadiusX=rightEye.RadiusX=w/2; leftEye.RadiusY=rightEye.RadiusY=w/2;
  double x1=d*(0.5-0.21)-w/2, x2=d*(0.5+0.08)-w/2, y=d*(0.5-0.245)-h/2;
  if(d<=20) { x1=Math.Round(x1); x2=Math.Round(x2); y=Math.Round(y); RenderOptions.SetEdgeMode(leftEye,EdgeMode.Aliased); RenderOptions.SetEdgeMode(rightEye,EdgeMode.Aliased); } else { RenderOptions.SetEdgeMode(leftEye,EdgeMode.Unspecified); RenderOptions.SetEdgeMode(rightEye,EdgeMode.Unspecified); }
  SetLeft(leftEye,x1); SetLeft(rightEye,x2); SetTop(leftEye,y); SetTop(rightEye,y);
  SetDragShape(leftDrag,d,-0.21,true); SetDragShape(rightDrag,d,0.08,false); // ><：各自放在眼睛的位置，中间留出空隙
 }
 void SetDragShape(System.Windows.Shapes.Path path,double d,double x,bool left) {
  double arm=Math.Max(2.4,d*0.11), w=arm*Math.Cos(35*Math.PI/180), h=arm*Math.Sin(35*Math.PI/180); var g=new StreamGeometry(); using(var c=g.Open()) { c.BeginFigure(new Point(left?0:w,0),false,false); c.LineTo(new Point(left?w:0,h),true,false); c.LineTo(new Point(left?0:w,h*2),true,false); } path.Data=g; path.StrokeThickness=Math.Max(1.2,0.06*d); SetLeft(path,d*(0.5+x)-w/2); SetTop(path,d*(0.5-0.245)-h);
 }
 public void SetDragFace(bool active,bool animate,Action finished) {
  int generation=++faceGeneration; if(!animate) { leftEye.Visibility=rightEye.Visibility=active?Visibility.Collapsed:Visibility.Visible; leftDrag.Visibility=rightDrag.Visibility=active?Visibility.Visible:Visibility.Collapsed; if(finished!=null) finished(); return; }
  var from=active?new[]{leftScale,rightScale}:new[]{leftDragScale,rightDragScale};
  var close=new DoubleAnimation(from[0].ScaleY,0.15,TimeSpan.FromMilliseconds(70)) { FillBehavior=FillBehavior.Stop }; Timeline.SetDesiredFrameRate(close,60);
  close.Completed+=(sender,args)=> { if(generation!=faceGeneration) return; leftEye.Visibility=rightEye.Visibility=active?Visibility.Collapsed:Visibility.Visible; leftDrag.Visibility=rightDrag.Visibility=active?Visibility.Visible:Visibility.Collapsed; from[0].BeginAnimation(ScaleTransform.ScaleYProperty,null); from[1].BeginAnimation(ScaleTransform.ScaleYProperty,null); from[0].ScaleY=from[1].ScaleY=1; var target=active?new[]{leftDragScale,rightDragScale}:new[]{leftScale,rightScale}; var open=new DoubleAnimation(0.6,1,TimeSpan.FromMilliseconds(90)) { EasingFunction=new BackEase { Amplitude=0.4, EasingMode=EasingMode.EaseOut }, FillBehavior=FillBehavior.Stop }; Timeline.SetDesiredFrameRate(open,60); open.Completed+=(s,e)=> { if(generation==faceGeneration && finished!=null) finished(); }; target[0].BeginAnimation(ScaleTransform.ScaleYProperty,open); target[1].BeginAnimation(ScaleTransform.ScaleYProperty,open); };
  from[0].BeginAnimation(ScaleTransform.ScaleYProperty,close); from[1].BeginAnimation(ScaleTransform.ScaleYProperty,close);
 }
 static void SizeAt(FrameworkElement e,double size,double offset) { e.Width=e.Height=size; SetLeft(e,offset); SetTop(e,offset); }
 static Color Tint(double hue,string hex) { var c=(Color)ColorConverter.ConvertFromString(hex); double h,s,l; Palette.ToHsl(c,out h,out s,out l); return Palette.FromHsl(hue,s,l); }
 public void Recolor(Color accent,bool light) {
  double h,s,l; Palette.ToHsl(accent,out h,out s,out l); if(s<0.15) h=211;
  var g=new RadialGradientBrush { Center=new Point(0.5,0.5), GradientOrigin=new Point(0.5,0.5), RadiusX=0.5, RadiusY=0.5 };
  string[] colors={"#208BFF","#288EFF","#3794FF","#4C9EFF","#65A9FF","#83B6FA"}; for(int i=0;i<colors.Length;i++) g.GradientStops.Add(new GradientStop(Tint(h,colors[i]),i/5.0));
  var shade=new LinearGradientBrush { StartPoint=new Point(0.5,0), EndPoint=new Point(0.5,1) }; shade.GradientStops.Add(new GradientStop(Colors.Transparent,0.4)); shade.GradientStops.Add(new GradientStop(Color.FromArgb(35,255,255,255),1));
  var drawing=new DrawingGroup(); var rect=new RectangleGeometry(new Rect(0,0,1,1)); drawing.Children.Add(new GeometryDrawing(g,null,rect)); drawing.Children.Add(new GeometryDrawing(shade,null,rect)); ball.Fill=new DrawingBrush(drawing);
  if(writeHands.Count>0) { Brush handEdge=HandEdge(); for(int i=0;i<writeHands.Count;i++) { writeHands[i].Fill=ball.Fill; writeHands[i].Stroke=handEdge; } }
  Color c=Tint(h,"#208BFF"); var halo=new RadialGradientBrush { Center=new Point(0.5,0.5), GradientOrigin=new Point(0.5,0.5), RadiusX=0.5, RadiusY=0.5 }; halo.GradientStops.Add(new GradientStop(Color.FromArgb(150,c.R,c.G,c.B),0)); halo.GradientStops.Add(new GradientStop(Color.FromArgb(90,c.R,c.G,c.B),0.58)); halo.GradientStops.Add(new GradientStop(Color.FromArgb(0,c.R,c.G,c.B),1)); glow.Fill=halo; glow.Opacity=0.45;
 }
 // 写字用的图层在播放时才创建；停止时移除时钟和图层，避免留下透明残影。
 // 动作顺序：手和道具先藏在球后，贴着左右两侧水平绕出来；侧面时用 ScaleX 压窄，
 // 像本子侧对着我们；完全离开球体轮廓后换到前面，再一边转正一边摆到写字姿势。
 // 塞回反过来：先转侧、绕出去，换回球后，再水平收进身体后面。
 public void StartWrite(Action done) {
  StopWrite(); double d=Diameter;
  // 总长 4.44 秒；0–0.84 秒从球后绕到写字姿势，3.60–4.44 秒原路塞回。
  const double total=4.44, forwardEnd=0.84, swapFront=0.40, swapBack=4.04, stowStart=3.60;
  // 藏匿位置（相对写字姿势的平移量，单位 D）：所有像素都落在球体圆盘内。
  const double hiddenLx=0.272, hiddenLy=-0.238, hiddenRx=-0.081, hiddenRy=-0.115;
  // 侧面绕行的横向鼓包：左组往左、右组往右；纵向只从藏匿高度回到写字高度，不往下荡。
  const double bumpLx=-0.380, bumpRx=0.335;
  // 侧面压窄：本子压到 0.35，笔也压到 0.35；手不压，保持小圆手。
  const double sideL=0.35, sideR=0.35, startScale=0.85;
  var easeOut=new CubicEase { EasingMode=EasingMode.EaseOut };
  var easeIn=new CubicEase { EasingMode=EasingMode.EaseIn };
  writeRearLeft=WriteCanvas(); writeRearRight=WriteCanvas(); writeFrontLeft=WriteCanvas(); writeFrontRight=WriteCanvas();
  writeRearLeft.Visibility=writeRearRight.Visibility=Visibility.Visible;
  writeFrontLeft.Visibility=writeFrontRight.Visibility=Visibility.Hidden;
  var leftScaleWrite=new ScaleTransform(startScale,startScale,d*0.5,d*0.5);
  var leftMoveWrite=new TranslateTransform(hiddenLx*d,hiddenLy*d);
  var leftTransform=new TransformGroup(); leftTransform.Children.Add(leftScaleWrite); leftTransform.Children.Add(leftMoveWrite);
  var rightScaleWrite=new ScaleTransform(startScale,startScale,d*0.5,d*0.5);
  var rightMoveWrite=new TranslateTransform(hiddenRx*d,hiddenRy*d);
  var rightWriteMove=new TranslateTransform();
  var rightTransform=new TransformGroup(); rightTransform.Children.Add(rightScaleWrite); rightTransform.Children.Add(rightMoveWrite); rightTransform.Children.Add(rightWriteMove);
  writeRearLeft.RenderTransform=writeFrontLeft.RenderTransform=leftTransform;
  writeRearRight.RenderTransform=writeFrontRight.RenderTransform=rightTransform;
  // 侧面压窄只作用于本子和笔；两套副本共用同一个 ScaleTransform，换层时形状完全一致。
  var notebookSide=new ScaleTransform(sideL,1,d*0.094,d*0.2866);
  var penSide=new ScaleTransform(sideR,1,d*0.90,d*0.72);
  var penRotation=new RotateTransform(0,d*0.7,d*0.55);
  var inkScales=new List<ScaleTransform>(); for(int i=0;i<3;i++) inkScales.Add(new ScaleTransform(0,1));
  BuildLeftContent(writeRearLeft,d,inkScales,notebookSide); BuildLeftContent(writeFrontLeft,d,inkScales,notebookSide);
  BuildRightContent(writeRearRight,d,penRotation,penSide); BuildRightContent(writeFrontRight,d,penRotation,penSide);
  Children.Insert(1,writeRearLeft); Children.Insert(2,writeRearRight); Children.Add(writeFrontLeft); Children.Add(writeFrontRight);
  // 球体本身保持不动；只让道具从 0.85 轻微放大到 1，解除“从球心缩放 0→1”的观感。
  foreach(var scale in new[]{leftScaleWrite,rightScaleWrite}) foreach(var property in new[]{ScaleTransform.ScaleXProperty,ScaleTransform.ScaleYProperty}) {
   var frames=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(total) };
   frames.KeyFrames.Add(new LinearDoubleKeyFrame(startScale,KeyTime.FromTimeSpan(TimeSpan.Zero)));
   frames.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(swapFront)),easeOut));
   frames.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(swapBack))));
   frames.KeyFrames.Add(new EasingDoubleKeyFrame(startScale,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(total)),easeIn));
   AddWriteAnimation(scale,property,frames);
  }
  var leftX=RouteFrames(hiddenLx*d,0,bumpLx*d,forwardEnd,total,stowStart);
  var leftY=RouteFrames(hiddenLy*d,0,0,forwardEnd,total,stowStart);
  AddWriteAnimation(leftMoveWrite,TranslateTransform.XProperty,leftX);
  AddWriteAnimation(leftMoveWrite,TranslateTransform.YProperty,leftY);
  var rightX=RouteFrames(hiddenRx*d,0,bumpRx*d,forwardEnd,total,stowStart);
  var rightY=RouteFrames(hiddenRy*d,0,0,forwardEnd,total,stowStart);
  AddWriteAnimation(rightMoveWrite,TranslateTransform.XProperty,rightX);
  AddWriteAnimation(rightMoveWrite,TranslateTransform.YProperty,rightY);
  // 侧窄 → 正面：0.40 秒换层后开始展开，0.84 秒到 1；塞回时 3.60–4.04 秒重新压窄。
  AddWriteAnimation(notebookSide,ScaleTransform.ScaleXProperty,SideFrames(sideL,total,swapFront,swapBack,stowStart,forwardEnd));
  AddWriteAnimation(penSide,ScaleTransform.ScaleXProperty,SideFrames(sideR,total,swapFront,swapBack,stowStart,forwardEnd));
  // 写字时笔尖沿本子来回三趟；3.18 秒停笔，3.60 秒回到起始横向位置，方便沿侧面塞回。
  var motion=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(total) }; double[] times={0,0.90,1.12,1.72,1.75,2.35,2.38,2.98,3.18,stowStart,total}; double[] positions={0,0,-0.12,0.16,-0.12,0.16,-0.12,0.16,0.16,0,0};
  for(int i=0;i<times.Length;i++) motion.KeyFrames.Add(new LinearDoubleKeyFrame(positions[i]*d,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i]))));
  AddWriteAnimation(rightWriteMove,TranslateTransform.XProperty,motion);
  var tilt=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(total) }; double[] tiltTimes={0,0.90,1.12,1.72,1.75,2.35,2.38,2.98,3.18,stowStart,total}; double[] tiltValues={0,0,-8,8,-8,8,-8,8,8,0,0};
  for(int i=0;i<tiltTimes.Length;i++) tilt.KeyFrames.Add(new LinearDoubleKeyFrame(tiltValues[i],KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tiltTimes[i]))));
  AddWriteAnimation(penRotation,RotateTransform.AngleProperty,tilt);
  for(int i=0;i<3;i++) {
   var ink=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(total) }; double start=1.0+i*0.65;
   ink.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.Zero)));
   ink.KeyFrames.Add(new LinearDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(start))));
   ink.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(start+0.6))));
   AddWriteAnimation(inkScales[i],ScaleTransform.ScaleXProperty,ink);
  }
  // 换层：0.40 秒 rear→front，4.04 秒 front→rear；两套内容同一姿态，且实际像素与球体没有重叠。
  AddWriteAnimation(writeRearLeft,UIElement.VisibilityProperty,VisibilityFrames(true,swapFront,swapBack,total));
  AddWriteAnimation(writeRearRight,UIElement.VisibilityProperty,VisibilityFrames(true,swapFront,swapBack,total));
  AddWriteAnimation(writeFrontLeft,UIElement.VisibilityProperty,VisibilityFrames(false,swapFront,swapBack,total));
  AddWriteAnimation(writeFrontRight,UIElement.VisibilityProperty,VisibilityFrames(false,swapFront,swapBack,total));
  writeClocks[0].Completed+=(sender,args)=> { if(!writeSeeking) { StopWrite(); if(done!=null) done(); } };
 }
 // 一条连续弧线：hidden→side→writing。前 0.84 秒采样前进，3.60 秒后沿同一曲线倒放回 hidden。
 // 采样的线性关键帧之间没有停留；smoothstep 让起止柔和，但中间速度连续。
 DoubleAnimationUsingKeyFrames RouteFrames(double hidden,double final,double bump,double forwardEnd,double total,double stowStart) {
  var a=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(total) };
  int n=42;
  for(int i=0;i<=n;i++) { double t=forwardEnd*i/n; double s=t/forwardEnd; double u=s*s*(3-2*s); a.KeyFrames.Add(new LinearDoubleKeyFrame(hidden+(final-hidden)*u+bump*Math.Sin(Math.PI*u),KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)))); }
  a.KeyFrames.Add(new LinearDoubleKeyFrame(final,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(stowStart))));
  int m=42;
  for(int i=1;i<=m;i++) { double tr=stowStart+(total-stowStart)*i/m; double tf=forwardEnd*(1.0-(tr-stowStart)/(total-stowStart)); double s=tf/forwardEnd; double u=s*s*(3-2*s); a.KeyFrames.Add(new LinearDoubleKeyFrame(hidden+(final-hidden)*u+bump*Math.Sin(Math.PI*u),KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tr)))); }
  return a;
 }
 // 侧面压窄 → 正面展开；塞回时反向压窄。0.40/4.04 秒换层时都还是窄的。
 DoubleAnimationUsingKeyFrames SideFrames(double narrow,double total,double swapFront,double swapBack,double stowStart,double forwardEnd) {
  var a=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(total) };
  a.KeyFrames.Add(new LinearDoubleKeyFrame(narrow,KeyTime.FromTimeSpan(TimeSpan.Zero)));
  a.KeyFrames.Add(new LinearDoubleKeyFrame(narrow,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(swapFront))));
  int n=22;
  for(int i=1;i<=n;i++) { double t=swapFront+(forwardEnd-swapFront)*i/n; double s=(t-swapFront)/(forwardEnd-swapFront); double u=s*s*(3-2*s); a.KeyFrames.Add(new LinearDoubleKeyFrame(narrow+(1-narrow)*u,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)))); }
  a.KeyFrames.Add(new LinearDoubleKeyFrame(1,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(stowStart))));
  int m=22;
  for(int i=1;i<=m;i++) { double t=stowStart+(swapBack-stowStart)*i/m; double s=(t-stowStart)/(swapBack-stowStart); double u=s*s*(3-2*s); a.KeyFrames.Add(new LinearDoubleKeyFrame(1+(narrow-1)*u,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t)))); }
  a.KeyFrames.Add(new LinearDoubleKeyFrame(narrow,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(total))));
  return a;
 }
 ObjectAnimationUsingKeyFrames VisibilityFrames(bool rear,double swapFront,double swapBack,double total) {
  var a=new ObjectAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(total) };
  a.KeyFrames.Add(new DiscreteObjectKeyFrame(rear?Visibility.Visible:Visibility.Hidden,KeyTime.FromTimeSpan(TimeSpan.Zero)));
  a.KeyFrames.Add(new DiscreteObjectKeyFrame(rear?Visibility.Hidden:Visibility.Visible,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(swapFront))));
  a.KeyFrames.Add(new DiscreteObjectKeyFrame(rear?Visibility.Visible:Visibility.Hidden,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(swapBack))));
  return a;
 }
 void BuildLeftContent(Canvas layer,double d,List<ScaleTransform> inkScales,ScaleTransform notebookSide) {
  layer.Children.Add(Hand(d,0.08,0.80));
  var notebook=new Canvas { Width=d*0.42, Height=d*0.50 };
  SetLeft(notebook,-0.01*d); SetTop(notebook,0.53*d);
  var rotate=new RotateTransform(-8,d*0.21,d*0.25);
  var group=new TransformGroup(); group.Children.Add(rotate); group.Children.Add(notebookSide);
  notebook.RenderTransform=group;
  var paper=new Border { Width=d*0.42, Height=d*0.50, Background=Palette.Make("#F4F1E8"), BorderBrush=Palette.Make("#D9D3C4"), BorderThickness=new Thickness(Math.Max(0.5,d*0.006)), CacheMode=new BitmapCache(), CornerRadius=new CornerRadius(0.05*d) }; notebook.Children.Add(paper);
  var spine=new Border { Width=d*0.07, Height=d*0.50, Background=WriteSpine(), CornerRadius=new CornerRadius(0.05*d,0,0,0.05*d) }; notebook.Children.Add(spine);
  double[] lengths={0.23,0.18,0.25};
  for(int i=0;i<3;i++) {
   var line=new System.Windows.Shapes.Rectangle { Width=d*lengths[i], Height=Math.Max(0.8,d*0.03), Fill=Palette.Make("#5A5A64"), RadiusX=d*0.015, RadiusY=d*0.015, RenderTransform=inkScales[i] };
   SetLeft(line,d*0.12); SetTop(line,d*(0.12+0.10*i)); notebook.Children.Add(line);
  }
  layer.Children.Add(notebook);
 }
 void BuildRightContent(Canvas layer,double d,RotateTransform penRotation,ScaleTransform penSide) {
  layer.Children.Add(Hand(d,0.90,0.72));
  var pen=new Canvas { Width=d, Height=d };
  var group=new TransformGroup(); group.Children.Add(penRotation); group.Children.Add(penSide);
  pen.RenderTransform=group;
  var stem=new System.Windows.Shapes.Line { X1=d*0.20, Y1=d*0.91, X2=d*0.79, Y2=d*0.43, Stroke=Palette.Make("#3A3A40"), StrokeThickness=Math.Max(1,d*0.04), StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round }; pen.Children.Add(stem);
  var feather=new StreamGeometry(); using(var c=feather.Open()) { c.BeginFigure(new Point(d*0.49,d*0.72),true,true); c.BezierTo(new Point(d*0.46,d*0.49),new Point(d*0.69,d*0.31),new Point(d*0.83,d*0.34),true,false); c.BezierTo(new Point(d*0.86,d*0.48),new Point(d*0.69,d*0.67),new Point(d*0.49,d*0.72),true,false); } pen.Children.Add(new System.Windows.Shapes.Path { Data=feather, Fill=Brushes.White, CacheMode=new BitmapCache() }); pen.Children.Add(new System.Windows.Shapes.Line { X1=d*0.52,Y1=d*0.69,X2=d*0.77,Y2=d*0.40,Stroke=Palette.Make("#A1A1A7"),StrokeThickness=Math.Max(0.6,d*0.018) });
  layer.Children.Add(pen);
 }
 Canvas WriteCanvas() { return new Canvas { Width=Diameter, Height=Diameter, ClipToBounds=false, IsHitTestVisible=false }; }
 System.Windows.Shapes.Ellipse Hand(double d,double x,double y) { var hand=new System.Windows.Shapes.Ellipse { Width=d*0.26, Height=d*0.26, Fill=ball.Fill, CacheMode=new BitmapCache(), Stroke=HandEdge(), StrokeThickness=Math.Max(0.4,d*0.008) }; SetLeft(hand,d*x-d*0.13); SetTop(hand,d*y-d*0.13); writeHands.Add(hand); return hand; }
 Color BallColor() { var brush=ball.Fill as DrawingBrush; var drawings=brush==null?null:brush.Drawing as DrawingGroup; var geom=drawings==null?null:drawings.Children[0] as GeometryDrawing; var gradient=geom==null?null:geom.Brush as RadialGradientBrush; return gradient==null?Colors.SteelBlue:gradient.GradientStops[0].Color; }
 Brush WriteSpine() { return new SolidColorBrush(Palette.Mix(BallColor(),Colors.Black,0.5)); }
 Brush HandEdge() { return new SolidColorBrush(Palette.Mix(BallColor(),Colors.White,0.55)); }
 // 写字时钟先不设 60 上限；在 240Hz 屏上实测不封顶时 CPU 比待机多 15.6 个百分点，按任务约定改为封顶 120：
 // 60Hz 屏仍是 60，240Hz 屏跑 120。眨眼、环顾等仍保持原来的 60 帧设置。
 void AddWriteAnimation(DependencyObject target,DependencyProperty property,Timeline animation) { Timeline.SetDesiredFrameRate(animation,120); var clock=(AnimationClock)animation.CreateClock(true); var animatable=(IAnimatable)target; animatable.ApplyAnimationClock(property,clock); writeClocks.Add(clock); writeTargets.Add(animatable); writeProperties.Add(property); }
 public void SeekWrite(double seconds) { writeSeeking=true; foreach(var clock in writeClocks.ToArray()) { clock.Controller.Pause(); clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(seconds),TimeSeekOrigin.BeginTime); } }
 public void StopWrite() { writeSeeking=false; for(int i=0;i<writeClocks.Count;i++) { writeTargets[i].ApplyAnimationClock(writeProperties[i],null); writeClocks[i].Controller.Stop(); } writeClocks.Clear(); writeTargets.Clear(); writeProperties.Clear(); writeHands.Clear(); foreach(var layer in new[]{writeRearLeft,writeRearRight,writeFrontLeft,writeFrontRight}) if(layer!=null) Children.Remove(layer); writeRearLeft=writeRearRight=writeFrontLeft=writeFrontRight=null; }

 public void Reset() { floating.BeginAnimation(TranslateTransform.YProperty,null); floating.Y=0; ballScale.BeginAnimation(ScaleTransform.ScaleXProperty,null); ballScale.BeginAnimation(ScaleTransform.ScaleYProperty,null); ballScale.ScaleX=ballScale.ScaleY=1; glow.BeginAnimation(UIElement.OpacityProperty,null); glow.Opacity=0.45; gaze.BeginAnimation(TranslateTransform.XProperty,null); gaze.BeginAnimation(TranslateTransform.YProperty,null); gaze.X=gaze.Y=0; leftScale.BeginAnimation(ScaleTransform.ScaleYProperty,null); rightScale.BeginAnimation(ScaleTransform.ScaleYProperty,null); leftScale.ScaleY=rightScale.ScaleY=1; }
}

}
