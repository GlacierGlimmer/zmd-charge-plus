namespace EndfieldChargePlus.Customization;

public sealed partial class CustomHudRuntime
{
    public event Action<string,string>? ProfileSelected;
    private CustomHudRuntime? _selectionOwner;
    private string _selectionInstanceId="";
    private int _manualRollDirection;
    public string CurrentProfileId(string instanceId="")
    {
        var target=string.IsNullOrEmpty(instanceId)?this:_instances.TryGetValue(instanceId,out var child)?child.Runtime:null;
        return target is null?"":!string.IsNullOrEmpty(target._manualProfileId)?target._manualProfileId:
            !string.IsNullOrEmpty(target._persistentProfileId)?target._persistentProfileId:target._settings.ActiveProfileId;
    }
    private void NotifyProfileSelected(string id)=>(_selectionOwner??this).ProfileSelected?.Invoke(_selectionInstanceId,id);
}
