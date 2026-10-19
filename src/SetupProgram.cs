using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

[assembly:AssemblyTitle("BeniZip Setup")]
[assembly:AssemblyProduct("BeniZip")]
namespace BeniZip {
 public static class SetupProgram {
  static readonly string[] Files={"BeniZip.exe","BeniZip.Update.exe","BeniZip.Cli.exe","BeniZip.ico","BeniZip.exe.config","BeniZip.Cli.exe.config","BeniZip.Update.exe.config","assets/icon.png","engine/7z.exe","engine/7z.dll","engine/License.txt","engine/source-url.txt"};
  static void Resource(string name,string path){using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))using(var output=File.Create(path))input.CopyTo(output);}
  static UpdateInfo Prepare(string cache,string stage){
   ArchiveEngine.NoLinks(cache);ArchiveEngine.NoLinks(stage);Directory.CreateDirectory(cache);
   string package=Path.Combine(cache,"package.zip"),manifest=Path.Combine(cache,"manifest.json");Resource("BeniZip.Package",package);Resource("BeniZip.Manifest",manifest);
   var info=Updates.Verify(File.ReadAllText(manifest));Updates.VerifyPackage(info,package);if(Directory.Exists(stage)||File.Exists(stage))throw new IOException("The staging folder must be new.");Directory.CreateDirectory(stage);
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;using(var zip=ZipFile.OpenRead(package))foreach(var entry in zip.Entries){if(entry.FullName.EndsWith("/"))continue;ArchiveEngine.ValidateName(entry.FullName);if(!Files.Contains(entry.FullName,StringComparer.OrdinalIgnoreCase)||!seen.Add(entry.FullName)||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Unexpected installer contents.");total+=entry.Length;if(total>256L*1024*1024)throw new InvalidDataException("The installer payload is too large.");string path=Path.Combine(stage,entry.FullName.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(path));entry.ExtractToFile(path,false);}
   if(seen.Count!=Files.Length||new Version(FileVersionInfo.GetVersionInfo(Path.Combine(stage,"BeniZip.exe")).FileVersion)!=info.Version)throw new InvalidDataException("The installer payload is incomplete or has the wrong version.");return info;
  }
  static void ValidateExisting(string root){ArchiveEngine.NoLinks(root);if(!Directory.Exists(root))return;foreach(string child in Directory.GetFileSystemEntries(root)){ArchiveEngine.NoLinks(child);if(Directory.Exists(child))ValidateExisting(child);else{string relative=child.Substring(ArchiveAssociations.InstallRoot.Length+1).Replace('\\','/');if(!Files.Contains(relative,StringComparer.OrdinalIgnoreCase))throw new IOException("The installation contains an extra file. Keep it elsewhere before installing: "+Path.GetFileName(child));}}}
  static void Install(string[] extensions,bool desktop){
   string root=ArchiveAssociations.InstallRoot,app=ArchiveAssociations.AppPath;
   foreach(var process in Process.GetProcessesByName("BeniZip")){using(process){try{if(string.Equals(process.MainModule.FileName,app,StringComparison.OrdinalIgnoreCase))throw new IOException("Close BeniZip and finish any archive operation before installing.");}catch(System.ComponentModel.Win32Exception){}}}
   ValidateExisting(root);ArchiveEngine.NoLinks(Updates.DataRoot);
   string cache=Path.Combine(Updates.DataRoot,"setup-"+Guid.NewGuid().ToString("N")),stage=Path.Combine(Path.GetDirectoryName(root),".BeniZip-install-"+Guid.NewGuid().ToString("N")),backup=root+".setup-backup";bool replaced=false;
   if(Directory.Exists(backup))throw new IOException("An earlier setup backup needs review before installing again.");
   try{
    var info=Prepare(cache,stage);if(File.Exists(app)&&new Version(FileVersionInfo.GetVersionInfo(app).FileVersion)>info.Version)throw new IOException("A newer BeniZip is already installed.");
    if(Directory.Exists(root))Directory.Move(root,backup);try{Directory.Move(stage,root);replaced=true;}catch{if(Directory.Exists(backup))Directory.Move(backup,root);throw;}
    string tools=Path.Combine(Updates.DataRoot,"tools");ArchiveEngine.NoLinks(tools);Directory.CreateDirectory(tools);Resource("BeniZip.Uninstaller",Path.Combine(tools,"BeniZip.Uninstall.exe"));
    ArchiveAssociations.Register(extensions);ArchiveAssociations.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),"Programs"));if(desktop)ArchiveAssociations.CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    File.WriteAllText(Path.Combine(Updates.DataRoot,"last-install.json"),new JavaScriptSerializer().Serialize(new{version=info.Release.version,sha256=info.Release.sha256,signatureVerified=true,extensions=extensions,completedUtc=DateTime.UtcNow.ToString("o")}));
    if(Directory.Exists(backup))Directory.Delete(backup,true);
   }catch{if(replaced&&Directory.Exists(backup)){try{Directory.Move(root,stage);Directory.Move(backup,root);}catch{}}throw;}
   finally{try{if(Directory.Exists(stage))Directory.Delete(stage,true);if(Directory.Exists(cache))Directory.Delete(cache,true);}catch{}}
  }
  [STAThread]public static int Main(string[] args){try{
   if(args.Length==3&&args[0]=="--verify-payload"){
    string parent=Path.GetFullPath(args[1]);if(!Directory.Exists(parent))throw new IOException("Create the verification directory first.");string stage=Path.Combine(parent,"setup-payload-"+Guid.NewGuid().ToString("N")),cache=Path.Combine(parent,"setup-cache-"+Guid.NewGuid().ToString("N"));
    try{var info=Prepare(cache,stage);File.WriteAllText(args[2],new JavaScriptSerializer().Serialize(new{verified=true,version=info.Release.version,files=Directory.GetFiles(stage,"*",SearchOption.AllDirectories).Length,sha256=info.Release.sha256}));}finally{if(Directory.Exists(stage))Directory.Delete(stage,true);if(Directory.Exists(cache))Directory.Delete(cache,true);}return 0;
   }
   var application=new Application();var window=new Window{Title="Install BeniZip",Width=580,Height=760,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=Brushes.White,FontFamily=new FontFamily("Segoe UI"),FontSize=14};
   var panel=new StackPanel{Margin=new Thickness(30)};window.Content=panel;panel.Children.Add(new TextBlock{Text="BeniZip",FontSize=30,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(25,45,53))});panel.Children.Add(new TextBlock{Text="Your archives, simply.",Margin=new Thickness(0,5,0,18),Foreground=Brushes.DimGray});
   panel.Children.Add(new TextBlock{Text="Open these archive types with BeniZip",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});var choices=new AssociationOptions(true);panel.Children.Add(choices);
   var desktop=new CheckBox{Content="Create a desktop shortcut",IsChecked=true,Margin=new Thickness(0,14,0,8)};panel.Children.Add(desktop);var windows=new CheckBox{Content="Open Windows confirmation for existing defaults",IsChecked=true};panel.Children.Add(windows);
   var status=new TextBlock{Text="Installs for your account. Windows may ask you to confirm an existing default app.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DimGray,Margin=new Thickness(0,18,0,12)};panel.Children.Add(status);
   var progress=new ProgressBar{Height=4,Minimum=0,Maximum=100,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,12)};panel.Children.Add(progress);
   var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=new Button{Content="Cancel",Padding=new Thickness(18,8,18,8),Margin=new Thickness(0,0,10,0)};var install=new Button{Content="Install BeniZip",Padding=new Thickness(18,8,18,8),IsDefault=true};buttons.Children.Add(cancel);buttons.Children.Add(install);panel.Children.Add(buttons);bool working=false,finished=false;string[] selected=null;
   window.Closing+=(s,e)=>{if(working)e.Cancel=true;};cancel.Click+=(s,e)=>window.Close();install.Click+=async(s,e)=>{
    if(finished){window.Close();Process.Start(new ProcessStartInfo(ArchiveAssociations.AppPath){UseShellExecute=true});if(windows.IsChecked==true&&selected.Any(x=>!ArchiveAssociations.IsDefault(x)))ArchiveAssociations.OpenWindowsDefaults();return;}
    selected=choices.Selected;bool shortcut=desktop.IsChecked==true;working=true;install.IsEnabled=false;cancel.IsEnabled=false;choices.IsEnabled=false;desktop.IsEnabled=false;windows.IsEnabled=false;progress.Visibility=Visibility.Visible;progress.IsIndeterminate=true;status.Text="Verifying and installing BeniZip…";
    try{await Task.Run(()=>Install(selected,shortcut));finished=true;choices.Refresh();int count=selected.Count(ArchiveAssociations.IsDefault);status.Text="BeniZip is installed. "+selected.Length+" archive types selected; "+count+" currently open in BeniZip."+(count<selected.Length?" Windows confirmation is needed for the remaining defaults.":"");install.Content="Finish";progress.IsIndeterminate=false;progress.Value=100;windows.IsEnabled=true;}
    catch(Exception ex){status.Text=ex.Message;choices.IsEnabled=true;desktop.IsEnabled=true;windows.IsEnabled=true;}finally{working=false;install.IsEnabled=true;cancel.IsEnabled=true;}
   };application.Run(window);return 0;
  }catch(Exception ex){MessageBox.Show(ex.Message,"BeniZip Setup",MessageBoxButton.OK,MessageBoxImage.Warning);return 2;}}
 }
}
