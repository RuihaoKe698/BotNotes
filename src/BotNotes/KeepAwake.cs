using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace NotchNotesWindows {
 // “合上盖子时”动作的读写入口。测试里换成内存假实现，绝不碰真实电源方案。
 internal interface IPowerSettings {
  bool GetActiveScheme(out Guid scheme);
  bool ReadLidAction(Guid scheme,out uint ac,out uint dc);
  bool WriteLidAction(Guid scheme,uint ac,uint dc);
  bool SetActiveScheme(Guid scheme);
 }

 // powrprof.dll 的真实实现：0 = 合上盖子时不采取任何操作。
 // 子组固定为 GUID_SYSTEM_BUTTON_SUBGROUP，设置项固定为 GUID_LIDSWITCH_ACTION。
 internal sealed class NativePowerSettings : IPowerSettings {
  const uint ErrorSuccess=0;
  static readonly Guid SystemButtonSubgroup=new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");
  static readonly Guid LidSwitchAction=new Guid("5ca83367-6e45-459f-a27b-476b1d01c936");

  [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey,out IntPtr activePolicyGuid);
  [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr rootPowerKey,ref Guid schemeGuid,ref Guid subGroupGuid,ref Guid settingGuid,out uint value);
  [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey,ref Guid schemeGuid,ref Guid subGroupGuid,ref Guid settingGuid,out uint value);
  [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey,ref Guid schemeGuid,ref Guid subGroupGuid,ref Guid settingGuid,uint value);
  [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey,ref Guid schemeGuid,ref Guid subGroupGuid,ref Guid settingGuid,uint value);
  [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey,ref Guid schemeGuid);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr hMem);

  public bool GetActiveScheme(out Guid scheme) {
   scheme=Guid.Empty; IntPtr p=IntPtr.Zero;
   try {
    if(PowerGetActiveScheme(IntPtr.Zero,out p)!=ErrorSuccess || p==IntPtr.Zero) return false;
    scheme=(Guid)Marshal.PtrToStructure(p,typeof(Guid)); return scheme!=Guid.Empty;
   } catch { return false; }
   finally { if(p!=IntPtr.Zero) LocalFree(p); }
  }
  public bool ReadLidAction(Guid scheme,out uint ac,out uint dc) {
   ac=0; dc=0; Guid s=scheme, sub=SystemButtonSubgroup, lid=LidSwitchAction;
   try {
    if(PowerReadACValueIndex(IntPtr.Zero,ref s,ref sub,ref lid,out ac)!=ErrorSuccess) return false;
    if(PowerReadDCValueIndex(IntPtr.Zero,ref s,ref sub,ref lid,out dc)!=ErrorSuccess) return false;
    return true;
   } catch { return false; }
  }
  public bool WriteLidAction(Guid scheme,uint ac,uint dc) {
   Guid s=scheme, sub=SystemButtonSubgroup, lid=LidSwitchAction;
   try {
    bool ok=PowerWriteACValueIndex(IntPtr.Zero,ref s,ref sub,ref lid,ac)==ErrorSuccess;
    ok=PowerWriteDCValueIndex(IntPtr.Zero,ref s,ref sub,ref lid,dc)==ErrorSuccess && ok;
    return ok;
   } catch { return false; }
  }
  public bool SetActiveScheme(Guid scheme) {
   Guid s=scheme;
   try { return PowerSetActiveScheme(IntPtr.Zero,ref s)==ErrorSuccess; } catch { return false; }
  }
 }

 // settings.ini 里的 lidRestore=<方案GUID>,<AC原值>,<DC原值>。
 internal sealed class LidRestoreRecord {
  internal readonly Guid Scheme;
  internal readonly uint Ac, Dc;
  internal LidRestoreRecord(Guid scheme,uint ac,uint dc) { Scheme=scheme; Ac=ac; Dc=dc; }
  internal static bool TryParse(string text,out LidRestoreRecord record) {
   record=null;
   if(string.IsNullOrWhiteSpace(text)) return false;
   var parts=text.Split(',');
   if(parts.Length!=3) return false;
   Guid scheme; uint ac, dc;
   if(!Guid.TryParse(parts[0].Trim(),out scheme) || scheme==Guid.Empty) return false;
   if(!uint.TryParse(parts[1].Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out ac)) return false;
   if(!uint.TryParse(parts[2].Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out dc)) return false;
   record=new LidRestoreRecord(scheme,ac,dc); return true;
  }
  public override string ToString() {
   return Scheme.ToString("D")+","+Ac.ToString(CultureInfo.InvariantCulture)+","+Dc.ToString(CultureInfo.InvariantCulture);
  }
 }

 // 咖啡的合盖逻辑：开启时先持久化原值再改 0；恢复时按记录里的方案写回。
 // 所有电源读写异常都吞掉：失败只意味着合盖仍会睡，咖啡本身照常工作。
 internal sealed class KeepAwakeSession {
  readonly IPowerSettings power;
  readonly Func<string> readRecord;
  readonly Func<string,bool> writeRecord;

  internal KeepAwakeSession(IPowerSettings power,Func<string> readRecord,Func<string,bool> writeRecord) {
   this.power=power; this.readRecord=readRecord; this.writeRecord=writeRecord;
  }

  internal bool Enable() {
   if(power==null || readRecord==null || writeRecord==null) return false;
   Guid scheme; uint ac, dc;
   try {
    if(!power.GetActiveScheme(out scheme)) return false;
    if(!power.ReadLidAction(scheme,out ac,out dc)) return false;
   } catch { return false; }
   if(ac==0 && dc==0) return false; // 本来就是“不采取任何操作”：不用记，也不用改
   try { if(!string.IsNullOrEmpty(readRecord())) return false; } catch { return false; } // 已有没恢复完的记录，不能覆盖真正的原值
   var record=new LidRestoreRecord(scheme,ac,dc);
   try { if(!writeRecord(record.ToString())) return false; } catch { return false; } // 先落盘，再改电源
   try { power.WriteLidAction(scheme,0,0); } catch {}
   try { power.SetActiveScheme(scheme); } catch {}
   return true;
  }

  // 恢复记录里的方案。只有它仍是当前活动方案时才重新激活；用户开咖啡期间换过方案就不切回去。
  internal bool Restore() {
   if(power==null || readRecord==null || writeRecord==null) return false;
   string text;
   try { text=readRecord(); } catch { return false; }
   LidRestoreRecord record;
   if(!LidRestoreRecord.TryParse(text,out record)) return false;
   bool ok;
   try {
    ok=power.WriteLidAction(record.Scheme,record.Ac,record.Dc);
    if(ok) {
     Guid active;
     if(power.GetActiveScheme(out active)) { if(active==record.Scheme) ok=power.SetActiveScheme(record.Scheme); }
     else ok=false;
    }
   } catch { ok=false; }
   if(ok) { try { ok=writeRecord(null); } catch { ok=false; } }
   return ok;
  }
 }

 internal static class KeepAwake {
  internal static readonly IPowerSettings Native=new NativePowerSettings();

  // 启动时清掉上次没来得及恢复的记录；崩溃处理也走这里。格式错误、读失败、恢复失败都静默。
  internal static bool RestoreFromSettings(string dataDir) { return RestoreFromSettings(Native,dataDir); }
  internal static bool RestoreFromSettings(IPowerSettings power,string dataDir) {
   if(power==null || string.IsNullOrEmpty(dataDir)) return false;
   MainWindow.SettingsData settings;
   try { settings=MainWindow.ReadSettingsFile(dataDir); } catch { return false; }
   if(settings==null || string.IsNullOrEmpty(settings.LidRestore)) return false;
   var session=new KeepAwakeSession(power,()=>settings.LidRestore,v=> { settings.LidRestore=v; MainWindow.WriteSettingsFile(dataDir,settings); return true; });
   return session.Restore();
  }
 }
}
