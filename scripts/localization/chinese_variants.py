"""Chinese script/terminology conversion for authored ECP source text."""
import ctypes

def traditional(text):
    # Translate longer compounds before their components: a drive is a 磁碟機,
    # not a driver (驅動程式), and 設定檔 must not consume 設定資料夾.
    terms={
        '保存并应用':'儲存並套用','配置文件夹':'設定資料夾','配置文件':'設定檔',
        '驱动器':'磁碟機','驱动程序':'驅動程式','应用程序':'應用程式',
        '数据源':'資料來源','默认':'預設','变量':'變數','硬件':'硬體',
        '软件':'軟體','文件夹':'資料夾','文件':'檔案','鼠标':'滑鼠',
        '内存':'記憶體','显卡':'顯示卡','驱动':'驅動程式','网络':'網路',
        '窗口':'視窗','配置':'設定','数据':'資料','信息':'資訊',
        '保存':'儲存','汇总':'彙總','当前':'目前','链接':'連結',
        '禁用':'停用','粘贴':'貼上','磁盘':'磁碟','任务栏':'工作列',
        '剪贴板':'剪貼簿','搜索':'搜尋','用户':'使用者','密钥':'金鑰',
        '字节':'位元組','屏幕':'螢幕','计算机':'電腦'
    }
    for original,value in terms.items():text=text.replace(original,value)
    mapper=ctypes.windll.kernel32.LCMapStringEx
    mapper.argtypes=[ctypes.c_wchar_p,ctypes.c_ulong,ctypes.c_wchar_p,ctypes.c_int,ctypes.c_wchar_p,ctypes.c_int,ctypes.c_void_p,ctypes.c_void_p,ctypes.c_longlong]
    size=mapper('zh-TW',0x04000000,text,len(text),None,0,None,None,0)
    if not size:raise RuntimeError('Traditional Chinese conversion failed')
    buffer=ctypes.create_unicode_buffer(size+1)
    if not mapper('zh-TW',0x04000000,text,len(text),buffer,size,None,None,0):raise RuntimeError('Traditional Chinese conversion failed')
    return buffer.value

def hong_kong(text):
    for a,b in [('資料夾','文件夾'),('硬體','硬件'),('軟體','軟件'),('程式碼','代碼'),('使用者','用戶'),('金鑰','密鑰'),('螢幕','屏幕')]:text=text.replace(a,b)
    return text
