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
 // ——— 格式按钮 ———
 void AddTool(FrameworkElement icon,string tip,string before,string after,string lineKind) {
  var b=Ui.Button(icon,tip); b.Width=32; b.Padding=new Thickness(0); b.Foreground=Palette.Muted; b.Margin=new Thickness(0,0,2,0);
  b.Focusable=false; // 点按钮不抢走输入框的焦点，选区保持不变
  b.Click+=(s,e)=> { var t=ActiveBox(); if(t==null) return; if(lineKind!=null) ToggleLinePrefix(t,lineKind); else Wrap(t,before,after); };
  toolbar.Children.Add(b);
 }
 TextBox ActiveBox() { return previewing?lineBox:(mapping?mapBox:editor); }
 bool Writing { get { return !previewing && !mapping; } } // 在“编辑”模式（源码输入框可见）
 static void Wrap(TextBox t,string before,string after) { int start=t.SelectionStart; string selected=t.SelectedText; t.SelectedText=before+selected+after; t.Focus(); t.Select(start+before.Length,selected.Length); }
 const string ListPrefix=@"(?:[-*+] \[[ xX]\] |[-*+] |\d{1,9}[.)] )";
 static Regex PrefixOf(string kind) {
  if(kind=="quote") return new Regex(@"^(\s*)> ?");
  if(kind=="task") return new Regex(@"^(\s*)[-*+] \[[ xX]\] ");
  if(kind=="bullet") return new Regex(@"^(\s*)[-*+] (?!\[[ xX]\] )");
  return new Regex(@"^(\s*)\d{1,9}[.)] ");
 }
 // 引用 / 列表 / 有序 / 待办：作用于选区覆盖的每一行；选中的行都已是这种格式时再点一次取消。
 static void ToggleLinePrefix(TextBox t,string kind) {
  string text=t.Text; int s=t.SelectionStart, e=s+t.SelectionLength; bool collapsed=t.SelectionLength==0;
  if(e>s && text[e-1]=='\n') e--;
  int ls=s==0?0:text.LastIndexOf('\n',s-1)+1;
  int le=text.IndexOf('\n',e); if(le<0) le=text.Length;
  if(le>ls && text[le-1]=='\r') le--; // 不把 \r\n 拆开选，否则替换后会留下孤立的 \r
  string[] parts=text.Substring(ls,le-ls).Split('\n');
  var has=PrefixOf(kind); bool any=false, all=true;
  foreach(string p in parts) { string core=p.TrimEnd('\r'); if(core.Trim().Length==0) continue; any=true; if(!has.IsMatch(core)) all=false; }
  if(!any) all=false;
  int n=1;
  for(int i=0;i<parts.Length;i++) {
   string core=parts[i].TrimEnd('\r'), cr=parts[i].EndsWith("\r")?"\r":"";
   if(any && core.Trim().Length==0) continue;
   if(all) core=has.Replace(core,"$1",1);
   else if(kind=="quote") { if(!has.IsMatch(core)) { var m=Regex.Match(core,@"^(\s*)(.*)$"); core=m.Groups[1].Value+"> "+m.Groups[2].Value; } }
   else {
    var m=Regex.Match(core,@"^(\s*)"+ListPrefix+"?(.*)$");
    string prefix=kind=="bullet"?"- ":(kind=="task"?"- [ ] ":(n++)+". ");
    core=m.Groups[1].Value+prefix+m.Groups[2].Value;
   }
   parts[i]=core+cr;
  }
  string result=string.Join("\n",parts);
  t.Select(ls,le-ls); t.SelectedText=result; t.Focus();
  if(collapsed) t.Select(ls+result.TrimEnd('\r').Length,0); else t.Select(ls,result.Length);
 }

 // ——— 编辑与预览 ———
 // 切换编辑 / 预览 / 脑图、换笔记时，新内容 150ms 淡入，不再一下子跳过去。
 static void FadeIn(UIElement e) { if(!SystemParameters.ClientAreaAnimation) return; e.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0.3,1,TimeSpan.FromMilliseconds(150)) { EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut } }); }
 void SetPreview(bool on) {
  if(Presenting) ExitPresentation();
  if(mapBox!=null) EndMapEdit(true);
  if(on!=previewing || mapping) FadeIn(on?(UIElement)previewScroll:editor);
  if(editingBlock>=0) FinishEdit();
  // 在“编辑”里改过内容再回到预览，之前记下的撤销步骤就对不上了，清掉。
  if(on && !previewing && editor.Text!=leftPreviewText) ClearHistory();
  if(!on && previewing) { leftPreviewText=editor.Text; ClearSelection(); }
  mapping=false; mapView.Visibility=Visibility.Collapsed; Choose(mapTab,false);
  previewing=on; if(on) RenderPreview();
  previewScroll.Visibility=on?Visibility.Visible:Visibility.Collapsed; editor.Visibility=on?Visibility.Collapsed:Visibility.Visible;
  Choose(editTab,!on); Choose(previewTab,on); UpdateToolbar();
  if(!on && expanded) editor.Focus();
 }
 void UpdateToolbar() { toolbar.Visibility=(Writing || (previewing && editingBlock>=0))?Visibility.Visible:Visibility.Collapsed; mapBar.Visibility=mapping?Visibility.Visible:Visibility.Collapsed; UpdatePresenting(); }
 void LoadLines() {
  string text=editor.Text; eol=text.Contains("\r\n")?"\r\n":"\n";
  lines.Clear(); lines.AddRange(text.Replace("\r\n","\n").Replace('\r','\n').Split('\n'));
 }
 void WriteLines() { WriteLines(null); }
 // group：同一个对象连续改动可以合成一步撤销；null 表示单独一步；NoUndo 不记（比如点末尾时补的空行）。
 void WriteLines(object group) {
  string next=string.Join(eol,lines); if(next==editor.Text) return;
  if((previewing || mapping) && group!=NoUndo) Remember(group);
  editor.Text=next; if(mapping) leftPreviewText=next; // 回到预览时撤销记录还接得上
 }
}
}
