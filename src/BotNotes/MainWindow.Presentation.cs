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
 void UnfoldPresentation(MapNode n) { n.Folded=false; foreach(var k in n.Kids) UnfoldPresentation(k); }
 void TogglePresentation() { if(Presenting) ExitPresentation(); else if(mapping && mapRoot!=null) EnterPresentation(); }
 void EnterPresentation() { CloseHistory(); if(!mapping || mapRoot==null || Presenting) return; if(mapBox!=null) EndMapEdit(true); presentationWasFull=fullscreen; if(!fullscreen) SetFullscreen(true); presenting=true; presentCount=1; RenderMap(false); UpdatePresenting(); mapMoved=false; Dispatcher.BeginInvoke(new Action(()=> { if(Presenting) FitMap(); }),DispatcherPriority.Loaded); mapView.Focus(); }
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
}
}
