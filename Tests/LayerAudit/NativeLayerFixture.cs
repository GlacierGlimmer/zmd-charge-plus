using System.Runtime.InteropServices;
using System.Text;
internal static class NativeLayerFixture
{
    internal static int Run(string[] args)
    {
        var desktop=new StringBuilder(256);GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()),2,desktop,512,out _);
        if(!desktop.ToString().StartsWith("ECP-Audit-"))return 3;
        var window=CreateWindowEx(0x08000080,"STATIC","ECP private layer fixture",0x80000000,int.Parse(args[1]),int.Parse(args[2]),int.Parse(args[3]),int.Parse(args[4]),IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(window==IntPtr.Zero)return 4;
        SetWindowPos(window,new IntPtr(-1),0,0,0,0,1|2|16|64);
        File.WriteAllText(args[0],window.ToInt64().ToString());
        while(GetMessage(out var message,IntPtr.Zero,0,0)>0){TranslateMessage(ref message);DispatchMessage(ref message);}return 0;
    }
    [StructLayout(LayoutKind.Sequential)]private struct Message {public IntPtr Window;public uint Id;public UIntPtr WParam;public IntPtr LParam;public uint Time;public int X,Y;public uint Private;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateWindowEx(uint ex,string cls,string name,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]static extern int GetMessage(out Message message,IntPtr window,uint min,uint max);
    [DllImport("user32.dll")]static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")]static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr desktop,int index,StringBuilder name,uint bytes,out uint needed);
}
