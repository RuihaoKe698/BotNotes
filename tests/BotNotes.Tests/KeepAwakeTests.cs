using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class KeepAwakeTests
 {
  // 内存里的电源方案：测试绝不调用真实的 powrprof。
  sealed class FakePower : IPowerSettings
  {
   public Guid Active;
   public readonly Dictionary<Guid, uint[]> Values = new Dictionary<Guid, uint[]>();
   public bool FailRead, FailWrite;
   public int ReadCount, WriteCount, SetActiveCount;
   public readonly List<Guid> Activated = new List<Guid>();

   public void Set(Guid scheme, uint ac, uint dc)
   {
    Values[scheme] = new[] { ac, dc };
   }

   public bool GetActiveScheme(out Guid scheme)
   {
    scheme = Active;
    return Active != Guid.Empty;
   }

   public bool ReadLidAction(Guid scheme, out uint ac, out uint dc)
   {
    ReadCount++;
    ac = 0;
    dc = 0;
    if (FailRead) return false;
    uint[] values;
    if (!Values.TryGetValue(scheme, out values)) return false;
    ac = values[0];
    dc = values[1];
    return true;
   }

   public bool WriteLidAction(Guid scheme, uint ac, uint dc)
   {
    WriteCount++;
    if (FailWrite) return false;
    Values[scheme] = new[] { ac, dc };
    return true;
   }

   public bool SetActiveScheme(Guid scheme)
   {
    SetActiveCount++;
    Activated.Add(scheme);
    Active = scheme;
    return true;
   }
  }

  // 用临时目录里的 settings.ini 当存储，和 MainWindow.SaveSettings 走同一套序列化。
  sealed class TempSettings
  {
   public readonly string Dir;
   public readonly MainWindow.SettingsData Data = new MainWindow.SettingsData();
   public int Saves;
   public bool FailSave;

   public TempSettings(string dir)
   {
    Dir = dir;
   }

   public bool Save(string value)
   {
    if (FailSave) return false;
    string old = Data.LidRestore;
    Data.LidRestore = value;
    try
    {
     MainWindow.WriteSettingsFile(Dir, Data);
     Saves++;
     return true;
    }
    catch
    {
     Data.LidRestore = old;
     return false;
    }
   }
  }

  static KeepAwakeSession Session(FakePower power, TempSettings store)
  {
   return new KeepAwakeSession(power, () => store.Data.LidRestore, store.Save);
  }

  static string Record(Guid scheme, uint ac, uint dc)
  {
   return scheme.ToString("D") + "," + ac + "," + dc;
  }

  [Fact]
  public void Enable_records_original_values_and_writes_zero_to_both_ac_and_dc()
  {
   using (var sb = new Sandbox())
   {
    var power = new FakePower();
    var scheme = Guid.NewGuid();
    power.Active = scheme;
    power.Set(scheme, 5, 7);
    var store = new TempSettings(sb.BotNotesDir);
    var session = Session(power, store);

    Assert.True(session.Enable());

    Assert.Equal(new uint[] { 0, 0 }, power.Values[scheme]);
    Assert.Equal(Record(scheme, 5, 7), store.Data.LidRestore);
    Assert.Equal(1, power.SetActiveCount); // 改完要重新激活同一个方案才生效
    Assert.Contains("lidRestore=" + Record(scheme, 5, 7), File.ReadAllLines(MainWindow.SettingsPath(sb.BotNotesDir)));
   }
  }

  [Fact]
  public void Enable_when_both_values_are_already_zero_records_nothing_and_writes_nothing()
  {
   using (var sb = new Sandbox())
   {
    var power = new FakePower();
    var scheme = Guid.NewGuid();
    power.Active = scheme;
    power.Set(scheme, 0, 0);
    var store = new TempSettings(sb.BotNotesDir);
    var session = Session(power, store);

    Assert.False(session.Enable());

    Assert.Null(store.Data.LidRestore);
    Assert.Equal(0, store.Saves);
    Assert.Equal(0, power.WriteCount);
    Assert.Equal(0, power.SetActiveCount);
    Assert.False(File.Exists(MainWindow.SettingsPath(sb.BotNotesDir)));
   }
  }

  [Fact]
  public void Enable_does_not_change_power_when_settings_cannot_be_saved()
  {
   using (var sb = new Sandbox())
   {
    var power = new FakePower();
    var scheme = Guid.NewGuid();
    power.Active = scheme;
    power.Set(scheme, 1, 2);
    var store = new TempSettings(sb.BotNotesDir) { FailSave = true };
    var session = Session(power, store);

    Assert.False(session.Enable());

    Assert.Equal(new uint[] { 1, 2 }, power.Values[scheme]);
    Assert.Equal(0, power.WriteCount);
    Assert.Equal(0, power.SetActiveCount);
   }
  }

  [Fact]
  public void Power_read_or_write_failures_are_silent_and_keep_the_record_for_a_retry()
  {
   using (var sb = new Sandbox())
   {
    var power = new FakePower();
    var scheme = Guid.NewGuid();
    power.Active = scheme;
    power.Set(scheme, 1, 2);
    var store = new TempSettings(sb.BotNotesDir);

    power.FailRead = true;
    Assert.False(Session(power, store).Enable());
    Assert.Null(store.Data.LidRestore);
    Assert.Equal(0, power.WriteCount);

    power.FailRead = false;
    power.FailWrite = true;
    Assert.True(Session(power, store).Enable()); // 原值已落盘；电源没改成功也不抛
    Assert.Equal(Record(scheme, 1, 2), store.Data.LidRestore);

    // 恢复时写失败：记录保留，下次启动 / 关闭咖啡还能再试。
    Assert.False(Session(power, store).Restore());
    Assert.Equal(Record(scheme, 1, 2), store.Data.LidRestore);
   }
  }

  [Fact]
  public void Disable_restores_original_values_and_removes_lid_restore()
  {
   using (var sb = new Sandbox())
   {
    var power = new FakePower();
    var scheme = Guid.NewGuid();
    power.Active = scheme;
    power.Set(scheme, 3, 4);
    var store = new TempSettings(sb.BotNotesDir);
    var session = Session(power, store);

    Assert.True(session.Enable());
    Assert.True(session.Restore());

    Assert.Equal(new uint[] { 3, 4 }, power.Values[scheme]);
    Assert.Null(store.Data.LidRestore);
    Assert.DoesNotContain("lidRestore=", File.ReadAllText(MainWindow.SettingsPath(sb.BotNotesDir)));
    Assert.Equal(2, power.SetActiveCount); // 开启一次、恢复一次
   }
  }

  [Fact]
  public void Restore_uses_the_recorded_scheme_after_the_user_switches_schemes()
  {
   using (var sb = new Sandbox())
   {
    var power = new FakePower();
    var original = Guid.NewGuid();
    var current = Guid.NewGuid();
    power.Active = original;
    power.Set(original, 5, 6);
    power.Set(current, 9, 9);
    var store = new TempSettings(sb.BotNotesDir);
    var session = Session(power, store);

    Assert.True(session.Enable());
    power.Active = current; // 用户开咖啡期间换了电源方案
    Assert.True(session.Restore());

    Assert.Equal(new uint[] { 5, 6 }, power.Values[original]);
    Assert.Equal(new uint[] { 9, 9 }, power.Values[current]); // 不碰用户后来选的方案
    Assert.Equal(current, power.Active);
    Assert.Equal(1, power.SetActiveCount); // 恢复时当前方案不是它，不切回去
   }
  }

  [Fact]
  public void Startup_restore_writes_back_the_recorded_values_and_removes_lid_restore()
  {
   using (var sb = new Sandbox())
   {
    var scheme = Guid.NewGuid();
    MainWindow.WriteSettingsFile(sb.BotNotesDir, new MainWindow.SettingsData { LidRestore = Record(scheme, 3, 4) });
    var power = new FakePower();
    power.Active = scheme;
    power.Set(scheme, 0, 0);

    Assert.True(KeepAwake.RestoreFromSettings(power, sb.BotNotesDir));

    Assert.Equal(new uint[] { 3, 4 }, power.Values[scheme]);
    Assert.Equal(1, power.SetActiveCount);
    Assert.Null(MainWindow.ReadSettingsFile(sb.BotNotesDir).LidRestore);
    Assert.DoesNotContain("lidRestore=", File.ReadAllText(MainWindow.SettingsPath(sb.BotNotesDir)));
   }
  }

  [Fact]
  public void Startup_restore_does_nothing_when_there_is_no_record()
  {
   using (var sb = new Sandbox())
   {
    MainWindow.WriteSettingsFile(sb.BotNotesDir, new MainWindow.SettingsData());
    var power = new FakePower();
    var scheme = Guid.NewGuid();
    power.Active = scheme;
    power.Set(scheme, 1, 2);

    Assert.False(KeepAwake.RestoreFromSettings(power, sb.BotNotesDir));

    Assert.Equal(new uint[] { 1, 2 }, power.Values[scheme]);
    Assert.Equal(0, power.ReadCount);
    Assert.Equal(0, power.WriteCount);
    Assert.Equal(0, power.SetActiveCount);
   }

   using (var sb = new Sandbox())
   {
    var power = new FakePower();
    Assert.False(KeepAwake.RestoreFromSettings(power, sb.BotNotesDir));
    Assert.Equal(0, power.WriteCount);
   }
  }

  [Fact]
  public void Malformed_lid_restore_is_ignored_without_throwing()
  {
   using (var sb = new Sandbox())
   {
    string guid = Guid.NewGuid().ToString("D");
    foreach (string bad in new[]
    {
     "lidRestore=",
     "lidRestore=garbage",
     "lidRestore=" + guid + ",1",
     "lidRestore=" + guid + ",1,2,3",
     "lidRestore=not-a-guid,1,2",
     "lidRestore=" + guid + ",x,2",
     "lidRestore=" + guid + ",1,y",
     "lidRestore=" + guid + ",-1,2"
    })
    {
     var settings = MainWindow.ParseSettings(new[] { bad }, sb.BotNotesDir);
     Assert.Null(settings.LidRestore);
    }

    MainWindow.WriteSettingsFile(sb.BotNotesDir, new MainWindow.SettingsData { LidRestore = "garbage" });
    var power = new FakePower();
    Assert.False(KeepAwake.RestoreFromSettings(power, sb.BotNotesDir));
    Assert.Equal(0, power.WriteCount);
   }
  }

  [Fact]
  public void Settings_roundtrip_keeps_lid_restore()
  {
   using (var sb = new Sandbox())
   {
    var scheme = Guid.NewGuid();
    string record = Record(scheme, 11, 12);
    MainWindow.WriteSettingsFile(sb.BotNotesDir, new MainWindow.SettingsData { LidRestore = record });

    var loaded = MainWindow.ReadSettingsFile(sb.BotNotesDir);
    Assert.Equal(record, loaded.LidRestore);
    Assert.Contains("lidRestore=" + record, MainWindow.SerializeSettings(loaded));

    // 没有记录时不写空键，下一次保存不会留下 lidRestore=。
    Assert.DoesNotContain(MainWindow.SerializeSettings(new MainWindow.SettingsData()), line => line.StartsWith("lidRestore=", StringComparison.Ordinal));
   }
  }
 }
}
