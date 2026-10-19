using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BeniZip {
 public sealed class ArchiveItem {
  public string Path, Modified, Crc, Method;
  internal string SourcePath;
  public long Size, Packed;
  public bool Folder, Encrypted, Link;
  public string Name { get { return System.IO.Path.GetFileName(Path.TrimEnd('/')); } }
 }
 public sealed class ArchiveIndex {
  public string File, Format; public long SourceLength;public DateTime SourceModified;
  public List<ArchiveItem> Items = new List<ArchiveItem>();
  public long Total { get { return Items.Where(x => !x.Folder).Sum(x => x.Size); } }
 }
 public sealed class EngineException : Exception {
  public bool PasswordRequired;
  public EngineException(string message, bool password) : base(message) { PasswordRequired = password; }
 }
 public sealed class ProgressInfo {
  public int Percent; public string Message;
  // These optional fields contain only values actually reported by the engine.
  // Its entry counter can include the current entry; it is not a byte count or
  // a count of files already published into the final destination.
  public string CurrentFile;
  public long? ProcessedEntries;
  public ProgressInfo(int p, string m) : this(p,m,null,null) { }
  public ProgressInfo(int p,string m,string currentFile,long? processedEntries) {Percent=p;Message=m;CurrentFile=currentFile;ProcessedEntries=processedEntries;}
 }
 public static class ArchiveEngine {
  static readonly Regex ProgressLine=new Regex(@"^\s*(?<percent>\d{1,3})%(?:\s+(?<entries>\d+))?(?: [+-] (?<file>.+))?\s*$",RegexOptions.CultureInvariant);
  public static string Seven { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"engine","7z.exe"); } }
  public static string Rar { get {
   string p=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WinRAR","Rar.exe");
   return System.IO.File.Exists(p)?p:null;
  } }
  public static bool IsArchive(string path) {
   string e=System.IO.Path.GetExtension(path).ToLowerInvariant();
   return new[]{".zip",".rar",".7z",".tar",".gz",".tgz",".bz2",".xz",".zst",".cab",".iso",".jar",".arj",".lzh",".001",".wim",".esd",".vhd",".img",".epub",".apk",".msix",".appx"}.Contains(e);
  }
  public static string Quote(string value) {
   if(value==null) value="";
   StringBuilder s=new StringBuilder("\""); int n=0;
   foreach(char c in value) { if(c=='\\') {n++;continue;} if(c=='\"') {s.Append('\\',n*2+1).Append(c);n=0;continue;} s.Append('\\',n).Append(c); n=0; }
   return s.Append('\\',n*2).Append('"').ToString();
  }
  public static string Run(string exe,IEnumerable<string> args,string cwd,CancellationToken token,Action<ProgressInfo> progress,bool listing) {
   var start=new ProcessStartInfo(exe,string.Join(" ",args.Select(Quote))) {
    UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true,
    StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8, WorkingDirectory=cwd??Environment.CurrentDirectory
   };
   using(var p=new Process()) {
    p.StartInfo=start; var output=new StringBuilder(); var errors=new StringBuilder(); var gate=new object();
    p.OutputDataReceived+=(s,e)=> { if(e.Data==null)return; lock(gate) { if(listing || output.Length<131072) output.AppendLine(e.Data); }
     if(progress!=null) {var m=ProgressLine.Match(e.Data);if(m.Success){long count;long? entries=long.TryParse(m.Groups["entries"].Value,NumberStyles.None,CultureInfo.InvariantCulture,out count)?(long?)count:null;string file=m.Groups["file"].Success?m.Groups["file"].Value:null;progress(new ProgressInfo(Math.Min(100,int.Parse(m.Groups["percent"].Value,CultureInfo.InvariantCulture)),"Processing archive",file,entries));}}
    };
    p.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(gate)if(errors.Length<32768)errors.AppendLine(e.Data);};
    token.ThrowIfCancellationRequested(); p.Start(); p.StandardInput.Close(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
    using(token.Register(()=>{try{if(!p.HasExited)p.Kill();}catch{}})) { p.WaitForExit(); p.WaitForExit(); }
    token.ThrowIfCancellationRequested();
    if(p.ExitCode!=0) {
     string details=(errors.ToString()+"\n"+output.ToString()).Trim();
     bool pw=Regex.IsMatch(details,"password|encrypted",RegexOptions.IgnoreCase);
     if(pw)throw new EngineException("A password is required, or the password is incorrect.",true);
     if(details.Length>2400) details=details.Substring(details.Length-2400);
     throw new EngineException("The archive operation did not complete (code "+p.ExitCode+").\n"+details,false);
    }
    return output.ToString();
   }
  }
  static List<string> BaseArgs(string command,string password) {
   var a=new List<string>{command,"-sccUTF-8","-scsUTF-8","-mmt=on","-y","-spd"};
   // A closed stdin prevents an invisible password prompt from hanging the app.
   a.Add("-p"+(password??"BeniZip-no-password-supplied")); return a;
  }
  public static ArchiveIndex List(string file,string password,CancellationToken token) {
   return ListCore(file,password,token,true);
  }
  static ArchiveIndex ListCore(string file,string password,CancellationToken token,bool fastZip) {
   file=System.IO.Path.GetFullPath(file);var identity=new FileInfo(file);var index=new ArchiveIndex{File=file,Format=System.IO.Path.GetExtension(file).TrimStart('.').ToUpperInvariant(),SourceLength=identity.Length,SourceModified=identity.LastWriteTimeUtc};
   // Direct central-directory reads avoid launching a process for ordinary ZIP browsing.
   if(fastZip&&new[]{".zip",".jar",".epub",".apk",".msix",".appx"}.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant())) {
    try {
     using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
     using(var zip=new ZipArchive(stream,ZipArchiveMode.Read,false)) {
      foreach(var e in zip.Entries) {
       token.ThrowIfCancellationRequested(); string raw=e.FullName.Replace('\\','/'); var item=new ArchiveItem{SourcePath=raw,Path=NormalizeName(raw),Size=e.Length,Packed=e.CompressedLength,Folder=e.FullName.EndsWith("/")||e.FullName.EndsWith("\\"),Modified=e.LastWriteTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),Crc="",Method="ZIP"};
       // Unix symbolic links are represented in the high mode bits.
       item.Link=((e.ExternalAttributes>>16)&0xF000)==0xA000 || (e.ExternalAttributes&0x400)!=0;
       if(item.Path.Length!=0||!item.Folder)index.Items.Add(item);
      }
     }
     return index;
    } catch(InvalidDataException) { index.Items.Clear(); }
   }
   var args=BaseArgs("l",password); args.AddRange(new[]{"-slt","-ba","--",file});
   string text=Run(Seven,args,null,token,null,true);
   var record=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   Action flush=()=> {
    string path; if(!record.TryGetValue("Path",out path)) {record.Clear();return;}
    long size=0,packed=0; string v;
    if(record.TryGetValue("Size",out v))long.TryParse(v,out size);
    if(record.TryGetValue("Packed Size",out v))long.TryParse(v,out packed);
    bool folder=(record.TryGetValue("Folder",out v)&&v=="+") || (record.TryGetValue("Attributes",out v)&&v.StartsWith("D"));
    string raw=path.Replace('\\','/');var item=new ArchiveItem{SourcePath=raw,Path=NormalizeName(raw),Size=size,Packed=packed,Folder=folder,Crc=record.ContainsKey("CRC")?record["CRC"]:"",Modified=record.ContainsKey("Modified")?record["Modified"]:"",Method=record.ContainsKey("Method")?record["Method"]:"",Encrypted=record.TryGetValue("Encrypted",out v)&&v=="+"};
    item.Link=(record.TryGetValue("Symbolic Link",out v)&&!string.IsNullOrWhiteSpace(v)) || (record.TryGetValue("Hard Link",out v)&&!string.IsNullOrWhiteSpace(v)) || (record.TryGetValue("Mode",out v)&&Regex.IsMatch(v,@"^[lbcpSs]")) || (record.TryGetValue("Attributes",out v)&&(v.Contains(" lrwx")||v.Contains("reparse"))) || (record.TryGetValue("Anti",out v)&&v=="+");
    if(item.Path.Length!=0||!item.Folder)index.Items.Add(item);record.Clear();
   };
   using(var reader=new StringReader(text)) { string line; while((line=reader.ReadLine())!=null) {
    if(line.Length==0) {flush();continue;} int k=line.IndexOf(" = ",StringComparison.Ordinal);
    if(k<=0)throw new InvalidDataException("An archive entry contains an unsupported multiline name or metadata. It cannot be safely extracted.");
    string key=line.Substring(0,k); if(record.ContainsKey(key))throw new InvalidDataException("Ambiguous archive metadata; extraction is blocked.");
    record[key]=line.Substring(k+3);
   } flush(); }
   return index;
  }
  static string NormalizeName(string path) {path=path.Replace('\\','/');while(path.StartsWith("./",StringComparison.Ordinal))path=path.Substring(2);return path=="."?"":path;}
  public static void ValidateName(string path) {
   if(string.IsNullOrEmpty(path)||path.Any(c=>char.IsControl(c))||path.IndexOf(':')>=0||path.StartsWith("/")||path.StartsWith("\\")||System.IO.Path.IsPathRooted(path))throw new InvalidDataException("Unsafe archive path: "+path);
   string[] parts=path.Replace('\\','/').TrimEnd('/').Split('/');
   foreach(string part in parts) {
    if(part.Length==0||part=="."||part==".."||part.EndsWith(".")||part.EndsWith(" ")||part.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0||part.StartsWith(".benizip-partial-",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",RegexOptions.IgnoreCase)) throw new InvalidDataException("Unsafe or unsupported archive name: "+path);
   }
  }
  public static void NoLinks(string path) {
   string full=System.IO.Path.GetFullPath(path); string current=full;
   while(!string.IsNullOrEmpty(current)) {FileAttributes attrs;if(TryAttributes(current,out attrs)&&(attrs&FileAttributes.ReparsePoint)!=0)throw new IOException("A symbolic link or junction is not an extraction destination: "+current); current=System.IO.Path.GetDirectoryName(current); }
  }
  static bool TryAttributes(string path,out FileAttributes attrs) {try{attrs=File.GetAttributes(path);return true;}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}attrs=0;return false;}
  [StructLayout(LayoutKind.Sequential)] struct HandleInfo {public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Created,Accessed,Written;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle handle,out HandleInfo information);
  // Holding directory handles without delete sharing prevents checked ancestors
  // being renamed into junctions while a merge publishes its files.
  sealed class DirectoryPins:IDisposable {
   readonly Dictionary<string,SafeFileHandle> handles=new Dictionary<string,SafeFileHandle>(StringComparer.OrdinalIgnoreCase);
   public void Ensure(string path,bool create) {
    path=System.IO.Path.GetFullPath(path);if(handles.ContainsKey(path))return;
    string parent=System.IO.Path.GetDirectoryName(path);if(parent!=null)Ensure(parent,create);
    FileAttributes attrs;if(!TryAttributes(path,out attrs)){if(!create)throw new DirectoryNotFoundException(path);Directory.CreateDirectory(path);}
    SafeFileHandle h=CreateFile(path,0,3,IntPtr.Zero,3,0x02200000,IntPtr.Zero);
    if(h.IsInvalid){int code=Marshal.GetLastWin32Error();h.Dispose();throw new IOException("Cannot safely access destination folder: "+path,new Win32Exception(code));}
    HandleInfo info;if(!GetFileInformationByHandle(h,out info)){int code=Marshal.GetLastWin32Error();h.Dispose();throw new IOException("Cannot validate destination folder: "+path,new Win32Exception(code));}
    if((info.Attributes&(uint)FileAttributes.ReparsePoint)!=0||(info.Attributes&(uint)FileAttributes.Directory)==0){h.Dispose();throw new IOException("A symbolic link, junction or file is not an extraction folder: "+path);}
    handles.Add(path,h);
   }
   public void Dispose(){foreach(var h in handles.Values)h.Dispose();handles.Clear();}
  }
  static Dictionary<string,bool> ValidateEntries(IEnumerable<ArchiveItem> items) {
   var names=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
   foreach(var item in items){ValidateName(item.Path);if(item.Link)throw new InvalidDataException("This archive contains links. BeniZip blocks link extraction to protect files outside the destination.");string key=item.Path.TrimEnd('/');if(names.ContainsKey(key))throw new InvalidDataException("Ambiguous duplicate archive path: "+item.Path);names.Add(key,item.Folder);}
   foreach(string path in names.Keys){int end=path.LastIndexOf('/');while(end>=0){bool folder;string parent=path.Substring(0,end);if(names.TryGetValue(parent,out folder)&&!folder)throw new InvalidDataException("An archive file is also used as a folder: "+parent);end=parent.LastIndexOf('/');}}
   return names;
  }
  static bool Selected(ArchiveItem e,string[] selection) {
   return selection==null||selection.Length==0||selection.Any(p=>e.Path.Equals(p,StringComparison.Ordinal)||e.Path.StartsWith(p.TrimEnd('/')+"/",StringComparison.Ordinal));
  }
  public static List<string> Extract(ArchiveIndex index,string destination,string[] selection,string password,string collision,CancellationToken token,Action<ProgressInfo> progress) {
   using(var sourceLock=new FileStream(index.File,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if(sourceLock.Length!=index.SourceLength||File.GetLastWriteTimeUtc(index.File)!=index.SourceModified)throw new IOException("The archive changed after it was opened. Refresh it before extracting.");
    return ExtractLocked(index,destination,selection,password,collision,token,progress);
   }
  }
  static List<string> ExtractLocked(ArchiveIndex index,string destination,string[] selection,string password,string collision,CancellationToken token,Action<ProgressInfo> progress) {
   ValidateEntries(index.Items);
   if(collision!="Skip"&&collision!="Overwrite"&&collision!="Rename")throw new ArgumentException("Choose Rename, Skip or Overwrite for existing files.");
   var wanted=index.Items.Where(e=>Selected(e,selection)).ToArray();
   if(wanted.Length==0)throw new InvalidOperationException("No matching entries to extract.");
   destination=System.IO.Path.GetFullPath(destination).TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
   if(destination.Length==2&&destination[1]==':')destination+=System.IO.Path.DirectorySeparatorChar;
   string parent=System.IO.Path.GetDirectoryName(destination);
   if(parent==null)throw new IOException("Choose an extraction folder below the drive root.");
   long bytes=wanted.Where(x=>!x.Folder).Sum(x=>x.Size);
   var drive=new DriveInfo(System.IO.Path.GetPathRoot(destination));
   if(drive.IsReady&&drive.AvailableFreeSpace<bytes+16L*1024*1024) throw new IOException("There is not enough free space for "+FormatSize(bytes)+" of extracted files.");
   // A sibling staging directory shares the destination volume. Fresh output is
   // published by one rename only after decoding, CRC checks and validation pass.
   string stage=System.IO.Path.Combine(parent,".benizip-partial-"+Guid.NewGuid().ToString("N"));
   string listFile=null;var moved=new List<string>();
   using(var pins=new DirectoryPins()) {
   pins.Ensure(parent,true);FileAttributes destinationAttrs;bool exists=TryAttributes(destination,out destinationAttrs);
   if(exists&&((destinationAttrs&FileAttributes.ReparsePoint)!=0||(destinationAttrs&FileAttributes.Directory)==0))throw new IOException("A symbolic link, junction or file is not an extraction destination: "+destination);
   if(exists)pins.Ensure(destination,false);Directory.CreateDirectory(stage);
   try {
    var args=BaseArgs("x",password);args.AddRange(new[]{"-bsp1","-bso0","-bse2","-o"+stage});
    if(selection!=null&&selection.Length>0) { listFile=stage+".selection.txt";File.WriteAllLines(listFile,wanted.Select(x=>x.SourcePath??x.Path).Distinct(),new UTF8Encoding(false));args.Add("-i@"+listFile); }
    args.AddRange(new[]{"--",index.File});
    Run(Seven,args,null,token,progress,false); if(listFile!=null)File.Delete(listFile);
    token.ThrowIfCancellationRequested();
    var allowed=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
    foreach(var item in wanted){string key=item.Path.TrimEnd('/');allowed[key]=item.Folder;int slash=key.LastIndexOf('/');while(slash>=0){key=key.Substring(0,slash);if(!allowed.ContainsKey(key))allowed.Add(key,true);slash=key.LastIndexOf('/');}}
    var dirs=new List<string>();var files=new List<string>();var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var pending=new Stack<string>();pending.Push(stage);
    while(pending.Count!=0){string dir=pending.Pop();foreach(string child in Directory.EnumerateFileSystemEntries(dir)){FileAttributes attrs=File.GetAttributes(child);if((attrs&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("The archive produced a symbolic link or junction.");string rel=child.Substring(stage.Length+1).Replace('\\','/');ValidateName(rel);bool folder=(attrs&FileAttributes.Directory)!=0,expected;if(!allowed.TryGetValue(rel,out expected)||folder!=expected)throw new InvalidDataException("The archive produced an unexpected entry: "+rel);found.Add(rel);if(folder){dirs.Add(rel);pending.Push(child);}else files.Add(rel);}}
    if(wanted.Any(e=>!found.Contains(e.Path.TrimEnd('/'))))throw new IOException("Some requested archive entries were not extracted.");
    // Cancellation is honoured before publishing any successfully decoded files.
    token.ThrowIfCancellationRequested();
    if(!exists){Directory.Move(stage,destination);moved.AddRange(files.Select(rel=>System.IO.Path.Combine(destination,rel.Replace('/',System.IO.Path.DirectorySeparatorChar))));}
    else {
     // Preflight every destination before moving any file. Cache and pin each
     // distinct directory rather than walking its ancestors for every entry.
     foreach(string rel in dirs.OrderBy(p=>p.Length))pins.Ensure(System.IO.Path.Combine(destination,rel.Replace('/',System.IO.Path.DirectorySeparatorChar)),true);
     var targets=new List<string>();var overwrite=new List<bool>();var reserved=new HashSet<string>(files.Select(rel=>System.IO.Path.Combine(destination,rel.Replace('/',System.IO.Path.DirectorySeparatorChar))),StringComparer.OrdinalIgnoreCase);
     foreach(string rel in files){string dst=System.IO.Path.Combine(destination,rel.Replace('/',System.IO.Path.DirectorySeparatorChar));if(dst.Equals(index.File,StringComparison.OrdinalIgnoreCase))throw new IOException("Extraction cannot replace the archive being read.");pins.Ensure(System.IO.Path.GetDirectoryName(dst),true);FileAttributes attrs;bool occupied=TryAttributes(dst,out attrs);if(occupied&&(attrs&FileAttributes.ReparsePoint)!=0)throw new IOException("A symbolic link or junction is not an extraction destination: "+dst);if(occupied&&(attrs&FileAttributes.Directory)!=0)throw new IOException("An extracted file conflicts with an existing folder: "+dst);
      if(occupied&&collision=="Skip"){targets.Add(null);overwrite.Add(false);continue;}
      if(occupied&&collision=="Rename"){string stem=System.IO.Path.GetFileNameWithoutExtension(dst),ext=System.IO.Path.GetExtension(dst),folder=System.IO.Path.GetDirectoryName(dst);int n=2;do{dst=System.IO.Path.Combine(folder,stem+" ("+n+")"+ext);n++;}while(reserved.Contains(dst)||TryAttributes(dst,out attrs));reserved.Add(dst);}
      targets.Add(dst);overwrite.Add(occupied&&collision=="Overwrite");
     }
     token.ThrowIfCancellationRequested();
     for(int n=0;n<files.Count;n++){string dst=targets[n];if(dst==null)continue;string source=System.IO.Path.Combine(stage,files[n].Replace('/',System.IO.Path.DirectorySeparatorChar));if(overwrite[n])File.Replace(source,dst,null);else File.Move(source,dst);moved.Add(dst);}
    }
    if(progress!=null)progress(new ProgressInfo(100,"Extraction complete"));return moved;
   } finally {try{if(listFile!=null&&File.Exists(listFile))File.Delete(listFile);if(Directory.Exists(stage))Directory.Delete(stage,true);}catch{}}
   }
  }
  public static void Test(string file,string password,CancellationToken token,Action<ProgressInfo> progress) {
   var args=BaseArgs("t",password);args.AddRange(new[]{"-bsp1","-bso0","--",file});Run(Seven,args,null,token,progress,false);
  }
  public static string CommonParent(string[] paths) {
   if(paths==null||paths.Length==0)throw new IOException("Choose files or a folder first.");
   var full=paths.Select(System.IO.Path.GetFullPath).ToArray();string root=System.IO.Path.GetDirectoryName(full[0].TrimEnd('\\','/'))??System.IO.Path.GetPathRoot(full[0]);
   while(!full.All(p=>p.StartsWith(root.TrimEnd('\\','/')+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))){root=System.IO.Path.GetDirectoryName(root.TrimEnd('\\','/'));if(root==null)throw new IOException("Choose files on the same drive.");}
   return root;
  }
  public static void Create(string[] inputs,string output,string format,int level,string password,bool hideNames,string split,CancellationToken token,Action<ProgressInfo> progress) {
   if(inputs.Length==0)throw new InvalidOperationException("Choose files or a folder first.");
   foreach(string p in inputs) {CheckSource(p); if(p.Any(char.IsControl))throw new IOException("Unsupported source name.");}
   string cwd=CommonParent(inputs),parent=System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output));Directory.CreateDirectory(parent);
   if(inputs.Any(p=>Directory.Exists(p)&&System.IO.Path.GetFullPath(output).StartsWith(System.IO.Path.GetFullPath(p).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)))throw new IOException("Save the archive outside the folder being compressed.");
   string temp=System.IO.Path.Combine(parent,".benizip-"+Guid.NewGuid().ToString("N")+"."+format.ToLowerInvariant());
   var args=new List<string>();string engine;
   if(format=="RAR") {if(Rar==null)throw new IOException("RAR creation needs an installed, appropriately licensed RAR helper. Choose ZIP or 7z instead.");engine=Rar;args.AddRange(new[]{"a","-m"+Math.Min(5,level),"-idq","-y"});if(!string.IsNullOrEmpty(password))args.Add((hideNames?"-hp":"-p")+password);}
   else {engine=Seven;args=BaseArgs("a",password);args.AddRange(new[]{"-t"+format.ToLowerInvariant(),"-mx="+level,"-bsp1","-bso0"});if(password==null)args.RemoveAll(x=>x.StartsWith("-p"));else if(format=="ZIP")args.Add("-mem=AES256");else if(hideNames)args.Add("-mhe=on");}
   if(!string.IsNullOrEmpty(split)) {if(!Regex.IsMatch(split,@"^\d+[kmg]$",RegexOptions.IgnoreCase))throw new IOException("Split size must be a number followed by k, m or g.");args.Add("-v"+split.ToLowerInvariant());}
   args.Add("--");args.Add(temp);foreach(string p in inputs)args.Add(System.IO.Path.GetFullPath(p).Substring(cwd.Length).TrimStart('\\'));
   try {
    Run(engine,args,cwd,token,progress,false);
    string tempStem=System.IO.Path.GetFileNameWithoutExtension(temp);var produced=Directory.GetFiles(parent,tempStem+"*").OrderBy(x=>x).ToArray();
    if(produced.Length==0)throw new IOException("The engine produced no archive.");
    string outputStem=System.IO.Path.Combine(parent,System.IO.Path.GetFileNameWithoutExtension(output));string sourceStem=System.IO.Path.Combine(parent,tempStem);
    var targets=produced.Select(p=>outputStem+p.Substring(sourceStem.Length)).ToArray();
    if(targets.Any(p=>File.Exists(p)||Directory.Exists(p)))throw new IOException("An archive with this name already exists. Choose a new name.");
    token.ThrowIfCancellationRequested();for(int i=0;i<produced.Length;i++)File.Move(produced[i],targets[i]);
   } finally {foreach(string p in Directory.GetFiles(parent,System.IO.Path.GetFileNameWithoutExtension(temp)+"*"))try{File.Delete(p);}catch{}}
  }
  static void CheckSource(string path){NoLinks(path);if(!Directory.Exists(path))return;var pending=new Stack<string>();pending.Push(path);while(pending.Count>0){string dir=pending.Pop();foreach(string child in Directory.EnumerateFileSystemEntries(dir)){var attrs=File.GetAttributes(child);if((attrs&FileAttributes.ReparsePoint)!=0)throw new IOException("Source folders containing symbolic links or junctions must be archived separately: "+child);if((attrs&FileAttributes.Directory)!=0)pending.Push(child);}}}
  public static void Edit(ArchiveIndex index,string[] members,string[] inputs,string password,bool delete,CancellationToken token,Action<ProgressInfo> progress) {
   string ext=System.IO.Path.GetExtension(index.File).ToLowerInvariant();
   if(!new[]{".zip",".7z",".rar"}.Contains(ext))throw new IOException("Editing is supported for ZIP, 7z and RAR archives.");
   string temp=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(index.File),".benizip-"+Guid.NewGuid().ToString("N")+ext);
   string listFile=null;
   try {
    NoLinks(index.File);
    var identity=new FileInfo(index.File);
    if(identity.Length!=index.SourceLength||identity.LastWriteTimeUtc!=index.SourceModified)throw new IOException("The archive changed after it was opened. Refresh it before editing.");
    // ZIP's fast browsing index intentionally avoids reading payloads. Before
    // editing, inspect actual encryption metadata and verify protected archives.
    // A missing password must never become an encryption key for new members.
    var current=ListCore(index.File,password,token,false);
    if(current.Items.Any(x=>x.Encrypted)){
     if(string.IsNullOrEmpty(password))throw new EngineException("Enter the archive password before editing protected files.",true);
     Test(index.File,password,token,progress);
    }
    File.Copy(index.File,temp);token.ThrowIfCancellationRequested();string exe=ext==".rar"?Rar:Seven;
    if(exe==null)throw new IOException("Editing RAR archives needs the installed RAR helper.");
    var a=ext==".rar"?new List<string>{delete?"d":"a","-y","-idq"}:BaseArgs(delete?"d":"a",password);
    if(ext!=".rar"&&password==null)a.RemoveAll(x=>x.StartsWith("-p",StringComparison.Ordinal));
    if(ext==".rar"&&!string.IsNullOrEmpty(password))a.Add("-p"+password);
    a.Add("--");a.Add(temp);string cwd=null;
    if(delete){foreach(string m in members){ValidateName(m);a.Add(m);} }
    else {cwd=CommonParent(inputs);foreach(string p in inputs){CheckSource(p);a.Add(System.IO.Path.GetFullPath(p).Substring(cwd.Length).TrimStart('\\'));}}
    Run(exe,a,cwd,token,progress,false);token.ThrowIfCancellationRequested();
    identity.Refresh();if(identity.Length!=index.SourceLength||identity.LastWriteTimeUtc!=index.SourceModified)throw new IOException("The archive changed while it was being edited. Refresh it and try again.");
    File.Replace(temp,index.File,null);
   } finally {try{if(File.Exists(temp))File.Delete(temp);if(listFile!=null)File.Delete(listFile);}catch{}}
  }
  public static string FormatSize(long n) {string[] units={"B","KB","MB","GB","TB"};double value=n;int i=0;while(value>=1024&&i<4){value/=1024;i++;}return value.ToString(i==0?"N0":"0.##",CultureInfo.CurrentCulture)+" "+units[i];}
 }
}
