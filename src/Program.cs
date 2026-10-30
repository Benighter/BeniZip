using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly:AssemblyTitle("BeniZip")]
[assembly:AssemblyDescription("Native Windows archive manager")]
[assembly:AssemblyCompany("Bennet")]
[assembly:AssemblyProduct("BeniZip")]

namespace BeniZip {
 public sealed class CliRequest {public string action,archive,destination,password,collision,format,output,split;public string[] inputs,selection;public int level=1,cancelAfterMs;public bool hideNames,details,allowRarRebuild;}
 public static class Program {
  [STAThread] public static int Main(string[] args) {try{return Run(args);}catch(Exception ex){try{Directory.CreateDirectory(Updates.DataRoot);File.WriteAllText(Path.Combine(Updates.DataRoot,"last-error.log"),ex.ToString());}catch{}MessageBox.Show(ex.Message,"BeniZip",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 2;}}
  static int Run(string[] args) {
   if(args.Length>=2&&args[0]=="--cli")return Cli(args[1]);
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   ArchiveAssociations.RefreshInstalledRegistration();
   Application.ThreadException+=(s,e)=>MessageBox.Show(e.Exception.Message,"BeniZip",MessageBoxButtons.OK,MessageBoxIcon.Warning);
   string path=args.FirstOrDefault(x=>!x.StartsWith("--"));
   var application=new System.Windows.Application();
   application.DispatcherUnhandledException+=(s,e)=>{try{Directory.CreateDirectory(Updates.DataRoot);File.WriteAllText(Path.Combine(Updates.DataRoot,"last-error.log"),e.Exception.ToString());}catch{}MessageBox.Show("This action could not finish.\n\n"+e.Exception.Message,"BeniZip",MessageBoxButtons.OK,MessageBoxIcon.Warning);e.Handled=true;};
   var main=new ArchiveWindow(args.Contains("--create")&&path!=null?Path.GetDirectoryName(Path.GetFullPath(path)):path);
   if(args.Contains("--create")&&path!=null)main.StartupInputs=new[]{Path.GetFullPath(path)};
   if(args.Contains("--extract-here"))main.StartupAction="extract-here";else if(args.Contains("--extract"))main.StartupAction="extract";
   application.Run(main.Window);return 0;
  }
  static int Cli(string requestPath){var serializer=new JavaScriptSerializer{MaxJsonLength=64*1024*1024};var watch=Stopwatch.StartNew();var cancellation=new CancellationTokenSource();try{
   var request=serializer.Deserialize<CliRequest>(File.ReadAllText(requestPath));object result=null;if(request.cancelAfterMs>0)cancellation.CancelAfter(request.cancelAfterMs);var token=cancellation.Token;
   switch(request.action){
    case "list":var index=ArchiveEngine.List(request.archive,request.password,token);result=request.details?(object)index:new{entries=index.Items.Count,bytes=index.Total};break;
    case "extract":var i=ArchiveEngine.List(request.archive,request.password,token);var files=ArchiveEngine.Extract(i,request.destination,request.selection,request.password,request.collision??"Rename",token,null);result=new{files=files.Count};break;
    case "test":ArchiveEngine.Test(request.archive,request.password,token,null);result=new{passed=true};break;
    case "create":ArchiveEngine.Create(request.inputs,request.output,request.format??"ZIP",request.level,request.password,request.hideNames,request.split,token,null);result=new{created=request.output};break;
    case "delete":ArchiveEngine.Edit(ArchiveEngine.List(request.archive,request.password,token),request.selection,null,request.password,true,token,null,request.allowRarRebuild);result=new{edited=true};break;
    case "add":ArchiveEngine.Edit(ArchiveEngine.List(request.archive,request.password,token),null,request.inputs,request.password,false,token,null,request.allowRarRebuild);result=new{edited=true};break;
    case "verify-update":result=new{version=Updates.Verify(File.ReadAllText(request.archive)).Version.ToString()};break;
    case "verify-update-package":var update=Updates.Verify(File.ReadAllText(request.archive));Updates.VerifyPackage(update,request.output);result=new{version=update.Version.ToString(),verified=true};break;
    default:throw new ArgumentException("Unknown CLI action.");
   }
   Console.WriteLine(serializer.Serialize(new{ok=true,elapsedMs=watch.Elapsed.TotalMilliseconds,result=result}));return 0;
  }catch(Exception ex){Console.WriteLine(serializer.Serialize(new{ok=false,elapsedMs=watch.Elapsed.TotalMilliseconds,error=ex.Message}));return 2;}finally{cancellation.Dispose();}}
 }
}
