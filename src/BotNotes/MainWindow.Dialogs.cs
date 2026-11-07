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
 void Msg(string text) { modal++; try { Dialog(IsLoaded?this:null,null,text,"知道了",null,false,null,null); } finally { modal--; } }
 bool Ask(string text,string title,string ok,bool warn) { modal++; try { return Dialog(IsLoaded?this:null,title,text,ok,"取消",warn,null,null); } finally { modal--; } }
 internal static void Notice(string text) { Dialog(null,null,text,"知道了",null,false,null,null); }
 // ——— 对话框 ———
 // 和面板同一套样式的小窗口，代替系统 MessageBox：面板色圆角卡片、柔和阴影、右下角按钮（主按钮强调色，危险操作用警示色）。
 // Enter 等于点主按钮，Esc 关闭。返回 true 表示点了主按钮；canOk 返回 false 时不关闭。
 static bool Dialog(Window owner,string title,object body,string ok,string cancel,bool warn,Func<bool> canOk,UIElement focus) {
  var w=new Window { WindowStyle=WindowStyle.None, AllowsTransparency=true, Background=Brushes.Transparent, ResizeMode=ResizeMode.NoResize, ShowInTaskbar=owner==null, Topmost=true, SizeToContent=SizeToContent.WidthAndHeight, FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI"), Title=title??"BotNotes" };
  if(owner!=null) { w.Owner=owner; w.WindowStartupLocation=WindowStartupLocation.CenterOwner; foreach(var key in new object[] { "HoverBrush","AccentBrush",typeof(ToolTip) }) if(owner.Resources.Contains(key)) w.Resources[key]=owner.Resources[key]; }
  else { w.WindowStartupLocation=WindowStartupLocation.CenterScreen; w.Resources["HoverBrush"]=new SolidColorBrush(Palette.Light?Colors.Black:Colors.White); w.Resources["AccentBrush"]=new SolidColorBrush(Palette.Accent.Color); }
  var root=new Grid { Margin=new Thickness(28) }; w.Content=root;
  root.Children.Add(new Border { Background=Palette.Panel, CornerRadius=new CornerRadius(12), Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=28, ShadowDepth=8, Direction=270, Opacity=0.45, Color=Colors.Black } });
  var card=new Border { Background=Palette.Panel, BorderBrush=Palette.Edge, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(12), Padding=new Thickness(20,18,20,16) }; root.Children.Add(card);
  var stack=new StackPanel { Width=340 }; card.Child=stack;
  if(!string.IsNullOrEmpty(title)) stack.Children.Add(new TextBlock { Text=title, Foreground=Palette.Text, FontSize=14.5, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,8) });
  if(body is string) stack.Children.Add(new TextBlock { Text=(string)body, Foreground=string.IsNullOrEmpty(title)?Palette.Text:Palette.Muted, FontSize=13, TextWrapping=TextWrapping.Wrap, LineHeight=20 });
  else if(body!=null) stack.Children.Add((UIElement)body);
  var row=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,18,0,0) }; stack.Children.Add(row);
  bool result=false;
  if(cancel!=null) { var c=Ui.Button(cancel,null); c.Background=Palette.Raised; c.Padding=new Thickness(16,0,16,0); c.Margin=new Thickness(0,0,8,0); c.Click+=(s,e)=>w.Close(); row.Children.Add(c); }
  var okBtn=Ui.Button(ok,null); okBtn.Padding=new Thickness(16,0,16,0); okBtn.FontWeight=FontWeights.SemiBold;
  if(warn) { okBtn.Background=new SolidColorBrush(Palette.Mix(Palette.Panel.Color,Palette.Warn.Color,0.18)); okBtn.Foreground=Palette.Warn; } else { okBtn.Background=Palette.AccentBg; okBtn.Foreground=Palette.Accent; }
  okBtn.Click+=(s,e)=> { if(canOk!=null && !canOk()) return; result=true; w.Close(); }; row.Children.Add(okBtn);
  w.PreviewKeyDown+=(s,e)=> { if(e.Key==Key.Escape) { w.Close(); e.Handled=true; } else if(e.Key==Key.Enter) { okBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); e.Handled=true; } };
  w.Loaded+=(s,e)=> { var f=focus??okBtn; f.Focus(); var tb=f as TextBox; if(tb!=null) tb.SelectAll(); };
  w.ShowDialog(); return result;
 }
 static bool HasFiles(DragEventArgs e) { return e.Data.GetDataPresent(DataFormats.FileDrop); }

}
}
