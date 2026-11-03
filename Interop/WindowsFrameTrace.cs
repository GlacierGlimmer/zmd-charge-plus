using System.Runtime.InteropServices;
namespace EndfieldChargePlus.Interop;

/// <summary>Stop only this application's trace session before killing its reader.</summary>
internal static class WindowsFrameTrace
{
    [StructLayout(LayoutKind.Sequential)] private struct Wnode
    { public uint Size,Provider; public ulong Context,Time; public Guid Guid; public uint Clock,Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Properties
    {
        public Wnode Wnode;
        public uint BufferSize,MinimumBuffers,MaximumBuffers,MaximumFileSize,LogFileMode,FlushTimer,EnableFlags,AgeLimit,
            NumberOfBuffers,FreeBuffers,EventsLost,BuffersWritten,LogBuffersLost,RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset,LoggerNameOffset;
    }
    internal static void Stop(string session)
    {
        int size=Marshal.SizeOf<Properties>();
        IntPtr memory=Marshal.AllocHGlobal(size+4096);
        try
        {
            for(int i=0;i<size+4096;i++) Marshal.WriteByte(memory,i,0);
            var properties=new Properties { Wnode=new() { Size=(uint)(size+4096) },LoggerNameOffset=(uint)size };
            Marshal.StructureToPtr(properties,memory,false);
            ControlTrace(0,session,memory,1);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    internal static void CleanupOrphans()
    {
        int size=Marshal.SizeOf<Properties>();
        var buffers=new IntPtr[128];
        try
        {
            for(int i=0;i<buffers.Length;i++)
            {
                buffers[i]=Marshal.AllocHGlobal(size+4096);
                for(int j=0;j<size+4096;j++) Marshal.WriteByte(buffers[i],j,0);
                var props=new Properties { Wnode=new() { Size=(uint)(size+4096) },LoggerNameOffset=(uint)size,LogFileNameOffset=(uint)(size+2048) };
                Marshal.StructureToPtr(props,buffers[i],false);
            }
            uint status=QueryAllTraces(buffers,(uint)buffers.Length,out uint count);
            if (status is not (0 or 234)) return;
            for(int i=0;i<Math.Min(count,buffers.Length);i++)
            {
                var props=Marshal.PtrToStructure<Properties>(buffers[i]);
                if(props.LoggerNameOffset<size || props.LoggerNameOffset>=size+4096) continue;
                string name=Marshal.PtrToStringUni(buffers[i]+(int)props.LoggerNameOffset) ?? "";
                if (!name.StartsWith("ECP-FPS-",StringComparison.Ordinal) || !int.TryParse(name[8..].Split('-')[0],out int pid)) continue;
                try { using var owner=System.Diagnostics.Process.GetProcessById(pid); if (!owner.HasExited) continue; }
                catch (ArgumentException) { }
                catch (System.ComponentModel.Win32Exception) { continue; }
                Stop(name);
            }
        }
        finally { foreach(var buffer in buffers) if(buffer!=IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("advapi32.dll",EntryPoint="QueryAllTracesW",CharSet=CharSet.Unicode)] private static extern uint QueryAllTraces([In] IntPtr[] properties,uint count,out uint total);
    [DllImport("advapi32.dll",EntryPoint="ControlTraceW",CharSet=CharSet.Unicode)] private static extern uint ControlTrace(ulong session,string name,IntPtr properties,uint control);
}
