// Filee Soft.axaml page reveal: 280 ms cubic fade with a 10 px slide.
// MIT, Copyright (c) 2026 Myeonggu Kim and Filee contributors. Licenses/Filee-MIT.txt.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Layout;
namespace EndfieldChargePlus.Settings;
internal static class WorkspaceMotion
{
    private sealed class State { public CancellationTokenSource? Token; public Thickness Margin; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,State> States=new();
    internal static async void Reveal(Control control)
    {
        var state=States.GetValue(control,c=>new(){Margin=c.Margin});state.Token?.Cancel();state.Token?.Dispose();
        var token=state.Token=new CancellationTokenSource();var m=state.Margin;
        var animation=new Animation{Duration=TimeSpan.FromMilliseconds(280),Easing=new CubicEaseOut(),Children={
            new KeyFrame{Cue=new Cue(0),Setters={new Setter(Visual.OpacityProperty,0d),new Setter(Layoutable.MarginProperty,new Thickness(m.Left,m.Top+10,m.Right,m.Bottom-10))}},
            new KeyFrame{Cue=new Cue(1),Setters={new Setter(Visual.OpacityProperty,1d),new Setter(Layoutable.MarginProperty,m)}}}};
        try {await animation.RunAsync(control,token.Token);}catch(OperationCanceledException){}
    }
}
