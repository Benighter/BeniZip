using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BeniZip {
 public sealed class SignedEnvelope {public string payload;public string signature;}
 public sealed class ReleasePayload {public string applicationId;public string version;public string packageUri;public long bytes;public string sha256;public string notes;}
 public sealed class UpdateInfo {
  public ReleasePayload Release;public string Envelope;
  public Version Version{get{return new Version(Release.version);}}public long Size{get{return Release.bytes;}}public string Notes{get{return Release.notes;}}
 }
 public sealed class ChannelConfig {public string feed;}
 public static class Updates {
  public const string DefaultFeed="https://github.com/Benighter/BeniZip/releases/latest/download/manifest.json";
  public static string DataRoot{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BeniZip");}}
  static DateTime lastCheck=DateTime.MinValue;static bool checking;
  public static string Hash(string path){using(var sha=SHA256.Create())using(var s=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
  public static UpdateInfo Verify(string json) {
   if(json.Length>262144)throw new InvalidDataException("Update manifest is too large.");
   var serializer=new JavaScriptSerializer();var env=serializer.Deserialize<SignedEnvelope>(json);
   if(env==null||env.payload==null||env.signature==null)throw new InvalidDataException("The update manifest is incomplete.");
   byte[] bytes=Convert.FromBase64String(env.payload),signature=Convert.FromBase64String(env.signature);
   string key;using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("BeniZip.ReleaseKey")))key=r.ReadToEnd();
   using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(key);if(!rsa.VerifyData(bytes,CryptoConfig.MapNameToOID("SHA256"),signature))throw new InvalidDataException("Update signature does not match BeniZip's release key.");}
   var payload=serializer.Deserialize<ReleasePayload>(Encoding.UTF8.GetString(bytes));Version version;Uri uri;
   if(payload.applicationId!="BeniZip.Windows.x64"||!Version.TryParse(payload.version,out version)||payload.bytes<=0||payload.bytes>256L*1024*1024||payload.sha256==null||!System.Text.RegularExpressions.Regex.IsMatch(payload.sha256,"^[a-fA-F0-9]{64}$")||!Uri.TryCreate(payload.packageUri,UriKind.Absolute,out uri)||(!uri.IsFile&&uri.Scheme!="https"))throw new InvalidDataException("The update is not a valid BeniZip Windows release.");
   return new UpdateInfo{Release=payload,Envelope=json};
  }
  static string ReadFeed(string url) {
   var uri=new Uri(url);if(uri.IsFile)return File.ReadAllText(uri.LocalPath);
   if(uri.Scheme!="https")throw new InvalidDataException("Online update feeds must use HTTPS.");
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;var request=(HttpWebRequest)WebRequest.Create(uri);request.Timeout=15000;request.ReadWriteTimeout=15000;
   using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){char[] chars=new char[262145];int count=reader.ReadBlock(chars,0,chars.Length);if(count>262144)throw new InvalidDataException("Update manifest is too large.");return new string(chars,0,count);}
  }
  public static async Task<UpdateInfo> CheckAsync(bool manual) {
   if(checking||(!manual&&(DateTime.UtcNow-lastCheck).TotalHours<6))return null;checking=true;lastCheck=DateTime.UtcNow;
   try {
    string config=Path.Combine(DataRoot,"channel.json"),feed=DefaultFeed;
    if(File.Exists(config)){var channel=new JavaScriptSerializer().Deserialize<ChannelConfig>(File.ReadAllText(config));if(channel!=null&&!string.IsNullOrWhiteSpace(channel.feed))feed=channel.feed;}
    var legacy=new Uri(Path.Combine(DataRoot,"releases","manifest.json")).AbsoluteUri;
    if(string.Equals(feed,legacy,StringComparison.OrdinalIgnoreCase)){feed=DefaultFeed;File.WriteAllText(config,new JavaScriptSerializer().Serialize(new ChannelConfig{feed=feed}));}
    var update=await Task.Run(()=>Verify(ReadFeed(feed)));
    return update.Version>Assembly.GetExecutingAssembly().GetName().Version?update:null;
   }finally{checking=false;}
  }
  public static async Task DownloadAndInstall(UpdateInfo info,CancellationToken token) {
   string cache=Path.Combine(DataRoot,"update-cache");Directory.CreateDirectory(cache);string package=Path.Combine(cache,"release-"+info.Version+".zip"),manifest=Path.Combine(cache,"release-"+info.Version+".json");
   await Task.Run(()=>{
    var checkedInfo=Verify(info.Envelope);var uri=new Uri(checkedInfo.Release.packageUri);
    using(var output=new FileStream(package,FileMode.Create,FileAccess.Write,FileShare.None)) {
     if(uri.IsFile){using(var input=File.OpenRead(uri.LocalPath))CopyBounded(input,output,info.Size,token);}
     else{ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;var request=(HttpWebRequest)WebRequest.Create(uri);request.Timeout=15000;request.ReadWriteTimeout=15000;using(token.Register(()=>request.Abort()))using(var response=request.GetResponse())using(var input=response.GetResponseStream())CopyBounded(input,output,info.Size,token);}
    }
    VerifyPackage(info,package);File.WriteAllText(manifest,info.Envelope,new UTF8Encoding(false));
   },token);
   token.ThrowIfCancellationRequested();
   string helper=Path.Combine(cache,"BeniZip.Update-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BeniZip.Update.exe"),helper);
   Process.Start(UpdateHelperStartInfo(helper,manifest,package,AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'),Process.GetCurrentProcess().Id));
  }
  static ProcessStartInfo UpdateHelperStartInfo(string helper,string manifest,string package,string root,int processId){
   // Never inherit the app's working directory: Windows holds it open and
   // the helper then cannot rename the installation directory during an update.
   return new ProcessStartInfo(helper,string.Join(" ",new[]{manifest,package,root,processId.ToString()}.Select(ArchiveEngine.Quote))){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(Path.GetFullPath(helper))};
  }
  static void CopyBounded(Stream input,Stream output,long expected,CancellationToken token){var buffer=new byte[1024*1024];long total=0;int n;while((n=input.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();total+=n;if(total>expected)throw new InvalidDataException("Downloaded update exceeds its signed size.");output.Write(buffer,0,n);}if(total!=expected)throw new InvalidDataException("Downloaded update is incomplete.");}
  public static void VerifyPackage(UpdateInfo info,string package){if(new FileInfo(package).Length!=info.Size||!Hash(package).Equals(info.Release.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The downloaded update does not match its signed checksum or size.");}
  public static void Apply(string manifest,string package,string root,int processId) {
   string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","BeniZip");root=Path.GetFullPath(root).TrimEnd('\\');if(!root.Equals(expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Updates can replace only the registered per-user BeniZip installation.");
   ArchiveEngine.NoLinks(root);var info=Verify(File.ReadAllText(manifest));VerifyPackage(info,package);
   Version installed=new Version(FileVersionInfo.GetVersionInfo(Path.Combine(root,"BeniZip.exe")).FileVersion);if(info.Version<=installed)throw new InvalidDataException("A release must be newer than the installed version.");
   try{using(var old=Process.GetProcessById(processId)){if(!old.WaitForExit(45000))throw new IOException("BeniZip is still running.");}}catch(ArgumentException){}
   foreach(var p in Process.GetProcessesByName("BeniZip")){using(p){try{if(p.MainModule.FileName.Equals(Path.Combine(root,"BeniZip.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("Close other BeniZip windows before installing the update.");}catch(System.ComponentModel.Win32Exception){}}}
   string stage=Path.Combine(Path.GetDirectoryName(root),".BeniZip-update-"+Guid.NewGuid().ToString("N")),backup=root+".rollback";if(Directory.Exists(backup))throw new IOException("A previous rollback folder needs review before another update.");Directory.CreateDirectory(stage);
   bool replaced=false;try{
    using(var zip=ZipFile.OpenRead(package)){
     if(zip.Entries.Count>200)throw new InvalidDataException("Unexpected update contents.");long size=0;
     string[] allowed={"BeniZip.exe","BeniZip.Update.exe","BeniZip.Cli.exe","BeniZip.ico","BeniZip.exe.config","BeniZip.Cli.exe.config","BeniZip.Update.exe.config","assets/icon.png","engine/7z.exe","engine/7z.dll","engine/License.txt","engine/source-url.txt"};
     foreach(var e in zip.Entries){if(e.FullName.EndsWith("/"))continue;ArchiveEngine.ValidateName(e.FullName);if(!allowed.Contains(e.FullName,StringComparer.OrdinalIgnoreCase)||((e.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Unexpected update member: "+e.FullName);size+=e.Length;if(size>256L*1024*1024)throw new InvalidDataException("Update is too large.");string file=Path.Combine(stage,e.FullName.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(file));e.ExtractToFile(file,false);}
    }
    string app=Path.Combine(stage,"BeniZip.exe");if(!File.Exists(app)||new Version(FileVersionInfo.GetVersionInfo(app).FileVersion)!=info.Version||!File.Exists(Path.Combine(stage,"engine","7z.dll")))throw new InvalidDataException("The package identity or version does not match the signed release.");
    try{Directory.Move(root,backup);}catch(IOException ex){throw new IOException("The BeniZip installation folder is in use. Close other BeniZip windows and programs using its installation folder, then try again.",ex);}try{Directory.Move(stage,root);replaced=true;}catch{Directory.Move(backup,root);throw;}
    Process.Start(new ProcessStartInfo(Path.Combine(root,"BeniZip.exe"),"--updated"){UseShellExecute=true});
    Directory.CreateDirectory(DataRoot);File.WriteAllText(Path.Combine(DataRoot,"last-update.json"),new JavaScriptSerializer().Serialize(new{version=info.Release.version,sha256=info.Release.sha256,installedPath=root,completedUtc=DateTime.UtcNow.ToString("o"),signatureVerified=true}));
    try{Directory.Delete(backup,true);File.Delete(package);File.Delete(manifest);}catch{}
   }catch{if(replaced&&Directory.Exists(backup)){try{Directory.Move(root,stage);Directory.Move(backup,root);}catch{}}throw;}
   finally{try{if(Directory.Exists(stage))Directory.Delete(stage,true);}catch{}}
  }
 }
}
