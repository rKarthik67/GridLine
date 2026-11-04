# Backs up the Gridline database (gridline2) and its uploaded files into this "db backup" folder.
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File "<repo>\db backup\Backup-Gridline.ps1"
# Makes: gridline2-<stamp>.dump (restore with pg_restore), gridline2-<stamp>.sql (plain SQL), gridline-files-<stamp>.zip
param([string]$Database = 'gridline2', [string]$DbHost = 'localhost', [int]$Port = 5432, [string]$User = 'postgres')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot; $repo = Split-Path $here -Parent
$pgDump = Get-ChildItem 'C:\Program Files\PostgreSQL', 'C:\Program Files (x86)\PostgreSQL' -Recurse -Filter pg_dump.exe -ErrorAction SilentlyContinue |
          Sort-Object FullName -Descending | Select-Object -First 1
if (-not $pgDump) { throw 'pg_dump.exe not found. Install PostgreSQL command line tools, or edit this script with its path.' }
Write-Host "Using $($pgDump.FullName)"
if (-not $env:PGPASSWORD) {
  $sec = Read-Host "Password for PostgreSQL user '$User'" -AsSecureString
  $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
  $clear = $true
}
try {
  $stamp = Get-Date -Format 'yyyyMMdd-HHmm'
  $dump = Join-Path $here "$Database-$stamp.dump"; $sql = Join-Path $here "$Database-$stamp.sql"
  & $pgDump.FullName -h $DbHost -p $Port -U $User -d $Database -Fc -f $dump
  if ($LASTEXITCODE) { throw "pg_dump (.dump) failed with exit code $LASTEXITCODE" }
  & $pgDump.FullName -h $DbHost -p $Port -U $User -d $Database --no-owner --no-privileges -f $sql
  if ($LASTEXITCODE) { throw "pg_dump (.sql) failed with exit code $LASTEXITCODE" }
  $data = Join-Path $repo 'api\src\Gridline.Api\data'
  if ((Test-Path $data) -and (Get-ChildItem $data -Recurse -File | Select-Object -First 1)) {
    Compress-Archive -Path (Join-Path $data '*') -DestinationPath (Join-Path $here "gridline-files-$stamp.zip") -Force
  } else { Write-Host 'No uploaded files to include (api\src\Gridline.Api\data is empty).' }
  Write-Host "`nBackup done:" -ForegroundColor Green
  Get-ChildItem $here -Filter "*$stamp*" | Format-Table Name, @{n='Size (KB)'; e={[math]::Round($_.Length / 1KB)}} -AutoSize
} finally { if ($clear) { Remove-Item Env:PGPASSWORD } }
