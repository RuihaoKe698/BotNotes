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
 // ——— 开机自动启动 ———
 // 写在当前用户的启动项（HKCU\...\Run）里，不需要管理员权限。任务管理器“启动应用”里禁用的，记在 StartupApproved 下，这里也认。
 internal static string RunName="BotNotes";
 internal static string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run", ApprovedKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
 internal static string RunCommand() { return "\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\""; }
 internal static bool AutoStartOn() {
  try {
   using(var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey)) if(k==null || k.GetValue(RunName)==null) return false;
   using(var a=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKey)) { var v=a==null?null:a.GetValue(RunName) as byte[]; if(v!=null && v.Length>0 && (v[0]&1)==1) return false; } // 单数表示已在任务管理器里禁用
   return true;
  } catch { return false; }
 }
 internal static string SetAutoStartValue(bool on) {
  try {
   using(var k=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)) { if(on) k.SetValue(RunName,RunCommand()); else k.DeleteValue(RunName,false); }
   using(var a=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKey,true)) if(a!=null) a.DeleteValue(RunName,false); // 清掉任务管理器里的“已禁用”，免得开了也不生效
   return null;
  } catch(Exception ex) { return ex.Message; }
 }
 void SetAutoStart(bool on) {
  string error=SetAutoStartValue(on);
  if(error!=null) Msg("无法更改开机自动启动："+error);
  UpdateSettingsView();
 }
 // 程序文件挪了位置后，启动项跟着指向新位置。只在真正启动程序时调用。
 internal static bool AutoStartPathChanged(string stored,string current) { return !SamePath((stored??"").Trim().Trim('"'),(current??"").Trim().Trim('"')); }
 internal static void FixAutoStartPath() {
  if(!AutoStartOn()) return;
  try { using(var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey,true)) { if(k==null) return; var v=k.GetValue(RunName) as string; if(v!=null && AutoStartPathChanged(v,RunCommand())) k.SetValue(RunName,RunCommand()); } } catch {}
 }
 void OpenSettings() { if(editingBlock>=0) { FinishEdit(); RenderPreview(); } settingsOpen=true; UpdateSettingsView(); settingsView.Visibility=Visibility.Visible; PreviewBreathe(); settingsButton.Background=Palette.Raised; settingsButton.Foreground=Palette.Text; }
 void CloseSettings() { if(!settingsOpen) return; settingsOpen=false; PreviewBreathe(); settingsView.Visibility=Visibility.Collapsed; settingsButton.Background=Brushes.Transparent; settingsButton.Foreground=Palette.Muted; if(expanded && Writing) editor.Focus(); }
 void PickArchive() {
  modal++;
  try {
   using(var d=new Forms.FolderBrowserDialog()) {
    d.Description="选择归档文件夹"; d.ShowNewFolderButton=true;
    try { if(Directory.Exists(ArchiveDir())) d.SelectedPath=ArchiveDir(); } catch {}
    if(d.ShowDialog(new Win32Owner(new System.Windows.Interop.WindowInteropHelper(this).Handle))!=Forms.DialogResult.OK || string.IsNullOrEmpty(d.SelectedPath)) return;
    if(SamePath(d.SelectedPath,dir)) { Msg("不能选笔记所在的文件夹，否则归档的笔记会重新出现在标签里。请换一个文件夹。"); return; }
    archiveDir=SamePath(d.SelectedPath,Path.Combine(dir,"archive"))?null:d.SelectedPath; SaveSettings(); UpdateSettingsView();
   }
  } finally { modal--; }
 }
 void OpenArchive() { try { string p=ArchiveDir(); Directory.CreateDirectory(p); System.Diagnostics.Process.Start("explorer.exe","\""+p+"\""); } catch(Exception ex) { Msg("无法打开归档文件夹："+ex.Message); } }

}
}
