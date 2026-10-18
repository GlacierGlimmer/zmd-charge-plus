using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Styling;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private readonly DispatcherTimer _hardwareTimer=new(){Interval=TimeSpan.FromSeconds(2)};
    private readonly CancellationTokenSource _hardwareLifetime=new();
    private readonly Dictionary<string,object?> _hardwareValues=new(StringComparer.OrdinalIgnoreCase);
    private TextBlock _hardwareStatus=null!,_hardwareHint=null!,_hardwareCount=null!;
    private TextBox _hardwareSearch=null!;
    private ListBox _hardwareList=null!;
    private readonly System.Collections.ObjectModel.ObservableCollection<HardwareRow> _hardwareRows=new();
    private Button _installDriver=null!,_refreshHardware=null!,_exportHardware=null!,_copyHardwareKey=null!,_restartHardware=null!;
    private bool _readingHardware;
    private sealed record HardwareRow(string Key,string Name,string Value,string Unit,string Id);
    private void InitializeHardwarePanel()
    {
        var panel=HardwarePanel;
        _hardwareStatus=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Yellow};
        _hardwareHint=new(){TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Gray,FontSize=12};
        _hardwareCount=new();_hardwareSearch=new(){Watermark="Search",MinWidth=220};
        _hardwareList=new(){MinHeight=260,MaxHeight=480};
        _hardwareList.ItemsSource=_hardwareRows;
        _hardwareList.Styles.Add(new Style(s=>s.OfType<ListBoxItem>()){Setters={new Setter(ListBoxItem.HorizontalContentAlignmentProperty,Avalonia.Layout.HorizontalAlignment.Stretch)}});
        _hardwareList.ItemTemplate=new FuncDataTemplate<HardwareRow>((row,_)=>{
            if(row is null)return new TextBlock();
            var grid=new Grid{ColumnDefinitions=new("3*,1*,Auto"),Margin=new Thickness(4,5)};
            var name=new TextBlock{Text=row!.Name,TextWrapping=TextWrapping.Wrap};
            ToolTip.SetTip(name,row.Id+"\n"+row.Key);
            var value=new TextBlock{Text=row.Value,Margin=new Thickness(14,0,10,0),TextWrapping=TextWrapping.Wrap};
            var unit=new TextBlock{Text=row.Unit,MinWidth=42,Foreground=Brushes.Gray};
            Grid.SetColumn(value,1);Grid.SetColumn(unit,2);grid.Children.Add(name);grid.Children.Add(value);grid.Children.Add(unit);return grid;
        });
        _installDriver=new();_refreshHardware=new();_exportHardware=new();_copyHardwareKey=new();_restartHardware=new();
        var actions=new WrapPanel{Orientation=Avalonia.Layout.Orientation.Horizontal};
        foreach(var button in new[]{_installDriver,_restartHardware,_refreshHardware,_exportHardware,_copyHardwareKey}) {button.Margin=new Thickness(0,0,8,8);actions.Children.Add(button);}
        panel.Children.Add(_hardwareStatus);panel.Children.Add(_hardwareHint);panel.Children.Add(actions);panel.Children.Add(_hardwareSearch);panel.Children.Add(_hardwareCount);panel.Children.Add(_hardwareList);
        _hardwareSearch.TextChanged+=(_,_)=>FilterHardwareRows();
        _refreshHardware.Click+=async(_,_)=>await RefreshHardwareAsync();
        _hardwareTimer.Tick+=async(_,_)=>{if(HardwarePage.IsSelected && DataManagementPage.IsSelected)await RefreshHardwareAsync();};
        HardwarePage.PropertyChanged+=async(_,e)=>{
            if(e.Property==TabItem.IsSelectedProperty && HardwarePage.IsSelected && DataManagementPage.IsSelected)await RefreshHardwareAsync();
        };
        DataManagementPage.PropertyChanged+=async(_,e)=>{if(e.Property==TabItem.IsSelectedProperty && DataManagementPage.IsSelected && HardwarePage.IsSelected)await RefreshHardwareAsync();};
        _installDriver.Click+=async(_,_)=>{
            _installDriver.IsEnabled=false;
            try {
                _hardwareStatus.Text=LocalizationManager.Text("正在获取官方驱动安装程序；校验后将打开安装向导。","Downloading the official driver installer; the setup wizard will open after verification.");
                int exit=await HardwareSensorDriver.InstallAsync(_hardwareLifetime.Token);
                _hardwareStatus.Text=exit is 0 or 3010 ? LocalizationManager.Text("驱动安装完成，请完全退出并以管理员身份重新启动 ECP。","Driver installation completed. Exit and restart ECP as administrator.")
                    : LocalizationManager.Formatted($"安装程序已退出，返回码 {exit}。",$"The installer exited with code {exit}.");
            } catch(OperationCanceledException) { }
            catch(Exception ex) {_hardwareStatus.Text=ex.Message;Diagnostics.AppLog.Error("Sensor driver installation failed.",ex);}
            finally {_installDriver.IsEnabled=true;}
        };
        _restartHardware.Click+=(_,_)=>RestartForHardware();
        _exportHardware.Click+=async(_,_)=>await ExportHardwareAsync();
        _copyHardwareKey.Click+=async(_,_)=>{
            if(_hardwareList.SelectedItem is HardwareRow row && Clipboard is { } clipboard)await clipboard.SetTextAsync("{"+row.Key+"}");
        };
        Closed+=(_,_)=>{_hardwareTimer.Stop();_hardwareLifetime.Cancel();_hardwareLifetime.Dispose();};
        _hardwareTimer.Start();ApplyHardwareLocalization();
    }
    private void ApplyHardwareLocalization()
    {
        if(_hardwareStatus is null)return;
        _hardwareHint.Text=LocalizationManager.Text("温度、功耗、电压等底层数据需要安装 PawnIO 并以管理员身份运行。AIDA64 用户可在“设置 → 硬件监控 → 外部程序”开启共享内存、注册表或 WMI；这里会读取其导出的全部项目。未提供的传感器显示 --。",
            "Low-level temperature, power and voltage readings require PawnIO and administrator access. AIDA64 users can enable shared memory, Registry or WMI under Preferences → Hardware Monitoring → External Applications; all exported items are available here. Missing readings show --.");
        _installDriver.Content=LocalizationManager.Text("安装/更新 PawnIO 驱动","Install / update PawnIO");_refreshHardware.Content=LocalizationManager.Text("重新检测","Refresh");
        _exportHardware.Content=LocalizationManager.Text("导出硬件诊断","Export hardware diagnostics");_copyHardwareKey.Content=LocalizationManager.Text("复制所选变量","Copy selected variable");
        _restartHardware.Content=LocalizationManager.Text("以管理员身份重新启动","Restart as administrator");
        _hardwareSearch.Watermark=LocalizationManager.Text("搜索名称、来源 ID 或变量","Search name, source ID or variable");FilterHardwareRows();
        UpdateHardwareStatus();
    }
    internal async Task RefreshHardwareAsync()
    {
        if(_readingHardware || _hardwareLifetime.IsCancellationRequested)return;
        _readingHardware=true;_refreshHardware.IsEnabled=false;
        try {
            var values=await _runtime.SnapshotHardwareAsync(_hardwareLifetime.Token);
            if(_hardwareLifetime.IsCancellationRequested)return;
            _hardwareValues.Clear();foreach(var pair in values)_hardwareValues[pair.Key]=pair.Value;
            UpdateHardwareStatus();
            FilterHardwareRows();Customizer.RefreshHardwareVariables();
        } catch(OperationCanceledException) { }
        catch(Exception ex) {_hardwareStatus.Text=ex.Message;Diagnostics.AppLog.Error("Hardware diagnostics refresh failed.",ex);}
        finally {_readingHardware=false;_refreshHardware.IsEnabled=true;}
    }
    private void UpdateHardwareStatus()
    {
        _hardwareStatus.Text=HardwareSensorDriver.Inspect().Description;
        if(_hardwareValues.Count>0)_hardwareStatus.Text+="\nCPU: "+(_hardwareValues.TryGetValue("cpu.temperature_max",out var temp) && temp is double number ? number.ToString("0.0",CultureInfo.CurrentCulture)+" °C" : "--")
            +"  ·  AIDA64: "+_hardwareValues.GetValueOrDefault("hardware.aida64.transport","Unavailable");
    }
    private void FilterHardwareRows()
    {
        if(_hardwareList is null)return;
        string query=_hardwareSearch.Text?.Trim() ?? "";string? selected=(_hardwareList.SelectedItem as HardwareRow)?.Key;
        var rows=HardwareTelemetryCatalog.Definitions.Where(d=>d.Key.EndsWith(".value",StringComparison.Ordinal))
            .Select(d=>new HardwareRow(d.Key,d.Name,Format(_hardwareValues.GetValueOrDefault(d.Key)),d.Unit,d.Description))
            .Where(r=>query.Length==0 || (r.Name+" "+r.Key+" "+r.Id).Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(_hardwareRows.Count==rows.Length && _hardwareRows.Select(r=>r.Key).SequenceEqual(rows.Select(r=>r.Key)))
        {
            for(int i=0;i<rows.Length;i++)if(_hardwareRows[i]!=rows[i])_hardwareRows[i]=rows[i];
        }
        else {_hardwareRows.Clear();foreach(var row in rows)_hardwareRows.Add(row);}
        _hardwareList.SelectedItem=_hardwareRows.FirstOrDefault(r=>r.Key==selected);
        _hardwareCount.Text=LocalizationManager.Formatted($"显示 {rows.Length} 个项目；包含原始传感器及固件信息。",(System.FormattableString)$"Showing {rows.Length} items, including sensors and firmware information.");
        static string Format(object? value)=>value is double number ? number.ToString("0.###",CultureInfo.CurrentCulture) : Convert.ToString(value,CultureInfo.CurrentCulture) ?? "--";
    }
    private async Task ExportHardwareAsync()
    {
        var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title=LocalizationManager.Text("导出硬件诊断","Export hardware diagnostics"),SuggestedFileName="ECP-hardware-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json",DefaultExtension="json"});
        if(file is null)return;
        try {
            var report=new { capturedAt=DateTimeOffset.Now,driver=HardwareSensorDriver.Inspect(),values=_hardwareValues.Where(p=>p.Key.StartsWith("hardware.") || p.Key.StartsWith("sensor.") || p.Key.StartsWith("cpu.temperature")).ToDictionary(p=>p.Key,p=>p.Value) };
            await using var stream=await file.OpenWriteAsync();stream.SetLength(0);await JsonSerializer.SerializeAsync(stream,report,new JsonSerializerOptions{WriteIndented=true});
        } catch(Exception ex) {_hardwareStatus.Text=ex.Message;}
    }
    private void RestartForHardware()
    {
        try {
            var info=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};
            info.ArgumentList.Add("--restart-from");info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            if(System.Diagnostics.Process.Start(info) is not null && Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)desktop.Shutdown();
        } catch(Exception ex) {_hardwareStatus.Text=ex.Message;}
    }
}
