# Database VPN Access - 2026-05-02

## Context

TryOutSpot production hosting on Web1 reaches the on-prem PostgreSQL server through the existing WireGuard VPN.

- Web1 public host: `174.138.189.222`
- Web1 WireGuard IP: `10.99.0.3`
- WireGuard VPN subnet: `10.99.0.0/24`
- PostgreSQL server: `ssvcpro200`
- PostgreSQL LAN IP: `192.168.48.15`
- PostgreSQL port for TryOutSpot: `5432`

Google social login reached the ASP.NET callback successfully, but the application failed when EF Core tried to connect to PostgreSQL. The production log showed a timeout connecting to `192.168.48.15:5432`.

## Findings

Web1 could ping LAN hosts over the VPN, so routing from Web1 to the LAN was working.

`ssvcpro200` could also ping Web1's VPN IP `10.99.0.3`, confirming return-path connectivity.

The remaining failure was TCP access to PostgreSQL:

```powershell
Test-NetConnection 192.168.48.15 -Port 5432
```

Initially returned:

```text
TcpTestSucceeded : False
```

The DB server had PostgreSQL listening on `192.168.48.15:5432`, but UFW and PostgreSQL host-based auth only allowed the LAN subnet `192.168.48.0/23`.

## Change Applied

Allowed PostgreSQL 15 access from the WireGuard VPN subnet.

UFW rule added on `ssvcpro200`:

```bash
sudo ufw allow from 10.99.0.0/24 to any port 5432 proto tcp comment "PostgreSQL 15 from WireGuard VPN"
```

PostgreSQL `pg_hba.conf` rule added:

```text
host    all             all             10.99.0.0/24            md5
```

A backup was created before editing:

```text
/etc/postgresql/15/main/pg_hba.conf.bak-tryoutspot-wireguard-20260502-232241
```

PostgreSQL was reloaded after the change.

## Verification

After the change, Web1 successfully connected to the PostgreSQL TCP port:

```powershell
Test-NetConnection 192.168.48.15 -Port 5432
```

Returned:

```text
SourceAddress    : 10.99.0.3
TcpTestSucceeded : True
```

## Follow-Up

Retry Google social login on `https://tryoutspot.com/api/social-login/challenge/Google`.

If login still fails, check the application log under:

```text
Application/TryOutSpot.Web/App_Data/Logs/
```

The next likely issue would be PostgreSQL credentials, database permissions, or an EF migration/table mismatch, not VPN routing.
