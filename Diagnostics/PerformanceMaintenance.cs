using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;

namespace EndfieldChargePlus.Diagnostics;

// Bounded caches and idle-time housekeeping, never a working-set-number reset.
internal static class PerformanceMaintenance
{
    private static readonly DispatcherTimer Periodic=new(){Interval=TimeSpan.FromMinutes(2)};
    private static readonly DispatcherTimer AfterClose=new(){Interval=TimeSpan.FromSeconds(4)};
    private static readonly List<WeakReference<Window>> Windows=new();
    private static bool _configured;
    private static long _lastInput,_lastAllocation,_lastReclaim;
    internal static int Reclaims {get;private set;}
    static PerformanceMaintenance()
    {
        Periodic.Tick+=(_,_)=>Reclaim(false);
        AfterClose.Tick+=(_,_)=>{AfterClose.Stop();Reclaim(true);};
    }
    internal static void Configure()
    {
        if(_configured)return;_configured=true;
        SKGraphics.SetResourceCacheTotalByteLimit(Math.Min(SKGraphics.GetResourceCacheTotalByteLimit(),16L*1024*1024));
        SKGraphics.SetFontCacheLimit(Math.Min(SKGraphics.GetFontCacheLimit(),8L*1024*1024));
    }
    internal static void Track(Window window)
    {
        Configure();Periodic.Start();Windows.RemoveAll(w=>!w.TryGetTarget(out _));Windows.Add(new(window));
        window.AddHandler(InputElement.PointerPressedEvent,(_,_)=>Touch(),Avalonia.Interactivity.RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent,(_,_)=>Touch(),Avalonia.Interactivity.RoutingStrategies.Tunnel);
        window.PointerMoved+=(_,_)=>Touch();
        window.Closed+=(_,_)=>{Windows.RemoveAll(w=>!w.TryGetTarget(out var target)||ReferenceEquals(target,window));AfterClose.Stop();AfterClose.Start();};
    }
    private static void Touch()=>_lastInput=Environment.TickCount64;
    internal static bool Reclaim(bool windowClosed)
    {
        long now=Environment.TickCount64;
        if(now-_lastInput<3000 || now-_lastReclaim<15000)return false;
        if((Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows.OfType<HudWindow>().Any(h=>h.IsHudBusy || h.IsRolling)==true)return false;
        Windows.RemoveAll(w=>!w.TryGetTarget(out _));
        long allocated=GC.GetTotalAllocatedBytes(false),live=GC.GetTotalMemory(false);
        if(!windowClosed && allocated-_lastAllocation<64L*1024*1024 && live<64L*1024*1024)return false;
        bool visible=Windows.Any(w=>w.TryGetTarget(out var target)&&target.IsVisible&&target.WindowState!=WindowState.Minimized);
        LocalizationManager.TrimUnusedResources();
        SKGraphics.PurgeResourceCache();
        if(!visible)SKGraphics.PurgeFontCache();
        if(live>=32L*1024*1024)GC.Collect(GC.MaxGeneration,windowClosed && !visible?GCCollectionMode.Forced:GCCollectionMode.Optimized,blocking:false,compacting:false);
        _lastAllocation=allocated;_lastReclaim=now;Reclaims++;return true;
    }
    internal static void Stop(){Periodic.Stop();AfterClose.Stop();Windows.Clear();}
}
