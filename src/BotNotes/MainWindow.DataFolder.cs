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
 readonly string dir=DataDir();
 // 数据放在 %LOCALAPPDATA%\BotNotes。改名历史：NotchNotesWindows → DotNotes → BotNotes。
 // 第一次启动时把最近的旧文件夹整个改名过来；改名失败（比如文件被占用）就继续用旧文件夹，笔记不会看起来“丢了”。
 internal static string LocalRoot=null; // 测试时换成临时位置
 internal static string[] OldDataNames={"DotNotes","NotchNotesWindows"}, OldRunNames={"DotNotes","NotchNotes"}, OldMutexes={@"Local\DotNotesSingleInstance",@"Local\NotchNotesWindowsSingleInstance"}, OldAppNames={"DotNotes","NotchNotes"};
 static string AppData(string name) { return Path.Combine(LocalRoot??Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),name); }
 static bool EmptyOrMissing(string d) { try { return !Directory.Exists(d) || Directory.GetFileSystemEntries(d).Length==0; } catch { return false; } }
 internal static string DataDir() { string now=AppData("BotNotes"); if(!EmptyOrMissing(now)) return now; foreach(var name in OldDataNames) { string old=AppData(name); if(Directory.Exists(old) && !EmptyOrMissing(old)) return old; } return now; }
 // 返回还在运行的旧版名字；都没在运行时返回 null。
 internal static string OldVersionRunning() { for(int i=0;i<OldMutexes.Length;i++) { try { using(System.Threading.Mutex.OpenExisting(OldMutexes[i])) return OldAppNames[i]; } catch {} } return null; }
 internal static void MigrateOldVersions() {
  string now=AppData("BotNotes");
  try {
   foreach(var name in OldDataNames) {
    string old=AppData(name); if(!Directory.Exists(old) || EmptyOrMissing(old)) continue;
    if(Directory.Exists(now) && EmptyOrMissing(now)) Directory.Delete(now); // 新文件夹是空的（没真正用过）就当不存在
    if(!Directory.Exists(now)) { Directory.Move(old,now); RewriteHistoryPathPrefix(now,old,now); }
    break; // 只搬最近的那一个
   }
  } catch {}
  MigrateOldAutoStart();
 }
 // 开机启动项：旧名字的删掉；只要有一条原来是开着的（没在任务管理器里禁用），就用新名字重新登记
 internal static void MigrateOldAutoStart() {
  try {
   using(var k=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey,true)) {
    if(k==null) return; bool found=false, enabled=false;
    foreach(var oldRun in OldRunNames) {
     if(k.GetValue(oldRun)==null) continue; found=true; bool disabled=false;
     using(var a=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(ApprovedKey,true)) if(a!=null) { var v=a.GetValue(oldRun) as byte[]; disabled=v!=null && v.Length>0 && (v[0]&1)==1; a.DeleteValue(oldRun,false); }
     k.DeleteValue(oldRun,false); if(!disabled) enabled=true;
    }
    if(found && enabled) k.SetValue(RunName,RunCommand());
   }
  } catch {}
 }
 // 旧数据文件夹搬走后，旧设置里指向它里面的归档位置 / 保存位置跟着改
 internal static string RewriteArchiveDir(string archiveDir,string newRoot) {
  foreach(var name in OldDataNames) { string oldRoot=AppData(name);
   if(archiveDir!=null && !Directory.Exists(oldRoot) && archiveDir.StartsWith(oldRoot+"\\",StringComparison.OrdinalIgnoreCase)) archiveDir=newRoot+archiveDir.Substring(oldRoot.Length); }
  return archiveDir;
 }
}
}
