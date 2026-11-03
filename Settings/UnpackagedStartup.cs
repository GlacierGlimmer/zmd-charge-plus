using Microsoft.Win32;

namespace EndfieldChargePlus.Settings;

// Launching never recreates the HKCU registration from stale JSON, and a
// portable copy cannot silently take another copy's ownership.
internal sealed class UnpackagedStartup
{
    internal const string RunPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ApprovedPath=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal const string ValueName="Endfield Charge Plus";
    private readonly string _executable,_runPath,_approvedPath,_value;
    private readonly RegistryKey _root;
    internal UnpackagedStartup(string executable,RegistryKey? root=null,string? runPath=null,string? approvedPath=null,string? value=null)
    {
        _executable=Path.GetFullPath(executable);_root=root ?? Registry.CurrentUser;
        _runPath=runPath ?? RunPath;_approvedPath=approvedPath ?? ApprovedPath;_value=value ?? ValueName;
    }
    internal static string Command(string path)=>$"\"{Path.GetFullPath(path)}\" --autostart";
    internal static string? ExecutableFromCommand(string? command)
    {
        if(string.IsNullOrWhiteSpace(command))return null;
        command=command.Trim();
        if(command[0]=='"') {int end=command.IndexOf('"',1);return end>1 ? command[1..end] : null;}
        int space=command.IndexOf(' ');return space<0 ? command : command[..space];
    }
    private string? RegisteredCommand {get {using var key=_root.OpenSubKey(_runPath);return key?.GetValue(_value) as string;}}
    private bool Owns(string? command)
    {
        try {return ExecutableFromCommand(command) is {} path && Path.GetFullPath(path).Equals(_executable,StringComparison.OrdinalIgnoreCase);}
        catch {return false;}
    }
    internal bool DisabledByWindows
    {
        get {using var key=_root.OpenSubKey(_approvedPath);return key?.GetValue(_value) is byte[] {Length:>0} state && state[0] is 3 or 7;}
    }
    internal StartupApplyResult Read()
    {
        bool owned=Owns(RegisteredCommand),blocked=DisabledByWindows;
        return new(false,owned && !blocked,true,owned && blocked ? "DisabledByUser" : "Unpackaged");
    }
    internal StartupApplyResult SetEnabled(bool enabled)
    {
        if(enabled && DisabledByWindows)return new(false,false,false,"DisabledByUser");
        using var key=_root.CreateSubKey(_runPath,writable:true);
        if(enabled)key.SetValue(_value,Command(_executable),RegistryValueKind.String);
        else if(Owns(key.GetValue(_value) as string))key.DeleteValue(_value,false);
        return Read();
    }
}
