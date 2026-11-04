# Gridline database backup

This folder holds backups of the Gridline database (`gridline2`, PostgreSQL) and the scripts to make and restore them.

| File | What it is |
|---|---|
| `gridline2-<date-time>.dump` | Full backup with all data. Restore it with `Restore-Gridline.ps1` |
| `gridline2-<date-time>.sql` | The same backup as plain SQL, readable and runnable with `psql` |
| `gridline-files-<date-time>.zip` | Uploaded documents and proposals (only made if there are any) |
| `Backup-Gridline.ps1` | Makes a new backup into this folder |
| `Restore-Gridline.ps1` | Restores a `.dump` into a new database |

## Restore (team members)

You need PostgreSQL 17 or newer on your PC.

1. In PowerShell, run (put in your repo path and the backup's date-time):

   ```powershell
   powershell -ExecutionPolicy Bypass -File "<their repo>\db backup\Restore-Gridline.ps1" -DumpFile "<their repo>\db backup\gridline2-<date-time>.dump"
   ```

   Example:

   ```powershell
   powershell -ExecutionPolicy Bypass -File "D:\GridLine\db backup\Restore-Gridline.ps1" -DumpFile "D:\GridLine\db backup\gridline2-20261104-2006.dump"
   ```

2. Enter your PostgreSQL `postgres` password when asked. The script creates the `gridline2` database, restores everything into it, and prints the schema version (should be **8**) and the number of projects.
   - If `gridline2` already exists, the script stops instead of overwriting it. Restore under another name with `-Database gridline2_copy`, or drop the old database first.
3. If there is a `gridline-files-<date-time>.zip`, unzip it into `api\src\Gridline.Api\data`.
4. Start the API (`api\Start-Gridline-API.ps1`). <http://localhost:5080/api/health> should show `"db":"gridline2"` and `"schema":8`.

## Make a new backup

```powershell
powershell -ExecutionPolicy Bypass -File "<repo>\db backup\Backup-Gridline.ps1"
```

## Notes

- The backup contains everything: users and their password hashes, every project, prices and bidder details. Share it privately.
- A `.dump` restores on the same PostgreSQL version that made it, or a newer one.
