Option Explicit

Dim fso, shell, scriptPath, packageRoot, command, result
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

packageRoot = fso.GetParentFolderName(WScript.ScriptFullName)
scriptPath = fso.BuildPath(packageRoot, "UpdateProject.ps1")

If Not fso.FileExists(scriptPath) Then
    MsgBox "لم يتم العثور على UpdateProject.ps1 بجانب هذا الملف." & vbCrLf & vbCrLf & _
           "نزّل أرشيف المشروع الكامل ZIP ثم فك الضغط كاملاً، وبعدها شغّل UpdateProject.vbs من داخله.", _
           vbExclamation, "تحديث المشروع"
    WScript.Quit 1
End If

command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " & _
          Quote(scriptPath) & " -LocalPackageRoot " & Quote(packageRoot)
result = shell.Run(command, 0, True)

If result <> 0 Then
    MsgBox "تعذر تشغيل أداة التحديث. تأكد من أن Windows PowerShell مثبت ثم حاول مرة أخرى.", _
           vbCritical, "تحديث المشروع"
End If

Function Quote(value)
    Quote = Chr(34) & value & Chr(34)
End Function
