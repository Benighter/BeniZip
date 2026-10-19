using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace BeniZip.EngineChecks {
 public sealed class Request {
  public string action,archive,destination,password,collision,format,output,split;
  public string[] inputs,selection;
  public int level=1,cancelAfterMs;
  public bool hideNames,details;
 }
 // A small independent black-box host keeps engine checks separate from UI work.
 public static class EngineRunner {
  public static int Main(string[] args) {
   var json=new JavaScriptSerializer{MaxJsonLength=64*1024*1024};var timer=Stopwatch.StartNew();
   try {
    var request=json.Deserialize<Request>(File.ReadAllText(args.Length==2?args[1]:args[0]));object result=null;
    using(var cancel=new CancellationTokenSource()) {
     if(request.cancelAfterMs>0)cancel.CancelAfter(request.cancelAfterMs);var token=cancel.Token;
     switch(request.action) {
      case "list":var index=ArchiveEngine.List(request.archive,request.password,token);result=request.details?(object)index:new{entries=index.Items.Count,bytes=index.Total};break;
      case "extract":var i=ArchiveEngine.List(request.archive,request.password,token);var files=ArchiveEngine.Extract(i,request.destination,request.selection,request.password,request.collision??"Rename",token,null);result=new{files=files.Count};break;
      case "test":ArchiveEngine.Test(request.archive,request.password,token,null);result=new{passed=true};break;
      case "create":ArchiveEngine.Create(request.inputs,request.output,request.format??"ZIP",request.level,request.password,request.hideNames,request.split,token,null);result=new{created=request.output};break;
      case "add":ArchiveEngine.Edit(ArchiveEngine.List(request.archive,request.password,token),null,request.inputs,request.password,false,token,null);result=new{edited=true};break;
      case "delete":ArchiveEngine.Edit(ArchiveEngine.List(request.archive,request.password,token),request.selection,null,request.password,true,token,null);result=new{edited=true};break;
      case "common-parent":result=new{parent=ArchiveEngine.CommonParent(request.inputs)};break;
      case "stale-index":var stale=ArchiveEngine.List(request.archive,request.password,token);using(var append=new FileStream(request.archive,FileMode.Append,FileAccess.Write))append.WriteByte(0);ArchiveEngine.Extract(stale,request.destination,null,request.password,"Rename",token,null);break;
      default:throw new ArgumentException("Unknown engine test action.");
     }
    }
    Console.WriteLine(json.Serialize(new{ok=true,elapsedMs=timer.Elapsed.TotalMilliseconds,result=result}));return 0;
   } catch(Exception ex) {
    Console.WriteLine(json.Serialize(new{ok=false,elapsedMs=timer.Elapsed.TotalMilliseconds,error=ex.Message,cancelled=ex is OperationCanceledException}));return 2;
   }
  }
 }
}
