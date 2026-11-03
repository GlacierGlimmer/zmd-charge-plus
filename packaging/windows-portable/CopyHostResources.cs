using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class EcpNativeHostResources
{
 private delegate bool NameCallback(IntPtr module,IntPtr type,IntPtr name,IntPtr context);
 private delegate bool LanguageCallback(IntPtr module,IntPtr type,IntPtr name,ushort language,IntPtr context);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryExW(string file,IntPtr reserved,uint flags);
 [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool EnumResourceNamesW(IntPtr module,IntPtr type,NameCallback callback,IntPtr context);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool EnumResourceLanguagesW(IntPtr module,IntPtr type,IntPtr name,LanguageCallback callback,IntPtr context);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr FindResourceExW(IntPtr module,IntPtr type,IntPtr name,ushort language);
 [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module,IntPtr resource);
 [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module,IntPtr resource);
 [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr BeginUpdateResourceW(string file,bool deleteExisting);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool UpdateResourceW(IntPtr update,IntPtr type,IntPtr name,ushort language,byte[] bytes,uint length);
 [DllImport("kernel32.dll",SetLastError=true)] private static extern bool EndUpdateResourceW(IntPtr update,bool discard);
 public static void Copy(string source,string destination)
 {
  IntPtr module=LoadLibraryExW(source,IntPtr.Zero,0x60);
  if(module==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
  IntPtr update=BeginUpdateResourceW(destination,false);
  if(update==IntPtr.Zero){FreeLibrary(module);throw new Win32Exception(Marshal.GetLastWin32Error());}
  Exception failure=null;
  try{
   foreach(int typeId in new[]{3,14,16,24}){
    int copied=0;
    NameCallback names=(m,type,name,context)=>{
     LanguageCallback languages=(lm,lt,ln,language,lc)=>{
      try{
       IntPtr found=FindResourceExW(lm,lt,ln,language);uint size=SizeofResource(lm,found);
       IntPtr data=LockResource(LoadResource(lm,found));
       if(found==IntPtr.Zero||data==IntPtr.Zero||size==0)throw new Win32Exception(Marshal.GetLastWin32Error());
       byte[] bytes=new byte[checked((int)size)];Marshal.Copy(data,bytes,0,bytes.Length);
       if(!UpdateResourceW(update,lt,ln,language,bytes,size))throw new Win32Exception(Marshal.GetLastWin32Error());
       copied++;return true;
      }catch(Exception ex){failure=ex;return false;}
     };
     bool ok=EnumResourceLanguagesW(m,type,name,languages,IntPtr.Zero);GC.KeepAlive(languages);
     if(!ok&&failure==null)failure=new Win32Exception(Marshal.GetLastWin32Error());return ok;
    };
    bool success=EnumResourceNamesW(module,new IntPtr(typeId),names,IntPtr.Zero);GC.KeepAlive(names);
    if(failure!=null)throw failure;
    if(!success||copied==0)throw new InvalidOperationException("Missing EXE resource type "+typeId);
   }
   if(!EndUpdateResourceW(update,false))throw new Win32Exception(Marshal.GetLastWin32Error());update=IntPtr.Zero;
  }finally{if(update!=IntPtr.Zero)EndUpdateResourceW(update,true);FreeLibrary(module);}
 }
}
