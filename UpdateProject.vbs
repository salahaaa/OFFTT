Option Explicit

Dim fso, shell, http, stream, tempScript, scriptUrl, command, status
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

scriptUrl = "https://raw.githubusercontent.com/salahaaa/OFFTT/arena/01a0ac34-offtt/UpdateProject.ps1"
tempScript = fso.BuildPath(fso.GetSpecialFolder(2), "MfgSystem_UpdateProject.ps1")

On Error Resume Next
Set http = CreateObject("WinHttp.WinHttpRequest.5.1")
http.Open "GET", scriptUrl, False
http.Send
status = http.Status
If Err.Number <> 0 Or status <> 200 Then
    Err.Clear
    Set http = CreateObject("MSXML2.XMLHTTP")
    http.Open "GET", scriptUrl, False
    http.Send
    status = http.Status
End If

If status <> 200 Then
    MsgBox "تعذر تنزيل أداة التحديث من GitHub." & vbCrLf & "تحقق من اتصال الإنترنت ثم حاول مرة أخرى.", vbCritical, "تحديث المشروع"
    WScript.Quit 1
End If

Set stream = CreateObject("ADODB.Stream")
stream.Type = 1
stream.Open
stream.Write http.ResponseBody
stream.SaveToFile tempScript, 2
stream.Close

command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " & Quote(tempScript)
shell.Run command, 0, True
If fso.FileExists(tempScript) Then fso.DeleteFile tempScript, True

Function Quote(value)
    Quote = Chr(34) & value & Chr(34)
End Function
