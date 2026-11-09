using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Documents;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using D = System.Drawing;

namespace NotchNotesWindows {
partial class MainWindow {
 // ——— 历史版本 ———
 // 笔记本身仍然只有一份；history 目录里的是只读快照，用来在误删、被别的程序改坏或网盘冲突后找回。
 // 每篇笔记一个固定编号（GUID 短串），编号和当前路径的对应关系记在 history\index.txt。
 internal const int HistoryKeepRecent=30;
 internal const int HistoryKeepDays=30;
 internal const long HistoryMaxBytes=50L*1024*1024;
 internal const int HistoryIntervalMinutes=5;

 internal sealed class HistoryEntry {
  public string Id="";
  public string Path="";
  public DateTime LastSnapshotUtc=DateTime.MinValue;
 }

 internal sealed class HistorySnapshot {
  public string Path="";
  public DateTime Time=DateTime.MinValue; // 本地时间，从文件名读出来
  public int Length=0;
 }

 internal sealed class HistorySaveResult {
  public bool Saved=false;
  public string Path=null;
  public DateTime Time=DateTime.MinValue;
 }

 internal sealed class HistoryFile {
  public string Path="";
  public DateTime Time=DateTime.MinValue;
  public int Suffix=1;
 }

 static readonly Regex HistoryNameRe=new Regex(@"^(\d{8}-\d{6})(?:-(\d+))?\.md$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);

 internal static string HistoryRoot(string dataDir) {
  if(string.IsNullOrEmpty(dataDir)) return null;
  return Path.Combine(dataDir,"history");
 }
 internal static string HistoryFolder(string dataDir,string id) {
  string root=HistoryRoot(dataDir);
  if(root==null || !ValidHistoryId(id)) return null;
  return Path.Combine(root,id);
 }
 internal static string HistoryIndexPath(string dataDir) {
  string root=HistoryRoot(dataDir);
  return root==null?null:Path.Combine(root,"index.txt");
 }
 static string FullPathOf(string path) {
  if(string.IsNullOrEmpty(path)) return null;
  try { return Path.GetFullPath(path); } catch { return null; }
 }
 static bool ValidHistoryId(string id) {
  if(string.IsNullOrEmpty(id)) return false;
  if(id=="." || id=="..") return false;
  return id.IndexOfAny(Path.GetInvalidFileNameChars())<0 && id.IndexOf('\\')<0 && id.IndexOf('/')<0;
 }

 // index.txt 每行“编号<Tab>绝对路径<Tab>最后快照时间（UTC，ISO 8601）”。
 internal static List<HistoryEntry> ReadHistoryIndex(string dataDir) {
  var list=new List<HistoryEntry>();
  try {
   string file=HistoryIndexPath(dataDir);
   if(file==null || !File.Exists(file)) return list;
   foreach(string raw in File.ReadAllLines(file)) {
    if(string.IsNullOrEmpty(raw)) continue;
    string[] parts=raw.Split('\t');
    if(parts.Length<2) continue;
    string id=parts[0].Trim(), path=parts[1].Trim();
    if(!ValidHistoryId(id) || path.Length==0) continue;
    string full=FullPathOf(path);
    if(full==null) continue;
    DateTime last=DateTime.MinValue;
    if(parts.Length>=3) {
     DateTime parsed;
     if(DateTime.TryParse(parts[2].Trim(),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out parsed)) last=parsed;
     if(last!=DateTime.MinValue && last.Kind!=DateTimeKind.Utc) last=last.ToUniversalTime();
    }
    bool duplicate=false;
    foreach(var entry in list) if(entry.Id.Equals(id,StringComparison.OrdinalIgnoreCase) || SamePath(entry.Path,full)) { duplicate=true; break; }
    if(duplicate) continue;
    list.Add(new HistoryEntry { Id=id, Path=full, LastSnapshotUtc=last });
   }
  } catch {}
  return list;
 }

 // index 也走“临时文件 + 替换”的原子写法，失败时静默。
 internal static void WriteHistoryIndex(string dataDir,IEnumerable<HistoryEntry> entries) {
  string root=HistoryRoot(dataDir); string temp=null;
  try {
   if(root==null) return;
   Directory.CreateDirectory(root);
   string target=Path.Combine(root,"index.txt"); temp=Path.Combine(root,"index.tmp");
   var lines=new List<string>();
   var seenId=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   var seenPath=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var entry in entries??new HistoryEntry[0]) {
    if(entry==null || !ValidHistoryId(entry.Id)) continue;
    string full=FullPathOf(entry.Path); if(full==null) continue;
    if(!seenId.Add(entry.Id) || !seenPath.Add(full)) continue;
    string last=entry.LastSnapshotUtc==DateTime.MinValue?"":entry.LastSnapshotUtc.ToUniversalTime().ToString("o",CultureInfo.InvariantCulture);
    lines.Add(entry.Id+"\t"+full+"\t"+last);
   }
   File.WriteAllLines(temp,lines.ToArray(),new UTF8Encoding(false));
   if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
  } catch {
   try { if(temp!=null && File.Exists(temp)) File.Delete(temp); } catch {}
  }
 }

 internal static HistoryEntry FindHistoryEntry(List<HistoryEntry> index,string path) {
  if(index==null) return null;
  foreach(var entry in index) if(SamePath(entry.Path,path)) return entry;
  return null;
 }
 static string NewHistoryId(string dataDir,List<HistoryEntry> index) {
  for(int attempt=0;attempt<1000;attempt++) {
   string id=Guid.NewGuid().ToString("N").Substring(0,8);
   bool clash=false;
   foreach(var entry in index) if(entry.Id.Equals(id,StringComparison.OrdinalIgnoreCase)) { clash=true; break; }
   if(!clash) { string folder=HistoryFolder(dataDir,id); if(folder!=null && Directory.Exists(folder)) clash=true; }
   if(!clash) return id;
  }
  return Guid.NewGuid().ToString("N").Substring(0,12);
 }
 // 同一路径沿用原编号；新路径分配一个短 GUID，并把自己加进 index。
 internal static HistoryEntry EnsureHistoryEntry(List<HistoryEntry> index,string dataDir,string path) {
  if(index==null) return null;
  var existing=FindHistoryEntry(index,path);
  if(existing!=null) return existing;
  string full=FullPathOf(path); if(full==null) return null;
  var entry=new HistoryEntry { Id=NewHistoryId(dataDir,index), Path=full, LastSnapshotUtc=DateTime.MinValue };
  index.Add(entry); return entry;
 }
 // 自动改名后把对应关系改到新路径，旧编号和旧快照都留着。
 internal static bool UpdateHistoryPath(List<HistoryEntry> index,string oldPath,string newPath) {
  if(index==null) return false;
  string full=FullPathOf(newPath); if(full==null) return false;
  var entry=FindHistoryEntry(index,oldPath); if(entry==null) return false;
  if(SamePath(entry.Path,full)) return false;
  var other=FindHistoryEntry(index,full);
  if(other!=null && !ReferenceEquals(other,entry)) { index.Remove(entry); return true; }
  entry.Path=full; return true;
 }
 internal static bool RenameHistoryPath(string dataDir,string oldPath,string newPath) {
  try {
   var index=ReadHistoryIndex(dataDir);
   if(!UpdateHistoryPath(index,oldPath,newPath)) return false;
   WriteHistoryIndex(dataDir,index); return true;
  } catch { return false; }
 }
 // 整个数据文件夹搬家（DotNotes / NotchNotesWindows → BotNotes）时，旧文件夹里的笔记路径跟着换前缀。
 internal static void RewriteHistoryPathPrefix(string dataDir,string oldPrefix,string newPrefix) {
  try {
   string from=FullPathOf(oldPrefix), to=FullPathOf(newPrefix);
   if(from==null || to==null || SamePath(from,to)) return;
   string prefix=from.TrimEnd('\\','/')+Path.DirectorySeparatorChar;
   var index=ReadHistoryIndex(dataDir); bool changed=false;
   foreach(var entry in index) {
    if(entry.Path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) {
     entry.Path=Path.Combine(to,entry.Path.Substring(prefix.Length)); changed=true;
    } else if(SamePath(entry.Path,from)) { entry.Path=to; changed=true; }
   }
   if(!changed) return;
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   for(int i=index.Count-1;i>=0;i--) if(!seen.Add(index[i].Path)) index.RemoveAt(i);
   WriteHistoryIndex(dataDir,index);
  } catch {}
 }
 // 迁移把笔记从旧位置搬到保存位置后，history/index.txt 里的路径跟着改。
 internal static void RewriteHistoryPaths(string dataDir,IDictionary<string,string> moves) {
  if(moves==null || moves.Count==0) return;
  try {
   var index=ReadHistoryIndex(dataDir); if(index.Count==0) return;
   var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var pair in moves) {
    string from=FullPathOf(pair.Key), to=FullPathOf(pair.Value);
    if(from!=null && to!=null) map[from]=to;
   }
   if(map.Count==0) return;
   bool changed=false;
   foreach(var entry in index) {
    string to;
    if(map.TryGetValue(entry.Path,out to) && !SamePath(entry.Path,to)) { entry.Path=to; changed=true; }
   }
   if(!changed) return;
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   for(int i=index.Count-1;i>=0;i--) if(!seen.Add(index[i].Path)) index.RemoveAt(i);
   WriteHistoryIndex(dataDir,index);
  } catch {}
 }

 // 快照文件名：yyyyMMdd-HHmmss.md，同一秒重名依次加 -2、-3。
 internal static string SnapshotFileName(DateTime time,int suffix) {
  string stamp=time.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture);
  return stamp+(suffix>1?"-"+suffix.ToString(CultureInfo.InvariantCulture):"")+".md";
 }
 static DateTime SnapshotTimeOf(string path) {
  var m=HistoryNameRe.Match(Path.GetFileName(path??""));
  if(!m.Success) return DateTime.MinValue;
  DateTime parsed;
  if(!DateTime.TryParseExact(m.Groups[1].Value,"yyyyMMdd-HHmmss",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed)) return DateTime.MinValue;
  return DateTime.SpecifyKind(parsed,DateTimeKind.Local);
 }
 internal static List<HistoryFile> ListHistoryFiles(string folder) {
  var list=new List<HistoryFile>();
  try {
   if(string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return list;
   foreach(string path in Directory.GetFiles(folder,"*.md")) {
    var m=HistoryNameRe.Match(Path.GetFileName(path));
    if(!m.Success) continue;
    DateTime time=SnapshotTimeOf(path);
    if(time==DateTime.MinValue) continue;
    int suffix=1;
    if(m.Groups[2].Success) int.TryParse(m.Groups[2].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out suffix);
    if(suffix<1) suffix=1;
    list.Add(new HistoryFile { Path=path, Time=time, Suffix=suffix });
   }
  } catch {}
  list.Sort((a,b)=> {
   int c=b.Time.CompareTo(a.Time); if(c!=0) return c;
   c=b.Suffix.CompareTo(a.Suffix); if(c!=0) return c;
   return string.Compare(a.Path,b.Path,StringComparison.OrdinalIgnoreCase);
  });
  return list;
 }
 static HistoryFile LatestHistoryFile(string folder) {
  var files=ListHistoryFiles(folder);
  return files.Count==0?null:files[0];
 }
 static bool LatestSnapshotMatches(string folder,string text) {
  var latest=LatestHistoryFile(folder);
  if(latest==null) return false;
  try { return string.Equals(File.ReadAllText(latest.Path),text??"",StringComparison.Ordinal); } catch { return false; }
 }
 // 写一份快照；与上一份完全相同（force=false）时不写。返回新文件路径，未写返回 null。
 internal static string WriteHistorySnapshotFile(string folder,string text,DateTime time,bool force) {
  try {
   if(string.IsNullOrEmpty(folder)) return null;
   text=text??"";
   if(!force && LatestSnapshotMatches(folder,text)) return null;
   Directory.CreateDirectory(folder);
   DateTime stampTime=time.Kind==DateTimeKind.Utc?time.ToLocalTime():time;
   string path=null;
   for(int suffix=1;;suffix++) {
    path=Path.Combine(folder,SnapshotFileName(stampTime,suffix));
    if(!File.Exists(path)) break;
   }
   WriteTextFile(path,text,new UTF8Encoding(true),true);
   return path;
  } catch { return null; }
 }

 // 存一份历史快照并更新 index；失败静默。force=true 时即使和上一份相同也写（恢复前用）。
 internal static HistorySaveResult SaveHistorySnapshot(string dataDir,string notePath,string text,DateTime now,bool force=false) {
  var result=new HistorySaveResult();
  try {
   string full=FullPathOf(notePath);
   if(full==null || HistoryRoot(dataDir)==null) return result;
   DateTime localNow=now.Kind==DateTimeKind.Utc?now.ToLocalTime():now;
   var index=ReadHistoryIndex(dataDir);
   var entry=FindHistoryEntry(index,full);
   if(entry==null) { entry=EnsureHistoryEntry(index,dataDir,full); if(entry==null) return result; }
   string folder=HistoryFolder(dataDir,entry.Id);
   string file=WriteHistorySnapshotFile(folder,text,localNow,force);
   if(file==null) {
    // 没写（内容没变或写失败）。如果只是 index 里的时间落后了，顺手补上，计时快照才准确。
    var latest=LatestHistoryFile(folder);
    if(latest!=null) {
     DateTime utc=latest.Time.ToUniversalTime();
     if(utc>entry.LastSnapshotUtc) { entry.LastSnapshotUtc=utc; WriteHistoryIndex(dataDir,index); }
    }
    return result;
   }
   entry.LastSnapshotUtc=localNow.ToUniversalTime();
   WriteHistoryIndex(dataDir,index);
   CleanupHistoryForNote(dataDir,entry.Id,localNow);
   result.Saved=true; result.Path=file; result.Time=SnapshotTimeOf(file);
   if(result.Time==DateTime.MinValue) result.Time=localNow;
   return result;
  } catch { return result; }
 }

 // 距离上一份快照满 5 分钟、且内容和最新快照不同，下一次自动保存时存一份。
 internal static bool ShouldTakeTimedSnapshot(string dataDir,string notePath,string currentText,DateTime now,int minutes=HistoryIntervalMinutes) {
  try {
   var index=ReadHistoryIndex(dataDir);
   var entry=FindHistoryEntry(index,notePath);
   if(entry==null || entry.LastSnapshotUtc==DateTime.MinValue) return false;
   DateTime utc=now.Kind==DateTimeKind.Utc?now:now.ToUniversalTime();
   if(utc<entry.LastSnapshotUtc) return false;
   if((utc-entry.LastSnapshotUtc).TotalMinutes<minutes) return false;
   var latest=LatestHistoryFile(HistoryFolder(dataDir,entry.Id));
   if(latest==null) return false;
   try { return !string.Equals(File.ReadAllText(latest.Path),currentText??"",StringComparison.Ordinal); } catch { return true; }
  } catch { return false; }
 }

 // 一次编辑减少超过一半，且减少超过 200 个字符。
 internal static bool ShouldSnapshotBigShrink(string previous,string current) {
  if(previous==null) return false;
  long before=previous.Length, after=(current??"").Length, removed=before-after;
  return removed>200 && removed*2L>before;
 }

 // 列出某篇笔记的快照，最新在上。
 internal static List<HistorySnapshot> ListHistorySnapshots(string dataDir,string notePath) {
  var list=new List<HistorySnapshot>();
  try {
   var index=ReadHistoryIndex(dataDir);
   var entry=FindHistoryEntry(index,notePath);
   if(entry==null) return list;
   foreach(var file in ListHistoryFiles(HistoryFolder(dataDir,entry.Id))) {
    int length=0;
    try { length=File.ReadAllText(file.Path).Length; } catch {}
    list.Add(new HistorySnapshot { Path=file.Path, Time=file.Time, Length=length });
   }
  } catch {}
  return list;
 }

 internal static string HistoryTimeLabel(DateTime time,DateTime now) {
  string hm=time.ToString("HH:mm",CultureInfo.InvariantCulture);
  if(time.Date==now.Date) return "今天 "+hm;
  if(time.Date==now.Date.AddDays(-1)) return "昨天 "+hm;
  if(time.Year==now.Year) return time.Month.ToString(CultureInfo.InvariantCulture)+"月"+time.Day.ToString(CultureInfo.InvariantCulture)+"日 "+hm;
  return time.Year.ToString(CultureInfo.InvariantCulture)+"年"+time.Month.ToString(CultureInfo.InvariantCulture)+"月"+time.Day.ToString(CultureInfo.InvariantCulture)+"日 "+hm;
 }
 internal static string FormatHistoryCharCount(int count) {
  return count.ToString("N0",CultureInfo.InvariantCulture)+" 字";
 }

 static bool TryDeleteHistoryFile(string path) {
  try { if(File.Exists(path)) File.Delete(path); return true; } catch { return false; }
 }
 static void TryDeleteHistoryFolder(string path) {
  try { if(!string.IsNullOrEmpty(path) && Directory.Exists(path)) Directory.Delete(path,true); } catch {}
 }

 // 每篇保留最近 30 份，另外 30 天内每天保留当天最后一份；其余删除。
 internal static void CleanupHistoryForNote(string dataDir,string id,DateTime now,int keepRecent=HistoryKeepRecent,int keepDays=HistoryKeepDays) {
  try {
   string folder=HistoryFolder(dataDir,id); if(folder==null || !Directory.Exists(folder)) return;
   var files=ListHistoryFiles(folder); if(files.Count==0) return;
   DateTime localNow=now.Kind==DateTimeKind.Utc?now.ToLocalTime():now;
   if(keepRecent<1) keepRecent=1;
   var keep=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   for(int i=0;i<files.Count && i<keepRecent;i++) keep.Add(files[i].Path);
   if(keepDays>0) {
    var daySeen=new HashSet<DateTime>();
    foreach(var file in files) {
     int days=(localNow.Date-file.Time.Date).Days;
     if(days<0 || days>=keepDays) continue;
     if(daySeen.Add(file.Time.Date)) keep.Add(file.Path);
    }
   }
   foreach(var file in files) if(!keep.Contains(file.Path)) TryDeleteHistoryFile(file.Path);
  } catch {}
 }

 // 启动时清理全部：每篇的保留规则、对应文件已不存在且最后快照超过 30 天的整组、以及 50 MB 总量上限。
 internal static void CleanupHistoryAll(string dataDir,DateTime now,long maxBytes=HistoryMaxBytes,int keepRecent=HistoryKeepRecent,int keepDays=HistoryKeepDays) {
  try {
   string root=HistoryRoot(dataDir); if(root==null) return;
   DateTime localNow=now.Kind==DateTimeKind.Utc?now.ToLocalTime():now;
   DateTime utcNow=localNow.ToUniversalTime();
   var index=ReadHistoryIndex(dataDir);
   bool changed=false;
   var cleaned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var entry in index) if(cleaned.Add(entry.Id)) CleanupHistoryForNote(dataDir,entry.Id,localNow,keepRecent,keepDays);

   // 对应文件被删掉或移走，且最后一份快照超过 30 天：整个编号文件夹删除，index 里也去掉。
   for(int i=index.Count-1;i>=0;i--) {
    var entry=index[i];
    if(File.Exists(entry.Path)) continue;
    DateTime last=entry.LastSnapshotUtc;
    var files=ListHistoryFiles(HistoryFolder(dataDir,entry.Id));
    if(files.Count>0) last=files[0].Time.ToUniversalTime();
    if(last==DateTime.MinValue || (utcNow-last).TotalDays>keepDays) {
     TryDeleteHistoryFolder(HistoryFolder(dataDir,entry.Id));
     index.RemoveAt(i); changed=true;
    }
   }

   // 总量超过上限时，从全局最旧的快照开始删，直到不超；每篇至少留最新 1 份。
   var all=new List<HistoryFile>();
   try { if(Directory.Exists(root)) foreach(string folder in Directory.GetDirectories(root)) all.AddRange(ListHistoryFiles(folder)); } catch {}
   long total=0;
   foreach(var file in all) try { total+=new FileInfo(file.Path).Length; } catch {}
   if(total>maxBytes) {
    all.Sort((a,b)=> {
     int c=a.Time.CompareTo(b.Time); if(c!=0) return c;
     c=a.Suffix.CompareTo(b.Suffix); if(c!=0) return c;
     return string.Compare(a.Path,b.Path,StringComparison.OrdinalIgnoreCase);
    });
    var counts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    foreach(var file in all) {
     string folder=Path.GetDirectoryName(file.Path); int count;
     counts.TryGetValue(folder,out count); counts[folder]=count+1;
    }
    foreach(var file in all) {
     if(total<=maxBytes) break;
     string folder=Path.GetDirectoryName(file.Path); int count;
     if(!counts.TryGetValue(folder,out count) || count<=1) continue;
     long size=0; try { size=new FileInfo(file.Path).Length; } catch {}
     if(TryDeleteHistoryFile(file.Path)) { counts[folder]=count-1; total-=size; }
    }
   }

   // 清掉空文件夹；index 里指向已不存在文件夹的条目也去掉。
   try { if(Directory.Exists(root)) foreach(string folder in Directory.GetDirectories(root)) { if(Directory.GetFileSystemEntries(folder).Length==0) Directory.Delete(folder); } } catch {}
   for(int i=index.Count-1;i>=0;i--) {
    string folder=HistoryFolder(dataDir,index[i].Id);
    if(folder==null || !Directory.Exists(folder)) { index.RemoveAt(i); changed=true; }
   }
   if(changed) WriteHistoryIndex(dataDir,index);
  } catch {}
 }

 // ——— 历史版本面板 ———
 Border historyView; bool historyOpen=false;
 TextBlock historyTitle, historyEmpty;
 StackPanel historyListPanel;
 TextBox historyPreview;
 Button historyRestore, historyCloseButton;
 Grid historyBody;
 readonly List<HistorySnapshot> historySnapshots=new List<HistorySnapshot>();
 int historySelected=-1;
 Popup tabMenu; Button tabMenuItem; string tabMenuNote=null;

 Border BuildHistoryView() {
  var view=new Border { Background=Palette.Panel, Visibility=Visibility.Collapsed };
  var card=new Border { Background=Palette.Surface, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(10), Padding=new Thickness(18,16,18,14) };
  view.Child=card;
  var dock=new DockPanel(); card.Child=dock;

  var footer=new DockPanel { LastChildFill=true, Margin=new Thickness(0,12,0,0) }; DockPanel.SetDock(footer,Dock.Bottom); dock.Children.Add(footer);
  historyRestore=Ui.Button("恢复这个版本","把这个版本放回编辑区；恢复前会先保存当前内容"); historyRestore.Padding=new Thickness(16,0,16,0); historyRestore.FontWeight=FontWeights.SemiBold; historyRestore.Background=Palette.AccentBg; historyRestore.Foreground=Palette.Accent; historyRestore.IsEnabled=false; historyRestore.Click+=(s,e)=>RestoreHistorySnapshot(); DockPanel.SetDock(historyRestore,Dock.Left); footer.Children.Add(historyRestore);
  var hint=new TextBlock { Text="恢复前会先保存当前内容，恢复错了可以再找回来", Foreground=Palette.Muted, FontSize=12, TextWrapping=TextWrapping.Wrap, LineHeight=18, Margin=new Thickness(12,0,0,0), VerticalAlignment=VerticalAlignment.Center }; footer.Children.Add(hint);

  var header=new DockPanel { LastChildFill=true, Margin=new Thickness(0,0,0,12) }; DockPanel.SetDock(header,Dock.Top); dock.Children.Add(header);
  var close=Ui.Button(Ui.Icon(G_Close,10),"关闭（Esc）"); close.Width=30; close.Height=30; close.Padding=new Thickness(0); close.Foreground=Palette.Muted; close.Click+=(s,e)=>CloseHistory(); DockPanel.SetDock(close,Dock.Right); header.Children.Add(close); historyCloseButton=close;
  historyTitle=new TextBlock { Text="历史版本", Foreground=Palette.Text, FontSize=16, FontWeight=FontWeights.SemiBold, TextTrimming=TextTrimming.CharacterEllipsis, VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(0,0,8,0) }; header.Children.Add(historyTitle);

  var body=new Grid(); dock.Children.Add(body);
  historyBody=new Grid(); body.Children.Add(historyBody);
  historyBody.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(210) });
  historyBody.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
  var listScroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, FocusVisualStyle=null, Margin=new Thickness(0,0,12,0) };
  historyListPanel=new StackPanel(); listScroll.Content=historyListPanel; historyBody.Children.Add(listScroll);
  var previewBorder=new Border { Background=Palette.Panel, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(8), Padding=new Thickness(2) };
  historyPreview=new TextBox { IsReadOnly=true, AcceptsReturn=true, TextWrapping=TextWrapping.Wrap, Background=Brushes.Transparent, Foreground=Palette.Text, CaretBrush=Palette.Text, SelectionBrush=Palette.Accent, SelectionOpacity=0.35, BorderThickness=new Thickness(0), Padding=new Thickness(10,8,10,10), VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, IsUndoEnabled=false, FocusVisualStyle=null, Cursor=Cursors.IBeam };
  historyPreview.SetValue(TextBlock.LineHeightProperty,Scaled(25.0));
  previewBorder.Child=historyPreview; Grid.SetColumn(previewBorder,1); historyBody.Children.Add(previewBorder);
  historyEmpty=new TextBlock { Text="还没有历史版本", Foreground=Palette.Muted, FontSize=13, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, Visibility=Visibility.Collapsed }; body.Children.Add(historyEmpty);
  return view;
 }

 void BuildTabMenu() {
  var list=new StackPanel { Margin=new Thickness(4) };
  var label=new TextBlock { Text="历史版本…", Foreground=Palette.Text, FontSize=13, VerticalAlignment=VerticalAlignment.Center };
  var item=Ui.Button(label,null); item.Width=176; item.Height=34; item.Padding=new Thickness(10,0,10,0); item.HorizontalContentAlignment=HorizontalAlignment.Stretch; item.Focusable=false;
  item.Click+=(s,e)=> { tabMenu.IsOpen=false; string path=tabMenuNote; if(!string.IsNullOrEmpty(path) && path!=DraftKey) OpenHistory(path); };
  tabMenuItem=item; list.Children.Add(item);
  var card=new Border { Child=list, Background=Palette.Raised, BorderBrush=Palette.Line, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(10), Margin=new Thickness(12),
   Effect=new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth=4, BlurRadius=18, Opacity=0.35, Color=Colors.Black, Direction=270 } };
  tabMenu=new Popup { Child=card, AllowsTransparency=true, StaysOpen=false, Placement=PlacementMode.MousePoint, PopupAnimation=PopupAnimation.Fade, VerticalOffset=4, HorizontalOffset=4 };
 }
 void ShowTabMenu(Button anchor,string path) {
  if(tabMenu==null) BuildTabMenu();
  tabMenuNote=path;
  bool enabled=path!=DraftKey;
  tabMenuItem.IsEnabled=enabled;
  tabMenuItem.ToolTip=enabled?null:"空白草稿还没有历史版本";
  tabMenu.PlacementTarget=anchor;
  tabMenu.IsOpen=true;
 }
 void OpenHistory(string path) {
  if(path==DraftKey) return;
  if(path!=currentNote) { SwitchNote(path); if(currentNote!=path) return; }
  if(mapBox!=null) EndMapEdit(true);
  if(editingBlock>=0) { FinishEdit(); if(previewing) RenderPreview(); }
  CloseSettings();
  if(confirmOpen) HideConfirm();
  historyOpen=true;
  historyTitle.Text="历史版本 · "+TitleOf(currentNote==DraftKey?"":NoteTextOf(currentNote));
  historySnapshots.Clear(); historySnapshots.AddRange(ListHistorySnapshots(dir,currentNote));
  historySelected=historySnapshots.Count>0?0:-1;
  historyView.Visibility=Visibility.Visible;
  RefreshHistoryPanel();
  if(historySelected>=0 && historyPreview.Focus()) return;
  if(historyCloseButton!=null) historyCloseButton.Focus();
 }
 void CloseHistory() {
  if(!historyOpen) return;
  historyOpen=false; historyView.Visibility=Visibility.Collapsed;
  historySnapshots.Clear(); historySelected=-1;
  if(historyListPanel!=null) historyListPanel.Children.Clear();
  if(historyPreview!=null) historyPreview.Text="";
  if(expanded && Writing && !settingsOpen) editor.Focus();
 }
 void RefreshHistoryPanel() {
  if(historyListPanel==null) return;
  historyPreview.FontSize=Scaled(15); historyPreview.SetValue(TextBlock.LineHeightProperty,Scaled(25.0));
  historyListPanel.Children.Clear();
  bool any=historySnapshots.Count>0;
  historyBody.Visibility=any?Visibility.Visible:Visibility.Collapsed;
  historyEmpty.Visibility=any?Visibility.Collapsed:Visibility.Visible;
  historyRestore.IsEnabled=any && historySelected>=0;
  for(int i=0;i<historySnapshots.Count;i++) {
   var snap=historySnapshots[i]; bool on=i==historySelected;
   var row=new Grid();
   row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
   row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var time=new TextBlock { Text=HistoryTimeLabel(snap.Time,DateTime.Now), Foreground=on?Palette.Accent:Palette.Text, FontSize=12, FontWeight=on?FontWeights.SemiBold:FontWeights.Normal, VerticalAlignment=VerticalAlignment.Center, TextTrimming=TextTrimming.CharacterEllipsis };
   var count=new TextBlock { Text=FormatHistoryCharCount(snap.Length), Foreground=Palette.Muted, FontSize=10.5, Margin=new Thickness(6,0,0,0), VerticalAlignment=VerticalAlignment.Center };
   row.Children.Add(time); Grid.SetColumn(count,1); row.Children.Add(count);
   var b=Ui.Button(row,null); b.Height=32; b.Padding=new Thickness(6,0,6,0); b.Margin=new Thickness(0,0,0,2); b.HorizontalContentAlignment=HorizontalAlignment.Stretch; b.Background=on?Palette.AccentBg:Brushes.Transparent; int index=i;
   b.Click+=(s,e)=>SelectHistorySnapshot(index);
   historyListPanel.Children.Add(b);
  }
  UpdateHistoryPreview();
 }
 void SelectHistorySnapshot(int index) {
  if(index<0 || index>=historySnapshots.Count) return;
  historySelected=index; RefreshHistoryPanel();
 }
 void UpdateHistoryPreview() {
  if(historySelected<0 || historySelected>=historySnapshots.Count) { historyPreview.Text=""; return; }
  try { historyPreview.Text=File.ReadAllText(historySnapshots[historySelected].Path); }
  catch { historyPreview.Text="无法读取这个版本"; }
  try { historyPreview.CaretIndex=0; historyPreview.ScrollToHome(); } catch {}
 }
 void RestoreHistorySnapshot() {
  if(historySelected<0 || historySelected>=historySnapshots.Count || currentNote==DraftKey) return;
  var snap=historySnapshots[historySelected];
  string text;
  try { text=File.ReadAllText(snap.Path); } catch { return; }
  if(mapBox!=null) EndMapEdit(true);
  // 预览里正在改的那一行先并回正文，再把当前内容存一份（即使和上一份相同也存），确保能退回；存不下就不恢复。
  if(editingBlock>=0) { FinishEdit(); if(previewing) RenderPreview(); }
  if(!SaveHistorySnapshot(dir,currentNote,editor.Text,DateTime.Now,true).Saved) { Msg("没能先保存当前内容，这次没有恢复。请检查磁盘后再试。"); return; }
  loading=true; editor.Text=text; loading=false;
  ClearHistory();
  if(previewing) RenderPreview(); else if(mapping) RenderMap(true);
  bool ok=SaveNote(true);
  RefreshTabs();
  CloseHistory();
  if(previewing) RenderPreview(); else if(mapping) RenderMap(true); else if(expanded) editor.Focus();
  if(ok) SetStatus("已恢复到 "+HistoryTimeLabel(snap.Time,DateTime.Now),Palette.Accent);
 }

 // 在打开、切换、收起、关闭、退出等时机挂快照；草稿不记。
 void SnapshotCurrentNote(bool force=false) {
  if(currentNote==DraftKey) return;
  try { SaveHistorySnapshot(dir,currentNote,editor.Text,DateTime.Now,force); } catch {}
 }
 void SnapshotCurrentIfTimed() {
  if(currentNote==DraftKey) return;
  try {
   if(ShouldTakeTimedSnapshot(dir,currentNote,editor.Text,DateTime.Now,HistoryIntervalMinutes))
    SaveHistorySnapshot(dir,currentNote,editor.Text,DateTime.Now,false);
  } catch {}
 }
}
}
