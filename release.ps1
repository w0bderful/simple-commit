param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$RemovePrevious
)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$env:GIT_TERMINAL_PROMPT='0'
$env:GCM_INTERACTIVE='Never'
function CheckExit([string]$Action){if($LASTEXITCODE -ne 0){throw "$Action failed"}}
if(git status --porcelain){throw 'Commit source changes before creating a release.'}
CheckExit 'git status'
$packagePath=Join-Path $PSScriptRoot 'electron/package.json'
$package=Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
$explicitVersion=!!$Version
if(!$Version){$parts=$package.version.Split('.');$Version="$($parts[0]).$($parts[1]).$([int]$parts[2]+1)"}
$tag="v$Version"
$lines="protocol=https`nhost=github.com`n`n" | git credential fill
CheckExit 'GitHub authentication'
$credential=@{}
foreach($line in $lines){if($line -match '^([^=]+)=(.*)$'){$credential[$matches[1]]=$matches[2]}}
$headers=@{Authorization=('Bearer '+$credential['password']);Accept='application/vnd.github+json';'User-Agent'='SimpleCommit-release'}
$api='https://api.github.com/repos/w0bderful/simple-commit'
$previous=@();$page=1
do{$batch=@(Invoke-RestMethod "$api/releases?per_page=100&page=$page" -Headers $headers);$previous+=$batch;$page++}while($batch.Count -eq 100)
if($previous | Where-Object tag_name -eq $tag){throw "Release $tag already exists. Each release must use a new version."}
$remoteTag=git ls-remote --tags origin "refs/tags/$tag"
CheckExit 'Tag lookup'
if($remoteTag -and !$explicitVersion){throw "Tag $tag already exists. Choose a new version explicitly."}
$package.version=$Version
$json=$package | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($packagePath,$json+[Environment]::NewLine)
Write-Output "Preparing $tag"
Push-Location electron
try {
    pnpm test
    CheckExit 'Tests'
    pnpm run build
    CheckExit 'Build'
} finally {Pop-Location}
$exe=Join-Path $PSScriptRoot 'electron/dist/SimpleCommit.exe'
$testDir=Join-Path $PSScriptRoot ('electron/dist/release-smoke-'+[Guid]::NewGuid().ToString('N'))
Remove-Item Env:ELECTRON_RUN_AS_NODE -ErrorAction SilentlyContinue
Start-Process -FilePath $exe -ArgumentList @('--smoke-test','--ui-test','--tray',('--smoke-data="'+$testDir+'"')) -WindowStyle Hidden | Out-Null
$log=Join-Path $testDir 'electron-test.log'
$deadline=(Get-Date).AddSeconds(60)
do {
    Start-Sleep -Milliseconds 500
    $text=if(Test-Path -LiteralPath $log){[IO.File]::ReadAllText($log)}else{''}
    if($text -match 'ERROR '){throw "Packaged UI test failed: $text"}
}while(($text -notmatch 'UI_CHECKS' -or $text -notmatch 'RELAUNCH_RESTORE_PASS') -and (Get-Date) -lt $deadline)
if($text -notmatch 'UI_CHECKS' -or $text -notmatch ('"version":"'+[regex]::Escape($Version)+'"') -or $text -notmatch 'RELAUNCH_RESTORE_PASS'){throw 'Packaged UI test timed out or version mismatch.'}
Write-Output "Packaged UI tests passed for $tag"
$zip=Join-Path $PSScriptRoot "electron/dist/SimpleCommit-$Version-Windows.zip"
Compress-Archive -LiteralPath @($exe,(Join-Path $PSScriptRoot '사용법.txt')) -DestinationPath $zip -Force
git add -- electron/package.json
CheckExit 'Stage version'
git commit -m "Release $tag"
CheckExit 'Version commit'
git push origin main
CheckExit 'Source push'
$commit=(git rev-parse HEAD).Trim()
if($remoteTag){
    Invoke-RestMethod "$api/git/refs/tags/$tag" -Method Patch -Headers $headers -ContentType 'application/json' -Body (@{sha=$commit;force=$true}|ConvertTo-Json) | Out-Null
}else{
    Invoke-RestMethod "$api/git/refs" -Method Post -Headers $headers -ContentType 'application/json' -Body (@{ref="refs/tags/$tag";sha=$commit}|ConvertTo-Json) | Out-Null
}
$notes=@"
## SimpleCommit $Version Preview

Electron/Node.js 기반 Windows 커밋 알리미입니다. 별도 .NET 설치가 필요하지 않습니다.

- 여러 저장소 등록, 브랜치 자동 조회, 커밋 및 ZIP 최신 상태 확인
- 간결한 목록, 드래그 정렬, 선택 목록 저장
- 설정 변경 자동 저장, 자체 무음 알림 및 알림 테스트
- 프로그램 내부에 버전 표시, 배포 실행 파일명은 SimpleCommit.exe로 고정
- 트레이 실행, 재실행 시 기존 창 열기
- ZIP 업데이트와 기존 파일 정리, 목록 JSON 가져오기/내보내기

### 다운로드
SimpleCommit.exe를 실행하거나 SimpleCommit-$Version-Windows.zip을 풀어 실행하세요.
실행 중인 이전 프로그램을 트레이에서 종료한 뒤 새 파일로 교체하세요.
설정과 목록은 %LOCALAPPDATA%\SimpleCommitWeb에 보존됩니다.

검증: 백엔드 테스트 및 패키지 UI 자동 저장·알림·트레이·재실행 검사 통과.
소스: $commit
"@
$body=@{tag_name=$tag;target_commitish=$commit;name="SimpleCommit $Version Preview";body=$notes;draft=$true;prerelease=$true}|ConvertTo-Json
$release=Invoke-RestMethod "$api/releases" -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $body
$expected=@{}
foreach($file in @($exe,$zip)){
    $name=[IO.Path]::GetFileName($file)
    $hash=(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $url=($release.upload_url -replace '\{.*$','')+'?name='+[Uri]::EscapeDataString($name)
    Write-Output "Uploading $name"
    $asset=Invoke-RestMethod $url -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $file -TimeoutSec 600
    if($asset.digest -ne ('sha256:'+$hash) -or $asset.size -ne (Get-Item $file).Length){throw "Asset verification failed: $name"}
    $expected[$name]='sha256:'+$hash
}
Invoke-RestMethod "$api/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body (@{draft=$false}|ConvertTo-Json) | Out-Null
$published=Invoke-RestMethod "$api/releases/tags/$tag" -Headers $headers
foreach($name in $expected.Keys){$asset=@($published.assets|Where-Object name -eq $name);if($asset.Count -ne 1 -or $asset[0].digest -ne $expected[$name]){throw 'Published asset verification failed'}}
if($RemovePrevious){
    foreach($old in $previous){
        Invoke-RestMethod "$api/releases/$($old.id)" -Method Delete -Headers $headers | Out-Null
        Write-Output "Removed previous release $($old.tag_name)"
    }
}
$published | Select-Object html_url,tag_name,prerelease,target_commitish,@{n='assets';e={@($_.assets|Select-Object name,size,digest)}} | ConvertTo-Json -Depth 5
