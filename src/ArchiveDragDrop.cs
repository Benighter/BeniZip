using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace BeniZip {
 // Explorer asks for virtual file streams after the drop. Data is decoded
 // directly into the destination rather than duplicated on the system drive.
 public sealed class DragTransfer {public ArchiveIndex Index;public string Password;}
 [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
 public sealed class ArchiveDragDrop : IDataObject, IDisposable {
  const int FormatError=unchecked((int)0x80040064), MediumError=unchecked((int)0x80040069), IndexError=unchecked((int)0x80040068), NotImplemented=unchecked((int)0x80004001), NoAdvisory=unchecked((int)0x80040003);
  static readonly short DescriptorFormat=Register("FileGroupDescriptorW"), ContentsFormat=Register("FileContents"), PreferredFormat=Register("Preferred DropEffect");
  readonly List<DragEntry> entries=new List<DragEntry>();
  readonly Func<DragTransfer> prepare;
  readonly List<ArchiveDragStream> streams=new List<ArchiveDragStream>();
  DragTransfer transfer;
  bool prepared,failed;

  sealed class DragEntry {public string Relative,Source;public bool Folder;public long Size;public string Modified;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
  struct FileDescriptor {
   public uint flags;public Guid clsid;public int sizeX,sizeY,pointX,pointY;public uint attributes;
   public System.Runtime.InteropServices.ComTypes.FILETIME created,accessed,written;
   public uint sizeHigh,sizeLow;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string name;
  }
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterClipboardFormat(string name);
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr GlobalAlloc(uint flags,UIntPtr bytes);
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr GlobalLock(IntPtr handle);
  [DllImport("kernel32.dll")]static extern bool GlobalUnlock(IntPtr handle);
  [DllImport("kernel32.dll")]static extern IntPtr GlobalFree(IntPtr handle);
  [DllImport("ole32.dll",PreserveSig=true)]static extern int DoDragDrop([MarshalAs(UnmanagedType.Interface)] IDataObject data,[MarshalAs(UnmanagedType.Interface)] IDropSource source,uint effects,out uint effect);
  [DllImport("ole32.dll")]static extern void ReleaseStgMedium(ref STGMEDIUM medium);
  static short Register(string name){uint id=RegisterClipboardFormat(name);if(id==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return unchecked((short)id);}

  public ArchiveDragDrop(ArchiveIndex index,string prefix,string[] selection,Func<DragTransfer> prepare) {
   if(index==null||selection==null||selection.Length==0||prepare==null)throw new ArgumentException("Select files or folders to drag.");
   ArchiveEngine.ValidateEntries(index.Items);
   this.prepare=prepare;
   var byName=new Dictionary<string,DragEntry>(StringComparer.OrdinalIgnoreCase);
   Action<string,string,bool,long,string> add=(relative,source,folder,size,modified)=>{
    relative=relative.Replace('/','\\');if(relative.Length==0)return;
    if(relative.Length>=260)throw new IOException("This path is too long to drag to Explorer. Use Extract To for this selection.");
    DragEntry existing;if(byName.TryGetValue(relative,out existing)){if(!folder||!existing.Folder)throw new InvalidDataException("Ambiguous dragged file name: "+relative);return;}
    var entry=new DragEntry{Relative=relative,Source=source,Folder=folder,Size=size,Modified=modified};byName.Add(relative,entry);entries.Add(entry);
   };
   foreach(var item in index.Items.Where(x=>selection.Any(p=>x.Path.Equals(p,StringComparison.Ordinal)||x.Path.StartsWith(p.TrimEnd('/')+"/",StringComparison.Ordinal)))){
    if(!item.Path.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidDataException("An item is outside the current archive folder.");
    string path=item.Path.TrimEnd('/'),relative=path.Substring(prefix.Length);int slash=relative.IndexOf('/');
    while(slash>=0){string parent=relative.Substring(0,slash);add(parent,prefix+parent,true,0,null);slash=relative.IndexOf('/',slash+1);}
    add(relative,path,item.Folder,item.Size,item.Modified);
   }
   if(entries.Count==0)throw new InvalidOperationException("No matching entries to drag.");
  }
  public void Start(){uint effect;int result=DoDragDrop(this,new DragSource(),1,out effect);if(result<0)Marshal.ThrowExceptionForHR(result);}
  public void Dispose(){ArchiveDragStream[] pending;lock(streams){pending=streams.ToArray();streams.Clear();}foreach(var stream in pending)stream.Dispose();}

  void EnsurePrepared(){
   if(prepared)return;if(failed)throw new COMException("Extraction was cancelled or failed.",unchecked((int)0x8003001E));
   try{transfer=prepare();if(transfer==null||transfer.Index==null)throw new OperationCanceledException();prepared=true;}
   catch{failed=true;throw;}
  }
  public int QueryGetData(ref FORMATETC format){
   if(format.dwAspect!=DVASPECT.DVASPECT_CONTENT)return FormatError;
   if(format.cfFormat==DescriptorFormat||format.cfFormat==PreferredFormat)return (format.tymed&TYMED.TYMED_HGLOBAL)!=0?0:MediumError;
   if(format.cfFormat==ContentsFormat){if((format.tymed&TYMED.TYMED_ISTREAM)==0)return MediumError;if(format.lindex<-1||format.lindex>=entries.Count)return IndexError;return 0;}
   return FormatError;
  }
  public void GetData(ref FORMATETC format,out STGMEDIUM medium){
   medium=new STGMEDIUM();int check=QueryGetData(ref format);if(check!=0)Marshal.ThrowExceptionForHR(check);
   if(format.cfFormat==ContentsFormat){
    if(format.lindex<0||entries[format.lindex].Folder)Marshal.ThrowExceptionForHR(IndexError);
    EnsurePrepared();var entry=entries[format.lindex];var item=transfer.Index.Items.FirstOrDefault(x=>x.Path.TrimEnd('/').Equals(entry.Source,StringComparison.Ordinal));
    if(item==null||item.Folder||item.Size!=entry.Size)throw new InvalidDataException("The archive entry changed before it was dragged.");
    var stream=new ArchiveDragStream(transfer.Index,item,transfer.Password,streams);lock(streams)streams.Add(stream);
    medium.tymed=TYMED.TYMED_ISTREAM;medium.unionmember=Marshal.GetComInterfaceForObject(stream,typeof(IStream));return;
   }
   int descriptorSize=Marshal.SizeOf(typeof(FileDescriptor));int bytes=format.cfFormat==PreferredFormat?4:checked(4+descriptorSize*entries.Count);
   IntPtr handle=GlobalAlloc(0x42,(UIntPtr)bytes);if(handle==IntPtr.Zero)throw new OutOfMemoryException();
   try{IntPtr pointer=GlobalLock(handle);if(pointer==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    try{
     if(format.cfFormat==PreferredFormat)Marshal.WriteInt32(pointer,1);
     else{Marshal.WriteInt32(pointer,entries.Count);for(int i=0;i<entries.Count;i++){
      var entry=entries[i];var descriptor=new FileDescriptor{flags=0x4,attributes=entry.Folder?0x10u:0x80u,name=entry.Relative};
      if(!entry.Folder){descriptor.flags|=0x40;descriptor.sizeHigh=(uint)((ulong)entry.Size>>32);descriptor.sizeLow=(uint)entry.Size;}
      DateTime modified;if(DateTime.TryParse(entry.Modified,out modified)){long time=modified.ToFileTime();descriptor.flags|=0x20;descriptor.written.dwLowDateTime=(int)time;descriptor.written.dwHighDateTime=(int)(time>>32);}
      Marshal.StructureToPtr(descriptor,IntPtr.Add(pointer,4+i*descriptorSize),false);
     }}
    }finally{GlobalUnlock(handle);}
    medium.tymed=TYMED.TYMED_HGLOBAL;medium.unionmember=handle;handle=IntPtr.Zero;
   }finally{if(handle!=IntPtr.Zero)GlobalFree(handle);}
  }
  public void GetDataHere(ref FORMATETC format,ref STGMEDIUM medium){Marshal.ThrowExceptionForHR(NotImplemented);}
  public int GetCanonicalFormatEtc(ref FORMATETC input,out FORMATETC output){output=new FORMATETC();return NotImplemented;}
  public void SetData(ref FORMATETC format,ref STGMEDIUM medium,bool release){if(release)ReleaseStgMedium(ref medium);}
  public IEnumFORMATETC EnumFormatEtc(DATADIR direction){if(direction!=DATADIR.DATADIR_GET)Marshal.ThrowExceptionForHR(NotImplemented);return new FormatEnumerator(new[]{Format(DescriptorFormat,TYMED.TYMED_HGLOBAL),Format(ContentsFormat,TYMED.TYMED_ISTREAM),Format(PreferredFormat,TYMED.TYMED_HGLOBAL)});}
  static FORMATETC Format(short id,TYMED medium){return new FORMATETC{cfFormat=id,dwAspect=DVASPECT.DVASPECT_CONTENT,lindex=-1,tymed=medium};}
  public int DAdvise(ref FORMATETC format,ADVF flags,IAdviseSink sink,out int connection){connection=0;return NoAdvisory;}
  public void DUnadvise(int connection){Marshal.ThrowExceptionForHR(NoAdvisory);}
  public int EnumDAdvise(out IEnumSTATDATA result){result=null;return NoAdvisory;}
 }
 [ComVisible(true),Guid("00000121-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 public interface IDropSource {
  [PreserveSig]int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)]bool escape,uint keys);
  [PreserveSig]int GiveFeedback(uint effect);
 }
 [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
 public sealed class DragSource:IDropSource {
  public int QueryContinueDrag(bool escape,uint keys){return escape?0x40101:(keys&1)==0?0x40100:0;}
  public int GiveFeedback(uint effect){return 0x40102;}
 }
 [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
 public sealed class FormatEnumerator:IEnumFORMATETC {
  readonly FORMATETC[] formats;int position;
  public FormatEnumerator(FORMATETC[] formats){this.formats=formats;}
  public int Next(int count,FORMATETC[] result,int[] fetched){int n=0;while(n<count&&position<formats.Length)result[n++]=formats[position++];if(fetched!=null&&fetched.Length>0)fetched[0]=n;return n==count?0:1;}
  public int Skip(int count){int n=Math.Min(count,formats.Length-position);position+=n;return n==count?0:1;}
  public int Reset(){position=0;return 0;}
  public void Clone(out IEnumFORMATETC result){var copy=new FormatEnumerator(formats);copy.position=position;result=copy;}
 }
}
