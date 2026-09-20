$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe "/out:$PSScriptRoot\SimpleCommit.exe" "/win32icon:$PSScriptRoot\app.ico" "/resource:$PSScriptRoot\app.ico,app.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$PSScriptRoot\SimpleCommit.cs" "$PSScriptRoot\MainWindow.cs" "$PSScriptRoot\BulkRepoDialog.cs" "$PSScriptRoot\ToastWindow.cs" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
