using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace BeniZip {
 public sealed class AssociationPreference {public string[] extensions;public string registeredVersion;}
 public sealed class PreviousAssociation {public string Extension,PerUserClass,UserChoice;}
 public static class ArchiveAssociations {
  public static readonly string[] Extensions={".zip",".rar",".7z",".tar",".gz",".tgz",".bz2",".xz",".zst",".cab",".arj",".lzh",".001"};
  public static string InstallRoot{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","BeniZip");}}
  public static string AppPath{get{return Path.Combine(InstallRoot,"BeniZip.exe");}}
  static string PreferencePath{get{return Path.Combine(Updates.DataRoot,"associations.json");}}
  [DllImport("shlwapi.dll",CharSet=CharSet.Unicode)]static extern int AssocQueryString(uint flags,uint kind,string association,string verb,StringBuilder output,ref uint size);
  [DllImport("shell32.dll")]static extern void SHChangeNotify(uint e,uint f,IntPtr a,IntPtr b);
  public static string DefaultExecutable(string extension){var text=new StringBuilder(4096);uint size=4096;return AssocQueryString(0,2,extension,"open",text,ref size)==0?text.ToString():"";}
  public static bool IsDefault(string extension){return string.Equals(DefaultExecutable(extension),AppPath,StringComparison.OrdinalIgnoreCase);}
  public static string[] Selected(){try{var prefs=new JavaScriptSerializer().Deserialize<AssociationPreference>(File.ReadAllText(PreferencePath));if(prefs!=null&&prefs.extensions!=null)return Extensions.Where(x=>prefs.extensions.Contains(x,StringComparer.OrdinalIgnoreCase)).ToArray();}catch{}return (string[])Extensions.Clone();}
  static void Set(string path,string name,object value){using(var key=Registry.CurrentUser.CreateSubKey(path)){key.SetValue(name,value,value is int?RegistryValueKind.DWord:RegistryValueKind.String);}}
  static string Read(string path,string name){using(var key=Registry.CurrentUser.OpenSubKey(path))return key==null?null:key.GetValue(name) as string;}
  static void Delete(string path,string name){using(var key=Registry.CurrentUser.OpenSubKey(path,true))if(key!=null)key.DeleteValue(name,false);}
  public static void RefreshInstalledRegistration(){
   if(!string.Equals(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\'),InstallRoot,StringComparison.OrdinalIgnoreCase))return;
   try{var version=FileVersionInfo.GetVersionInfo(AppPath).FileVersion;AssociationPreference prefs=null;if(File.Exists(PreferencePath))prefs=new JavaScriptSerializer().Deserialize<AssociationPreference>(File.ReadAllText(PreferencePath));if(prefs==null||prefs.registeredVersion!=version)Register(Selected());}catch(Exception ex){try{Directory.CreateDirectory(Updates.DataRoot);File.WriteAllText(Path.Combine(Updates.DataRoot,"registration-error.log"),ex.Message);}catch{}}
  }
  public static void Register(string[] selected){
   if(!File.Exists(AppPath))throw new IOException("Install BeniZip before choosing archive types.");
   ArchiveEngine.NoLinks(InstallRoot);Directory.CreateDirectory(Updates.DataRoot);
   selected=Extensions.Where(x=>selected.Contains(x,StringComparer.OrdinalIgnoreCase)).ToArray();
   var serializer=new JavaScriptSerializer();string backup=Path.Combine(Updates.DataRoot,"association-backup.json");
   if(!File.Exists(backup)){var rows=Extensions.Select(ext=>new PreviousAssociation{Extension=ext,PerUserClass=Read("Software\\Classes\\"+ext,""),UserChoice=Read("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\"+ext+"\\UserChoice","ProgId")}).ToArray();File.WriteAllText(backup,serializer.Serialize(rows));}
   PreviousAssociation[] previous=serializer.Deserialize<PreviousAssociation[]>(File.ReadAllText(backup));
   string quoted="\""+AppPath+"\"",prog="Software\\Classes\\BeniZip.Archive";
   Set(prog,"","BeniZip archive");Set(prog+"\\DefaultIcon","",quoted+",0");Set(prog+"\\shell\\open\\command","",quoted+" \"%1\"");
   Set(prog+"\\Application","ApplicationName","BeniZip");Set(prog+"\\Application","ApplicationDescription","View, extract, test and create archives");Set(prog+"\\Application","ApplicationIcon",AppPath+",0");
   if(Read(prog+"\\Application","AppUserModelID")=="Bennet.BeniZip")Delete(prog+"\\Application","AppUserModelID");
   string applications="Software\\Classes\\Applications\\BeniZip.exe";
   Set(applications,"FriendlyAppName","BeniZip");Set(applications+"\\shell\\open\\command","",quoted+" \"%1\"");Set(applications+"\\Application","ApplicationName","BeniZip");Set(applications+"\\Application","ApplicationIcon",AppPath+",0");
   Set("Software\\BeniZip\\Capabilities","ApplicationName","BeniZip");Set("Software\\BeniZip\\Capabilities","ApplicationDescription","View, extract, test and create archives");Set("Software\\BeniZip\\Capabilities","ApplicationIcon",quoted+",0");
   foreach(string ext in Extensions){
    Set(applications+"\\SupportedTypes",ext,"");Set("Software\\BeniZip\\Capabilities\\FileAssociations",ext,"BeniZip.Archive");Set("Software\\Classes\\"+ext+"\\OpenWithProgids","BeniZip.Archive","");
    if(selected.Contains(ext))Set("Software\\Classes\\"+ext,"","BeniZip.Archive");
    else if(Read("Software\\Classes\\"+ext,"")=="BeniZip.Archive"){var old=previous.FirstOrDefault(x=>x.Extension==ext);if(old==null||old.PerUserClass==null)Delete("Software\\Classes\\"+ext,"");else Set("Software\\Classes\\"+ext,"",old.PerUserClass);}
   }
   Set("Software\\RegisteredApplications","BeniZip","Software\\BeniZip\\Capabilities");
   string[] names={"BeniZip.Open","BeniZip.Extract","BeniZip.ExtractHere"},labels={"Open with BeniZip","Extract with BeniZip…","Extract here with BeniZip"},flags={"","--extract","--extract-here"};
   string applies=string.Join(" OR ",Extensions.Select(x=>"System.FileExtension:=\""+x+"\""));
   for(int i=0;i<names.Length;i++){string path="Software\\Classes\\*\\shell\\"+names[i];Set(path,"",labels[i]);Set(path,"Icon",AppPath);Set(path,"AppliesTo",applies);Set(path+"\\command","",quoted+" "+flags[i]+" \"%1\"");}
   foreach(string kind in new[]{"*","Directory"}){string path="Software\\Classes\\"+kind+"\\shell\\BeniZip.Create";Set(path,"","Compress with BeniZip…");Set(path,"Icon",AppPath);Set(path+"\\command","",quoted+" --create \"%1\"");}
   string arp="Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\BeniZip",uninstaller=Path.Combine(Updates.DataRoot,"tools","BeniZip.Uninstall.exe");
   Set(arp,"DisplayName","BeniZip");Set(arp,"DisplayVersion",FileVersionInfo.GetVersionInfo(AppPath).FileVersion);Set(arp,"Publisher","Bennet Nkolele");Set(arp,"InstallLocation",InstallRoot);Set(arp,"DisplayIcon",AppPath+",0");Set(arp,"UninstallString","\""+uninstaller+"\"");Set(arp,"NoModify",1);Set(arp,"NoRepair",1);
   File.WriteAllText(PreferencePath,serializer.Serialize(new AssociationPreference{extensions=selected,registeredVersion=FileVersionInfo.GetVersionInfo(AppPath).FileVersion}));
   SHChangeNotify(0x08000000,0x1000,IntPtr.Zero,IntPtr.Zero);
  }
  public static void OpenWindowsDefaults(){Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=BeniZip"){UseShellExecute=true});}
  public static void CreateShortcut(string folder){Directory.CreateDirectory(folder);object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));object shortcut=null;try{shortcut=shell.GetType().InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{Path.Combine(folder,"BeniZip.lnk")});var type=shortcut.GetType();type.InvokeMember("TargetPath",System.Reflection.BindingFlags.SetProperty,null,shortcut,new object[]{AppPath});type.InvokeMember("WorkingDirectory",System.Reflection.BindingFlags.SetProperty,null,shortcut,new object[]{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)});type.InvokeMember("IconLocation",System.Reflection.BindingFlags.SetProperty,null,shortcut,new object[]{AppPath+",0"});type.InvokeMember("Save",System.Reflection.BindingFlags.InvokeMethod,null,shortcut,null);}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);Marshal.FinalReleaseComObject(shell);}}
 }
 public sealed class AssociationOptions : StackPanel {
  readonly Dictionary<string,CheckBox> choices=new Dictionary<string,CheckBox>();readonly Dictionary<string,TextBlock> statuses=new Dictionary<string,TextBlock>();
  public AssociationOptions(bool all){
   var tools=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,10)};foreach(string label in new[]{"Select all","Clear"}){var button=new Button{Content=label,Padding=new Thickness(10,4,10,4),Margin=new Thickness(0,0,8,0)};bool value=label=="Select all";button.Click+=(s,e)=>{foreach(var check in choices.Values)check.IsChecked=value;};tools.Children.Add(button);}Children.Add(tools);
   string[] selected=ArchiveAssociations.Selected();foreach(string ext in ArchiveAssociations.Extensions){var row=new DockPanel{Margin=new Thickness(0,4,0,4)};var check=new CheckBox{Content=ext.ToUpperInvariant(),IsChecked=all||selected.Contains(ext),Width=110,VerticalContentAlignment=VerticalAlignment.Center};var status=new TextBlock{FontSize=12,VerticalAlignment=VerticalAlignment.Center,Foreground=new SolidColorBrush(Color.FromRgb(100,113,123))};choices.Add(ext,check);statuses.Add(ext,status);row.Children.Add(check);row.Children.Add(status);Children.Add(row);}Refresh();
  }
  public string[] Selected{get{return choices.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToArray();}}
  public void Refresh(){foreach(string ext in ArchiveAssociations.Extensions)statuses[ext].Text=ArchiveAssociations.IsDefault(ext)?"Opens in BeniZip":"Windows confirmation may be needed";}
 }
}
