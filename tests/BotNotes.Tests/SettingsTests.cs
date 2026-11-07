using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class SettingsTests
 {
  static MainWindow.SettingsData Parse(params string[] lines)
  {
   return MainWindow.ParseSettings(lines, @"C:\data\BotNotes");
  }

  [Fact]
  public void SaveThenLoad_RoundTripsAllValues()
  {
   using (var sb = new Sandbox())
   {
    var saved = new MainWindow.SettingsData
    {
     HoverDelayMs = 1500,
     PanelLeft = 10.5,
     PanelTop = -2.5,
     DotX = -3.2,
     DotY = 8.5,
     PanelW = 600.5,
     PanelH = 700.5,
     HoverEnabled = false,
     Pinned = true,
     BotSize = 36,
     Accent = Color.FromRgb(0x9C, 0xC7, 0xF5),
     Background = Color.FromRgb(0x1C, 0x1D, 0x21),
     ArchiveDir = Path.Combine(sb.Root, "custom-archive")
    };

    string file = Path.Combine(sb.Root, "settings.ini");
    File.WriteAllLines(file, MainWindow.SerializeSettings(saved), Encoding.UTF8);
    var loaded = MainWindow.ParseSettings(File.ReadAllLines(file), sb.BotNotesDir);

    Assert.Equal(saved.HoverDelayMs, loaded.HoverDelayMs);
    Assert.Equal(saved.PanelLeft, loaded.PanelLeft);
    Assert.Equal(saved.PanelTop, loaded.PanelTop);
    Assert.Equal(saved.DotX, loaded.DotX);
    Assert.Equal(saved.DotY, loaded.DotY);
    Assert.Equal(saved.PanelW, loaded.PanelW);
    Assert.Equal(saved.PanelH, loaded.PanelH);
    Assert.Equal(saved.HoverEnabled, loaded.HoverEnabled);
    Assert.Equal(saved.Pinned, loaded.Pinned);
    Assert.Equal(saved.BotSize, loaded.BotSize);
    Assert.Equal(saved.Accent, loaded.Accent);
    Assert.Equal(saved.Background, loaded.Background);
    Assert.Equal(saved.ArchiveDir, loaded.ArchiveDir);
   }
  }

  [Fact]
  public void MissingValues_UseTheOriginalDefaults()
  {
   var s = Parse();
   var d = new MainWindow.SettingsData();

   Assert.Equal(d.HoverDelayMs, s.HoverDelayMs);
   Assert.Equal(d.PanelLeft, s.PanelLeft);
   Assert.Equal(d.PanelTop, s.PanelTop);
   Assert.Equal(d.DotX, s.DotX);
   Assert.Equal(d.DotY, s.DotY);
   Assert.Equal(d.PanelW, s.PanelW);
   Assert.Equal(d.PanelH, s.PanelH);
   Assert.Equal(d.HoverEnabled, s.HoverEnabled);
   Assert.Equal(d.Pinned, s.Pinned);
   Assert.Equal(d.BotSize, s.BotSize);
   Assert.Equal(d.Accent, s.Accent);
   Assert.Equal(d.Background, s.Background);
   Assert.Null(s.ArchiveDir);
  }

  [Fact]
  public void OutOfRangeValues_AreClampedLikeTheOriginalCode()
  {
   var low = Parse("hoverDelayMs=-1", "panelW=0", "panelH=100", "botSize=99");
   Assert.Equal(0, low.HoverDelayMs);
   Assert.Equal(460, low.PanelW);
   Assert.Equal(360, low.PanelH);
   Assert.Equal(22, low.BotSize);

   var high = Parse("hoverDelayMs=5000", "panelW=460", "panelH=360", "botSize=16");
   Assert.Equal(3000, high.HoverDelayMs);
   Assert.Equal(460, high.PanelW);
   Assert.Equal(360, high.PanelH);
   Assert.Equal(16, high.BotSize);

   var tooBig = Parse("botSize=41");
   Assert.Equal(22, tooBig.BotSize);
  }

  [Fact]
  public void BadLinesAndUnknownKeys_AreIgnored()
  {
   var s = Parse(
    "garbage",
    "=x",
    "unknownKey=1",
    "panelLeft=abc",
    "panelTop=1.2.3",
    "hoverDelayMs=abc",
    "botSize=abc",
    "accent=not-a-color",
    "background=#12",
    "archiveDir=");
   var d = new MainWindow.SettingsData();

   Assert.Equal(d.PanelLeft, s.PanelLeft);
   Assert.Equal(d.PanelTop, s.PanelTop);
   Assert.Equal(d.HoverDelayMs, s.HoverDelayMs);
   Assert.Equal(d.BotSize, s.BotSize);
   Assert.Equal(d.Accent, s.Accent);
   Assert.Equal(d.Background, s.Background);
   Assert.Null(s.ArchiveDir);
  }

  [Fact]
  public void BooleanFlags_FollowTheOriginalComparisons()
  {
   var on = Parse("hoverEnabled=1", "pinned=1");
   Assert.True(on.HoverEnabled);
   Assert.True(on.Pinned);

   var off = Parse("hoverEnabled=0", "pinned=0");
   Assert.False(off.HoverEnabled);
   Assert.False(off.Pinned);

   var odd = Parse("hoverEnabled=2", "pinned=yes");
   Assert.True(odd.HoverEnabled);
   Assert.False(odd.Pinned);
  }

  [Fact]
  public void NumbersUseInvariantCulture_EvenWhenCurrentCultureIsGerman()
  {
   CultureInfo old = CultureInfo.CurrentCulture;
   try
   {
    CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    var s = Parse("panelLeft=12.5", "dotY=8.25", "panelW=520.5", "panelH=590.5", "hoverDelayMs=1500");
    Assert.Equal(12.5, s.PanelLeft);
    Assert.Equal(8.25, s.DotY);
    Assert.Equal(520.5, s.PanelW);
    Assert.Equal(590.5, s.PanelH);
    Assert.Equal(1500, s.HoverDelayMs);

    var lines = MainWindow.SerializeSettings(new MainWindow.SettingsData
    {
     PanelLeft = 12.5,
     DotY = 8.25,
     PanelW = 520.5,
     PanelH = 590.5
    });
    Assert.Contains("panelLeft=12.5", lines);
    // 原有代码把位置和尺寸四舍五入到一位小数再写盘（默认的银行家舍入）。
    Assert.Contains("dotY=8.2", lines);
    Assert.Contains("panelW=520.5", lines);
    Assert.Contains("panelH=590.5", lines);
   }
   finally
   {
    CultureInfo.CurrentCulture = old;
   }
  }
 }
}
