using System;
using System.IO;
using Microsoft.Win32;
using NotchNotesWindows;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace BotNotes.Tests
{
 // 每个测试一个临时数据根目录和一个一次性的 HKCU 测试键；Dispose 时一定还原静态并清理。
 internal sealed class Sandbox : IDisposable
 {
  public string Root { get; }
  public string RegRoot { get; }
  public string BotNotesDir { get { return Path.Combine(Root, "BotNotes"); } }
  public string DotNotesDir { get { return Path.Combine(Root, "DotNotes"); } }
  public string NotchNotesWindowsDir { get { return Path.Combine(Root, "NotchNotesWindows"); } }
  public string DocumentsDir { get { return Path.Combine(Root, "Documents"); } }
  public string SaveDir { get { return Path.Combine(DocumentsDir, "BotNotes"); } }

  readonly string oldLocalRoot = MainWindow.LocalRoot;
  readonly string oldDocumentsRoot = MainWindow.DocumentsRoot;
  readonly string oldRunKey = MainWindow.RunKey;
  readonly string oldApprovedKey = MainWindow.ApprovedKey;

  public Sandbox()
  {
   Root = Path.Combine(Path.GetTempPath(), "BotNotesTests", Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(Root);
   MainWindow.LocalRoot = Root;
   MainWindow.DocumentsRoot = DocumentsDir;
   RegRoot = @"Software\BotNotesTests\" + Guid.NewGuid().ToString("N");
   MainWindow.RunKey = RegRoot + @"\Run";
   MainWindow.ApprovedKey = RegRoot + @"\StartupApproved\Run";
  }

  public string WriteFile(string folder, string name, string content)
  {
   Directory.CreateDirectory(folder);
   string path = Path.Combine(folder, name);
   File.WriteAllText(path, content);
   return path;
  }

  public void Dispose()
  {
   MainWindow.LocalRoot = oldLocalRoot;
   MainWindow.DocumentsRoot = oldDocumentsRoot;
   MainWindow.RunKey = oldRunKey;
   MainWindow.ApprovedKey = oldApprovedKey;
   try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
   try { Registry.CurrentUser.DeleteSubKeyTree(RegRoot, false); } catch { }
   try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\BotNotesTests")) if (k != null && k.SubKeyCount == 0 && k.ValueCount == 0) { k.Close(); Registry.CurrentUser.DeleteSubKey(@"Software\BotNotesTests", false); } } catch { }
  }
 }

 internal static class Reg
 {
  public static void SetRun(string name, string value)
  {
   using (var k = Registry.CurrentUser.CreateSubKey(MainWindow.RunKey)) k.SetValue(name, value);
  }

  public static void SetApproved(string name, byte[] value)
  {
   using (var k = Registry.CurrentUser.CreateSubKey(MainWindow.ApprovedKey)) k.SetValue(name, value, RegistryValueKind.Binary);
  }

  public static string GetRun(string name)
  {
   using (var k = Registry.CurrentUser.OpenSubKey(MainWindow.RunKey)) return k == null ? null : k.GetValue(name) as string;
  }

  public static byte[] GetApproved(string name)
  {
   using (var k = Registry.CurrentUser.OpenSubKey(MainWindow.ApprovedKey)) return k == null ? null : k.GetValue(name) as byte[];
  }

  public static bool RunKeyExists()
  {
   using (var k = Registry.CurrentUser.OpenSubKey(MainWindow.RunKey)) return k != null;
  }
 }
}
