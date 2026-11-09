using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace NotchNotesWindows {
partial class MainWindow {
 // ——— 笔记存储 ———
 // 标签本身就是文件路径；空白草稿用空字符串表示，不碰磁盘。
 internal const string DraftKey="";
 // 测试时把“文档”指到临时目录，避免碰到用户真实的文档文件夹。
 internal static string DocumentsRoot=null;

 internal static string DefaultSaveDir() {
  string docs=DocumentsRoot;
  if(string.IsNullOrEmpty(docs)) docs=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
  if(string.IsNullOrEmpty(docs)) docs=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
  return Path.Combine(docs,"BotNotes");
 }

 // 标题取法沿用原来的规则：去掉 Markdown 符号，非法文件名字符换成空格，合并空白。
 // 文件名最多 40 个字；没有标题时叫“未命名”。
 internal static string TitleOf(string text) {
  string t="";
  foreach(string raw in (text??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
   t=raw.Trim();
   t=Regex.Replace(t,@"^#{1,6}\s*",""); t=Regex.Replace(t,@"^(>\s*)+","");
   t=Regex.Replace(t,@"^([-*+]\s+(\[[ xX]\]\s+)?|\d{1,9}[.)]\s+)","");
   t=Regex.Replace(t,@"\[([^\]]+)\]\([^)]*\)","$1"); t=Regex.Replace(t,@"(\*\*|__|~~|`|\*)","");
   t=t.Trim(); if(t.Length>0) break;
  }
  foreach(char c in Path.GetInvalidFileNameChars()) t=t.Replace(c,' ');
  t=Regex.Replace(t,@"\s+"," ").Trim().TrimEnd('.',' ');
  if(t.Length>40) t=t.Substring(0,40).TrimEnd('.',' ');
  return t.Length==0?"未命名":t;
 }

 // 标题到文件名。保留扩展名，重名时由 UniqueFile 加“ (2)”。
 internal static string NoteFileName(string title,string extension=".md") {
  string t=(title??"").Trim();
  foreach(char c in Path.GetInvalidFileNameChars()) t=t.Replace(c,' ');
  t=Regex.Replace(t,@"\s+"," ").Trim().TrimEnd('.',' ');
  if(t.Length>40) t=t.Substring(0,40).TrimEnd('.',' ');
  if(t.Length==0) t="未命名";
  if(string.IsNullOrEmpty(extension)) extension=".md";
  if(extension[0]!='.') extension="."+extension;
  return t+extension;
 }
 internal static string AddNameSuffix(string fileName,int n) {
  if(n<2) return fileName;
  return Path.GetFileNameWithoutExtension(fileName)+" ("+n+")"+Path.GetExtension(fileName);
 }
 static bool SameFile(string a,string b) { return b!=null && SamePath(a,b); }
 internal static string UniqueFile(string folder,string fileName,string excludePath=null) {
  string candidate=Path.Combine(folder,fileName);
  if((!File.Exists(candidate) && !Directory.Exists(candidate)) || SameFile(candidate,excludePath)) return candidate;
  for(int n=2;;n++) {
   candidate=Path.Combine(folder,AddNameSuffix(fileName,n));
   if((!File.Exists(candidate) && !Directory.Exists(candidate)) || SameFile(candidate,excludePath)) return candidate;
  }
 }
 // 新建草稿落盘时用“未命名.md”“未命名 2.md”……（和标题重名的“ (2)”不同）。
 internal static string NewDraftPath(string folder) {
  for(int n=1;;n++) {
   string name=n==1?"未命名.md":"未命名 "+n+".md";
   string path=Path.Combine(folder,name);
   if(!File.Exists(path) && !Directory.Exists(path)) return path;
  }
 }
 // 只给保存位置顶层、标题真的变了的文件改名；别处的文件永远不动。
 internal static bool ShouldRename(string path,string title,string namedTitle,string saveDir) {
  if(string.IsNullOrEmpty(path) || string.IsNullOrEmpty(title) || path==DraftKey) return false;
  if(title==namedTitle) return false;
  try { return SamePath(Path.GetDirectoryName(path),saveDir); } catch { return false; }
 }

 // ——— 编码 ———
 internal sealed class NoteFileState {
  public string Path="";
  public Encoding Encoding=new UTF8Encoding(true);
  public bool Bom=true;
  public DateTime LastWriteUtc=DateTime.MinValue;
  public long Length=-1;
  public string LastSavedText="";
  public string NamedTitle="未命名";
  public bool Readable=true;
 }
 // 读成文字：有 BOM 按 BOM；否则先按严格 UTF-8，不是合法 UTF-8 就按系统默认编码（中文 Windows 为 GBK）。
 // 能打开返回 null，否则返回原因。
 internal static string ReadTextFile(string path,out string text,out Encoding encoding,out bool bom) {
  text=null; encoding=new UTF8Encoding(false,true); bom=false;
  try {
   if(new FileInfo(path).Length>5*1024*1024) return "文件超过 5 MB";
   byte[] bytes=File.ReadAllBytes(path);
   int skip=0;
   if(bytes.Length>=3 && bytes[0]==0xEF && bytes[1]==0xBB && bytes[2]==0xBF) { encoding=new UTF8Encoding(true,true); bom=true; skip=3; }
   else if(bytes.Length>=4 && bytes[0]==0xFF && bytes[1]==0xFE && bytes[2]==0 && bytes[3]==0) { encoding=new UTF32Encoding(false,true,true); bom=true; skip=4; }
   else if(bytes.Length>=4 && bytes[0]==0 && bytes[1]==0 && bytes[2]==0xFE && bytes[3]==0xFF) { encoding=new UTF32Encoding(true,true,true); bom=true; skip=4; }
   else if(bytes.Length>=2 && bytes[0]==0xFF && bytes[1]==0xFE) { encoding=new UnicodeEncoding(false,true,true); bom=true; skip=2; }
   else if(bytes.Length>=2 && bytes[0]==0xFE && bytes[1]==0xFF) { encoding=new UnicodeEncoding(true,true,true); bom=true; skip=2; }
   try { text=encoding.GetString(bytes,skip,bytes.Length-skip); }
   catch(DecoderFallbackException) {
    encoding=Encoding.Default;
    if(encoding.CodePage!=936) { try { encoding=Encoding.GetEncoding(936); } catch {} } // 中文 Windows 的系统默认就是 GBK
    bom=false; text=encoding.GetString(bytes);
   }
   if(text.IndexOf('\0')>=0) { text=null; return "看起来不是文本文件"; }
   return null;
  } catch(Exception ex) { text=null; return ex.Message; }
 }
 // 当前编码能不能不丢字符地表示整篇文字。Unicode 编码不受影响；GBK / ASCII 等遇到 emoji、生僻字会返回 false。
 internal static bool CanEncodeLosslessly(string text,Encoding encoding) {
  if(encoding==null) return false;
  switch(encoding.CodePage) { case 65001: case 1200: case 1201: case 12000: case 12001: return true; }
  try {
   var strict=(Encoding)encoding.Clone();
   strict.EncoderFallback=EncoderFallback.ExceptionFallback;
   strict.DecoderFallback=DecoderFallback.ExceptionFallback;
   strict.GetBytes(text??"");
   return true;
  } catch(EncoderFallbackException) { return false; }
  catch { return false; }
 }
 // 不能无损表示时改存 UTF-8 带 BOM；返回是否切换了编码。
 internal static bool EnsureWritableEncoding(string text,ref Encoding encoding,ref bool bom) {
  if(CanEncodeLosslessly(text,encoding)) return false;
  encoding=new UTF8Encoding(true); bom=true; return true;
 }
 // 写文件用“同目录隐藏临时文件 + File.Replace”的原子写法，失败时清理临时文件。
 internal static void WriteTextFile(string path,string text,Encoding encoding,bool bom) {
  if(encoding==null) encoding=new UTF8Encoding(false);
  string folder=Path.GetDirectoryName(path);
  if(!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
  string temp=Path.Combine(string.IsNullOrEmpty(folder)?".":folder,".~"+Guid.NewGuid().ToString("N")+".tmp");
  try {
   using(var fs=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
    if(bom) { byte[] pre=encoding.GetPreamble(); if(pre!=null && pre.Length>0) fs.Write(pre,0,pre.Length); }
    byte[] data=encoding.GetBytes(text??""); fs.Write(data,0,data.Length);
   }
   try { File.SetAttributes(temp,File.GetAttributes(temp)|FileAttributes.Hidden); } catch {}
   if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
   try { File.SetAttributes(path,File.GetAttributes(path)&~FileAttributes.Hidden); } catch {}
  } finally {
   try { if(File.Exists(temp)) File.Delete(temp); } catch {}
  }
 }

 // ——— order.txt / folds.txt ———
 internal static bool IsLegacyNoteName(string name) {
  return Regex.IsMatch((name??"").Trim(),@"^note(_\d+)?\.md$",RegexOptions.IgnoreCase);
 }
 // 新格式每行一个绝对路径；旧格式的 note.md / note_N.md 按数据文件夹换成绝对路径。
 internal static List<string> ParseOrderPaths(string[] lines,string dataDir) {
  var list=new List<string>(); var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(string raw in lines??new string[0]) {
   string line=(raw??"").Trim().Trim('"');
   if(line.Length==0) continue;
   string path=null;
   if(IsLegacyNoteName(line)) { try { path=Path.GetFullPath(Path.Combine(dataDir,line)); } catch {} }
   else if(Path.IsPathRooted(line)) { try { path=Path.GetFullPath(line); } catch {} }
   if(path!=null && seen.Add(path)) list.Add(path);
  }
  return list;
 }
 internal static string[] SerializeOrderPaths(IEnumerable<string> paths) {
  var list=new List<string>(); var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(string path in paths??new string[0]) {
   if(string.IsNullOrEmpty(path)) continue;
   string full; try { full=Path.GetFullPath(path); } catch { continue; }
   if(seen.Add(full)) list.Add(full);
  }
  return list.ToArray();
 }
 internal static void SaveOrderFile(string dataDir,string[] paths) {
  try {
   string target=Path.Combine(dataDir,"order.txt"), temp=Path.Combine(dataDir,"order.tmp");
   Directory.CreateDirectory(dataDir);
   File.WriteAllLines(temp,paths??new string[0],new UTF8Encoding(false));
   if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
  } catch {}
 }
 // folds 的键也按路径记；旧文件里的 note.md / note_N.md 读时换成绝对路径。
 internal static List<KeyValuePair<string,string>> ParseFoldLines(string[] lines,string dataDir) {
  var list=new List<KeyValuePair<string,string>>();
  foreach(string raw in lines??new string[0]) {
   int t=raw.IndexOf('\t'); if(t<=0) continue;
   string key=raw.Substring(0,t).Trim(); if(key.Length==0) continue;
   if(IsLegacyNoteName(key)) key=Path.GetFullPath(Path.Combine(dataDir,key));
   try { list.Add(new KeyValuePair<string,string>(key,Encoding.UTF8.GetString(Convert.FromBase64String(raw.Substring(t+1))))); } catch {}
  }
  return list;
 }
 internal static void SaveFoldFile(string dataDir,List<KeyValuePair<string,string>> entries) {
  try {
   var lines=new List<string>();
   foreach(var e in entries) lines.Add(e.Key+"\t"+Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Value)));
   string target=Path.Combine(dataDir,"folds.txt"), temp=Path.Combine(dataDir,"folds.tmp");
   Directory.CreateDirectory(dataDir);
   File.WriteAllLines(temp,lines.ToArray(),new UTF8Encoding(false));
   if(File.Exists(target)) File.Replace(temp,target,null); else File.Move(temp,target);
  } catch {}
 }

 // ——— 一次性迁移 ———
 internal sealed class StorageMigrationResult {
  public bool Ran=false, BackupFailed=false;
  public string BackupDir=null, Error=null;
  public readonly List<string> Failures=new List<string>();
  public readonly List<string> TabPaths=new List<string>();
  public string Message {
   get {
    var lines=new List<string>();
    if(BackupFailed) lines.Add("迁移前的备份失败，没有移动任何笔记："+Error);
    if(Failures.Count>0) lines.Add("以下文件没有迁移成功，仍留在原处：\n"+string.Join("\n",Failures.ToArray()));
    return lines.Count==0?null:string.Join("\n\n",lines.ToArray());
   }
  }
 }
 internal static StorageMigrationResult MigrateStorage() {
  string dataDir=DataDir(), saveDir=DefaultSaveDir();
  try {
   string f=Path.Combine(dataDir,"settings.ini");
   if(File.Exists(f)) {
    var s=ParseSettings(File.ReadAllLines(f),dataDir);
    if(!string.IsNullOrEmpty(s.SaveDir)) saveDir=s.SaveDir;
   }
  } catch {}
  return MigrateStorage(dataDir,saveDir);
 }
 internal static StorageMigrationResult MigrateStorage(string dataDir,string saveDir) {
  var result=new StorageMigrationResult();
  try {
   string settingsFile=Path.Combine(dataDir,"settings.ini");
   string[] settingsLines=File.Exists(settingsFile)?File.ReadAllLines(settingsFile):new string[0];
   if(HasStorage2(settingsLines)) return result;
   var noteFiles=LegacyNoteFiles(dataDir);
   if(noteFiles.Count==0) return result;
   result.Ran=true;
   try { result.BackupDir=CreateMigrationBackup(dataDir); }
   catch(Exception ex) { result.BackupFailed=true; result.Error=ex.Message; return result; }

   Directory.CreateDirectory(saveDir);
   var oldOrder=new List<string>();
   try { if(File.Exists(Path.Combine(dataDir,"order.txt"))) foreach(string raw in File.ReadAllLines(Path.Combine(dataDir,"order.txt"))) { string n=raw.Trim(); if(IsLegacyNoteName(n) && !oldOrder.Contains(n,StringComparer.OrdinalIgnoreCase)) oldOrder.Add(n); } } catch {}
   var ordered=new List<string>();
   foreach(string name in oldOrder) if(noteFiles.Contains(name,StringComparer.OrdinalIgnoreCase) && !ordered.Contains(name,StringComparer.OrdinalIgnoreCase)) ordered.Add(name);
   foreach(string name in noteFiles) if(!ordered.Contains(name,StringComparer.OrdinalIgnoreCase)) ordered.Add(name);

   var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   var pathMoves=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(string name in ordered) {
    string source=Path.Combine(dataDir,name);
    string text, error=ReadTextFile(source,out text,out var encoding,out var bom);
    if(error!=null || text==null) { result.Failures.Add(source+"："+(error??"无法读取")); map[name]=source; result.TabPaths.Add(source); continue; }
    if(text.Trim().Length==0) {
     try { File.Delete(source); } catch(Exception ex) { result.Failures.Add(name+"："+ex.Message); map[name]=source; result.TabPaths.Add(source); }
     continue;
    }
    string dest;
    try {
     dest=UniqueFile(saveDir,NoteFileName(TitleOf(text)));
     File.Move(source,dest);
    } catch(Exception ex) {
     result.Failures.Add(source+"："+ex.Message); map[name]=source; result.TabPaths.Add(source); continue;
    }
    map[name]=dest; result.TabPaths.Add(dest); pathMoves[source]=dest;
   }

   // 默认归档文件夹里的文件原样搬到保存位置；自定义归档位置就是保存位置时不用动。
   string archive=Path.Combine(dataDir,"archive");
   if(Directory.Exists(archive) && !SamePath(archive,saveDir)) {
    foreach(string file in Directory.GetFiles(archive)) {
     try {
      string name=Path.GetFileName(file);
      string dest=UniqueFile(saveDir,name);
      File.Move(file,dest);
      pathMoves[file]=dest;
     } catch(Exception ex) { result.Failures.Add(file+"："+ex.Message); }
    }
    try { if(Directory.GetFileSystemEntries(archive).Length==0) Directory.Delete(archive); } catch {}
   }

   SaveOrderFile(dataDir,SerializeOrderPaths(result.TabPaths));

   string foldsFile=Path.Combine(dataDir,"folds.txt");
   if(File.Exists(foldsFile)) {
    var output=new List<KeyValuePair<string,string>>();
    foreach(var e in ParseFoldLines(File.ReadAllLines(foldsFile),dataDir)) {
     string key=e.Key;
     if(IsLegacyNoteName(key)) {
      string mapped; if(map.TryGetValue(key,out mapped)) key=mapped; else continue;
     } else if(IsLegacyNoteName(Path.GetFileName(key)) && SamePath(Path.GetDirectoryName(key),dataDir)) {
      // ParseFoldLines 已经把旧文件名变成了数据文件夹下的路径。
      string mapped; if(map.TryGetValue(Path.GetFileName(key),out mapped)) key=mapped; else continue;
     }
     output.Add(new KeyValuePair<string,string>(key,e.Value));
    }
    SaveFoldFile(dataDir,output);
   }
   RewriteHistoryPaths(dataDir,pathMoves);
   WriteStorageMarker(dataDir);
   return result;
  } catch(Exception ex) {
   result.Failures.Add(ex.Message);
   return result;
  }
 }
 internal static bool HasStorage2(string[] lines) {
  foreach(string raw in lines??new string[0]) {
   int i=raw.IndexOf('='); if(i<=0) continue;
   if(raw.Substring(0,i).Trim()=="storage" && raw.Substring(i+1).Trim()=="2") return true;
  }
  return false;
 }
 internal static void WriteStorageMarker(string dataDir) {
  try {
   string file=Path.Combine(dataDir,"settings.ini"); Directory.CreateDirectory(dataDir);
   var lines=File.Exists(file)?new List<string>(File.ReadAllLines(file)):new List<string>();
   bool found=false;
   for(int i=0;i<lines.Count;i++) { int eq=lines[i].IndexOf('='); if(eq>0 && lines[i].Substring(0,eq).Trim()=="storage") { lines[i]="storage=2"; found=true; } }
   if(!found) lines.Add("storage=2");
   string temp=Path.Combine(dataDir,"settings.tmp");
   File.WriteAllLines(temp,lines.ToArray(),Encoding.UTF8);
   try { if(File.Exists(file)) File.Replace(temp,file,null); else File.Move(temp,file); }
   catch { File.WriteAllLines(file,lines.ToArray(),Encoding.UTF8); try { if(File.Exists(temp)) File.Delete(temp); } catch {} }
  } catch {}
 }
 internal static List<string> LegacyNoteFiles(string dataDir) {
  var list=new List<string>();
  try {
   foreach(string path in Directory.GetFiles(dataDir,"*.md")) {
    string name=Path.GetFileName(path);
    if(IsLegacyNoteName(name)) list.Add(name);
   }
  } catch {}
  list.Sort((a,b)=> {
   if(a.Equals("note.md",StringComparison.OrdinalIgnoreCase)) return -1;
   if(b.Equals("note.md",StringComparison.OrdinalIgnoreCase)) return 1;
   return StringComparer.OrdinalIgnoreCase.Compare(a,b);
  });
  return list;
 }
 internal static string CreateMigrationBackup(string dataDir) {
  string stem="backup-迁移前-"+DateTime.Now.ToString("yyyyMMdd");
  string dest=Path.Combine(dataDir,stem);
  for(int n=2;Directory.Exists(dest)||File.Exists(dest);n++) dest=Path.Combine(dataDir,stem+"_"+n);
  try { CopyFolder(dataDir,dest,dest); }
  catch { try { if(Directory.Exists(dest)) Directory.Delete(dest,true); } catch {} throw; }
  return dest;
 }
 static void CopyFolder(string source,string dest,string backupRoot) {
  Directory.CreateDirectory(dest);
  foreach(string dir in Directory.GetDirectories(source)) {
   string name=Path.GetFileName(dir);
   if(name.StartsWith("backup-",StringComparison.OrdinalIgnoreCase) || SamePath(dir,backupRoot)) continue;
   CopyFolder(dir,Path.Combine(dest,name),backupRoot);
  }
  foreach(string file in Directory.GetFiles(source)) {
   if(SamePath(Path.GetDirectoryName(file),backupRoot)) continue;
   File.Copy(file,Path.Combine(dest,Path.GetFileName(file)),true);
  }
 }
}
}
