# 打发布包：编译 Release、跑测试、打成 zip 并算出 SHA256。有证书时顺便给 BotNotes.exe 签名。
#
# 用法（在仓库根目录）：
#   powershell -ExecutionPolicy Bypass -File release.ps1
#
# 签名（可选）：目前不签名发布。以后有了代码签名证书，把指纹放进环境变量 BOTNOTES_SIGN_THUMBPRINT
# （证书要在“当前用户 → 个人”证书存储里，硬件令牌或云签名的证书也是这样出现的），脚本就会签名并校验。
#   $env:BOTNOTES_SIGN_THUMBPRINT = "证书指纹"
# 时间戳服务器默认用 DigiCert 的，可以用 BOTNOTES_TIMESTAMP_URL 换成证书颁发机构给的地址。
#
# -Test：项目地址还是占位符时也照样打包，只用于自己测试，不能发给别人。
param([switch]$Test)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

function Fail($message) { Write-Host $message -ForegroundColor Red; exit 1 }

[xml]$project = Get-Content "src\BotNotes\BotNotes.csproj" -Encoding UTF8
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { Fail "在 BotNotes.csproj 里没找到 Version。" }
Write-Host "BotNotes $version"

$thumbprint = $env:BOTNOTES_SIGN_THUMBPRINT
$sign = [bool]$thumbprint

# 项目地址还是占位符时不能发布：“查看新版本”“反馈问题”和 README 的链接都会打不开。
$placeholders = Select-String -Path "src\BotNotes\About.cs", "README.md" -Pattern "github.com/OWNER/" -SimpleMatch
if ($placeholders) {
  if (-not $Test) { Fail "项目地址还是占位符（About.cs 的 ProjectUrl 和 README.md 里的 OWNER），请先改成真实地址。只是自己测试的话，加 -Test。" }
  Write-Host "注意：项目地址还是占位符，这个包只适合自己测试。" -ForegroundColor Yellow
}

dotnet build BotNotes.sln -c Release
if ($LASTEXITCODE -ne 0) { Fail "编译失败，没有打包。" }
dotnet test BotNotes.sln -c Release --no-build
if ($LASTEXITCODE -ne 0) { Fail "测试没有全部通过，没有打包。" }

$exe = Join-Path $root "src\BotNotes\bin\Release\net48\BotNotes.exe"

if ($sign) {
  $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source
  if (-not $signtool) {
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path $kits) {
      $signtool = Get-ChildItem $kits -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -match "\\x64\\" } |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
  }
  if (-not $signtool) { Fail "找不到 signtool.exe。请安装 Windows SDK（只需勾选 “Windows SDK Signing Tools”）。" }
  $timestamp = $env:BOTNOTES_TIMESTAMP_URL
  if (-not $timestamp) { $timestamp = "http://timestamp.digicert.com" }
  & $signtool sign /sha1 $thumbprint /fd SHA256 /tr $timestamp /td SHA256 /d "BotNotes" $exe
  if ($LASTEXITCODE -ne 0) { Fail "签名失败，没有打包。" }
  & $signtool verify /pa $exe
  if ($LASTEXITCODE -ne 0) { Fail "签名校验没有通过，没有打包。" }
}

$dist = Join-Path $root "dist"
$stage = Join-Path $dist "BotNotes-$version"
$zip = Join-Path $dist "BotNotes-$version.zip"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item $exe $stage
Copy-Item (Join-Path $root "使用说明.md") $stage
foreach ($name in "LICENSE", "NOTICE.md") {
  $file = Join-Path $root $name
  if (Test-Path $file) { Copy-Item $file $stage } else { Write-Host "注意：仓库里没有 $name，发布包里也不会有。" -ForegroundColor Yellow }
}

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item $stage -Recurse -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  BotNotes-$version.zip" | Out-File "$zip.sha256" -Encoding ascii

Write-Host ""
Write-Host "发布包：$zip"
Write-Host "SHA256：$hash"
if ($sign) { Write-Host "BotNotes.exe 已签名。" } else { Write-Host "BotNotes.exe 没有签名；用户第一次打开时会看到 SmartScreen 提示，README 里写了怎么放行。" }
