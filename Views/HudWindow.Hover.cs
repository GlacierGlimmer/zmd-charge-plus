namespace EndfieldChargePlus.Views;
public partial class HudWindow
{
    internal Func<bool>? ShouldHoldTransient {get;set;}
    private async Task WaitForTransientHoverAsync(CancellationToken token)
    {
        long deadline=Environment.TickCount64;
        while(!token.IsCancellationRequested)
        {
            if(ShouldHoldTransient?.Invoke()==true)
                deadline=Environment.TickCount64+(long)(Math.Clamp(_settings.DisplayDurationSeconds,3,10)*1000);
            if(Environment.TickCount64>=deadline)return;
            await Task.Delay(100,token);
        }
        token.ThrowIfCancellationRequested();
    }
}
