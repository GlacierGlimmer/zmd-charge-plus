using System.Reflection;
using System.Text.Json;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Customization;

string workspace=Path.GetFullPath(args[0]);
string root=Path.Combine(workspace,"upgrade-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
int count=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);count++;}
void Write(string path,string contents="fixture"){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,contents);}
try
{
    string local=Path.Combine(root,"Local"),package=Path.Combine(local,"Packages","GlacierGlimmer.EndfieldChargePlus_test");
    string primary=Path.Combine(package,"LocalState","EndfieldChargePlus","settings.json"),cache=Path.Combine(package,"LocalCache","Local","EndfieldChargePlus","settings.json"),ac=Path.Combine(package,"AC","EndfieldChargePlus","settings.json"),portable=Path.Combine(local,"EndfieldChargePlus","settings.json"),backup=Path.Combine(local,"EndfieldChargePlus","StoreBackup","settings.json");
    Write(portable,"portable");Write(backup,"old Store backup");Write(cache,"existing Store user");
    Check(SettingsManager.ResolveSettingsReadPath(primary,local,package)==cache,"An installed Store update must prefer its own v0.1.1 LocalCache over a portable copy or stale backup.");
    Write(primary,"current LocalState");
    Check(SettingsManager.ResolveSettingsReadPath(primary,local,package)==primary,"Existing LocalState must never be replaced by legacy data.");
    File.Delete(primary);File.Delete(cache);Write(ac,"older isolated local folder");
    Check(SettingsManager.ResolveSettingsReadPath(primary,local,package)==ac,"Older package-isolated local data must migrate before unrelated portable settings.");
    File.Delete(ac);
    Check(SettingsManager.ResolveSettingsReadPath(primary,local,package)==backup,"A previous explicit Store backup remains a fallback when no own package data exists.");
    Check(SettingsManager.ResolveSettingsReadPath(portable,local,null)==portable,"Portable configuration stays in its own absolute user directory.");
    Check(File.ReadAllText(portable)=="portable"&&File.ReadAllText(backup)=="old Store backup","Path resolution must not modify either installation's data.");
    bool refused=false;try{SettingsManager.ResolveSettingsReadPath("settings.json",local,package);}catch(ArgumentException){refused=true;}
    Check(refused,"Relative settings paths must not fall back to the installation directory.");

    const string testSecret="ecp-upgrade-fixture-not-a-real-api-key";
    string protectedKey=SecretStore.Protect(testSecret);
    Check(protectedKey.Length>0&&protectedKey!=testSecret,"DPAPI must produce a protected dummy key under the current test user.");
    var fixture=new{
        HudEnabled=true,StartWithWindows=true,UiLanguage="en-US",GlobalScale=.85,HudOpacity=.8,AlwaysVisible=true,HudFontFamily="Upgrade Test Font",HudOffsetX=17,PersistentLayer=1,PositionMode=1,HudPosition=8,HudCustomX=42,HudCustomY=57,
        CustomHud=new{ActiveProfileId="custom-upgrade",DeepSeekApiKeyProtected=protectedKey,Profiles=new[]{new{Id="custom-upgrade",IsBuiltIn=false,Name="My v0.1.1 profile",PrimaryTemplate="{cpu.usage|0}",TitleTemplate="Upgrade fixture"}},CycleProfileIds=new[]{"custom-upgrade"}}
    };
    string json=JsonSerializer.Serialize(fixture);
    var old=JsonSerializer.Deserialize<AppSettings>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    var normalize=typeof(SettingsManager).GetMethod("Normalize",BindingFlags.NonPublic|BindingFlags.Static)!;
    var upgraded=(AppSettings)normalize.Invoke(null,new object[]{old})!;
    Check(upgraded.UiLanguage=="en-US"&&upgraded.HudFontFamily=="Upgrade Test Font","Existing language/font preferences must survive the v0.1.1 schema import.");
    Check(upgraded.AlwaysVisible&&Math.Abs(upgraded.GlobalScale-.85)<.0001&&Math.Abs(upgraded.HudOpacity-.8)<.0001&&upgraded.HudOffsetX==17,"Valid existing appearance/position values must be retained.");
    Check(upgraded.StartWithWindows,"The saved startup preference must survive schema import without enabling a Windows task.");
    Check(upgraded.PersistentLayer==PersistentHudLayer.Desktop&&upgraded.PositionMode==HudPositionMode.CustomCoordinates&&upgraded.HudPosition==HudPosition.BottomRight&&upgraded.HudCustomX==42&&upgraded.HudCustomY==57,"The old numeric enum values and custom coordinates must retain their meaning.");
    Check(upgraded.CustomHud.Profiles.Any(p=>p.Id=="custom-upgrade"&&p.Name=="My v0.1.1 profile"&&p.PrimaryTemplate=="{cpu.usage|0}"),"The user's custom profile and template must survive normalization.");
    Check(upgraded.CustomHud.ActiveProfileId=="custom-upgrade","The active custom scheme must survive normalization.");
    Check(upgraded.CustomHud.DeepSeekApiKeyProtected==protectedKey,"Normalization must not replace or re-encrypt the existing protected key.");
    Check(SecretStore.Unprotect(upgraded.CustomHud.DeepSeekApiKeyProtected)==testSecret,"The same Windows user's DPAPI key must remain readable independently of the application folder.");
    Check(StartupManager.StoreStartupTaskId=="EndfieldChargePlusStartup","The v0.1.1 Windows StartupTask identity must remain stable.");
    Console.WriteLine($"PASS {count} Store migration/schema/DPAPI/startup identity checks; only isolated fixture files and an in-memory dummy key were used.");
}
finally
{
    if(!Path.GetFullPath(root).StartsWith(workspace+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe fixture cleanup target.");
    Directory.Delete(root,true);
}
