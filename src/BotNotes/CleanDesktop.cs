using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace NotchNotesWindows {
 // “干净桌面”的桌面读写入口。测试里换成内存假实现，绝不碰真实桌面图标、壁纸和背景色。
 internal interface IDesktopState {
  bool TryGetIconsVisible(out bool visible);
  bool SetIconsVisible(bool visible);
  bool TryGetBackgroundColor(out uint color); // 0xRRGGBB
  bool TryGetWallpaperEnabled(out bool enabled);
  bool SetBackgroundColor(uint color); // 0xRRGGBB
  bool SetWallpaperEnabled(bool enabled);
  // 桌面整理类软件（如腾讯桌面整理）挂在桌面里、自己画图标的窗口；按窗口类名识别，只返回当前可见的。
  bool TryGetOverlayLayers(out string[] classNames);
  bool SetOverlayLayersVisible(string[] classNames,bool visible);
 }

 // 真实的桌面实现：桌面图标走 SHELLDLL_DefView 的“显示桌面图标”命令，背景走 IDesktopWallpaper。
 // 所有 Win32 / COM 异常都在这里吞掉：失败只意味着桌面的这一部分没做成，开关本身照常工作。
 internal sealed class NativeDesktopState : IDesktopState {
  const uint WM_COMMAND=0x0111, ToggleDesktopIcons=0x7402, SPI_GETDESKWALLPAPER=0x0073;
  static readonly Guid ClsidDesktopWallpaper=new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD");

  delegate bool EnumWindowsProc(IntPtr hWnd,IntPtr lParam);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string windowName);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr childAfter,string className,string windowName);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback,IntPtr lParam);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd,StringBuilder name,int maxCount);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SystemParametersInfo(uint action,uint param,[Out] StringBuilder data,uint winIni);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,EnumWindowsProc callback,IntPtr lParam);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd,uint flags);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd,out uint processId);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd,int command);
  const uint GA_PARENT=1; const int SW_HIDE=0, SW_SHOWNA=8;

  [StructLayout(LayoutKind.Sequential)] struct DesktopRect { public int Left, Top, Right, Bottom; }
  enum DesktopWallpaperPosition { Center=0, Tile=1, Stretch=2, Fit=3, Fill=4, Span=5 }
  enum DesktopSlideshowOptions { Shuffle=0 }
  enum DesktopSlideshowDirection { Forward=0, Backward=1 }
  [Flags] enum DesktopSlideshowState { Enabled=1, Slideshow=2, DisabledByRemoteSession=4 }

  // IDesktopWallpaper（shobjidl_core.h）的完整 vtable：Enable 在最后，前面的槽位不能省略。
  [ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IDesktopWallpaper {
   void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID,[MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
   [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
   [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
   uint GetMonitorDevicePathCount();
   DesktopRect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
   void SetBackgroundColor(uint color);
   uint GetBackgroundColor();
   void SetPosition(DesktopWallpaperPosition position);
   DesktopWallpaperPosition GetPosition();
   void SetSlideshow(IntPtr items);
   IntPtr GetSlideshow();
   void SetSlideshowOptions(DesktopSlideshowOptions options,uint slideshowTick);
   void GetSlideshowOptions(out DesktopSlideshowOptions options,out uint slideshowTick);
   void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID,DesktopSlideshowDirection direction);
   DesktopSlideshowState GetStatus();
   void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
  }

  // 桌面的 SHELLDLL_DefView：Win10/11 通常在 Progman 下；资源管理器重绘桌面后也可能挂在某个 WorkerW 下。
  static IntPtr FindDefView() {
   IntPtr progman=FindWindow("Progman",null);
   if(progman!=IntPtr.Zero) {
    IntPtr view=FindWindowEx(progman,IntPtr.Zero,"SHELLDLL_DefView",null);
    if(view!=IntPtr.Zero) return view;
   }
   IntPtr found=IntPtr.Zero;
   try {
    EnumWindows((h,l)=> {
     var name=new StringBuilder(32);
     if(GetClassName(h,name,name.Capacity)<=0 || name.ToString()!="WorkerW") return true;
     IntPtr view=FindWindowEx(h,IntPtr.Zero,"SHELLDLL_DefView",null);
     if(view==IntPtr.Zero) return true;
     found=view; return false;
    },IntPtr.Zero);
   } catch {}
   return found;
  }

  static IDesktopWallpaper CreateWallpaper() {
   try {
    Type type=Type.GetTypeFromCLSID(ClsidDesktopWallpaper);
    if(type==null) return null;
    return (IDesktopWallpaper)Activator.CreateInstance(type);
   } catch { return null; }
  }

  static uint ToRgb(uint colorRef) { // COLORREF 0x00BBGGRR → 0xRRGGBB
   return ((colorRef&0xFF)<<16)|(colorRef&0xFF00)|((colorRef>>16)&0xFF);
  }
  static uint ToColorRef(uint rgb) { // 0xRRGGBB → COLORREF 0x00BBGGRR
   return ((rgb&0xFF)<<16)|(rgb&0xFF00)|((rgb>>16)&0xFF);
  }

  public bool TryGetIconsVisible(out bool visible) {
   visible=false;
   try {
    IntPtr view=FindDefView(); if(view==IntPtr.Zero) return false;
    IntPtr list=FindWindowEx(view,IntPtr.Zero,"SysListView32","FolderView");
    if(list==IntPtr.Zero) list=FindWindowEx(view,IntPtr.Zero,"SysListView32",null);
    if(list==IntPtr.Zero) return false;
    visible=IsWindowVisible(list); return true;
   } catch { return false; }
  }

  public bool SetIconsVisible(bool visible) {
   try {
    bool now; if(!TryGetIconsVisible(out now)) return false;
    if(now==visible) return true;
    IntPtr view=FindDefView(); if(view==IntPtr.Zero) return false;
    SendMessage(view,WM_COMMAND,(IntPtr)ToggleDesktopIcons,IntPtr.Zero); // 等同右键“查看 → 显示桌面图标”
    return true;
   } catch { return false; }
  }

  static string ClassOf(IntPtr h) {
   var name=new StringBuilder(256);
   return GetClassName(h,name,name.Capacity)>0?name.ToString():"";
  }
  static uint ProcessOf(IntPtr h) { uint pid; GetWindowThreadProcessId(h,out pid); return pid; }

  // 挂在桌面（放 SHELLDLL_DefView 的 Progman / WorkerW）里、属于资源管理器以外进程的最外层窗口。
  // 跳过 WorkerW 里面的窗口：那是动态壁纸软件放壁纸的地方，不能当成图标层藏掉。
  static List<IntPtr> FindOverlayWindows() {
   var result=new List<IntPtr>();
   IntPtr view=FindDefView(); if(view==IntPtr.Zero) return result;
   IntPtr host=GetAncestor(view,GA_PARENT); if(host==IntPtr.Zero) return result;
   uint shell=ProcessOf(view);
   EnumChildWindows(host,(h,l)=> {
    if(ProcessOf(h)==shell) return true;
    bool keep=true;
    for(IntPtr p=GetAncestor(h,GA_PARENT); p!=IntPtr.Zero && p!=host; p=GetAncestor(p,GA_PARENT)) {
     if(ClassOf(p)=="WorkerW" || ProcessOf(p)!=shell) { keep=false; break; }
    }
    if(keep) result.Add(h);
    return true;
   },IntPtr.Zero);
   return result;
  }

  public bool TryGetOverlayLayers(out string[] classNames) {
   classNames=new string[0];
   try {
    var names=new List<string>();
    foreach(IntPtr h in FindOverlayWindows()) {
     if(!IsWindowVisible(h)) continue;
     string name=ClassOf(h);
     if(name.Length>0 && !names.Contains(name)) names.Add(name);
    }
    classNames=names.ToArray(); return true;
   } catch { return false; }
  }

  public bool SetOverlayLayersVisible(string[] classNames,bool visible) {
   try {
    if(classNames==null || classNames.Length==0) return true;
    foreach(IntPtr h in FindOverlayWindows()) {
     if(Array.IndexOf(classNames,ClassOf(h))<0 || IsWindowVisible(h)==visible) continue;
     ShowWindow(h,visible?SW_SHOWNA:SW_HIDE);
    }
    return true; // 那个软件已经退出、找不到窗口时也算恢复完成
   } catch { return false; }
  }

  public bool TryGetBackgroundColor(out uint color) {
   color=0;
   try {
    IDesktopWallpaper wallpaper=CreateWallpaper(); if(wallpaper==null) return false;
    color=ToRgb(wallpaper.GetBackgroundColor()); return true;
   } catch { return false; }
  }

  public bool TryGetWallpaperEnabled(out bool enabled) {
   enabled=false;
   try {
    var path=new StringBuilder(260); // 文档保证不超过 MAX_PATH
    if(!SystemParametersInfo(SPI_GETDESKWALLPAPER,(uint)path.Capacity,path,0)) return false;
    enabled=path.Length>0; // 没有壁纸时返回空串
    return true;
   } catch { return false; }
  }

  public bool SetBackgroundColor(uint color) {
   try {
    IDesktopWallpaper wallpaper=CreateWallpaper(); if(wallpaper==null) return false;
    wallpaper.SetBackgroundColor(ToColorRef(color)); return true;
   } catch { return false; }
  }

  public bool SetWallpaperEnabled(bool enabled) {
   try {
    IDesktopWallpaper wallpaper=CreateWallpaper(); if(wallpaper==null) return false;
    wallpaper.Enable(enabled); // S_FALSE（本来就是这个状态）不算失败
    return true;
   } catch { return false; }
  }
 }

 // settings.ini 里的 cleanRestore=icons:1,bg:RRGGBB,wall:1,layers:类名|类名；只写这次动到的类别，字段可缺省；壁纸原来关着时记 wall:0。
 internal sealed class CleanRestoreRecord {
  internal bool HasIcons, IconsVisible;
  internal bool HasBackground; internal uint BackgroundColor; // 0xRRGGBB
  internal bool HasWallpaper, WallpaperEnabled;
  internal string[] Layers; // 被收起的桌面整理类窗口的类名；null 表示没动

  internal bool HasLayers { get { return Layers!=null && Layers.Length>0; } }

  // 类名里不能有记录用的分隔符，有的话这个窗口就不动它。
  internal static bool IsStorableClassName(string name) {
   return !string.IsNullOrEmpty(name) && name.IndexOfAny(new[] { ',',':','|','\r','\n' })<0 && name.Trim()==name;
  }

  internal static bool TryParse(string text,out CleanRestoreRecord record) {
   record=null;
   if(string.IsNullOrWhiteSpace(text)) return false;
   var parsed=new CleanRestoreRecord(); bool any=false;
   foreach(var part in text.Split(',')) {
    int i=part.IndexOf(':'); if(i<=0) continue;
    string key=part.Substring(0,i).Trim(), value=part.Substring(i+1).Trim();
    if(string.Equals(key,"icons",StringComparison.OrdinalIgnoreCase)) {
     if(value=="0" || value=="1") { parsed.HasIcons=true; parsed.IconsVisible=value=="1"; any=true; }
    } else if(string.Equals(key,"bg",StringComparison.OrdinalIgnoreCase)) {
     uint color;
     if(value.Length==6 && uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out color)) { parsed.HasBackground=true; parsed.BackgroundColor=color; any=true; }
    } else if(string.Equals(key,"wall",StringComparison.OrdinalIgnoreCase)) {
     if(value=="0" || value=="1") { parsed.HasWallpaper=true; parsed.WallpaperEnabled=value=="1"; any=true; }
    } else if(string.Equals(key,"layers",StringComparison.OrdinalIgnoreCase)) {
     var names=new List<string>();
     foreach(var name in value.Split('|')) if(IsStorableClassName(name) && !names.Contains(name)) names.Add(name);
     if(names.Count>0) { parsed.Layers=names.ToArray(); any=true; }
    }
   }
   if(!any) return false;
   record=parsed; return true;
  }

  public override string ToString() {
   var parts=new List<string>();
   if(HasIcons) parts.Add("icons:"+(IconsVisible?"1":"0"));
   if(HasBackground) parts.Add("bg:"+BackgroundColor.ToString("X6",CultureInfo.InvariantCulture));
   if(HasWallpaper) parts.Add("wall:"+(WallpaperEnabled?"1":"0"));
   if(HasLayers) parts.Add("layers:"+string.Join("|",Layers));
   return string.Join(",",parts.ToArray());
  }
 }

 // 干净桌面的开关逻辑：开启时先记下原图标可见性、背景色、壁纸启用状态并立即写盘，再修改；
 // 关闭、退出、崩溃、下次启动时按记录恢复，全部成功后才删记录。失败静默，能做的部分照做。
 internal sealed class CleanDesktopSession {
  const uint Black=0x000000, White=0xFFFFFF;
  readonly IDesktopState desktop;
  readonly Func<string> readRecord;
  readonly Func<string,bool> writeRecord;
  readonly Func<string> readBackground;

  internal CleanDesktopSession(IDesktopState desktop,Func<string> readRecord,Func<string,bool> writeRecord,Func<string> readBackground) {
   this.desktop=desktop; this.readRecord=readRecord; this.writeRecord=writeRecord; this.readBackground=readBackground;
  }

  internal bool Enable() {
   if(desktop==null || readRecord==null || writeRecord==null) return false;
   string existing;
   try { existing=readRecord(); } catch { return false; }
   if(!string.IsNullOrEmpty(existing)) return false; // 已有没恢复完的记录，不能覆盖真正的原状态

   var record=new CleanRestoreRecord(); bool changed=false;

   // 图标：只有当前可见才需要收起；原来就隐藏的不记也不动。
   bool iconsVisible;
   try {
    if(desktop.TryGetIconsVisible(out iconsVisible) && iconsVisible) { record.HasIcons=true; record.IconsVisible=true; changed=true; }
   } catch {}

   // 桌面整理类软件自己画的图标层：可见的都记下类名，一起收起。
   try {
    string[] layers;
    if(desktop.TryGetOverlayLayers(out layers) && layers!=null) {
     var names=new List<string>();
     foreach(var name in layers) if(CleanRestoreRecord.IsStorableClassName(name) && !names.Contains(name)) names.Add(name);
     if(names.Count>0) { record.Layers=names.ToArray(); changed=true; }
    }
   } catch {}

   // 背景：只有设置为纯黑 / 纯白时才改；保持当前壁纸时完全不碰背景。
   string mode=null;
   try { mode=readBackground==null?null:readBackground(); } catch {}
   uint target=0; bool hasTarget=false;
   if(mode=="black" || mode=="white") {
    target=mode=="white"?White:Black; hasTarget=true;
    uint original=0; bool hasColor=false;
    try { hasColor=desktop.TryGetBackgroundColor(out original); } catch { hasColor=false; }
    bool wallpaperEnabled=false; bool hasWallpaper=false;
    try { hasWallpaper=desktop.TryGetWallpaperEnabled(out wallpaperEnabled); } catch { hasWallpaper=false; }
    if(hasColor && original!=target) { record.HasBackground=true; record.BackgroundColor=original; changed=true; }
    // 壁纸启用时记 wall:1；本来就没启用、但背景色要改时记 wall:0，恢复时才知道不要启用。
    if(hasWallpaper && wallpaperEnabled) { record.HasWallpaper=true; record.WallpaperEnabled=true; changed=true; }
    else if(hasWallpaper && hasColor && original!=target) { record.HasWallpaper=true; record.WallpaperEnabled=false; }
   }

   if(!changed) return false; // 桌面已经是目标状态，不写记录也不做修改
   try { if(!writeRecord(record.ToString())) return false; } catch { return false; } // 先落盘，再改桌面

   if(record.HasIcons) { try { desktop.SetIconsVisible(false); } catch {} }
   if(record.HasLayers) { try { desktop.SetOverlayLayersVisible(record.Layers,false); } catch {} }
   if(hasTarget && record.HasBackground) { try { desktop.SetBackgroundColor(target); } catch {} }
   if(record.HasWallpaper && record.WallpaperEnabled) { try { desktop.SetWallpaperEnabled(false); } catch {} }
   return true;
  }

  // 按记录恢复。图标原来隐藏（icons:0）不重新显示；壁纸原来禁用（wall:0）不启用。
  // 有任一步失败就保留记录，下次关闭 / 退出 / 启动还能再试。
  internal bool Restore() {
   if(desktop==null || readRecord==null || writeRecord==null) return false;
   string text;
   try { text=readRecord(); } catch { return false; }
   CleanRestoreRecord record;
   if(!CleanRestoreRecord.TryParse(text,out record)) return false;
   bool ok=true;
   if(record.HasIcons && record.IconsVisible) {
    bool done=false; try { done=desktop.SetIconsVisible(true); } catch { done=false; }
    ok=done;
   }
   if(record.HasLayers) {
    bool done=false; try { done=desktop.SetOverlayLayersVisible(record.Layers,true); } catch { done=false; }
    ok=done && ok;
   }
   if(record.HasBackground) {
    bool done=false; try { done=desktop.SetBackgroundColor(record.BackgroundColor); } catch { done=false; }
    ok=done && ok;
   }
   if(record.HasWallpaper && record.WallpaperEnabled) {
    bool done=false; try { done=desktop.SetWallpaperEnabled(true); } catch { done=false; }
    ok=done && ok;
   }
   if(ok) { try { ok=writeRecord(null); } catch { ok=false; } }
   return ok;
  }
 }

 internal static class CleanDesktop {
  internal static readonly IDesktopState Native=new NativeDesktopState();

  // 启动时清掉上次没来得及恢复的记录；崩溃处理也走这里。格式错误、读失败、恢复失败都静默。
  internal static bool RestoreFromSettings(string dataDir) { return RestoreFromSettings(Native,dataDir); }
  internal static bool RestoreFromSettings(IDesktopState desktop,string dataDir) {
   if(desktop==null || string.IsNullOrEmpty(dataDir)) return false;
   MainWindow.SettingsData settings;
   try { settings=MainWindow.ReadSettingsFile(dataDir); } catch { return false; }
   if(settings==null || string.IsNullOrEmpty(settings.CleanRestore)) return false;
   var session=new CleanDesktopSession(desktop,()=>settings.CleanRestore,v=> { settings.CleanRestore=v; MainWindow.WriteSettingsFile(dataDir,settings); return true; },()=>settings.CleanBg);
   return session.Restore();
  }
 }
}
