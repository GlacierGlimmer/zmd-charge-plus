using Avalonia.Input;
namespace EndfieldChargePlus.Interop;
internal readonly record struct HudHotkeyGesture(uint Modifiers,uint VirtualKey,string Text)
{
    internal bool IsMouse=>VirtualKey is 1 or 2 or 4 or 5 or 6;
    internal int MouseButton=>VirtualKey switch{1=>1,2=>2,4=>3,5=>4,6=>5,_=>0};
    internal bool IsModifier=>VirtualKey is >=160 and <=165 or 91 or 92;
    private static readonly Dictionary<uint,string> Names=BuildNames();
    private static readonly Dictionary<string,uint> Codes=BuildCodes();
    private static Dictionary<uint,string> BuildNames()
    {
        var n=new Dictionary<uint,string>{[1]="Mouse1",[2]="Mouse2",[4]="Mouse3",[5]="Mouse4",[6]="Mouse5",[8]="Backspace",[9]="Tab",[12]="Clear",[13]="Enter",[19]="Pause",[20]="CapsLock",[21]="KanaMode",[25]="HanjaMode",[27]="Escape",[28]="ImeConvert",[29]="ImeNonConvert",[31]="ImeModeChange",[32]="Space",[33]="PageUp",[34]="PageDown",[35]="End",[36]="Home",[37]="Left",[38]="Up",[39]="Right",[40]="Down",[41]="Select",[42]="Print",[43]="Execute",[44]="PrintScreen",[45]="Insert",[46]="Delete",[47]="Help",[91]="LWin",[92]="RWin",[93]="Apps",[95]="Sleep",[106]="Multiply",[107]="Add",[108]="Separator",[109]="Subtract",[110]="Decimal",[111]="Divide",[144]="NumLock",[145]="ScrollLock",[160]="LeftShift",[161]="RightShift",[162]="LeftCtrl",[163]="RightCtrl",[164]="LeftAlt",[165]="RightAlt",[166]="BrowserBack",[167]="BrowserForward",[168]="BrowserRefresh",[169]="BrowserStop",[170]="BrowserSearch",[171]="BrowserFavorites",[172]="BrowserHome",[173]="VolumeMute",[174]="VolumeDown",[175]="VolumeUp",[176]="MediaNextTrack",[177]="MediaPreviousTrack",[178]="MediaStop",[179]="MediaPlayPause",[180]="LaunchMail",[181]="SelectMedia",[182]="LaunchApplication1",[183]="LaunchApplication2",[186]="OemSemicolon",[187]="OemPlus",[188]="OemComma",[189]="OemMinus",[190]="OemPeriod",[191]="OemQuestion",[192]="OemTilde",[219]="OemOpenBrackets",[220]="OemPipe",[221]="OemCloseBrackets",[222]="OemQuotes",[223]="Oem8",[226]="OemBackslash"};
        for(uint i=0;i<26;i++)n[65+i]=((char)(65+i)).ToString();
        for(uint i=0;i<10;i++){n[48+i]=i.ToString();n[96+i]="NumPad"+i;}
        for(uint i=0;i<24;i++)n[112+i]="F"+(i+1);return n;
    }
    private static Dictionary<string,uint> BuildCodes()
    {
        var c=Names.ToDictionary(x=>x.Value,x=>x.Key,StringComparer.OrdinalIgnoreCase);
        foreach(var (alias,key) in new (string,uint)[]{("Back",8),("Return",13),("Capital",20),("Esc",27),("Prior",33),("Next",34),("Snapshot",44),("Ins",45),("Del",46),("Scroll",145),("Oem1",186),("Oem2",191),("Oem3",192),("Oem4",219),("Oem5",220),("Oem6",221),("Oem7",222),("Oem102",226)})c[alias]=key;return c;
    }
    internal static bool TryParse(string? text,out HudHotkeyGesture gesture)
    {
        gesture=default;if(string.IsNullOrWhiteSpace(text))return false;
        var parts=text.Split('+',StringSplitOptions.TrimEntries);uint mods=0;
        for(int i=0;i<parts.Length-1;i++){
            uint flag=parts[i].ToUpperInvariant() switch{"CTRL" or "CONTROL"=>2u,"ALT" or "OPTION"=>1u,"SHIFT"=>4u,"WIN" or "META" or "CMD" or "COMMAND"=>8u,_=>0};
            if(flag==0 || (mods&flag)!=0)return false;mods|=flag;
        }
        if(!Codes.TryGetValue(parts[^1],out var vk))return false;gesture=Create(mods,vk);return true;
    }
    private static HudHotkeyGesture Create(uint mods,uint vk)=>new(mods,vk,((mods&2)!=0?"Ctrl+":"")+((mods&1)!=0?"Alt+":"")+((mods&4)!=0?"Shift+":"")+((mods&8)!=0?"Win+":"")+Names[vk]);
    internal static uint ModifierBits(KeyModifiers mods)=>((mods&KeyModifiers.Control)!=0?2u:0)|((mods&KeyModifiers.Alt)!=0?1u:0)|((mods&KeyModifiers.Shift)!=0?4u:0)|((mods&KeyModifiers.Meta)!=0?8u:0);
    internal static bool TryCapture(Key key,KeyModifiers modifiers,out HudHotkeyGesture gesture)
    {
        gesture=default;string name=key.ToString();if(key is >=Key.D0 and <=Key.D9)name=((int)key-(int)Key.D0).ToString();
        if(!Codes.TryGetValue(name,out uint vk))return false;uint mods=ModifierBits(modifiers);
        if(vk is 160 or 161)mods&=~4u;if(vk is 162 or 163)mods&=~2u;if(vk is 164 or 165)mods&=~1u;if(vk is 91 or 92)mods&=~8u;
        gesture=Create(mods,vk);return true;
    }
    internal static bool TryCapturePointer(PointerUpdateKind kind,KeyModifiers modifiers,out HudHotkeyGesture gesture)
    {
        uint vk=kind switch{PointerUpdateKind.LeftButtonPressed=>1u,PointerUpdateKind.RightButtonPressed=>2u,PointerUpdateKind.MiddleButtonPressed=>4u,PointerUpdateKind.XButton1Pressed=>5u,PointerUpdateKind.XButton2Pressed=>6u,_=>0};
        gesture=vk==0?default:Create(ModifierBits(modifiers),vk);return vk!=0;
    }
    internal string X11KeyName=>VirtualKey switch{
        8=>"BackSpace",13=>"Return",20=>"Caps_Lock",27=>"Escape",33=>"Prior",34=>"Next",44=>"Print",45=>"Insert",46=>"Delete",91=>"Super_L",92=>"Super_R",93=>"Menu",144=>"Num_Lock",145=>"Scroll_Lock",160=>"Shift_L",161=>"Shift_R",162=>"Control_L",163=>"Control_R",164=>"Alt_L",165=>"Alt_R",166=>"XF86Back",167=>"XF86Forward",168=>"XF86Refresh",169=>"XF86Stop",170=>"XF86Search",171=>"XF86Favorites",172=>"XF86HomePage",173=>"XF86AudioMute",174=>"XF86AudioLowerVolume",175=>"XF86AudioRaiseVolume",176=>"XF86AudioNext",177=>"XF86AudioPrev",178=>"XF86AudioStop",179=>"XF86AudioPlay",180=>"XF86Mail",181=>"XF86AudioMedia",182=>"XF86Launch0",183=>"XF86Launch1",186=>"semicolon",187=>"equal",188=>"comma",189=>"minus",190=>"period",191=>"slash",192=>"grave",219=>"bracketleft",220=>"backslash",221=>"bracketright",222=>"apostrophe",226=>"less",106=>"KP_Multiply",107=>"KP_Add",108=>"KP_Separator",109=>"KP_Subtract",110=>"KP_Decimal",111=>"KP_Divide",>=96 and <=105=>"KP_"+(VirtualKey-96),_=>Names[VirtualKey]};
}
