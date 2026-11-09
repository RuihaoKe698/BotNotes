using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace NotchNotesWindows {
// 出错记录：程序遇到没处理的错误时，把错误写进数据文件夹的 logs\crash.log，告诉用户文件在哪，然后退出。
// 笔记本来就在打字停顿 0.5 秒后自动保存，这里不再额外保存，免得在出错的状态下写坏笔记。
static class CrashLog {
 internal const string FileName="crash.log", OldFileName="crash.old.log";
 internal static long MaxBytes=1024*1024; // 超过 1 MB 时旧记录改名为 crash.old.log，只留这一份
 static bool reporting;

 internal static string Version() {
  var a=typeof(CrashLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
  return a!=null?a.InformationalVersion:typeof(CrashLog).Assembly.GetName().Version.ToString();
 }

 internal static string Describe(Exception ex,string source,DateTime now) {
  var sb=new StringBuilder();
  sb.AppendLine("==== "+now.ToString("yyyy-MM-dd HH:mm:ss",System.Globalization.CultureInfo.InvariantCulture)+" ====");
  sb.AppendLine("BotNotes "+Version());
  sb.AppendLine("Windows "+Environment.OSVersion.Version+(Environment.Is64BitOperatingSystem?" 64-bit":" 32-bit")+", process "+(Environment.Is64BitProcess?"64-bit":"32-bit"));
  sb.AppendLine(".NET CLR "+Environment.Version);
  sb.AppendLine("Source: "+source);
  sb.AppendLine(ex==null?"(no exception object)":ex.ToString());
  sb.AppendLine();
  return sb.ToString();
 }

 // 依次试每个文件夹，写进第一个能写的；返回日志文件路径，都写不进时返回 null。
 internal static string Write(Exception ex,string source,DateTime now,params string[] folders) {
  string text=Describe(ex,source,now);
  foreach(var folder in folders) {
   try {
    Directory.CreateDirectory(folder);
    string path=Path.Combine(folder,FileName);
    if(File.Exists(path) && new FileInfo(path).Length>MaxBytes) { string old=Path.Combine(folder,OldFileName); if(File.Exists(old)) File.Delete(old); File.Move(path,old); }
    File.AppendAllText(path,text,new UTF8Encoding(true));
    return path;
   } catch {}
  }
  return null;
 }

 static string[] Folders() {
  string data=null; try { data=Path.Combine(MainWindow.DataDir(),"logs"); } catch {}
  string temp=Path.Combine(Path.GetTempPath(),"BotNotes","logs");
  return data==null?new[] { temp }:new[] { data, temp };
 }

 internal static void Install(Application app) {
  AppDomain.CurrentDomain.UnhandledException+=(s,e)=> { Write(e.ExceptionObject as Exception,"background thread",DateTime.Now,Folders()); RestoreLidOnce(); RestoreCleanOnce(); };
  app.DispatcherUnhandledException+=(s,e)=> { e.Handled=true; Fail(e.Exception,"UI thread"); };
  Forms.Application.ThreadException+=(s,e)=>Fail(e.Exception,"tray"); // 通知区域图标和它的菜单走 WinForms 的消息处理
 }

 // 崩溃时尽力把合盖设置恢复一次；只试一次，失败也不影响写日志和退出。
 static bool lidRestoreTried;
 static void RestoreLidOnce() {
  if(lidRestoreTried) return;
  lidRestoreTried=true;
  try { KeepAwake.RestoreFromSettings(MainWindow.DataDir()); } catch {}
 }

 // 崩溃时也尽力恢复干净桌面（图标、壁纸、背景色）；同样只试一次。
 static bool cleanRestoreTried;
 static void RestoreCleanOnce() {
  if(cleanRestoreTried) return;
  cleanRestoreTried=true;
  try { CleanDesktop.RestoreFromSettings(MainWindow.DataDir()); } catch {}
 }

 static void Fail(Exception ex,string source) {
  string path=Write(ex,source,DateTime.Now,Folders());
  RestoreLidOnce(); RestoreCleanOnce();
  if(reporting) Environment.Exit(1); // 显示提示时又出错：不再弹窗，直接退出
  reporting=true;
  try {
   MainWindow.Notice("BotNotes 遇到问题，需要关闭。\n\n已保存的笔记不受影响，出错前一两秒内的输入可能没来得及保存。重新打开 BotNotes 就能继续使用。"
    +(path!=null?"\n\n出错记录：\n"+path+"\n反馈问题时请附上这个文件。":""));
  } catch {}
  try { var w=Application.Current==null?null:Application.Current.MainWindow as MainWindow; if(w!=null) w.HideTray(); } catch {}
  Environment.Exit(1);
 }
}
}
