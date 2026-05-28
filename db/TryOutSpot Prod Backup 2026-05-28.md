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

