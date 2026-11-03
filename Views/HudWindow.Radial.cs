using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Interop;
using EndfieldChargePlus.Settings;

namespace EndfieldChargePlus.Views;

public partial class HudWindow
{
    internal Func<IReadOnlyList<HudProfile>>? RadialProfilesProvider;
    internal event Action<string?>? RadialFinished;
    internal event Action? RadialOpened;
    internal bool IsRadialOpen=>_ringWindow is not null;
    private WindowsRadialInput? _radialInput;
    private UnixRadialInput? _unixRadialInput;
    private ProfileRingWindow? _ringWindow;
    private readonly DispatcherTimer _holdTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private readonly System.Diagnostics.Stopwatch _holdClock=new();
    private PixelPoint _holdOrigin,_holdPoint;
    private bool _middleHeld;
    private bool _radialMoved;
    private IntPtr _holdForeground;
    private IPointer? _capturedPointer;
    private bool CanStartRadial(PixelPoint point)=>!_closed && _settings.HudEnabled && _settings.Workspace.RadialEnabled && IsVisible && IsPointInsideVisibleHud(point) && (RadialProfilesProvider?.Invoke().Count ?? 0)>0;

    private void InitializeRadialInput()
    {
        _holdTimer.Tick+=(_,_)=>{
            if(!_middleHeld)return;
            if(_closed || !IsVisible || !_settings.HudEnabled || !_settings.Workspace.RadialEnabled ||
                !OperatingSystem.IsWindows() && UnixRadialInput.EscapePressed ||
                OperatingSystem.IsWindows() && (WindowsRadialInput.EscapePressed || WindowsRadialInput.Foreground!=_holdForeground && WindowsRadialInput.Foreground!=(_ringWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero))) {CancelRadial();return;}
            if(_ringWindow is null && _holdClock.ElapsedMilliseconds>=_settings.Workspace.HoldMilliseconds) OpenRadial();
        };
        if(!OperatingSystem.IsWindows()) {
            PointerPressed+=(_,e)=>{
                if(!e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)return;
                var point=this.PointToScreen(e.GetPosition(this));if(!CanStartRadial(point))return;
                _capturedPointer=e.Pointer;e.Pointer.Capture(this);HandleRadialInput(0,point,0);e.Handled=true;
            };
            PointerMoved+=(_,e)=>{if(_middleHeld)HandleRadialInput(1,this.PointToScreen(e.GetPosition(this)),0);};
            PointerReleased+=(_,e)=>{if(e.InitialPressMouseButton==MouseButton.Middle && _middleHeld){HandleRadialInput(2,this.PointToScreen(e.GetPosition(this)),0);e.Handled=true;}};
            PointerCaptureLost+=(_,_)=>CancelRadial();
        }
        PropertyChanged+=(_,e)=>{if(e.Property==IsVisibleProperty){if(!IsVisible)CancelRadial();EnsureRadialRegistration();}};
    }
    private void EnsureRadialRegistration()
    {
        if(SuppressRadialInput) { _radialInput?.Dispose();_unixRadialInput?.Dispose();_radialInput=null;_unixRadialInput=null;return; }
        if(_closed || !IsVisible || !_settings.HudEnabled || !(_settings.Workspace.RadialEnabled || OperatingSystem.IsWindows() && CanCycleOnClick)) {
            _radialInput?.Dispose();_unixRadialInput?.Dispose();_radialInput=null;_unixRadialInput=null;return;
        }
        if(OperatingSystem.IsWindows()) {_radialInput??=new(HandleRadialInput);RefreshRadialTarget();}
        else {_unixRadialInput??=new(CanStartRadial,HandleRadialInput);_unixRadialInput.WantsWheel=()=>IsRadialOpen;}
    }
    private void RefreshRadialTarget()
    {
        _radialInput?.UpdateTarget(GetVisibleHudBounds(),!_closed && IsVisible && _settings.HudEnabled &&
            _settings.Workspace.RadialEnabled && (RadialProfilesProvider?.Invoke().Count ?? 0)>0,IsRadialOpen,CanCycleOnClick && _persistent);
    }
    private void HandleRadialInput(int kind,PixelPoint point,int wheel)
    {
        if(kind==4) {if(CanCycleOnClick && !IsRadialOpen)HandleProfileWheel(wheel/120d);return;}
        if(kind==0) {
            _holdOrigin=_holdPoint=point;_middleHeld=true;_radialMoved=false;_holdForeground=WindowsRadialInput.Foreground;_holdClock.Restart();_holdTimer.Start();
        } else if(kind==1 && _middleHeld) {
            _holdPoint=point;
            _radialMoved |= Math.Sqrt(Math.Pow(point.X-_holdOrigin.X,2)+Math.Pow(point.Y-_holdOrigin.Y,2))>=_settings.Workspace.DragDistance;
            // Small accidental movements do not open a menu during a short middle click.
            if(_ringWindow is null && _holdClock.ElapsedMilliseconds>=_settings.Workspace.HoldMilliseconds &&
                Math.Sqrt(Math.Pow(point.X-_holdOrigin.X,2)+Math.Pow(point.Y-_holdOrigin.Y,2))>=_settings.Workspace.DragDistance)OpenRadial();
            if(_radialMoved)_ringWindow?.Track(point);
        } else if(kind==2 && _middleHeld) {
            if(_radialMoved)_ringWindow?.Track(point);string? selected=_radialMoved ? _ringWindow?.Ring.SelectedId : null;FinishRadial(selected);
        } else if(kind==3 && _ringWindow is {} ring) {ring.Ring.ChangePage(wheel>0 ? -1 : 1);ring.Track(point);}
    }
    private void OpenRadial()
    {
        if(!_middleHeld || _ringWindow is not null)return;
        var profiles=RadialProfilesProvider?.Invoke() ?? Array.Empty<HudProfile>();if(profiles.Count==0){CancelRadial();return;}
        RadialOpened?.Invoke();CancelPresentation();SetPersistentFinalState();
        _ringWindow=new(profiles,WorkspacePreferences.Normalize(_settings.Workspace));
        _ringWindow.CancelRequested+=CancelRadial;_ringWindow.OpenAt(_holdOrigin);if(_radialMoved)_ringWindow.Track(_holdPoint);
    }
    internal void CancelRadial()=>FinishRadial(null);
    private void FinishRadial(string? id)
    {
        bool opened=_ringWindow is not null;_middleHeld=false;_holdTimer.Stop();_capturedPointer?.Capture(null);_capturedPointer=null;
        var window=_ringWindow;_ringWindow=null;window?.Dismiss();
        if(opened)RadialFinished?.Invoke(id);
    }
    private void DisposeRadialInput(){CancelRadial();_radialInput?.Dispose();_unixRadialInput?.Dispose();_radialInput=null;_unixRadialInput=null;_holdTimer.Stop();}
}
