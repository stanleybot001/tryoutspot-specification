# PostgreSQL NAS Backup Standard

This is the reusable backup pattern for PostgreSQL servers that write nightly backups to the NAS at `192.168.49.238`.

## Design

- Use NFS mounts from each PostgreSQL server to the NAS.
- Use one local backup script per PostgreSQL server.
- The script discovers databases at run time. Do not hard-code database names from another server.
- Back up all connectable, non-template databases.
- Include PostgreSQL globals so roles and other cluster-level objects are recoverable.
- Keep the newest 7 successful nightly backup folders.
- Schedule backups during the overnight maintenance window. `ssvcpro200` uses 2:00 AM `America/Chicago`; the Azure backup runner uses 3:00 AM `America/Chicago`.
- Verify every custom-format dump with `pg_restore --list`.
- Write `SHA256SUMS.txt` and verify checksums before marking the run complete.
- Delete old backups only after a new backup succeeds.

## NAS Exports

| Site/source | NAS export | Typical mount point |
| --- | --- | --- |
| MAS | `192.168.49.238:/volume1/pg-backups-mas` | `/mnt/pg-backups-mas` |
| MTC | `192.168.49.238:/volume1/pg-backups-mtc` | `/mnt/pg-backups-mtc` |
| Azure | `192.168.49.238:/volume1/pg-backups-azure` | `/mnt/pg-backups-azure` |

Use this `/etc/fstab` option set unless there is a site-specific reason to change it:

```text
rw,hard,_netdev,nofail,x-systemd.automount,x-systemd.requires=network-online.target
```

The `x-systemd.automount` option means a share may not appear fully mounted after reboot until the path is accessed. This is expected.

## ssvcpro200 Current Setup

- PostgreSQL server: `ssvcpro200`
- Server IP: `192.168.48.15`
- Backup destination: `/mnt/pg-backups-mas/ssvcpro200/nightly`
- Script: `/usr/local/sbin/ssvcpro200-pg-backup-all.sh`
- Service: `/etc/systemd/system/ssvcpro200-pg-backup.service`
- Timer: `/etc/systemd/system/ssvcpro200-pg-backup.timer`
- Schedule: `OnCalendar=*-*-* 02:00:00 America/Chicago`
- Retention: newest 7 successful backup folders

The manual verification run created:

```text
/mnt/pg-backups-mas/ssvcpro200/nightly/20260528-162924-CDT
```

Databases discovered during that run:

```text
asdu894sd8
clawd_agents
postgres
stripe3_ssvcpro
testprovision_ssvcpro
tryoutspot_prod
```

These names are only the current `ssvcpro200` databases. Azure and MTC servers must discover and back up their own local databases.

## ssvcpro100 Azure Current Setup

- Azure PostgreSQL server: `ssvcpro100.postgres.database.azure.com`
- Backup runner VM: `DB-Backup-Runner`
- Runner IP: `192.168.48.110`
- Backup destination: `/mnt/pg-backups-azure/ssvcpro100/nightly`
- Script: `/usr/local/sbin/ssvcpro100-azure-pg-backup-all.sh`
- Service: `/etc/systemd/system/ssvcpro100-azure-pg-backup.service`
- Timer: `/etc/systemd/system/ssvcpro100-azure-pg-backup.timer`
- Schedule: `OnCalendar=*-*-* 03:00:00 America/Chicago`
- Retention: newest 7 successful backup folders
- Credential storage: local `.pgpass` on the backup runner, permissions `0600`

The first manual verification run created:

```text
/mnt/pg-backups-azure/ssvcpro100/nightly/20260528-180436-CDT
```

Databases discovered during that run:

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

The Azure-managed `azure_sys` and `azure_maintenance` databases are intentionally excluded from logical backups.

## Backup Artifacts

Each nightly folder contains:

- `globals.sql` from `pg_dumpall --globals-only`
- `databases.txt` with the database list for that run
- `manifest.txt`
- `backup_complete.txt`
- `SHA256SUMS.txt`
- One folder per database under `databases/`

Each database folder contains:

- `<database>_full.dump` custom-format backup from `pg_dump -Fc -Z9`
- `<database>_schema.sql` readable schema-only SQL
- `<database>_restore_list.txt` from `pg_restore --list`
- `manifest.txt`

## Runtime Checks

The script refuses to run if the NAS mount is not mounted. This prevents accidental backups from being written to the local root filesystem when the NAS is offline.

The standard script backs up databases using:

```sql
SELECT datname
FROM pg_database
WHERE datallowconn
  AND NOT datistemplate
ORDER BY datname;
```

For Azure PostgreSQL, also exclude Azure-managed internal databases:

```sql
AND datname NOT IN ('azure_sys', 'azure_maintenance')
```

## Operations

Check timer:

```bash
systemctl list-timers ssvcpro200-pg-backup.timer --all --no-pager
```

Run manually:

```bash
sudo systemctl start ssvcpro200-pg-backup.service
```

View logs:

```bash
journalctl -u ssvcpro200-pg-backup.service -n 200 --no-pager
```

Verify a backup folder:

```bash
cd /mnt/pg-backups-mas/ssvcpro200/nightly/<backup-folder>
sha256sum -c SHA256SUMS.txt
pg_restore --list databases/<database>/<database>_full.dump
```

## Replication Checklist For Azure And MTC

1. Confirm the NAS export exists and allows the PostgreSQL server IP:

   ```bash
   showmount -e 192.168.49.238
   ```

2. Install NFS client tooling if missing:

   ```bash
   sudo apt-get update
   sudo apt-get install -y nfs-common
   ```

3. Create the mount point for that server/site.

4. Add the correct NFS export to `/etc/fstab`.

5. Run `systemctl daemon-reload`.

6. Mount the path and verify with `mountpoint`, `findmnt`, and a temporary write test.

7. Install a server-specific copy of the backup script.

8. Set the server-specific values:

   - `SERVER_NAME`
   - `MOUNT_POINT`
   - `BACKUP_ROOT`
   - service and timer names

9. Validate the timer calendar:

   ```bash
   systemd-analyze calendar "*-*-* 02:00:00 America/Chicago"
   ```

10. Enable and start the timer.

11. Run one manual backup.

12. Verify checksums and `pg_restore --list` output.

13. Document the resulting mount path, script path, timer, destination path, and first verified backup folder.

## Security Notes

- Do not commit backup dump files.
- Do not store NAS passwords in repo documentation.
- NFS access is host-based for these exports.
- If a future server cannot use local PostgreSQL peer auth, use a secured `.pgpass` or equivalent local secret, not a committed connection string.
