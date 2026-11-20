$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Join-Path $PSScriptRoot '..')

if (Test-Path -LiteralPath 'src\PhotoCloud.App\bin') {
    Remove-Item -LiteralPath 'src\PhotoCloud.App\bin' -Recurse -Force
}
if (Test-Path -LiteralPath 'src\PhotoCloud.App\obj') {
    Remove-Item -LiteralPath 'src\PhotoCloud.App\obj' -Recurse -Force
}
if (Test-Path -LiteralPath 'src\PhotoCloud.Core\bin') {
    Remove-Item -LiteralPath 'src\PhotoCloud.Core\bin' -Recurse -Force
}
if (Test-Path -LiteralPath 'src\PhotoCloud.Core\obj') {
    Remove-Item -LiteralPath 'src\PhotoCloud.Core\obj' -Recurse -Force
}
if (Test-Path -LiteralPath 'src\PhotoCloud.Web\bin') {
    Remove-Item -LiteralPath 'src\PhotoCloud.Web\bin' -Recurse -Force
}
if (Test-Path -LiteralPath 'src\PhotoCloud.Web\obj') {
    Remove-Item -LiteralPath 'src\PhotoCloud.Web\obj' -Recurse -Force
}
if (Test-Path -LiteralPath 'tests\PhotoCloud.Tests\bin') {
    Remove-Item -LiteralPath 'tests\PhotoCloud.Tests\bin' -Recurse -Force
}
if (Test-Path -LiteralPath 'tests\PhotoCloud.Tests\obj') {
    Remove-Item -LiteralPath 'tests\PhotoCloud.Tests\obj' -Recurse -Force
}

Write-Output 'Lokale Builds wurden gelöscht.'
