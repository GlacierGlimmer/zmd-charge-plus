using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using EndfieldChargePlus.Customization;

internal static class FrameCollectorResourceChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var selection=typeof(FrameRateBackend).GetMethod("CollectorFor",BindingFlags.NonPublic|BindingFlags.Static)!;
        foreach(var architecture in new[] { Architecture.X64,Architecture.Arm64 })
        {
            var collector=((string FileName,string Resource,string Sha256))selection.Invoke(null,new object[] { architecture })!;
            check(collector.FileName==($"ECPFrames-{(architecture==Architecture.Arm64 ? "arm64" : "x64")}.exe"),
                $"The {architecture} Windows OS selects its native frame collector independently of app emulation.");
            using var source=typeof(FrameRateBackend).Assembly.GetManifestResourceStream(collector.Resource)!;
            using var data=new MemoryStream();source.CopyTo(data);var bytes=data.ToArray();
            int offset=BitConverter.ToInt32(bytes,0x3c);
            check(Convert.ToHexString(SHA256.HashData(bytes))==collector.Sha256 && bytes[offset]==(byte)'P' && bytes[offset+1]==(byte)'E'
                && BitConverter.ToUInt16(bytes,offset+4)==(architecture==Architecture.Arm64 ? 0xaa64 : 0x8664),
                $"The embedded {architecture} native collector has the expected PE machine and release integrity hash.");
        }
    }
}
