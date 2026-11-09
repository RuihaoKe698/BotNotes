using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NotchNotesWindows {
partial class MainWindow {
 // ——— 字号 ———
 // 五档：小 / 较小 / 标准 / 大 / 特大。编辑区正文分别 13 / 14 / 15 / 17 / 19；
 // 预览和脑图里的所有正文字号、行高都按“档位字号 / 15”缩放，默认标准档就是原来的 15，乘 1 后数值不变。
 static readonly string[] FontNames={ "小","较小","标准","大","特大" };
 static readonly double[] FontSizes={13,14,15,17,19};
 const int DefaultFontStep=2;
 const string G_Check="\uE73E";
 int fontStep=DefaultFontStep;
 Button fontButton; Popup fontMenu; FrameworkElement[] fontChecks;
 double FontScale { get { return FontSizes[fontStep]/15.0; } }
 double Scaled(double value) { return Math.Round(value*FontScale,1); }
 // 预览的正文 / 行高：原来 14.5 / 24；用属性让每处渲染都跟着当前档位走。
 double BodySize { get { return Scaled(14.5); } }
 double BodyLine { get { return Scaled(24); } }

 Button BuildFontButton() {
  var b=Ui.Button(FontButtonLabel(),"字号（Ctrl+滚轮、Ctrl+= / Ctrl+-）");
  b.Width=42; b.Padding=new Thickness(0); b.Foreground=Palette.Muted; b.Focusable=false;
  b.Margin=new Thickness(8,0,0,0); b.HorizontalAlignment=HorizontalAlignment.Right; b.VerticalAlignment=VerticalAlignment.Center;
  b.Click+=(s,e)=>ShowFontMenu(b);
  return b;
 }
 static FrameworkElement FontButtonLabel() {
  // 和标题按钮“H⌄”一样：文字（Aa）+ 9px 下箭头。
  var row=new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center };
  row.Children.Add(new TextBlock { Text="Aa", FontSize=14.5, FontWeight=FontWeights.SemiBold, VerticalAlignment=VerticalAlignment.Center });
  var chev=LineIcon.Make("M7 10 L12 15 L17 10",null,9); chev.Margin=new Thickness(1,1,0,0); row.Children.Add(chev);
  return row;
 }
 void ShowFontMenu(Button anchor) {
  if(fontMenu==null) BuildFontMenu();
  UpdateFontMenuChecks();
  fontMenu.PlacementTarget=anchor; fontMenu.IsOpen=!fontMenu.IsOpen;
 }
 void BuildFontMenu() {
  var list=new StackPanel { Margin=new Thickness(4) };
  fontChecks=new FrameworkElement[FontNames.Length];
  for(int i=0;i<FontNames.Length;i++) {
   var row=new Grid { Width=176 };
   row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(22) });
   row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
   var check=Ui.Icon(G_Check,11); check.Foreground=Palette.Accent; check.HorizontalAlignment=HorizontalAlignment.Left; check.VerticalAlignment=VerticalAlignment.Center; check.Visibility=Visibility.Hidden;
   row.Children.Add(check); fontChecks[i]=check;
   var label=new TextBlock { Text=FontNames[i], FontSize=FontSizes[i], Foreground=Palette.Text, VerticalAlignment=VerticalAlignment.Center };
   Grid.SetColumn(label,1); row.Children.Add(label);
   var b=Ui.Button(row,null); b.Height=34; b.Padding=new Thickness(6,0,10,0); b.Focusable=false; b.HorizontalContentAlignment=HorizontalAlignment.Stretch; int step=i;
   b.Click+=(s,e)=> { fontMenu.IsOpen=false; SetFontStep(step); };
   list.Children.Add(b);
  }
  // “Ctrl+滚轮调节”只是一行说明，不可点；不跟菜单项一起抢焦点。
  list.Children.Add(new TextBlock { Text="Ctrl+滚轮调节", Foreground=Palette.Muted, FontSize=11.5, Margin=new Thickness(8,5,8,4) });
  var card=new Border { Child=list, Background=Palette.Raised, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(10), Margin=new Thickness(12),
   Effect=new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth=4, BlurRadius=18, Opacity=0.35, Color=Colors.Black, Direction=270 } };
  fontMenu=new Popup { Child=card, AllowsTransparency=true, StaysOpen=false, Placement=PlacementMode.Top, PopupAnimation=PopupAnimation.Fade, VerticalOffset=10, HorizontalOffset=-12 };
 }
 void UpdateFontMenuChecks() {
  if(fontChecks==null) return;
  for(int i=0;i<fontChecks.Length;i++) fontChecks[i].Visibility=i==fontStep?Visibility.Visible:Visibility.Hidden;
 }
 void SetFontStep(int step) {
  step=Math.Max(0,Math.Min(FontSizes.Length-1,step));
  if(step==fontStep) return;
  double oldScale=FontScale;
  fontStep=step;
  SaveSettings();
  ApplyFontStep(oldScale);
  UpdateFontMenuChecks();
 }
 void StepFont(int delta) { SetFontStep(fontStep+delta); }
 // 立即重排编辑区、预览和脑图；编辑区尽量保持光标和滚动位置，预览里正在改的那一行仍然保持编辑。
 void ApplyFontStep(double oldScale) {
  if(editor!=null) {
   int caret=editor.CaretIndex, selStart=editor.SelectionStart, selLen=editor.SelectionLength; var scroll=FindScroll(editor); double offset=scroll==null?0:scroll.VerticalOffset;
   editor.FontSize=Scaled(15); editor.SetValue(TextBlock.LineHeightProperty,Scaled(25.0));
   if(selLen>0) editor.Select(selStart,Math.Min(selLen,editor.Text.Length-selStart)); else editor.CaretIndex=Math.Min(caret,editor.Text.Length);
   editor.UpdateLayout();
   if(scroll!=null) scroll.ScrollToVerticalOffset(offset*FontScale/oldScale);
  }
  if(previewing) {
   int block=-1, caret=0; double offset=previewScroll.VerticalOffset;
   if(editingBlock>=0 && editingBlock<blocks.Count) { block=editingBlock; caret=lineBox==null?0:lineBox.CaretIndex; FinishEdit(); }
   RenderPreview();
   previewScroll.UpdateLayout();
   previewScroll.ScrollToVerticalOffset(offset*FontScale/oldScale);
   if(block>=0 && block<blocks.Count) BeginEdit(block,caret);
  }
  else if(mapping) {
   RenderMap(false); if(!mapMoved) FitMap();
  }
 }
 static ScrollViewer FindScroll(DependencyObject root) {
  if(root==null) return null;
  var sv=root as ScrollViewer; if(sv!=null) return sv;
  int n=VisualTreeHelper.GetChildrenCount(root);
  for(int i=0;i<n;i++) { var found=FindScroll(VisualTreeHelper.GetChild(root,i)); if(found!=null) return found; }
  return null;
 }
 void UpdateFontEntry() { if(fontButton!=null) fontButton.Visibility=toolbar.Visibility; }
}
}
