using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace BeniZip {
 // A read-only Shell stream backed by 7-Zip stdout. Each member is checked by
 // the archive engine before its final bytes are delivered to the drop target.
 [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
 public sealed class ArchiveDragStream:IStream,IDisposable {
  readonly ArchiveIndex index;readonly ArchiveItem item;readonly string password;readonly List<ArchiveDragStream> owner;
  readonly object gate=new object();readonly StringBuilder errors=new StringBuilder();
  Process process;FileStream sourceLock;long position,decoded;bool complete,disposed;
  const int AccessDenied=unchecked((int)0x80030005),ReadFault=unchecked((int)0x8003001E),SeekError=unchecked((int)0x80030019);
  public ArchiveDragStream(ArchiveIndex index,ArchiveItem item,string password,List<ArchiveDragStream> owner){this.index=index;this.item=item;this.password=password;this.owner=owner;}
  void Start(){
   if(disposed)throw new ObjectDisposedException("ArchiveDragStream");if(process!=null)return;
   sourceLock=new FileStream(index.File,FileMode.Open,FileAccess.Read,FileShare.Read);
   try{
    if(sourceLock.Length!=index.SourceLength||File.GetLastWriteTimeUtc(index.File)!=index.SourceModified)throw new IOException("The archive changed after it was opened. Refresh it before dragging.");
    var args=new[]{"x","-sccUTF-8","-scsUTF-8","-mmt=on","-y","-spd","-p"+(password??"BeniZip-no-password-supplied"),"-so","-bd","-bso0","-bse2","--",index.File,item.SourcePath??item.Path};
    process=new Process{StartInfo=new ProcessStartInfo(ArchiveEngine.Seven,string.Join(" ",args.Select(ArchiveEngine.Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.GetDirectoryName(ArchiveEngine.Seven)}};
    process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(errors)if(errors.Length<32768)errors.AppendLine(e.Data);};
    if(!process.Start())throw new IOException("Could not start archive extraction.");process.StandardInput.Close();process.BeginErrorReadLine();decoded=0;complete=false;
   }catch{Stop();throw;}
  }
  void Stop(){
   if(process!=null){try{if(!process.HasExited)process.Kill();}catch{}try{process.Dispose();}catch{}process=null;}
   if(sourceLock!=null){sourceLock.Dispose();sourceLock=null;}
  }
  void Restart(){Stop();decoded=0;complete=false;lock(errors)errors.Length=0;}
  void Finish(){
   if(complete)return;
   if(process.StandardOutput.BaseStream.ReadByte()!=-1){Stop();throw new InvalidDataException("The archive produced more data than expected.");}
   process.WaitForExit();process.WaitForExit();int code=process.ExitCode;string details;lock(errors)details=errors.ToString().Trim();
   Stop();complete=true;
   if(code!=0){if(details.Length>1500)details=details.Substring(details.Length-1500);throw new IOException("The archive file could not be extracted or failed its integrity check."+(details.Length>0?"\n"+details:""));}
  }
  int Decode(byte[] buffer,int offset,int count){
   int read=process.StandardOutput.BaseStream.Read(buffer,offset,count);
   if(read==0&&decoded<item.Size){Finish();throw new EndOfStreamException("The archive file ended before its expected size.");}
   decoded+=read;if(decoded==item.Size)Finish();return read;
  }
  void PositionDecoder(){
   if(complete||process!=null&&decoded>position)Restart();Start();
   byte[] discard=new byte[65536];while(decoded<position&&decoded<item.Size){int n=Decode(discard,0,(int)Math.Min(discard.Length,Math.Min(position,item.Size)-decoded));if(n==0)break;}
  }
  public void Read(byte[] buffer,int count,IntPtr bytesRead){
   lock(gate){if(bytesRead!=IntPtr.Zero)Marshal.WriteInt32(bytesRead,0);if(disposed)throw new ObjectDisposedException("ArchiveDragStream");if(count<0||count>buffer.Length)throw new ArgumentException("Invalid stream read size.");if(count==0)return;
    try{if(position>=item.Size){if(item.Size==0&&!complete){Start();Finish();}return;}PositionDecoder();int n=Decode(buffer,0,(int)Math.Min((long)count,item.Size-position));position+=n;if(bytesRead!=IntPtr.Zero)Marshal.WriteInt32(bytesRead,n);}
    catch(Exception ex){Stop();throw new COMException(ex.Message,ReadFault);}
   }
  }
  public void Seek(long offset,int origin,IntPtr newPosition){lock(gate){long next;try{next=checked((origin==0?0:origin==1?position:origin==2?item.Size:throwOrigin())+offset);}catch{throw new COMException("Invalid seek position.",SeekError);}if(next<0)throw new COMException("Invalid seek position.",SeekError);position=next;if(newPosition!=IntPtr.Zero)Marshal.WriteInt64(newPosition,position);}}
  static long throwOrigin(){throw new ArgumentOutOfRangeException("origin");}
  public void CopyTo(IStream destination,long count,IntPtr bytesRead,IntPtr bytesWritten){
   long totalRead=0,totalWritten=0;IntPtr n=Marshal.AllocHGlobal(4),written=Marshal.AllocHGlobal(4);
   try{var buffer=new byte[1024*1024];while(totalRead<count){Read(buffer,(int)Math.Min(buffer.Length,count-totalRead),n);int actual=Marshal.ReadInt32(n);if(actual==0)break;totalRead+=actual;Marshal.WriteInt32(written,0);destination.Write(buffer,actual,written);int accepted=Marshal.ReadInt32(written);totalWritten+=accepted;if(accepted!=actual)throw new COMException("The destination did not accept the complete file.",ReadFault);}}
   finally{if(bytesRead!=IntPtr.Zero)Marshal.WriteInt64(bytesRead,totalRead);if(bytesWritten!=IntPtr.Zero)Marshal.WriteInt64(bytesWritten,totalWritten);Marshal.FreeHGlobal(n);Marshal.FreeHGlobal(written);}
  }
  public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG stat,int flags){stat=new System.Runtime.InteropServices.ComTypes.STATSTG{type=2,cbSize=item.Size,grfMode=0,pwcsName=(flags&1)!=0?null:item.Name};}
  public void Clone(out IStream result){lock(gate){var clone=new ArchiveDragStream(index,item,password,owner);clone.position=position;lock(owner)owner.Add(clone);result=clone;}}
  public void Dispose(){lock(gate){if(disposed)return;disposed=true;Stop();}}
  public void Write(byte[] buffer,int count,IntPtr bytesWritten){throw new COMException("The archive stream is read-only.",AccessDenied);}
  public void SetSize(long size){throw new COMException("The archive stream is read-only.",AccessDenied);}
  public void Commit(int flags){}
  public void Revert(){throw new COMException("The archive stream is read-only.",AccessDenied);}
  public void LockRegion(long offset,long count,int type){throw new COMException("The archive stream is read-only.",AccessDenied);}
  public void UnlockRegion(long offset,long count,int type){throw new COMException("The archive stream is read-only.",AccessDenied);}
 }
}
