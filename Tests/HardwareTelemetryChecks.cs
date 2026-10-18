using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

internal static class HardwareTelemetryChecks
{
    private const string Fixture="<temp><id>TCPUPKG</id><label>CPU Package</label><value>76.5</value></temp>"+
        "<temp><id>TCC-1-1</id><label>Core 1</label><value>72</value></temp>"+
        "<fan><id>FCPU</id><label>CPU Fan</label><value>0</value></fan>"+
        "<volt><id>VCPU</id><label>CPU Core</label><value>1.125</value></volt>"+
        "<pwr><id>PCPUPKG</id><label>CPU Package</label><value>35.75</value></pwr>"+
        "<curr><id>CCPU</id><label>CPU Current</label><value>12.5</value></curr>"+
        "<sys><id>SMEMTIM</id><label>Memory Timings</label><value>40-40-40-80 CR2</value></sys>"+
        "<sys><id>SFUTURE</id><label>New Data &amp; Label</label><value>custom status</value></sys>";
    internal static void Run(Action<bool,string> check,bool liveOnly=false)
    {
        if(!liveOnly)Models(check);
        var theme=new Avalonia.Themes.Fluent.FluentTheme();var application=Application.Current!;var previousTheme=application.RequestedThemeVariant;
        application.Styles.Add(theme);application.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;
        using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(120));Exception? failure=null;bool completed=false;
        Dispatcher.UIThread.Post(async()=>{try{await Live(check,liveOnly);completed=true;}catch(Exception ex){failure=ex;}finally{finish.Cancel();}});
        try{Dispatcher.UIThread.MainLoop(finish.Token);}finally{application.Styles.Remove(theme);application.RequestedThemeVariant=previousTheme;}
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if(!completed)throw new TimeoutException("Hardware diagnostics checks timed out.");
    }
    private static void Models(Action<bool,string> check)
    {
        var readings=Aida64SensorSource.Parse(Fixture);
        check(readings.Count==8,"AIDA64 exports all numeric and text categories, including unknown system fields.");
        check(Aida64SensorSource.Parse("<AIDA64>"+Fixture+"</AIDA64>").Count==8,"Both the official XML fragment and a wrapped export preserve every field.");
        check(readings.Single(r=>r.Id=="FCPU").Value is double and 0,"An actually stopped AIDA64 fan remains zero, not missing.");
        check(readings.Single(r=>r.Id=="VCPU").Unit=="V" && readings.Single(r=>r.Id=="PCPUPKG").Unit=="W" && readings.Single(r=>r.Id=="CCPU").Unit=="A","AIDA64 physical quantities retain the correct units.");
        check(readings.Single(r=>r.Id=="SMEMTIM").Value is "40-40-40-80 CR2" && readings.Single(r=>r.Id=="SFUTURE").Name=="New Data & Label","Memory timings, custom text and escaped labels remain available.");
        var cpu=readings.First();
        check(cpu.Key==(cpu with{Name="Renamed",Device="Other display name"}).Key,"Sensor identity is independent of labels and display order.");
        check(cpu.Key!=(cpu with{Source="lhm"}).Key,"Providers cannot overwrite one another's sensor keys.");
        check(Aida64SensorSource.Parse("<temp><id>TCPU</id><value>NaN</value></temp><temp><id>TCPU2</id><value>TRIAL</value></temp>").Count==0,"NaN and trial-locked numeric values cannot become synthetic measurements.");
        check(Aida64SensorSource.Parse("<temp>").Count==0 && Aida64SensorSource.Parse("<!DOCTYPE x SYSTEM 'file:///C:/Windows/win.ini'><temp/>").Count==0,"Malformed and external-entity XML is rejected without reading external files.");
        check(Aida64SensorSource.Parse(new string('x',1024*1024+1)).Count==0,"Shared memory parsing has a bounded size.");
        var registry=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"Value.TCPU","55.5"},{"Label.TCPU","CPU"},{"Value.SMEMTIM","32-39-39"},{"Label.SMEMTIM","Timings"}};
        check(Aida64SensorSource.ParseRegistry(registry).Count==2,"Official AIDA64 Registry value and label pairs support numeric and text fields.");
        var values=new Dictionary<string,object?>();HardwareTelemetryCatalog.Publish(readings);HardwareTelemetryCatalog.Write(values,readings);
        check((double)values[cpu.Key+".value"]! ==76.5 && VariableCatalog.Find(cpu.Key+".value") is not null,"Discovered AIDA64 values are both sampled and selectable in the variable library.");
        check(HudProfileRenderer.Render(new HudProfile{PrimaryTemplate="{"+cpu.Key+".value|0.0}",ProgressVariable="=50"},values).PrimaryText=="76.5","Dynamic sensor tokens render through the actual HUD template pipeline.");
        var unavailable=cpu with {Value=null};values.Clear();HardwareTelemetryCatalog.Write(values,new[]{unavailable});
        check(values[cpu.Key+".value"] is null && HudProfileRenderer.Render(new HudProfile{PrimaryTemplate="{"+cpu.Key+".value}",ProgressVariable="=50"},values).PrimaryText=="--","Missing readings stay unavailable rather than retaining a prior temperature.");
        check(AdvancedVariableProvider.FindExternalCpuTemperature(readings)==76.5,"AIDA64 CPU aliases include package and per-core temperatures.");
        check(AdvancedVariableProvider.FindExternalCpuTemperature(new[]{cpu with {Id="TGPU",Value=100d},cpu with {Id="TCPU",Value=0d}})==0,"CPU aliases exclude GPU temperatures and preserve a legitimate zero.");
        check(AdvancedVariableProvider.FindExternalCpuTemperature(new[]{cpu with {Source="lhmwmi",Id="/intelcpu/0/temperature/1",Name="Core Distance to TjMax",Value=40d}}) is null,"Distance to TjMax is never mistaken for actual CPU temperature.");
        check(HardwareTelemetryCatalog.Unit("Data")=="GiB" && HardwareTelemetryCatalog.Unit("SmallData")=="MiB" && HardwareTelemetryCatalog.Unit("Flow")=="L/h","LHM 0.9.6 memory and flow sensor units match the actual API version.");
        check(new HardwareDriverState(false,"",true,false).Status=="DriverMissing","Administrator rights alone cannot satisfy a missing driver.");
        check(new HardwareDriverState(true,"2.2.0",false,false).Status=="AdministratorRequired" && new HardwareDriverState(true,"2.2.0",true,true).Status=="RestartRequired","Driver, privilege and process restart requirements are distinguished.");
        check(new HardwareDriverState(true,"2.2.0",true,false,false,5).Status=="DriverInaccessible","An installed driver whose device cannot be opened is not reported ready.");
        var failed=DispatchProxy.Create<LibreHardwareMonitor.Hardware.IHardware,HardwareUpdateFailureProxy>();
        var snapshotType=typeof(AdvancedVariableProvider).GetNestedType("HardwareSnapshot",BindingFlags.NonPublic)!;
        var hardwareSnapshot=Activator.CreateInstance(snapshotType,true)!;
        typeof(AdvancedVariableProvider).GetMethod("ReadHardware",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{failed,hardwareSnapshot});
        check(!((HardwareUpdateFailureProxy)(object)failed).SensorsRead,"A failed hardware update cannot republish cached sensor values as current data.");
        string mapName="ECP-AIDA-fixture-"+Guid.NewGuid().ToString("N");
        using(var map=MemoryMappedFile.CreateNew(mapName,16384))
        {
            using(var view=map.CreateViewStream()) {var bytes=Encoding.UTF8.GetBytes(Fixture.Replace("CPU Package","CPU 封装")+"\0");view.Write(bytes);}
            var source=new Aida64SensorSource(mapName);var actual=source.Read();
            check(actual.Count==8 && source.Transport=="SharedMemory","The real Windows shared-memory transport reads the official null-terminated fragment format.");
            check(actual.First().Name=="CPU 封装","UTF-8 custom sensor labels survive shared-memory decoding.");
        }
    }
    private static async Task Live(Action<bool,string> check,bool requireTemperature)
    {
        var hud=new HudWindow();var settings=new AppSettings{HudEnabled=false};using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
        var snapshot=await runtime.SnapshotHardwareAsync(CancellationToken.None);
        check(snapshot.ContainsKey("hardware.driver.status") && snapshot.ContainsKey("hardware.cpu_temperature_status"),"A hardware snapshot always reports actionable driver and CPU collection state.");
        check(HardwareTelemetryCatalog.Definitions.Any(d=>d.Key.StartsWith("sensor.inventory.") && d.Name.Contains("memory.",StringComparison.Ordinal)),"Actual firmware memory-device information is exposed as HUD variables.");
        var readings=HardwareTelemetryCatalog.Definitions.Where(d=>d.Key.EndsWith(".value")).ToArray();
        int available=readings.Count(d=>snapshot.GetValueOrDefault(d.Key) is not null);
        Console.WriteLine($"Hardware snapshot: {readings.Length} fields, {available} available; driver={HardwareSensorDriver.Inspect().Status}; CPU={snapshot.GetValueOrDefault("cpu.temperature_max","--")}");
        check(available>10,"The native sampler returns actual hardware and firmware values.");
        if(!HardwareSensorDriver.Inspect().Installed && !Convert.ToBoolean(snapshot.GetValueOrDefault("hardware.aida64.available",false)))
            check(snapshot.GetValueOrDefault("cpu.temperature_max") is null,"A missing CPU driver cannot create a misleading 0 °C native reading.");
        if(requireTemperature)
        {
            check(HardwareSensorDriver.Inspect() is {Installed:true,Elevated:true,RestartRequired:false},"Live CPU sampling has the signed driver and elevated process.");
            check(snapshot.GetValueOrDefault("cpu.temperature_max") is double temp && double.IsFinite(temp) && temp>0 && temp<150,"The actual CPU temperature is available in the real HUD sampling pipeline.");
            await Task.Delay(1100);var again=await runtime.SnapshotHardwareAsync(CancellationToken.None);
            check(again.GetValueOrDefault("cpu.temperature_max") is double,"A subsequent real sample continues to report CPU temperature.");
        }
        var window=new SettingsWindow(settings,hud,runtime);window.Show();window.NavigateToSettingsSection("hardware");
        try {
            await window.RefreshHardwareAsync();await Task.Delay(250);
            check(window.FindControl<StackPanel>("HardwarePanel")!.Children.OfType<ListBox>().Single().ItemCount>10,"The actual Hardware Data page displays discovered sensor and inventory rows.");
            var panel=window.FindControl<StackPanel>("HardwarePanel")!;var search=panel.Children.OfType<TextBox>().Single();var list=panel.Children.OfType<ListBox>().Single();
            int total=list.ItemCount;search.Text="温度";await Task.Delay(100);
            check(list.ItemCount>0 && list.ItemCount<total,$"Chinese temperature search finds raw hardware sensors ({list.ItemCount}/{total}).");
            search.Text="内存条";await Task.Delay(100);check(list.ItemCount>0 && list.ItemCount<total,"Chinese memory-module search finds firmware inventory.");search.Text="";await Task.Delay(100);
            LocalizationManager.SetLanguage(AppLanguage.English);
            typeof(SettingsWindow).GetMethod("ApplyLocalization",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            check(window.FindControl<TabItem>("HardwarePage")!.Header?.ToString()=="Hardware Data","Hardware Data page localizes through the real language switch.");
            await Task.Delay(400);window.UpdateLayout();
            await window.RefreshHardwareAsync();
            check(window.FindControl<StackPanel>("HardwarePanel")!.Children.OfType<ListBox>().Single().ItemCount>10,"Repeated native list updates and localization preserve the live sensor rows.");
            check(panel.Children.OfType<TextBlock>().First().Text!.Contains("CPU:"),"Language changes retain the CPU collection summary.");
            if(Environment.GetEnvironmentVariable("ECP_HARDWARE_TEST_OUTPUT") is {Length:>0} output)
            {
                Directory.CreateDirectory(output);using var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height));bitmap.Render(window);bitmap.Save(Path.Combine(output,"hardware-data.png"));
                var report=snapshot.Where(p=>p.Key.StartsWith("hardware.") || p.Key.StartsWith("cpu.temperature")).ToDictionary(p=>p.Key,p=>p.Value);
                File.WriteAllText(Path.Combine(output,requireTemperature ? "live-hardware-after.json" : "live-hardware-before.json"),System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            }
        } finally {window.Close();hud.Close();LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);}
    }
}

public class HardwareUpdateFailureProxy : DispatchProxy
{
    internal bool SensorsRead;
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch {
        "Update"=>throw new IOException("Simulated device disappeared"),
        "get_SubHardware"=>Array.Empty<LibreHardwareMonitor.Hardware.IHardware>(),
        "get_Sensors"=>ReadSensors(),"get_HardwareType"=>LibreHardwareMonitor.Hardware.HardwareType.Cpu,"get_Name"=>"CPU",_=>null };
    private object ReadSensors() {SensorsRead=true;return Array.Empty<LibreHardwareMonitor.Hardware.ISensor>();}
}
