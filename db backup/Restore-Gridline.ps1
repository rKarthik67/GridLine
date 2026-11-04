# Restores a Gridline backup made by Backup-Gridline.ps1 into a NEW, empty database.
#   powershell -ExecutionPolicy Bypass -File "<repo>\db backup\Restore-Gridline.ps1" -DumpFile "<repo>\db backup\gridline2-<stamp>.dump"
# Then unzip gridline-files-<stamp>.zip into api\src\Gridline.Api\data and start the API (health check shows "schema":8).
param([Parameter(Mandatory)][string]$DumpFile, [string]$Database = 'gridline2', [string]$DbHost = 'localhost', [int]$Port = 5432, [string]$User = 'postgres')
$ErrorActionPreference = 'Stop'
$bin = (Get-ChildItem 'C:\Program Files\PostgreSQL', 'C:\Program Files (x86)\PostgreSQL' -Recurse -Filter pg_restore.exe -ErrorAction SilentlyContinue | Select-Object -First 1).DirectoryName
if (-not $bin) { throw 'pg_restore.exe not found. Install PostgreSQL 17 or newer.' }
if (-not $env:PGPASSWORD) {
  $sec = Read-Host "Password for PostgreSQL user '$User'" -AsSecureString
  $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)); $clear = $true
}
try {
  $exists = & "$bin\psql.exe" -h $DbHost -p $Port -U $User -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = '$Database'"
  if ($exists -eq '1') { throw "Database '$Database' already exists. Drop it first or pass -Database <new name>." }
  & "$bin\psql.exe" -h $DbHost -p $Port -U $User -d postgres -c "CREATE DATABASE `"$Database`""
  & "$bin\pg_restore.exe" -h $DbHost -p $Port -U $User -d $Database --no-owner --no-privileges $DumpFile
  if ($LASTEXITCODE) { throw "pg_restore failed with exit code $LASTEXITCODE" }
  & "$bin\psql.exe" -h $DbHost -p $Port -U $User -d $Database -c "select max(ChangeNo) as schema_version, (select count(*) from gridline.Project) as projects from gridline.SchemaChange"
  Write-Host "Restored into '$Database'." -ForegroundColor Green
} finally { if ($clear) { Remove-Item Env:PGPASSWORD } }
