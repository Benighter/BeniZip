using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
[assembly:AssemblyTitle("BeniZip Update")]
[assembly:AssemblyProduct("BeniZip")]
[assembly:AssemblyCompany("Bennet")]
namespace BeniZip {
 public static class UpdateProgram {
  [STAThread] public static int Main(string[] args){try{if(args.Length!=4)throw new ArgumentException("An update needs a signed manifest, package, install path and process ID.");Updates.Apply(args[0],args[1],args[2],int.Parse(args[3]));return 0;}
   catch(Exception ex){try{Directory.CreateDirectory(Updates.DataRoot);File.WriteAllText(Path.Combine(Updates.DataRoot,"update-error.txt"),DateTime.UtcNow.ToString("o")+"\n"+ex.Message);}catch{}MessageBox.Show("The update was not installed.\n\n"+ex.Message,"BeniZip update",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 2;}}
 }
}
