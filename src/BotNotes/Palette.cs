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
 public static void ToHsl(Color c,out double h,out double s,out double l) {
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

}
