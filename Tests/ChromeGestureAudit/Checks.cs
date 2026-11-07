using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using EndfieldChargePlus;
using EndfieldChargePlus.Customization;
using EndfieldChargePlus.Settings;
using EndfieldChargePlus.Views;
internal static partial class Audit {
 static async Task ChromeChecks(string output){
  LocalizationManager.Initialize("zh-CN");var settings=new AppSettings{HudEnabled=false};var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
  var window=new SettingsWindow(settings,hud,runtime){PersistLanguagePreference=_=>{}};Field<CancellationTokenSource>(window,"_statusLifetime").Cancel();window.Show();await Task.Delay(250);
  try{
   ChromeGestureChecks.Run(window,Check);await SaveFailureChecks.Run(window,Check);await UnifiedPromptChecks.Run(window,Check,Path.Combine(output,"prompts"));var handle=window.TryGetPlatformHandle()!.Handle;
   Check((GetWindowLong(handle,-16)&0x40000)!=0,"native WS_THICKFRAME retained without a title bar");
   foreach(var item in new[]{(new Point(3,window.Bounds.Height/2),10),(new Point(window.Bounds.Width-3,window.Bounds.Height/2),11),(new Point(window.Bounds.Width/2,3),12),(new Point(window.Bounds.Width/2,window.Bounds.Height-3),15),(new Point(3,3),13),(new Point(window.Bounds.Width-3,3),14),(new Point(3,window.Bounds.Height-3),16),(new Point(window.Bounds.Width-3,window.Bounds.Height-3),17)}){
    var point=window.PointToScreen(item.Item1);var packed=(IntPtr)((point.X&65535)|((point.Y&65535)<<16));
    Check(SendMessage(handle,0x84,IntPtr.Zero,packed).ToInt64()==item.Item2,"native non-client resize hit "+item.Item2);
   }
   double old=window.ClientSize.Width;
   // Post only to this private desktop's HWND; no SendInput/SetCursorPos or desktop switching.
   var keys=Task.Run(async()=>{await Task.Delay(180);for(int i=0;i<3;i++){PostMessage(handle,0x100,(IntPtr)0x27,IntPtr.Zero);PostMessage(handle,0x101,(IntPtr)0x27,IntPtr.Zero);await Task.Delay(40);}PostMessage(handle,0x100,(IntPtr)13,IntPtr.Zero);await Task.Delay(180);PostMessage(handle,0x1f,IntPtr.Zero,IntPtr.Zero);});
   SendMessage(handle,0x112,(IntPtr)0xF002,IntPtr.Zero);await keys;await Task.Delay(150);
   Check(window.ClientSize.Width>old,"native interactive SC_SIZE actually changes the private window width");
   window.Width=1120;await Task.Delay(120);window.NavigateToSettingsSection("ring");await Task.Delay(180);
   using(var bitmap=new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize,window.RenderScaling))){bitmap.Render(window);bitmap.Save(Path.Combine(output,"ring-settings.png"));}
   await LayerPromptChecks.Run(window,Check,Path.Combine(output,"layer-prompts"));
  }finally{window.Close();runtime.Dispose();hud.Close();}
 }
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
