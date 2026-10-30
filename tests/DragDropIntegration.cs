using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using BeniZip;

// Synthetic archives only. Exercises the actual Shell drop target as well as
// the delayed-rendering protocol; no mouse or Explorer window is required.
public static class DragDropIntegration {
 [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IShellItem {void BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,out IntPtr result);}
 [ComImport,Guid("00000122-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IDropTarget {
  [PreserveSig]int DragEnter([MarshalAs(UnmanagedType.Interface)]IDataObject data,uint keys,Point point,ref uint effect);
  [PreserveSig]int DragOver(uint keys,Point point,ref uint effect);
  [PreserveSig]int DragLeave();
  [PreserveSig]int Drop([MarshalAs(UnmanagedType.Interface)]IDataObject data,uint keys,Point point,ref uint effect);
 }
 [StructLayout(LayoutKind.Sequential)]struct Point {public int x,y;}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IShellItem item);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterClipboardFormat(string name);
 [DllImport("kernel32.dll")]static extern IntPtr GlobalLock(IntPtr handle);
 [DllImport("kernel32.dll")]static extern bool GlobalUnlock(IntPtr handle);
 [DllImport("ole32.dll")]static extern void ReleaseStgMedium(ref STGMEDIUM medium);
 [DllImport("ole32.dll")]static extern int OleInitialize(IntPtr reserved);
 [DllImport("ole32.dll")]static extern void OleUninitialize();
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
 static FORMATETC Format(string name,TYMED medium,int index){return new FORMATETC{cfFormat=unchecked((short)RegisterClipboardFormat(name)),dwAspect=DVASPECT.DVASPECT_CONTENT,lindex=index,tymed=medium};}
 static object Internal(string name,params object[] args){try{return typeof(ArchiveEngine).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
 static DragTransfer Prepare(ArchiveIndex index,string[] selection,string password){var detailed=(ArchiveIndex)Internal("ListForDrag",index.File,password,CancellationToken.None);Internal("CheckDragIndex",index,detailed);Internal("TestEncryptedForDrag",detailed,selection,password,CancellationToken.None,null);return new DragTransfer{Index=detailed,Password=password};}
 static ArchiveDragDrop Create(ArchiveIndex index,string prefix,string[] selection,Action onExtract){return new ArchiveDragDrop(index,prefix,selection,()=>{onExtract();return Prepare(index,selection,null);});}
 static string[] Names(ArchiveDragDrop data){var format=Format("FileGroupDescriptorW",TYMED.TYMED_HGLOBAL,-1);STGMEDIUM medium;data.GetData(ref format,out medium);try{IntPtr ptr=GlobalLock(medium.unionmember);try{int count=Marshal.ReadInt32(ptr);var names=new string[count];for(int i=0;i<count;i++)names[i]=Marshal.PtrToStringUni(IntPtr.Add(ptr,4+i*592+72));return names;}finally{GlobalUnlock(medium.unionmember);}}finally{ReleaseStgMedium(ref medium);}}
 static string Contents(ArchiveDragDrop data,int index){var format=Format("FileContents",TYMED.TYMED_ISTREAM,index);STGMEDIUM medium;data.GetData(ref format,out medium);try{var stream=(IStream)Marshal.GetObjectForIUnknown(medium.unionmember);try{byte[] bytes=new byte[1024];IntPtr count=Marshal.AllocHGlobal(4);try{stream.Read(bytes,bytes.Length,count);return Encoding.UTF8.GetString(bytes,0,Marshal.ReadInt32(count));}finally{Marshal.FreeHGlobal(count);}}finally{if(Marshal.IsComObject(stream))Marshal.ReleaseComObject(stream);}}finally{ReleaseStgMedium(ref medium);}}
 static void DesktopAccepts(IDataObject data,string desktop){
  Guid iid=new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");IShellItem item;int hr=SHCreateItemFromParsingName(desktop,IntPtr.Zero,ref iid,out item);if(hr<0)Marshal.ThrowExceptionForHR(hr);
  Guid handler=new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5"),dropIid=new Guid("00000122-0000-0000-C000-000000000046");IntPtr pointer;item.BindToHandler(IntPtr.Zero,ref handler,ref dropIid,out pointer);var drop=(IDropTarget)Marshal.GetObjectForIUnknown(pointer);Marshal.Release(pointer);
  try{uint effect=1;hr=drop.DragEnter(data,1,new Point(),ref effect);if(hr<0)Marshal.ThrowExceptionForHR(hr);Assert((effect&1)!=0,"Desktop folder refused the drag");drop.DragLeave();}finally{Marshal.ReleaseComObject(drop);Marshal.ReleaseComObject(item);}
 }
 public static void Drop(IDataObject data,string target){
  Guid iid=new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");IShellItem item;int hr=SHCreateItemFromParsingName(target,IntPtr.Zero,ref iid,out item);if(hr<0)Marshal.ThrowExceptionForHR(hr);
  Guid handler=new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5"),dropIid=new Guid("00000122-0000-0000-C000-000000000046");IntPtr pointer;
  item.BindToHandler(IntPtr.Zero,ref handler,ref dropIid,out pointer);var drop=(IDropTarget)Marshal.GetObjectForIUnknown(pointer);Marshal.Release(pointer);
  try{uint effect=1;hr=drop.DragEnter(data,1,new Point(),ref effect);if(hr<0)Marshal.ThrowExceptionForHR(hr);Assert((effect&1)!=0,"Shell refused the virtual files");hr=drop.Drop(data,0,new Point(),ref effect);if(hr<0)Marshal.ThrowExceptionForHR(hr);Assert((effect&1)!=0,"Shell did not copy the virtual files");}
  finally{Marshal.ReleaseComObject(drop);Marshal.ReleaseComObject(item);}
 }
 [STAThread]public static int Main(string[] args){OleInitialize(IntPtr.Zero);try{
  string archive=Path.GetFullPath(args[0]),root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);var index=ArchiveEngine.List(archive,null,CancellationToken.None);int calls=0;
  string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);if(Directory.Exists(desktop)){using(var data=Create(index,"",new[]{"hello.txt"},()=>calls++))DesktopAccepts(data,desktop);Assert(calls==0,"Hovering over the desktop started extraction");Console.WriteLine("PASS: desktop folder accepts the drag without extracting on hover");}
  using(var data=Create(index,"",new[]{"hello.txt"},()=>calls++)){Assert(Names(data).SequenceEqual(new[]{"hello.txt"}),"Single-file descriptor mismatch");Assert(calls==0,"Descriptors unexpectedly extracted the archive");Assert(Contents(data,0)=="BeniZip single file drag test\n","Single-file contents mismatch");Assert(calls==1,"Extraction did not happen exactly once");}
  calls=0;using(var data=Create(index,"",new[]{"folder"},()=>calls++)){var names=Names(data);Assert(names.Contains("folder")&&names.Contains("folder\\inside.txt")&&names.Contains("folder\\empty"),"Folder structure or empty folder missing");Assert(calls==0,"Folder descriptors extracted early");string destination=Path.Combine(root,"folder");Directory.CreateDirectory(destination);Drop(data,destination);Assert(File.ReadAllText(Path.Combine(destination,"folder","inside.txt"))=="BeniZip nested file drag test\n","Shell did not copy nested contents");Assert(Directory.Exists(Path.Combine(destination,"folder","empty")),"Shell did not create empty folder");Assert(calls==1,"Shell did not request the data once");}
  calls=0;using(var data=Create(index,"",new[]{"hello.txt","second.txt"},()=>calls++)){string destination=Path.Combine(root,"multiple");Directory.CreateDirectory(destination);Drop(data,destination);Assert(File.ReadAllText(Path.Combine(destination,"hello.txt"))=="BeniZip single file drag test\n","First selected file missing");Assert(File.ReadAllText(Path.Combine(destination,"second.txt"))=="BeniZip second file drag test\n","Second selected file missing");Assert(calls==1,"Multiple files should share one extraction");}
  using(var data=Create(index,"folder/",new[]{"folder/inside.txt"},()=>{})){Assert(Names(data).SequenceEqual(new[]{"inside.txt"}),"Inner-folder path was not relative");string destination=Path.Combine(root,"inner");Directory.CreateDirectory(destination);Drop(data,destination);Assert(File.Exists(Path.Combine(destination,"inside.txt")),"Inner-folder file missing");}
  using(var data=Create(index,"",new[]{"hello.txt"},()=>{})){
   var format=Format("FileContents",TYMED.TYMED_ISTREAM,0);STGMEDIUM medium;data.GetData(ref format,out medium);
   try{var stream=(IStream)Marshal.GetObjectForIUnknown(medium.unionmember);IntPtr count=Marshal.AllocHGlobal(4);try{var bytes=new byte[8];stream.Read(bytes,4,count);Assert(Encoding.UTF8.GetString(bytes,0,Marshal.ReadInt32(count))=="Beni","Initial stream read failed");stream.Seek(8,0,IntPtr.Zero);stream.Read(bytes,6,count);Assert(Encoding.UTF8.GetString(bytes,0,Marshal.ReadInt32(count))=="single","Forward seek failed");stream.Seek(0,0,IntPtr.Zero);stream.Read(bytes,4,count);Assert(Encoding.UTF8.GetString(bytes,0,Marshal.ReadInt32(count))=="Beni","Backward seek failed");IStream clone;stream.Clone(out clone);clone.Read(bytes,4,count);Assert(Encoding.UTF8.GetString(bytes,0,Marshal.ReadInt32(count))=="Zip ","Cloned stream position failed");}finally{Marshal.FreeHGlobal(count);if(Marshal.IsComObject(stream))Marshal.ReleaseComObject(stream);}}finally{ReleaseStgMedium(ref medium);}
  }
  if(args.Length>2){var encrypted=ArchiveEngine.List(Path.GetFullPath(args[2]),null,CancellationToken.None);var detailed=(ArchiveIndex)Internal("ListForDrag",encrypted.File,null,CancellationToken.None);Assert(detailed.Items.Any(x=>x.Encrypted),"Encrypted member was not detected");bool wrong=false;try{Prepare(encrypted,new[]{"secret.txt"},"incorrect");}catch(EngineException ex){wrong=ex.PasswordRequired;}Assert(wrong,"An incorrect archive password was accepted");using(var data=new ArchiveDragDrop(encrypted,"",new[]{"secret.txt"},()=>Prepare(encrypted,new[]{"secret.txt"},"drag-test-password"))){Assert(Contents(data,0)=="Encrypted drag test\n","Encrypted member could not be streamed");}}
  if(args.Length>3){var damaged=ArchiveEngine.List(Path.GetFullPath(args[3]),null,CancellationToken.None);bool failed=false;try{using(var data=Create(damaged,"",new[]{"damaged.txt"},()=>{}))Contents(data,0);}catch(Exception){failed=true;}Assert(failed,"A damaged archive member was accepted");}
  if(args.Length>4){string staleFile=Path.GetFullPath(args[4]);var stale=ArchiveEngine.List(staleFile,null,CancellationToken.None);File.AppendAllText(staleFile,"changed");bool failed=false;try{using(var data=Create(stale,"",new[]{"hello.txt"},()=>{}))Contents(data,0);}catch(Exception){failed=true;}Assert(failed,"A changed archive was accepted");}
  if(args.Length>5){var rar=ArchiveEngine.List(Path.GetFullPath(args[5]),null,CancellationToken.None);using(var data=Create(rar,"",new[]{"rarfile.txt"},()=>{})){Assert(Contents(data,0)=="RAR drag test\n","RAR member could not be streamed");}}
  if(args.Length>6){var large=ArchiveEngine.List(Path.GetFullPath(args[6]),null,CancellationToken.None);using(var data=Create(large,"",new[]{"large.bin"},()=>{})){var format=Format("FileContents",TYMED.TYMED_ISTREAM,0);STGMEDIUM medium;data.GetData(ref format,out medium);try{var stream=(IStream)Marshal.GetObjectForIUnknown(medium.unionmember);IntPtr count=Marshal.AllocHGlobal(4);try{var buffer=new byte[262144];long total=0;while(true){stream.Read(buffer,buffer.Length,count);int n=Marshal.ReadInt32(count);if(n==0)break;for(int i=0;i<n;i++)Assert(buffer[i]==0x6b,"Large stream contents changed");total+=n;}Assert(total==32L*1024*1024,"Large streamed member had the wrong size");}finally{Marshal.FreeHGlobal(count);if(Marshal.IsComObject(stream))Marshal.ReleaseComObject(stream);}}finally{ReleaseStgMedium(ref medium);}}}
  if(args.Length>7){var rar=ArchiveEngine.List(Path.GetFullPath(args[7]),"drag-test-password",CancellationToken.None);using(var data=new ArchiveDragDrop(rar,"",new[]{"protected-rarfile.txt"},()=>Prepare(rar,new[]{"protected-rarfile.txt"},"drag-test-password"))){Assert(Contents(data,0)=="Protected RAR drag test\n","Protected RAR member could not be streamed");}}
  var bad=new ArchiveIndex();bad.Items.Add(new ArchiveItem{Path="../escape.txt",Size=1});bool rejected=false;try{using(var data=Create(bad,"",new[]{"../escape.txt"},()=>{})){} }catch(InvalidDataException){rejected=true;}Assert(rejected,"Unsafe archive path accepted");
  bad=new ArchiveIndex();bad.Items.Add(new ArchiveItem{Path="link",Link=true});rejected=false;try{using(var data=Create(bad,"",new[]{"link"},()=>{})){} }catch(InvalidDataException){rejected=true;}Assert(rejected,"Archive link accepted");
  Console.WriteLine("PASS: Windows Shell transfers, files, folders, multiple selection, seek, ZIP/RAR, passwords, corruption, stale archives and unsafe names");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{OleUninitialize();}}
}
