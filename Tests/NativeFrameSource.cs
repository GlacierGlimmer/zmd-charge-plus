using System.Runtime.InteropServices;

internal sealed class NativeFrameSource : IDisposable
{
    internal IntPtr Window { get; }
    private IntPtr _device,_context,_chain,_view;
    private int _phase;
    [StructLayout(LayoutKind.Sequential)] private struct Mode { public uint Width,Height,RateNumerator,RateDenominator,Format,Scanline,Scaling; }
    [StructLayout(LayoutKind.Sequential)] private struct Samples { public uint Count,Quality; }
    [StructLayout(LayoutKind.Sequential)] private struct Description
    { public Mode Mode; public Samples Samples; public uint Usage,BufferCount; public IntPtr Window; public int Windowed; public uint Effect,Flags; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBuffer(IntPtr self,uint index,ref Guid iid,out IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateView(IntPtr self,IntPtr buffer,IntPtr description,out IntPtr view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ClearView(IntPtr self,IntPtr view,[In] float[] color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Present(IntPtr self,uint interval,uint flags);
    private static T Method<T>(IntPtr self,int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self),slot*IntPtr.Size));
    internal NativeFrameSource(int x=120,string title="ECP actual FPS test source")
    {
        Window=CreateWindowEx(0,"STATIC",title,0x10CF0000,x,120,420,280,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        var description=new Description { Mode=new() { Width=420,Height=280,Format=87 },Samples=new() { Count=1 },Usage=0x20,BufferCount=2,Window=Window,Windowed=1,Effect=4 };
        Marshal.ThrowExceptionForHR(D3D11CreateDeviceAndSwapChain(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,ref description,out _chain,out _device,out _,out _context));
        var iid=new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        Marshal.ThrowExceptionForHR(Method<GetBuffer>(_chain,9)(_chain,0,ref iid,out var buffer));
        try { Marshal.ThrowExceptionForHR(Method<CreateView>(_device,9)(_device,buffer,IntPtr.Zero,out _view)); }
        finally { Marshal.Release(buffer); }
    }
    internal void Render()
    {
        Method<ClearView>(_context,50)(_context,_view,new[] { (_phase++%255)/255f,.2f,.6f,1f });
        Marshal.ThrowExceptionForHR(Method<Present>(_chain,8)(_chain,0,0));
    }
    public void Dispose()
    {
        foreach(var pointer in new[] { _view,_chain,_context,_device }) if(pointer!=IntPtr.Zero) Marshal.Release(pointer);
        DestroyWindow(Window);
    }
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter,uint driver,IntPtr software,uint flags,IntPtr levels,uint levelCount,uint sdk,ref Description description,out IntPtr chain,out IntPtr device,out uint level,out IntPtr context);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex,string cls,string title,uint style,int x,int y,int w,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
}
