using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class CleanDesktopTests
 {
  // 内存里的桌面：测试绝不读写真实的桌面图标、壁纸和背景色。
  sealed class FakeDesktop : IDesktopState
  {
   public bool IconsVisible;
   public uint Background;
   public bool Wallpaper;
   public bool FailIconsRead, FailIconsWrite, FailBackgroundRead, FailBackgroundWrite, FailWallpaperRead, FailWallpaperWrite;
   public int SetIconsCalls, SetBackgroundCalls, SetWallpaperCalls;
   public readonly List<string> Log;

   public FakeDesktop(List<string> log)
   {
    Log = log;
   }

   public bool TryGetIconsVisible(out bool visible)
   {
    visible = IconsVisible;
    return !FailIconsRead;
   }

   public bool SetIconsVisible(bool visible)
   {
    SetIconsCalls++;
    Log.Add(visible ? "show-icons" : "hide-icons");
    if (FailIconsWrite) return false;
    IconsVisible = visible;
    return true;
   }

   public bool TryGetBackgroundColor(out uint color)
   {
    color = Background;
    return !FailBackgroundRead;
   }

   public bool SetBackgroundColor(uint color)
   {
    SetBackgroundCalls++;
    Log.Add("bg:" + color.ToString("X6"));
    if (FailBackgroundWrite) return false;
    Background = color;
    return true;
   }

   public bool TryGetWallpaperEnabled(out bool enabled)
   {
    enabled = Wallpaper;
    return !FailWallpaperRead;
   }

   public bool SetWallpaperEnabled(bool enabled)
   {
    SetWallpaperCalls++;
    Log.Add(enabled ? "wall-on" : "wall-off");
    if (FailWallpaperWrite) return false;
    Wallpaper = enabled;
    return true;
   }
  }

  // 和 MainWindow.SetCleanRestore 一样：先改内存里的值，写盘成功才算数。
  sealed class TempRecord
  {
   public string Record;
   public int Saves;
   public bool FailSave;
   public readonly List<string> Log;

   public TempRecord(List<string> log)
   {
    Log = log;
   }

   public bool Save(string value)
   {
    if (FailSave)
    {
     Log.Add("save-failed");
     return false;
    }
    Record = value;
    Saves++;
    Log.Add("save:" + (value ?? "<null>"));
    return true;
   }
  }

  sealed class Harness
  {
   public readonly List<string> Log = new List<string>();
   public readonly FakeDesktop Desktop;
   public readonly TempRecord Store;
   public readonly string Mode;

   public Harness(string mode, bool iconsVisible, uint background, bool wallpaper)
   {
    Mode = mode;
    Desktop = new FakeDesktop(Log)
    {
     IconsVisible = iconsVisible,
     Background = background,
     Wallpaper = wallpaper
    };
    Store = new TempRecord(Log);
   }

   public CleanDesktopSession Session()
   {
    return new CleanDesktopSession(Desktop, () => Store.Record, Store.Save, () => Mode);
   }
  }

  [Fact]
  public void Enable_hides_visible_icons_and_writes_the_record_before_changing()
  {
   var h = new Harness("keep", true, 0x1C2D3E, true);

   Assert.True(h.Session().Enable());

   Assert.Equal("icons:1", h.Store.Record);
   Assert.False(h.Desktop.IconsVisible);
   Assert.Equal(1, h.Desktop.SetIconsCalls);
   Assert.True(h.Log.IndexOf("save:icons:1") < h.Log.IndexOf("hide-icons"));
   Assert.Equal(0, h.Desktop.SetBackgroundCalls);
   Assert.Equal(0, h.Desktop.SetWallpaperCalls);
  }

  [Fact]
  public void Enable_leaves_icons_that_are_already_hidden_alone()
  {
   var h = new Harness("keep", false, 0x1C2D3E, true);

   Assert.False(h.Session().Enable());

   Assert.Null(h.Store.Record);
   Assert.Equal(0, h.Store.Saves);
   Assert.Equal(0, h.Desktop.SetIconsCalls);
   Assert.Empty(h.Log);
  }

  [Fact]
  public void Enable_black_records_the_original_background_and_wallpaper_then_changes_them()
  {
   var h = new Harness("black", false, 0x1C2D3E, true);

   Assert.True(h.Session().Enable());

   Assert.Equal("bg:1C2D3E,wall:1", h.Store.Record);
   Assert.Equal(0x000000u, h.Desktop.Background);
   Assert.False(h.Desktop.Wallpaper);
   Assert.Equal(1, h.Desktop.SetBackgroundCalls);
   Assert.Equal(1, h.Desktop.SetWallpaperCalls);
   Assert.True(h.Log.IndexOf("save:bg:1C2D3E,wall:1") < h.Log.IndexOf("bg:000000"));
   Assert.True(h.Log.IndexOf("save:bg:1C2D3E,wall:1") < h.Log.IndexOf("wall-off"));
  }

  [Fact]
  public void Enable_white_uses_ffffff()
  {
   var h = new Harness("white", false, 0x1C2D3E, true);

   Assert.True(h.Session().Enable());

   Assert.Equal("bg:1C2D3E,wall:1", h.Store.Record);
   Assert.Equal(0xFFFFFFu, h.Desktop.Background);
   Assert.False(h.Desktop.Wallpaper);
  }

  [Fact]
  public void Enable_black_handles_icons_color_and_wallpaper_together()
  {
   var h = new Harness("black", true, 0x1C2D3E, true);

   Assert.True(h.Session().Enable());

   Assert.Equal("icons:1,bg:1C2D3E,wall:1", h.Store.Record);
   Assert.False(h.Desktop.IconsVisible);
   Assert.Equal(0x000000u, h.Desktop.Background);
   Assert.False(h.Desktop.Wallpaper);
   int save = h.Log.IndexOf("save:icons:1,bg:1C2D3E,wall:1");
   Assert.True(save >= 0);
   Assert.True(save < h.Log.IndexOf("hide-icons"));
   Assert.True(save < h.Log.IndexOf("bg:000000"));
   Assert.True(save < h.Log.IndexOf("wall-off"));
  }

  [Fact]
  public void Enable_keep_wallpaper_only_hides_icons()
  {
   var h = new Harness("keep", true, 0x1C2D3E, true);

   Assert.True(h.Session().Enable());

   Assert.Equal("icons:1", h.Store.Record);
   Assert.False(h.Desktop.IconsVisible);
   Assert.Equal(0x1C2D3Eu, h.Desktop.Background);
   Assert.True(h.Desktop.Wallpaper);
   Assert.Equal(0, h.Desktop.SetBackgroundCalls);
   Assert.Equal(0, h.Desktop.SetWallpaperCalls);
  }

  [Fact]
  public void Enable_records_an_already_disabled_wallpaper_as_zero_and_never_enables_it()
  {
   var h = new Harness("black", false, 0x123456, false);

   Assert.True(h.Session().Enable());

   Assert.Equal("bg:123456,wall:0", h.Store.Record);
   Assert.Equal(0x000000u, h.Desktop.Background);
   Assert.False(h.Desktop.Wallpaper);
   Assert.Equal(0, h.Desktop.SetWallpaperCalls);
   Assert.Contains("wall:0", h.Store.Record);
  }

  [Fact]
  public void Enable_does_nothing_when_the_desktop_is_already_clean()
  {
   var h = new Harness("black", false, 0x000000, false);

   Assert.False(h.Session().Enable());

   Assert.Null(h.Store.Record);
   Assert.Equal(0, h.Store.Saves);
   Assert.Empty(h.Log);
  }

  [Fact]
  public void Enable_never_overwrites_an_existing_record()
  {
   var h = new Harness("black", true, 0x1C2D3E, true);
   h.Store.Record = "icons:1";

   Assert.False(h.Session().Enable());

   Assert.Equal("icons:1", h.Store.Record);
   Assert.Equal(0, h.Store.Saves);
   Assert.True(h.Desktop.IconsVisible);
   Assert.Equal(0, h.Desktop.SetIconsCalls);
   Assert.Equal(0, h.Desktop.SetBackgroundCalls);
  }

  [Fact]
  public void Enable_changes_nothing_when_the_record_cannot_be_saved()
  {
   var h = new Harness("black", true, 0x1C2D3E, true);
   h.Store.FailSave = true;

   Assert.False(h.Session().Enable());

   Assert.True(h.Desktop.IconsVisible);
   Assert.Equal(0x1C2D3Eu, h.Desktop.Background);
   Assert.True(h.Desktop.Wallpaper);
   Assert.Equal(0, h.Desktop.SetIconsCalls);
   Assert.Equal(0, h.Desktop.SetBackgroundCalls);
   Assert.Equal(0, h.Desktop.SetWallpaperCalls);
   Assert.Equal(new[] { "save-failed" }, h.Log);
  }

  [Fact]
  public void Enable_skips_unreadable_parts_and_still_does_the_rest()
  {
   var h = new Harness("black", true, 0x1C2D3E, true);
   h.Desktop.FailIconsRead = true;
   h.Desktop.FailBackgroundRead = true;

   Assert.True(h.Session().Enable());

   // 图标和背景色读不到就不碰；壁纸能读、原来开着，就只记并关掉壁纸。
   Assert.Equal("wall:1", h.Store.Record);
   Assert.True(h.Desktop.IconsVisible);
   Assert.Equal(0x1C2D3Eu, h.Desktop.Background);
   Assert.False(h.Desktop.Wallpaper);
   Assert.Equal(0, h.Desktop.SetIconsCalls);
   Assert.Equal(0, h.Desktop.SetBackgroundCalls);
   Assert.Equal(1, h.Desktop.SetWallpaperCalls);
  }

  [Fact]
  public void Enable_leaves_the_wallpaper_alone_when_its_state_cannot_be_read()
  {
   var h = new Harness("black", false, 0x1C2D3E, true);
   h.Desktop.FailWallpaperRead = true;

   Assert.True(h.Session().Enable());

   // 背景色读得到就照常改；壁纸状态读不到就不碰，免得恢复时猜错。
   Assert.Equal("bg:1C2D3E", h.Store.Record);
   Assert.Equal(0x000000u, h.Desktop.Background);
   Assert.True(h.Desktop.Wallpaper);
   Assert.Equal(0, h.Desktop.SetWallpaperCalls);
  }

  [Fact]
  public void Restore_brings_back_everything_and_removes_the_record()
  {
   var h = new Harness("keep", false, 0x000000, false);
   h.Store.Record = "icons:1,bg:1C2D3E,wall:1";

   Assert.True(h.Session().Restore());

   Assert.True(h.Desktop.IconsVisible);
   Assert.Equal(0x1C2D3Eu, h.Desktop.Background);
   Assert.True(h.Desktop.Wallpaper);
   Assert.Null(h.Store.Record);
   Assert.Equal(1, h.Store.Saves);
   Assert.Equal(1, h.Desktop.SetIconsCalls);
   Assert.Equal(1, h.Desktop.SetBackgroundCalls);
   Assert.Equal(1, h.Desktop.SetWallpaperCalls);
  }

  [Fact]
  public void Restore_does_not_reenable_a_wallpaper_that_was_already_disabled()
  {
   var h = new Harness("keep", false, 0x000000, false);
   h.Store.Record = "icons:0,bg:1C2D3E,wall:0";

   Assert.True(h.Session().Restore());

   Assert.False(h.Desktop.IconsVisible);
   Assert.Equal(0x1C2D3Eu, h.Desktop.Background);
   Assert.False(h.Desktop.Wallpaper);
   Assert.Equal(0, h.Desktop.SetIconsCalls);
   Assert.Equal(0, h.Desktop.SetWallpaperCalls);
   Assert.Null(h.Store.Record);
  }

  [Fact]
  public void Restore_keeps_the_record_when_a_step_fails()
  {
   var h = new Harness("keep", false, 0x000000, false);
   h.Store.Record = "icons:1,bg:1C2D3E,wall:1";
   h.Desktop.FailIconsWrite = true;

   Assert.False(h.Session().Restore());

   // 图标恢复失败，但背景和壁纸照常恢复；记录留着下次再试。
   Assert.False(h.Desktop.IconsVisible);
   Assert.Equal(0x1C2D3Eu, h.Desktop.Background);
   Assert.True(h.Desktop.Wallpaper);
   Assert.Equal("icons:1,bg:1C2D3E,wall:1", h.Store.Record);
   Assert.Equal(0, h.Store.Saves);
  }

  [Fact]
  public void Restore_keeps_the_record_when_a_background_or_wallpaper_write_fails()
  {
   var color = new Harness("keep", false, 0x000000, false);
   color.Store.Record = "bg:1C2D3E,wall:1";
   color.Desktop.FailBackgroundWrite = true;

   Assert.False(color.Session().Restore());

   Assert.Equal(0x000000u, color.Desktop.Background); // 背景色没写进去
   Assert.True(color.Desktop.Wallpaper);              // 壁纸照常恢复
   Assert.Equal("bg:1C2D3E,wall:1", color.Store.Record);

   var wallpaper = new Harness("keep", false, 0x000000, false);
   wallpaper.Store.Record = "bg:1C2D3E,wall:1";
   wallpaper.Desktop.FailWallpaperWrite = true;

   Assert.False(wallpaper.Session().Restore());

   Assert.Equal(0x1C2D3Eu, wallpaper.Desktop.Background); // 背景色照常恢复
   Assert.False(wallpaper.Desktop.Wallpaper);              // 壁纸没启用
   Assert.Equal("bg:1C2D3E,wall:1", wallpaper.Store.Record);
  }

  [Fact]
  public void Startup_restore_applies_the_record_and_removes_it_from_settings()
  {
   using (var sb = new Sandbox())
   {
    MainWindow.WriteSettingsFile(sb.BotNotesDir, new MainWindow.SettingsData { CleanRestore = "icons:1,bg:1C2D3E,wall:1" });
    var log = new List<string>();
    var desktop = new FakeDesktop(log) { IconsVisible = false, Background = 0x000000, Wallpaper = false };

    Assert.True(CleanDesktop.RestoreFromSettings(desktop, sb.BotNotesDir));

    Assert.True(desktop.IconsVisible);
    Assert.Equal(0x1C2D3Eu, desktop.Background);
    Assert.True(desktop.Wallpaper);
    Assert.Null(MainWindow.ReadSettingsFile(sb.BotNotesDir).CleanRestore);
    Assert.DoesNotContain("cleanRestore=", File.ReadAllText(MainWindow.SettingsPath(sb.BotNotesDir)));
   }
  }

  [Fact]
  public void Startup_restore_does_nothing_when_there_is_no_record()
  {
   using (var sb = new Sandbox())
   {
    MainWindow.WriteSettingsFile(sb.BotNotesDir, new MainWindow.SettingsData());
    var log = new List<string>();
    var desktop = new FakeDesktop(log) { IconsVisible = true, Background = 0x123456, Wallpaper = true };

    Assert.False(CleanDesktop.RestoreFromSettings(desktop, sb.BotNotesDir));

    Assert.Empty(log);
    Assert.True(desktop.IconsVisible);
    Assert.Equal(0x123456u, desktop.Background);
    Assert.True(desktop.Wallpaper);
   }
  }

  [Fact]
  public void Malformed_clean_restore_is_ignored_without_throwing()
  {
   using (var sb = new Sandbox())
   {
    foreach (string bad in new[]
    {
     "cleanRestore=",
     "cleanRestore=garbage",
     "cleanRestore=icons:x",
     "cleanRestore=bg:12345",
     "cleanRestore=bg:zzzzzz",
     "cleanRestore=wall:2",
     "cleanRestore=icons:1,garbage",
     "cleanRestore=wall:0,unknown:1"
    })
    {
     var settings = MainWindow.ParseSettings(new[] { bad }, sb.BotNotesDir);
     if (bad == "cleanRestore=icons:1,garbage" || bad == "cleanRestore=wall:0,unknown:1")
     {
      // 至少有一个字段可读：保留能恢复的部分，坏字段忽略。
      Assert.NotNull(settings.CleanRestore);
     }
     else
     {
      Assert.Null(settings.CleanRestore);
     }
    }

    // 整个值坏掉时：不调用任何桌面读写，也不崩。
    MainWindow.WriteSettingsFile(sb.BotNotesDir, new MainWindow.SettingsData { CleanRestore = "garbage" });
    var log = new List<string>();
    var desktop = new FakeDesktop(log) { IconsVisible = true, Background = 0x123456, Wallpaper = true };
    Assert.False(CleanDesktop.RestoreFromSettings(desktop, sb.BotNotesDir));
    Assert.Empty(log);
   }
  }

  [Fact]
  public void Settings_roundtrip_keeps_clean_restore_and_clean_bg()
  {
   using (var sb = new Sandbox())
   {
    var saved = new MainWindow.SettingsData { CleanRestore = "icons:1,bg:0A0B0C,wall:0", CleanBg = "white" };
    MainWindow.WriteSettingsFile(sb.BotNotesDir, saved);

    var loaded = MainWindow.ReadSettingsFile(sb.BotNotesDir);
    Assert.Equal("icons:1,bg:0A0B0C,wall:0", loaded.CleanRestore);
    Assert.Equal("white", loaded.CleanBg);
    Assert.Contains("cleanRestore=icons:1,bg:0A0B0C,wall:0", MainWindow.SerializeSettings(loaded));
    Assert.Contains("cleanBg=white", MainWindow.SerializeSettings(loaded));

    // 默认值：cleanBg=black，且没有记录时不写空键。
    var defaults = MainWindow.SerializeSettings(new MainWindow.SettingsData());
    Assert.Contains("cleanBg=black", defaults);
    Assert.DoesNotContain(defaults, line => line.StartsWith("cleanRestore=", StringComparison.Ordinal));

    Assert.Equal("keep", MainWindow.ParseSettings(new[] { "cleanBg=keep" }, sb.BotNotesDir).CleanBg);
    Assert.Equal("black", MainWindow.ParseSettings(new[] { "cleanBg=purple" }, sb.BotNotesDir).CleanBg);
   }
  }
 }
}
