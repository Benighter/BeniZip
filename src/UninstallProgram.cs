using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
[assembly:AssemblyTitle("BeniZip Uninstall")]
[assembly:AssemblyProduct("BeniZip")]
[assembly:AssemblyVersion("1.0.0.0")]
public static class BeniZipUninstall {
 static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","BeniZip");
 static readonly string DataRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BeniZip");
 static void CheckPath(string path){for(string p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("A redirected installation path needs manual review.");}
 static void CheckTree(string path){CheckPath(path);if(!Directory.Exists(path))return;foreach(string child in Directory.GetFileSystemEntries(path)){CheckPath(child);if(Directory.Exists(child))CheckTree(child);}}
 static string PreviousClass(string extension){try{var rows=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>[]>(File.ReadAllText(Path.Combine(DataRoot,"association-backup.json")));foreach(var row in rows)if(string.Equals(row["Extension"] as string,extension,StringComparison.OrdinalIgnoreCase))return row["PerUserClass"] as string;}catch{}return null;}
 [STAThread] public static int Main(string[] args){try{
  CheckPath(Root);if(args.Contains("--dry-run")){Console.WriteLine(new JavaScriptSerializer().Serialize(new{valid=true,installation=Root,installed=File.Exists(Path.Combine(Root,"BeniZip.exe")),preservesUserArchives=true,preservesPreferences=true}));return 0;}
  if(MessageBox.Show("Uninstall BeniZip?\n\nYour archives and saved preferences will be kept.","Uninstall BeniZip",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return 0;
  foreach(var process in Process.GetProcessesByName("BeniZip")){using(process){string file;try{file=process.MainModule.FileName;}catch{continue;}if(file.Equals(Path.Combine(Root,"BeniZip.exe"),StringComparison.OrdinalIgnoreCase)){process.CloseMainWindow();if(!process.WaitForExit(5000))throw new IOException("Close BeniZip and finish or cancel its current operation before uninstalling.");}}}
  CheckTree(Root);
  string[] owned={"Software\\Classes\\BeniZip.Archive","Software\\Classes\\Applications\\BeniZip.exe","Software\\BeniZip","Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\BeniZip","Software\\Classes\\*\\shell\\BeniZip.Open","Software\\Classes\\*\\shell\\BeniZip.Extract","Software\\Classes\\*\\shell\\BeniZip.ExtractHere","Software\\Classes\\*\\shell\\BeniZip.Create","Software\\Classes\\Directory\\shell\\BeniZip.Create"};
  using(var registered=Registry.CurrentUser.OpenSubKey("Software\\RegisteredApplications",true))if(registered!=null)registered.DeleteValue("BeniZip",false);
  foreach(string ext in new[]{".zip",".rar",".7z",".tar",".gz",".tgz",".bz2",".xz",".zst",".cab",".arj",".lzh",".001"}){
   using(var key=Registry.CurrentUser.OpenSubKey("Software\\Classes\\"+ext,true))if(key!=null&&string.Equals(key.GetValue("") as string,"BeniZip.Archive",StringComparison.OrdinalIgnoreCase)){string previous=PreviousClass(ext);if(previous==null)key.DeleteValue("",false);else key.SetValue("",previous,RegistryValueKind.String);}
   using(var key=Registry.CurrentUser.OpenSubKey("Software\\Classes\\"+ext+"\\OpenWithProgids",true))if(key!=null)key.DeleteValue("BeniZip.Archive",false);
  }
  foreach(string key in owned)Registry.CurrentUser.DeleteSubKeyTree(key,false);
  foreach(string folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs")}){string shortcut=Path.Combine(folder,"BeniZip.lnk");if(File.Exists(shortcut))File.Delete(shortcut);}
  if(Directory.Exists(Root))Directory.Delete(Root,true);MessageBox.Show("BeniZip was uninstalled. Your archives and preferences were kept.","BeniZip",MessageBoxButtons.OK,MessageBoxIcon.Information);return 0;
 }catch(Exception ex){MessageBox.Show(ex.Message,"BeniZip uninstall",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 2;}}
}
