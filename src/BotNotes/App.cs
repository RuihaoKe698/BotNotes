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
class App : Application {
 [STAThread] static void Main() {
  bool first;
  using(var mutex=new System.Threading.Mutex(true, @"Local\BotNotesSingleInstance", out first)) {
   if(!first) { NotchNotesWindows.MainWindow.Notice("BotNotes 已在运行，点屏幕顶部的小圆点或通知区域图标就能打开。"); return; }
   // 旧版（DotNotes 或更早的 NotchNotes）还开着时不能搬数据文件夹，否则它之后保存的内容会写回旧文件夹
   string oldName=NotchNotesWindows.MainWindow.OldVersionRunning(); if(oldName!=null) { NotchNotesWindows.MainWindow.Notice("旧版 "+oldName+" 还在运行。请先在通知区域右键它的图标选“退出”，再打开 BotNotes。笔记会自动搬过来。"); return; }
   NotchNotesWindows.MainWindow.MigrateOldVersions();
   var migration=NotchNotesWindows.MainWindow.MigrateStorage();
   NotchNotesWindows.MainWindow.FixAutoStartPath();
   var app = new App(); app.ShutdownMode=ShutdownMode.OnExplicitShutdown; CrashLog.Install(app);
   if(migration!=null && migration.Message!=null) NotchNotesWindows.MainWindow.Notice(migration.Message);
   app.Run(new MainWindow());
  }
 }
}

}
