using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace BeniZip {
 public sealed class RarRebuildRequiredException : IOException {
  public RarRebuildRequiredException(string message) : base(message) { }
 }
 public static class RarSupport {
  const string ResourceName="BeniZip.RarAdapter";
  const string ExpectedHash="5e8ceaf9ed9da4f8498ac7a87373e2b1af72e4101633a55cd10e18e7a28ec91d";
  static readonly object CacheLock=new object();
  public static bool Available {get{return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BeniZip.Cli.exe"));}}

  static string Hash(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
  static byte[] Resource(){
   Assembly assembly=Assembly.GetExecutingAssembly();Stream stream=assembly.GetManifestResourceStream(ResourceName);
   if(stream==null){string cli=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BeniZip.Cli.exe");ArchiveEngine.NoLinks(cli);if(!File.Exists(cli))throw new IOException("The built-in RAR engine is missing. Reinstall BeniZip.");stream=Assembly.LoadFrom(cli).GetManifestResourceStream(ResourceName);}
   if(stream==null)throw new IOException("The built-in RAR engine is missing. Reinstall BeniZip.");
   using(stream)using(var data=new MemoryStream()){stream.CopyTo(data);return data.ToArray();}
  }
  static string MemberPath(string root,string name){
   string normalized=name.Replace('\\','/');ArchiveEngine.ValidateName(normalized);
   string path=Path.GetFullPath(Path.Combine(root,normalized.Replace('/',Path.DirectorySeparatorChar)));
   if(!path.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid RAR engine package path.");
   return path;
  }
  static void RemoveOwnedDirectory(string path){
   if(!Directory.Exists(path))return;ArchiveEngine.NoLinks(path);var pending=new Stack<string>();pending.Push(path);
   while(pending.Count>0){foreach(string child in Directory.EnumerateFileSystemEntries(pending.Pop())){var attrs=File.GetAttributes(child);if((attrs&FileAttributes.ReparsePoint)!=0)throw new IOException("Temporary RAR data contains a link and was retained for safety.");if((attrs&FileAttributes.Directory)!=0)pending.Push(child);}}
   Directory.Delete(path,true);
  }
  static void VerifyCache(ZipArchive zip,string root){
   var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
   if(zip.Entries.Count>256)throw new IOException("Unexpected RAR engine package contents.");
   foreach(var entry in zip.Entries){
    if(entry.FullName.EndsWith("/"))continue;string path=MemberPath(root,entry.FullName);if(!expected.Add(path))throw new IOException("Duplicate RAR engine package entry.");
    total+=entry.Length;if(total>128L*1024*1024)throw new IOException("The RAR engine package is too large.");ArchiveEngine.NoLinks(path);
    if(!File.Exists(path)||new FileInfo(path).Length!=entry.Length)throw new IOException("The built-in RAR engine is incomplete. Reinstall BeniZip.");
    using(var source=entry.Open())using(var cached=File.OpenRead(path))if(Hash(source)!=Hash(cached))throw new IOException("The built-in RAR engine changed. Reinstall BeniZip.");
   }
   var dirs=new Stack<string>();dirs.Push(root);while(dirs.Count>0){foreach(string child in Directory.EnumerateFileSystemEntries(dirs.Pop())){var attrs=File.GetAttributes(child);if((attrs&FileAttributes.ReparsePoint)!=0)throw new IOException("The built-in RAR engine contains an unexpected link.");if((attrs&FileAttributes.Directory)!=0)dirs.Push(child);else if(!expected.Contains(child))throw new IOException("The built-in RAR engine contains an unexpected file.");}}
  }
  static string Engine(){
   lock(CacheLock){
    byte[] bytes=Resource();using(var stream=new MemoryStream(bytes))if(Hash(stream)!=ExpectedHash)throw new IOException("The built-in RAR engine failed its integrity check.");
    string tools=Path.Combine(Updates.DataRoot,"tools"),root=Path.Combine(tools,"rar-"+ExpectedHash.Substring(0,16));ArchiveEngine.NoLinks(tools);Directory.CreateDirectory(tools);ArchiveEngine.NoLinks(root);
    using(var buffer=new MemoryStream(bytes))using(var zip=new ZipArchive(buffer,ZipArchiveMode.Read)){
     if(!Directory.Exists(root)){
      string stage=Path.Combine(tools,".rar-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
      try{foreach(var entry in zip.Entries){if(entry.FullName.EndsWith("/"))continue;string file=MemberPath(stage,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(file));ArchiveEngine.NoLinks(file);entry.ExtractToFile(file,false);}try{Directory.Move(stage,root);}catch(IOException){if(!Directory.Exists(root))throw;}}
      finally{RemoveOwnedDirectory(stage);}
     }
     VerifyCache(zip,root);
    }
    string exe=Path.Combine(root,"BeniZip.RarAdapter.exe");if(!File.Exists(exe))throw new IOException("The built-in RAR engine is incomplete. Reinstall BeniZip.");return exe;
   }
  }
  public static void Run(Dictionary<string,object> request,CancellationToken token){
   token.ThrowIfCancellationRequested();string exe=Engine(),baseDir=Path.Combine(Updates.DataRoot,"rar-work");ArchiveEngine.NoLinks(baseDir);Directory.CreateDirectory(baseDir);
   string work=Path.Combine(baseDir,"operation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);request["stageDir"]=work;
   try{
    string json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(request),input=Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
    using(var process=new Process()){
     process.StartInfo=start;var output=new StringBuilder();var errors=new StringBuilder();var gate=new object();
     process.OutputDataReceived+=(s,e)=>{if(e.Data!=null)lock(gate)if(output.Length<32768)output.AppendLine(e.Data);};
     process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(gate)if(errors.Length<32768)errors.AppendLine(e.Data);};
     token.ThrowIfCancellationRequested();process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
     using(token.Register(()=>{try{if(!process.HasExited)process.Kill();}catch{}})){
      process.StandardInput.WriteLine(input);process.StandardInput.Close();process.WaitForExit();process.WaitForExit();
     }
     token.ThrowIfCancellationRequested();
     if(process.ExitCode!=0){
      string detail=output.ToString().Trim();try{var result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(detail);string kind=result.ContainsKey("kind")?Convert.ToString(result["kind"]):"";detail=result.ContainsKey("error")?Convert.ToString(result["error"]):detail;if(kind=="PasswordRequired"||kind=="BadPassword")throw new EngineException("A password is required, or the password is incorrect.",true);if(kind=="RarRebuildRequiredError")throw new RarRebuildRequiredException(detail);}catch(EngineException){throw;}catch(RarRebuildRequiredException){throw;}catch{}
      if(string.IsNullOrWhiteSpace(detail))detail=errors.ToString().Trim();if(detail.Length>2000)detail=detail.Substring(0,2000);throw new IOException("The RAR operation could not finish. "+detail);
     }
    }
   }finally{RemoveOwnedDirectory(work);}
  }
 }
}
