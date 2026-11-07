using Avalonia.Controls;using EndfieldChargePlus;using EndfieldChargePlus.Customization;using EndfieldChargePlus.Settings;using EndfieldChargePlus.Views;
internal static partial class Audit {static async Task LayerChecks(string output){
var settings=new AppSettings{HudEnabled=false};var hud=new HudWindow();using var runtime=new CustomHudRuntime(hud);runtime.ApplySettings(settings);
var window=new SettingsWindow(settings,hud,runtime){PersistLanguagePreference=_=>{}};Field<CancellationTokenSource>(window,"_statusLifetime").Cancel();window.Show();await Task.Delay(180);
try{await LayerPromptChecks.Run(window,Check,Path.Combine(output,"layer-prompts"));await UnifiedPromptChecks.Run(window,Check,Path.Combine(output,"prompts"));await SuperLayerChecks.Run(window,Check,Path.Combine(output,"super-layer"));}finally{window.Close();runtime.Dispose();hud.Close();}
}}
