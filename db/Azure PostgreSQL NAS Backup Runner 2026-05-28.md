# Azure PostgreSQL NAS Backup Runner - 2026-05-28

This documents the Azure PostgreSQL logical backup job installed on the dedicated backup runner VM.

## Target

- Azure PostgreSQL server: `ssvcpro100.postgres.database.azure.com`
- PostgreSQL version observed: `15.17`
- Backup runner VM: `DB-Backup-Runner`
- Runner IP: `192.168.48.110`
- Runner OS: Ubuntu 24.04.2 LTS on KVM
- NAS: `192.168.49.238`
- NAS export: `192.168.49.238:/volume1/pg-backups-azure`
- Runner mount point: `/mnt/pg-backups-azure`
- Backup destination: `/mnt/pg-backups-azure/ssvcpro100/nightly`

## Installed Components

- NFS client package: `nfs-common`
- PostgreSQL client package: `postgresql-client`
- PostgreSQL client tools observed: `psql`, `pg_dump`, and `pg_restore` version `16.14`
- Local credential file: `/home/edshere001/.pgpass`
- Credential file permissions: `0600`
- Backup script: `/usr/local/sbin/ssvcpro100-azure-pg-backup-all.sh`
- Systemd service: `/etc/systemd/system/ssvcpro100-azure-pg-backup.service`
- Systemd timer: `/etc/systemd/system/ssvcpro100-azure-pg-backup.timer`

Do not document or commit the Azure PostgreSQL password. It exists only in the runner user's local `.pgpass` file.

## Schedule And Retention

- Schedule: daily at 3:00 AM `America/Chicago`
- Timer calendar: `OnCalendar=*-*-* 03:00:00 America/Chicago`
- Timer persistence: `Persistent=true`
- Retention: keep newest 7 successful backup folders
- First next run observed after install: `2026-05-29 08:00:00 UTC`

## Backup Behavior

The script refuses to run unless `/mnt/pg-backups-azure` is mounted. This prevents a NAS outage from causing backups to be written to the VM root filesystem.

The script discovers databases at runtime with:

```sql
SELECT datname
FROM pg_database
WHERE datallowconn
  AND NOT datistemplate
  AND datname NOT IN ('azure_sys', 'azure_maintenance')
ORDER BY datname;
```

The Azure-managed `azure_sys` and `azure_maintenance` databases are intentionally excluded. The normal `postgres` database is included.

Each successful backup folder contains:

- `globals.sql` from `pg_dumpall --globals-only --no-role-passwords`
- `globals.stderr`
- `server_database_inventory.txt`
- `databases.txt`
- `manifest.txt`
- `backup_complete.txt`
- `SHA256SUMS.txt`
- Per-database `<database>_full.dump` custom-format dumps
- Per-database `<database>_schema.sql` readable schema-only dumps
- Per-database `<database>_restore_list.txt` from `pg_restore --list`

## First Manual Verification Run

Manual service run completed successfully on 2026-05-28.

Backup folder:

```text
/mnt/pg-backups-azure/ssvcpro100/nightly/20260528-180436-CDT
```

Databases backed up:

```text
APIKeyManager
SiteServicePro
demo
documentcontroller
iec
mas
masdc
postgres
ssvcpro
yuh0iosh9u8
```

Verification results:

- `ssvcpro100-azure-pg-backup.service` exited with `status=0/SUCCESS`.
- `sha256sum -c SHA256SUMS.txt` passed.
- Backup folder size observed: `11M`.
- Database count: 10.
- Full custom dump count: 10.
- Schema-only dump count: 10.
- Restore-list file count: 10.
- `globals.stderr` was empty.
- Timer state: enabled and active.

## Operations

Check timer:

```bash
systemctl list-timers ssvcpro100-azure-pg-backup.timer --all --no-pager
```

Run manually:

```bash
sudo systemctl start ssvcpro100-azure-pg-backup.service
```

View logs:

```bash
journalctl -u ssvcpro100-azure-pg-backup.service -n 200 --no-pager
```

Verify a backup folder:

```bash
cd /mnt/pg-backups-azure/ssvcpro100/nightly/<backup-folder>
sha256sum -c SHA256SUMS.txt
pg_restore --list APIKeyManager_full.dump
```

## Restore Notes

Use the custom-format `<database>_full.dump` files for normal per-database restores. The schema-only SQL files are included for review, diffing, and planning. `globals.sql` captures role and cluster-level metadata without role passwords.
