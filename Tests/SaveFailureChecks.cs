using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using EndfieldChargePlus;
using EndfieldChargePlus.Settings;

internal static class SaveFailureChecks
{
    internal static async Task Run(SettingsWindow window,Action<bool,string> check)
    {
        var persist=window.PersistLanguagePreference;string language=LocalizationManager.Preference;
        window.PersistLanguagePreference=_=>throw new IOException("Could not write preferences",new UnauthorizedAccessException("Read-only test directory"));
        try
        {
            typeof(SettingsWindow).GetMethod("OnLanguageSelected",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{language=="en-US"?"zh-CN":"en-US"});
            await Task.Delay(150);
            var dialog=window.OwnedWindows.Single(w=>w.Name=="SaveErrorDialog");
            var details=dialog.GetLogicalDescendants().OfType<TextBox>().Single(x=>x.Name=="SaveErrorDetails");
            check(details.IsReadOnly && details.Text!.Contains("IOException") && details.Text.Contains("UnauthorizedAccessException"),"save failure visibly exposes outer and inner exception types");
            check(details.Text!.Contains("Read-only test directory"),"save failure retains the concrete failure reason");
            var copy=dialog.GetLogicalDescendants().OfType<Button>().Single(x=>x.Name=="CopySaveError");
            check(copy.IsEnabled,"copy-error-details action is present without requiring the About page");
            dialog.Close();await Task.Delay(70);
            check(!window.OwnedWindows.Any(w=>w.Name=="SaveErrorDialog"),"error prompt dismisses without closing settings");
            window.PersistLanguagePreference=_=>{};
            typeof(SettingsWindow).GetMethod("OnLanguageSelected",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,new object[]{language});
            await Task.Delay(80);
            check(!SaveErrorDialog.Details(new Exception("Authorization: Bearer SECRET123")).Contains("SECRET123"),"copied error text redacts credentials");
        }
        finally {window.PersistLanguagePreference=persist;}
    }
}
