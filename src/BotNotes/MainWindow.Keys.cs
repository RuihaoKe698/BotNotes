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
 // ——— 快捷键 ———
 void OnKey(object sender,KeyEventArgs e) {
  var key=e.Key==Key.System?e.SystemKey:e.Key; bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0, shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
  if(expanded) transient=false;
  if(closeOpen) {
   if(key==Key.Escape) { HideCloseDialog(); e.Handled=true; return; }
   if(key==Key.Enter) { ConfirmClose("archive"); e.Handled=true; return; }
  }
  if(key==Key.F5 && expanded && mapping && !settingsOpen && !closeOpen) { TogglePresentation(); e.Handled=true; return; }
  if(key==Key.F11 && expanded) { SetFullscreen(!fullscreen); e.Handled=true; return; }
  if(key==Key.Escape) {
   if(Presenting) ExitPresentation();
   else if(settingsOpen) CloseSettings();
   else if(mapBox!=null) EndMapEdit(false);
   else if(mapping && !Presenting && MapSel()!=null) { mapSelLine=NoSel; RenderMap(false); }
   else if(previewing && selFrom>=0) ClearSelection();
   else if(fullscreen && editingBlock<0) SetFullscreen(false);
   else if(editingBlock>=0) { FinishEdit(); RenderPreview(); }
   else Collapse();
   e.Handled=true; return;
  }
  if(!ctrl && expanded && previewing && selFrom>=0 && editingBlock<0 && !settingsOpen && !closeOpen && (key==Key.Delete || key==Key.Back)) { DeleteSelection(); e.Handled=true; return; }
  if(!ctrl || !expanded) return;
  if(!mapping && !settingsOpen && !closeOpen && !shift) { // Ctrl+1/2/3 标题，Ctrl+0 正文（脑图里 Ctrl+0 是适应窗口）
   int lv=key==Key.D1 || key==Key.NumPad1?1:key==Key.D2 || key==Key.NumPad2?2:key==Key.D3 || key==Key.NumPad3?3:key==Key.D0 || key==Key.NumPad0?0:-1;
   if(lv>=0) { ApplyHeading(lv); e.Handled=true; return; }
  }
  // 字号：Ctrl+= / Ctrl+-（含小键盘）切换一档，到头不再变；脑图里不响应，Ctrl+0 仍归标题 / 适应窗口。
  if(!mapping && !settingsOpen && !closeOpen && (key==Key.OemPlus || key==Key.Add || key==Key.OemMinus || key==Key.Subtract)) {
   StepFont(key==Key.OemPlus || key==Key.Add?1:-1); e.Handled=true; return;
  }
  // 预览里的撤销、重做、全选、复制；正在改的那一行里有选中文字时，Ctrl+C 仍按输入框自己的复制。
  if(key==Key.M) { CloseSettings(); if(mapping) SetPreview(mapFromPreview); else SetMap(); e.Handled=true; return; }
  if(mapping && !settingsOpen) {
   if(key==Key.D0 || key==Key.NumPad0) { mapMoved=false; FitMap(); e.Handled=true; return; }
   if(key==Key.OemPlus || key==Key.Add) { ZoomMap(1.2,null); e.Handled=true; return; }
   if(key==Key.OemMinus || key==Key.Subtract) { ZoomMap(1/1.2,null); e.Handled=true; return; }
  }
  if(mapping && mapBox==null && !settingsOpen && !closeOpen && (key==Key.Z || key==Key.Y)) { MapUndo(key==Key.Y || shift); e.Handled=true; return; }
  if(previewing && !settingsOpen && !closeOpen) {
   if(key==Key.Z || key==Key.Y) { PreviewUndo(key==Key.Y || shift); e.Handled=true; return; }
   if(key==Key.A && editingBlock<0) { SelectAllBlocks(); e.Handled=true; return; }
   if(key==Key.C && selFrom>=0) { CopySelection(shift); e.Handled=true; return; }
   if(key==Key.X && selFrom>=0) { CutSelection(); e.Handled=true; return; }
  }
  var target=ActiveBox();
  if(key==Key.B && target!=null) { Wrap(target,"**","**"); e.Handled=true; }
  else if(key==Key.I && target!=null) { Wrap(target,"*","*"); e.Handled=true; }
  else if(key==Key.N) { NewNote(); e.Handled=true; }
  else if(key==Key.E) { CloseSettings(); SetPreview(!previewing); e.Handled=true; }
  else if(key==Key.Tab) { CycleNote(shift?-1:1); e.Handled=true; }
  else if(key==Key.W) { CloseNote(currentNote); e.Handled=true; }
  else if(key==Key.O) { CloseSettings(); OpenFiles(); e.Handled=true; }
 }
 // Ctrl+滚轮：只接在编辑区和预览上；脑图自己的滚轮缩放不受影响。到头时也吃掉滚轮，免得正文跟着滚动。
 void OnFontWheel(object sender,MouseWheelEventArgs e) {
  if(!expanded || mapping || settingsOpen || closeOpen) return;
  if((Keyboard.Modifiers&ModifierKeys.Control)==0 || e.Delta==0) return;
  StepFont(e.Delta>0?1:-1); e.Handled=true;
 }

}
}
