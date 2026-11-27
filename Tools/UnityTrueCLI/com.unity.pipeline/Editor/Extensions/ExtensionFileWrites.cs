using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Unity.Pipeline.Editor.Extensions
{
    public static class ExtensionFileWrites
    {
#if UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        private static extern bool MoveFileEx(string existingFile,string newFile,uint flags);
#endif
        // The caller has validated the project asset path. Publish one complete new
        // version atomically; never delete/truncate the old asset to work around a lock.
        public static void ReplaceExistingText(string path,string text)
        {
            if(!File.Exists(path))throw new FileNotFoundException("Existing asset disappeared.",path);
            var temporary=path+".truecli-"+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temporary,text,new UTF8Encoding(false));
            try
            {
                for(int attempt=0;;attempt++)
                {
                    try
                    {
#if UNITY_EDITOR_WIN
                        // Mono's File.Replace can fail while removing its internal
                        // replacement file on Windows. MoveFileEx publishes the same-
                        // volume temp directly, preserving the original on failure.
                        if(!MoveFileEx(temporary,path,1|8))throw new IOException("Atomic asset publication failed: "+new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);
#else
                        File.Replace(temporary,path,null);
#endif
                        break;
                    }
                    catch(IOException) {if(attempt==3)throw;Thread.Sleep(25*(attempt+1));}
                }
            }
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
