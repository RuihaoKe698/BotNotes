using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class CrashLogTests
 {
  static readonly DateTime When = new DateTime(2026, 11, 7, 14, 3, 22);

  static Exception Thrown()
  {
   try { throw new InvalidOperationException("测试用的错误"); } catch (Exception ex) { return ex; }
  }

  [Fact]
  public void Version_is_the_release_number()
  {
   Assert.Equal("1.0.0", CrashLog.Version());
  }

  [Fact]
  public void Write_creates_log_with_version_time_and_exception()
  {
   using (var box = new Sandbox())
   {
    string folder = Path.Combine(box.Root, "logs");
    string path = CrashLog.Write(Thrown(), "UI thread", When, folder);
    Assert.Equal(Path.Combine(folder, CrashLog.FileName), path);
    string text = File.ReadAllText(path);
    Assert.Contains("==== 2026-11-07 14:03:22 ====", text);
    Assert.Contains("BotNotes 1.0.0", text);
    Assert.Contains("Source: UI thread", text);
    Assert.Contains("InvalidOperationException: 测试用的错误", text);
    Assert.Contains("Thrown", text); // stack trace is kept
   }
  }

  [Fact]
  public void Write_appends_to_existing_log()
  {
   using (var box = new Sandbox())
   {
    string folder = Path.Combine(box.Root, "logs");
    CrashLog.Write(Thrown(), "first", When, folder);
    string path = CrashLog.Write(Thrown(), "second", When.AddMinutes(1), folder);
    string text = File.ReadAllText(path);
    Assert.Contains("Source: first", text);
    Assert.Contains("Source: second", text);
   }
  }

  [Fact]
  public void Write_moves_a_large_log_to_crash_old_log()
  {
   long saved = CrashLog.MaxBytes;
   try
   {
    using (var box = new Sandbox())
    {
     string folder = Path.Combine(box.Root, "logs");
     CrashLog.MaxBytes = 100;
     CrashLog.Write(Thrown(), "first", When, folder);
     string path = CrashLog.Write(Thrown(), "second", When, folder);
     string old = Path.Combine(folder, CrashLog.OldFileName);
     Assert.True(File.Exists(old));
     Assert.Contains("Source: first", File.ReadAllText(old));
     Assert.DoesNotContain("Source: first", File.ReadAllText(path));
     Assert.Contains("Source: second", File.ReadAllText(path));
    }
   }
   finally { CrashLog.MaxBytes = saved; }
  }

  [Fact]
  public void Write_falls_back_to_the_next_folder()
  {
   using (var box = new Sandbox())
   {
    string blocked = Path.Combine(box.Root, "blocked");
    File.WriteAllText(blocked, "a file where the folder should be");
    string fallback = Path.Combine(box.Root, "fallback");
    string path = CrashLog.Write(Thrown(), "x", When, blocked, fallback);
    Assert.Equal(Path.Combine(fallback, CrashLog.FileName), path);
   }
  }

  [Fact]
  public void Write_returns_null_when_nothing_is_writable()
  {
   using (var box = new Sandbox())
   {
    string blocked = Path.Combine(box.Root, "blocked");
    File.WriteAllText(blocked, "x");
    Assert.Null(CrashLog.Write(Thrown(), "x", When, blocked));
   }
  }

  [Fact]
  public void Every_line_icon_path_parses()
  {
   var fields = typeof(LineIcon).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != "BulletDots").ToList();
   Assert.True(fields.Count >= 12);
   foreach (var f in fields)
   {
    var g = Geometry.Parse((string)f.GetValue(null));
    Assert.False(g.IsEmpty(), f.Name);
    var b = g.Bounds; // all icons stay inside the 24x24 grid
    Assert.True(b.Left >= 0 && b.Top >= 0 && b.Right <= 24 && b.Bottom <= 24, f.Name + " " + b);
   }
  }
 }
}
