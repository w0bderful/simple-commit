$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$assets = Get-ChildItem "$PSScriptRoot\www" -File | ForEach-Object { "/resource:$($_.FullName),www.$($_.Name)" }
& $compiler /nologo /target:winexe /main:WebHost "/out:$PSScriptRoot\SimpleCommit.Web.exe" "/win32icon:$PSScriptRoot\app.ico" "/resource:$PSScriptRoot\app.ico,app.ico" $assets /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$PSScriptRoot\..\SimpleCommit.cs" "$PSScriptRoot\WebHost.cs"
if ($LASTEXITCODE -ne 0) { throw 'Web build failed' }
