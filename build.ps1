# Builds bin\ClaudeUsageTray.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# The program icon (src\app.ico) is drawn by the program itself (--ico); it is made on the first build.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$ico = "$root\src\app.ico"
New-Item -ItemType Directory -Force "$root\bin" | Out-Null
Get-Process ClaudeUsageTray -ErrorAction SilentlyContinue | Stop-Process -Force

function Compile($withIcon) {
    $a = @('/nologo', '/optimize+', '/target:winexe', '/codepage:65001', "/out:$root\bin\ClaudeUsageTray.exe",
        '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', "/r:$fw\System.Web.Extensions.dll")
    if ($withIcon) { $a += "/win32icon:$ico" }
    & "$fw\csc.exe" @a "$root\src\*.cs"
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }
}

if (-not (Test-Path $ico)) {
    Compile $false
    & "$root\bin\ClaudeUsageTray.exe" --ico $ico | Out-Null
}
Compile $true
"Built: $root\bin\ClaudeUsageTray.exe"
