Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName Microsoft.VisualBasic

$desktop = [Environment]::GetFolderPath('Desktop')
$file    = Join-Path $desktop 'TRAE电脑控制测试.txt'

# 写入文件（比打开记事本自动输入更可靠）
'电脑控制测试成功！' | Out-File -FilePath $file -Encoding UTF8

# 同时打开记事本让用户可见
$notepad = Start-Process notepad.exe -PassThru

# 等记事本窗口就绪，模拟 Ctrl+V 粘贴
Start-Sleep -Milliseconds 800
[Microsoft.VisualBasic.Interaction]::AppActivate($notepad.Id) 2>$null
Start-Sleep -Milliseconds 200
Set-Clipboard -Value "TRAE电脑控制测试"
[System.Windows.Forms.SendKeys]::SendWait('^v')
[System.Windows.Forms.SendKeys]::SendWait('^s')  # Ctrl+S 保存
Start-Sleep -Milliseconds 300
# 弹一下保存对话框路径（如果记事本问保存位置）
