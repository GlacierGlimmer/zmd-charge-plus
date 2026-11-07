// Filee Soft.axaml page reveal: 280 ms cubic fade with a 10 px slide.
// MIT, Copyright (c) 2026 Myeonggu Kim and Filee contributors. Licenses/Filee-MIT.txt.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Layout;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
namespace EndfieldChargePlus.Settings;
internal static class WorkspaceMotion
{
    private sealed class State
    {
        internal readonly TranslateTransform Offset=new();
        internal readonly Stopwatch Clock=new();
        internal readonly DispatcherTimer Timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
        internal double Opacity;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,State> States=new();
    internal static void Reveal(Control control)
    {
        var state=States.GetValue(control,c=>{
            var value=new State{Opacity=c.Opacity};
            c.RenderTransform=c.RenderTransform is {} previous ? new TransformGroup {Children={previous as Transform??new MatrixTransform(previous.Value),value.Offset}} : value.Offset;
            void Finish(){value.Timer.Stop();value.Offset.Y=0;c.Opacity=value.Opacity;}
            value.Timer.Tick+=(_,_)=>
            {
                double progress=Math.Clamp(value.Clock.Elapsed.TotalMilliseconds/280,0,1);
                double eased=1-Math.Pow(1-progress,3);
                value.Offset.Y=10*(1-eased);c.Opacity=value.Opacity*eased;
                if(progress>=1)Finish();
            };
            c.DetachedFromVisualTree+=(_,_)=>Finish();
            return value;
        });
        state.Clock.Restart();state.Offset.Y=10;control.Opacity=0;state.Timer.Start();
    }
}
