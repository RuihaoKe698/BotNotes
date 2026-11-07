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
class Win32Owner : Forms.IWin32Window { readonly IntPtr h; public Win32Owner(IntPtr h) { this.h=h; } public IntPtr Handle { get { return h; } } }

static class Ui {
 public static readonly FontFamily IconFont=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
 public static readonly FontFamily Mono=new FontFamily("Cascadia Mono, Consolas");
 static Style focusRing;

 public static TextBlock Icon(string glyph,double size) {
  return new TextBlock { Text=glyph, FontFamily=IconFont, FontSize=size, VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Center };
 }
 public static StackPanel IconLabel(string glyph,string text) {
  var row=new StackPanel { Orientation=Orientation.Horizontal };
  var icon=Icon(glyph,13); icon.Margin=new Thickness(0,0,6,0); row.Children.Add(icon);
  row.Children.Add(new TextBlock { Text=text, VerticalAlignment=VerticalAlignment.Center });
  return row;
 }
 // 键盘切换焦点时才显示的绿色描边；鼠标点击不出现。
 static Style FocusRing() {
  if(focusRing!=null) return focusRing;
  var t=new ControlTemplate(); var ring=new FrameworkElementFactory(typeof(Border));
  ring.SetResourceReference(Border.BorderBrushProperty,"AccentBrush"); ring.SetValue(Border.BorderThicknessProperty,new Thickness(1.5)); ring.SetValue(Border.CornerRadiusProperty,new CornerRadius(8)); ring.SetValue(FrameworkElement.MarginProperty,new Thickness(-2));
  t.VisualTree=ring; focusRing=new Style(); focusRing.Setters.Add(new Setter(Control.TemplateProperty,t)); return focusRing;
 }
 public static Button Button(object content,string tip) { return Button(content,tip,new CornerRadius(7)); }
 public static Button Button(object content,string tip,CornerRadius radius) {
  var b=new Button { Content=content, ToolTip=tip, Background=Brushes.Transparent, Foreground=Palette.Text, BorderThickness=new Thickness(0), Padding=new Thickness(10,0,10,0), Height=30, FontSize=12.5, Cursor=Cursors.Hand, FocusVisualStyle=FocusRing(), VerticalAlignment=VerticalAlignment.Center };
  var template=new ControlTemplate(typeof(System.Windows.Controls.Button));
  var grid=new FrameworkElementFactory(typeof(Grid));
  var surface=new FrameworkElementFactory(typeof(Border)); surface.SetValue(Border.CornerRadiusProperty,radius); surface.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
  // 悬停与按下用一层半透明白色叠加，任何底色（含选中的绿色标签）都能看到反馈。
  var overlay=new FrameworkElementFactory(typeof(Border)); overlay.Name="overlay"; overlay.SetValue(Border.CornerRadiusProperty,radius); overlay.SetResourceReference(Border.BackgroundProperty,"HoverBrush"); overlay.SetValue(UIElement.OpacityProperty,0.0); overlay.SetValue(UIElement.IsHitTestVisibleProperty,false); // 叠加层：深色背景上是白色，浅色背景上是黑色
  var content2=new FrameworkElementFactory(typeof(ContentPresenter)); content2.Name="content"; content2.SetValue(ContentPresenter.HorizontalAlignmentProperty,HorizontalAlignment.Center); content2.SetValue(ContentPresenter.VerticalAlignmentProperty,VerticalAlignment.Center); content2.SetValue(ContentPresenter.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));
  grid.AppendChild(surface); grid.AppendChild(overlay); grid.AppendChild(content2); template.VisualTree=grid;
  // 悬停叠加层淡入淡出（110 / 160ms），不再一下子跳变；按下时短暂变浅。
  // 悬停：只有这一个触发器动叠加层（淡入 7% / 淡出 0）。按下：只动内容的不透明度，稍微变淡。
  // 一个属性只交给一个触发器：WPF 里多个触发器的动画抢同一个属性时，排在后面的触发器永远赢，
  // 之前“松开回到 7%”排在后面，压住了“移开回 0”，点过的按钮就一直留着一块灰底。
  var hover=new Trigger { Property=UIElement.IsMouseOverProperty, Value=true }; hover.EnterActions.Add(Fade("overlay",0.07,110)); hover.ExitActions.Add(Fade("overlay",0,160)); template.Triggers.Add(hover);
  var press=new Trigger { Property=System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value=true }; press.EnterActions.Add(Fade("content",0.7,50)); press.ExitActions.Add(Fade("content",1,140)); template.Triggers.Add(press);
  var disabled=new Trigger { Property=UIElement.IsEnabledProperty, Value=false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.4)); template.Triggers.Add(disabled);
  b.Template=template; return b;
 }
 static BeginStoryboard Fade(string target,double to,int ms) {
  var a=new DoubleAnimation(to,TimeSpan.FromMilliseconds(ms)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } };
  Storyboard.SetTargetName(a,target); Storyboard.SetTargetProperty(a,new PropertyPath(UIElement.OpacityProperty));
  var sb=new Storyboard(); sb.Children.Add(a); return new BeginStoryboard { Storyboard=sb };
 }
 // 输入框：圆角、细边框，聚焦时边框变强调色，不用系统默认的蓝框。
 public static TextBox Input(string text) {
  var t=new TextBox { Text=text, Background=Palette.Raised, Foreground=Palette.Text, CaretBrush=Palette.Text, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), Padding=new Thickness(8,5,8,5), FontSize=13, SelectionBrush=Palette.Accent, FocusVisualStyle=null };
  var tpl=new ControlTemplate(typeof(TextBox)); var bd=new FrameworkElementFactory(typeof(Border)); bd.Name="bd";
  bd.SetValue(Border.CornerRadiusProperty,new CornerRadius(7)); bd.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty)); bd.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty)); bd.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
  var host=new FrameworkElementFactory(typeof(ScrollViewer)); host.Name="PART_ContentHost"; host.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty)); host.SetValue(Control.PaddingProperty,new Thickness(0)); bd.AppendChild(host); tpl.VisualTree=bd;
  var focus=new Trigger { Property=UIElement.IsKeyboardFocusWithinProperty, Value=true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty,new DynamicResourceExtension("AccentBrush"),"bd")); tpl.Triggers.Add(focus); // 引用资源，不把会换色的画刷封进模板
  t.Template=tpl; return t;
 }
 public static void HorizontalWheel(ScrollViewer sv) {
  sv.PreviewMouseWheel+=(s,e)=> { if(sv.ScrollableWidth<=0) return; sv.ScrollToHorizontalOffset(sv.HorizontalOffset-e.Delta/2.0); e.Handled=true; };
 }
}

// 托盘右键菜单：WinForms 默认菜单是灰底蓝高亮，换成和面板同色的深色菜单；Windows 11 上再请系统画圆角。
class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer {
 public D.Color Back=D.Color.FromArgb(15,16,19), Hover=D.Color.FromArgb(37,39,44), Text=D.Color.FromArgb(236,236,239), Line=D.Color.FromArgb(59,62,69);
 public TrayMenuRenderer() : base(new Forms.ProfessionalColorTable()) { RoundedEdges=false; }
 protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) { using(var b=new D.SolidBrush(Back)) e.Graphics.FillRectangle(b,e.AffectedBounds); }
 protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e) { using(var p=new D.Pen(Line)) e.Graphics.DrawRectangle(p,0,0,e.AffectedBounds.Width-1,e.AffectedBounds.Height-1); }
 protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) {}
 protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e) {
  if(!e.Item.Selected || !e.Item.Enabled) return;
  e.Graphics.SmoothingMode=D.Drawing2D.SmoothingMode.AntiAlias;
  var r=new D.RectangleF(4,1,e.Item.Width-8,e.Item.Height-2); float d=8;
  using(var p=new D.Drawing2D.GraphicsPath()) { p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); using(var b=new D.SolidBrush(Hover)) e.Graphics.FillPath(b,p); }
 }
 protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e) { e.TextColor=Text; base.OnRenderItemText(e); }
 protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e) { int y=e.Item.Height/2; using(var p=new D.Pen(Line)) e.Graphics.DrawLine(p,10,y,e.Item.Width-10,y); }
}

}
