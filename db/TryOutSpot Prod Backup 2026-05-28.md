# TryOutSpot Production Backup - 2026-05-28

## Source

- PostgreSQL server: `ssvcpro200`
- Source database: `tryoutspot_prod`
- PostgreSQL version observed through backup metadata: `15.15`

## Backup Artifacts

The backup run created these artifacts:

- `tryoutspot_prod_schema_20260528-204425.sql` - schema only
- `tryoutspot_prod_data_20260528-204425.sql` - data only
- `tryoutspot_prod_full_20260528-204425.dump` - full custom-format dump
- `SHA256SUMS.txt` - checksum manifest
- `backup_manifest.txt` - backup metadata

The data-only dump reported a circular foreign-key warning for `Comments`. Prefer the full custom-format dump for restores.

## Local Server Path

Backups were first created on `ssvcpro200` at:

```text
/home/edshere001/tryoutspot_backups/tryoutspot_prod_20260528-204425
```

## NAS Storage

The NAS NFS export discovered from `ssvcpro200` is:

```text
192.168.49.238:/volume1/pg-backups-mas
```

It is mounted persistently on `ssvcpro200` at:

```text
/mnt/pg-backups-mas
```

The backup set was copied to:

```text
/mnt/pg-backups-mas/ssvcpro200/tryoutspot_prod/tryoutspot_prod_20260528-204425
```

## Verification

- The NAS-mounted copies passed SHA256 validation against `SHA256SUMS.txt`.
- `pg_restore --list` successfully read `tryoutspot_prod_full_20260528-204425.dump` directly from the NAS mount.

## Additional NAS Backup Mounts

Two additional NAS backup exports were mounted on `ssvcpro200` so databases backed up by other PostgreSQL servers can be accessed for restore/load work on this server.

| Purpose | NAS export | Mount point on `ssvcpro200` |
| --- | --- | --- |
| MAS backups | `192.168.49.238:/volume1/pg-backups-mas` | `/mnt/pg-backups-mas` |
| MTC backups | `192.168.49.238:/volume1/pg-backups-mtc` | `/mnt/pg-backups-mtc` |
| Azure backups | `192.168.49.238:/volume1/pg-backups-azure` | `/mnt/pg-backups-azure` |

All three mounts are persistent in `/etc/fstab` with:

```text
rw,hard,_netdev,nofail,x-systemd.automount,x-systemd.requires=network-online.target
```

Verification performed from `ssvcpro200`:

- `showmount -e 192.168.49.238` confirmed all three exports are allowed for `192.168.48.15`.
- `findmnt` confirmed all three mount points are mounted as NFS.
- Temporary write tests succeeded on `/mnt/pg-backups-mtc` and `/mnt/pg-backups-azure`.

## Nightly Backup Schedule

All connectable, non-template PostgreSQL databases on `ssvcpro200` are backed up nightly to:

```text
/mnt/pg-backups-mas/ssvcpro200/nightly
```

Installed files:

```text
/usr/local/sbin/ssvcpro200-pg-backup-all.sh
/etc/systemd/system/ssvcpro200-pg-backup.service
/etc/systemd/system/ssvcpro200-pg-backup.timer
```

Schedule and retention:

- Runs daily at `2:00 AM America/Chicago`.
- Timer uses `Persistent=true`.
- Keeps the newest 7 successful nightly backup folders.
- The script discovers database names at runtime; it does not hard-code the current `ssvcpro200` database list.

The first verified scheduled-backup-format manual run created:

```text
/mnt/pg-backups-mas/ssvcpro200/nightly/20260528-162924-CDT
```

That run backed up these current `ssvcpro200` databases:

```text
asdu894sd8
clawd_agents
postgres
stripe3_ssvcpro
testprovision_ssvcpro
tryoutspot_prod
```

Each run creates:

- `globals.sql`
- `databases.txt`
- `manifest.txt`
- `backup_complete.txt`
- `SHA256SUMS.txt`
- Per-database custom-format `*_full.dump`
- Per-database readable `*_schema.sql`
- Per-database `*_restore_list.txt`

The manual run passed `sha256sum -c SHA256SUMS.txt` for all generated files.
