using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace EndfieldChargePlus.Settings;

public partial class SettingsWindow
{
    private readonly CancellationTokenSource _statusLifetime=new();
    private readonly StatusGauge[] _statusGauges=Enumerable.Range(0,6).Select(i=>new StatusGauge{Label=new[]{"CPU","RAM","VRAM"}[i%3],Accent=Color.Parse(new[]{"#FFFA00","#01FFA2","#FF1AAC"}[i%3])}).ToArray();
    private volatile bool _statusActive;
    private readonly TextBlock[] _statusTips=Enumerable.Range(0,6).Select(_=>new TextBlock{TextWrapping=TextWrapping.Wrap,MaxWidth=430,FontSize=13}).ToArray();
    internal SettingsStatusSample? LastStatusSample {get;private set;}
    private void InitializeStatusBar()
    {
        for(int i=0;i<6;i++) {ToolTip.SetTip(_statusGauges[i],_statusTips[i]);ToolTip.SetShowDelay(_statusGauges[i],250);}
        foreach(var gauge in _statusGauges.Take(3))SystemStatusGroup.Children.Add(gauge);
        foreach(var gauge in _statusGauges.Skip(3))AppStatusGroup.Children.Add(gauge);
        _statusGauges[4].Width=_statusGauges[5].Width=112;
        Opened+=(_,_)=>
        {
            _statusActive=true;
            _=Task.Run(async()=>
            {
                using var sampler=new SettingsStatusSampler();
                try
                {
                    while(!_statusLifetime.IsCancellationRequested)
                    {
                        if(_statusActive)
                        {
                            var sample=sampler.Read();
                            await Dispatcher.UIThread.InvokeAsync(()=>{if(!_statusLifetime.IsCancellationRequested && _statusActive)ApplyStatusSample(sample);});
                        }
                        await Task.Delay(2000,_statusLifetime.Token).ConfigureAwait(false);
                    }
                }
                catch(OperationCanceledException) { }
                catch(Exception ex){EndfieldChargePlus.Diagnostics.AppLog.Warn($"Settings status sampling stopped ({ex.GetType().Name}).");}
            });
        };
        PropertyChanged+=(_,e)=>{if(e.Property==IsVisibleProperty || e.Property==WindowStateProperty)_statusActive=IsVisible && WindowState!=WindowState.Minimized;};
        Closed+=(_,_)=>{_statusActive=false;_statusLifetime.Cancel();};
        RefreshStatusLocalization();
    }
    internal void ApplyStatusSample(SettingsStatusSample sample)
    {
        LastStatusSample=sample;
        var values=new[]{sample.Cpu,sample.Memory,sample.Gpu,sample.AppCpu,sample.AppMemory,sample.AppGpu};
        for(int i=0;i<4;i++)_statusGauges[i].Update(values[i]);
        _statusGauges[4].UpdateBytes(sample.AppMemoryBytes,sample.AppMemory);
        _statusGauges[5].UpdateBytes(sample.AppGpuBytes,sample.AppGpu);
        RefreshStatusLocalization();
    }
    private static readonly string[] StatusTipsEn=new[]{"System CPU load across all logical processors.","Used physical RAM divided by total physical RAM.","Used dedicated VRAM divided by total dedicated VRAM across GPUs. Shared memory is excluded.","Entire Endfield Charge Plus process: Settings and all HUDs. CPU is normalized to all logical processors.","Resident RAM of the entire Endfield Charge Plus process (Settings and all HUDs), including shared resident pages. Value: MiB/GiB; arc: share of physical RAM.","Dedicated VRAM of the entire Endfield Charge Plus process (Settings and all HUDs), excluding shared GPU memory. Value: MiB/GiB; arc: share of total dedicated VRAM."};
    private static readonly string[] StatusTipsZh=new[]{"所有逻辑处理器的系统 CPU 总使用率。","已用物理内存占物理内存总量的比例。","各物理 GPU 已用专用显存占专用显存总量的比例，不包含共享内存。","整个 Endfield Charge Plus 进程，包含设置界面和全部 HUD。CPU 占用按全部逻辑处理器归一化。","整个 Endfield Charge Plus 进程（设置界面和全部 HUD）的驻留内存，包含驻留的共享页。数值显示 MiB/GiB；圆弧表示占物理内存总量的比例。","整个 Endfield Charge Plus 进程（设置界面和全部 HUD）的专用显存，不含共享 GPU 内存。数值显示 MiB/GiB；圆弧表示占专用显存总量的比例。"};
    private void RefreshStatusLocalization()
    {
        SystemStatusLabel.Text=LocalizationManager.Text("当前系统","Current system");
        AppStatusLabel.Text=LocalizationManager.Text("软件占用","Software usage");
        var unavailable=LocalizationManager.Text("不可用","Unavailable");
        var names=new[]{"CPU",LocalizationManager.Text("内存","Memory"),LocalizationManager.Text("GPU 内存","GPU memory")};
        for(int i=0;i<6;i++)
        {
            string owner=i<3?SystemStatusLabel.Text!:AppStatusLabel.Text!;
            string measured=i>=4?(_statusGauges[i].ByteSample is {} bytes?StatusGauge.FormatBytes(bytes):unavailable):(_statusGauges[i].Sample is {} value?value.ToString("0.#")+"%":unavailable);
            string detail=$"{owner} · {names[i%3]}: "+measured;
            detail+="\n"+LocalizationManager.Text(StatusTipsZh[i],StatusTipsEn[i]);
            if(i is 0 or 3)detail+="\nCPU × "+Environment.ProcessorCount;
            if(i is 1 or 4 && LastStatusSample?.TotalMemoryBytes is {} total && total>0)
                detail+="\n"+StatusGauge.FormatBytes(i==1?total*(LastStatusSample.Memory??0)/100:LastStatusSample.AppMemoryBytes??0)+" / "+StatusGauge.FormatBytes(total);
            if(i is 2 or 5 && LastStatusSample?.Adapters is {Count:>0} adapters)
            {
                int adapterIndex=0;
                foreach(var adapter in adapters)
                {
                    string Bytes(double? value)=>value is {} n?StatusGauge.FormatBytes(n):unavailable;
                    detail+="\n\nGPU "+(adapterIndex++)+" · "+adapter.Name+"\nVRAM: "+Bytes(adapter.Used)+" / "+Bytes(adapter.Capacity);
                    if(SettingsStatusSampler.Ratio(adapter.Used,adapter.Capacity) is {} percent)detail+=$" ({percent:0.#}%)";
                    detail+="\nEndfield Charge Plus: "+Bytes(adapter.ProcessUsed);
                }
            }
            // Keep the same tooltip object during each two-second update so a
            // stationary pointer can continue reading the live detail panel.
            _statusTips[i].Text=detail;
            _statusTips[i].FlowDirection=LocalizationManager.IsRightToLeft?FlowDirection.RightToLeft:FlowDirection.LeftToRight;
            AutomationProperties.SetName(_statusGauges[i],detail);
        }
    }
}
