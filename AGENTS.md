# AGENTS.md

## Project

`Transiever.SieveRuler` is a cross-platform .NET rules engine and CLI. It owns
the provider-neutral JSON rules contract, optimization, Sieve import and
rendering, preservation and reconciliation policy, and safe ManageSieve
deployment.

It must not reference Outlook, Thunderbird, COM, or another source adapter.
Source-specific repositories produce `Transiever.SieveRuler` rule documents and
may wrap its public operations.

## Layout

```text
Transiever.SieveRuler.slnx
schemas/sieveruler.rules.schema.json
docs/architecture.md
src/
  Transiever.SieveRuler/
  Transiever.SieveRuler.Cli/
  Transiever.SieveRuler.UnitTest/
  Transiever.SieveRuler.Cli.UnitTest/
  Transiever.SieveRuler.IntegrationTest/
```

The library targets `net10.0`. The CLI command is `srtx`. Neither may assume
Windows.

## Contracts

Rules schema v2 uses `$schema: urn:sieveruler:rules:v2`, `schemaVersion: 2`, and
a non-empty document `sourceId`. A rule can override `sourceId`; otherwise it
belongs to the document source.

Continue reading legacy bare arrays, `Transiever.OutlookResiever` schema v1,
deployment-plan v1/v2, and valid `Transiever.OutlookResiever` v1 managed Sieve
regions. New rules output uses `Transiever.SieveRuler` schema version 2, new
deployment plans use version 3, and managed Sieve rule markers remain version
2.

Reconciliation removes obsolete managed rules only for the authoritative source
being updated. It must preserve other sources and opaque Sieve content. Generated
managed rules emit Open-Xchange-compatible `## Flag:` metadata for provider
rule editors; compatible imports prefer `Rulename:` from those flags, but opaque
provider flag comments outside adopted spans must stay byte-preserved.
Open-Xchange middleware parses rule metadata with a fixed shape:
`## Flag: <flags>|UniqueId:<integer>|Rulename: <name>`. Do not add
`LastModified`, `ModifiedBy`, or other fields to generated flags, and do not
move fields before `Rulename:`; suffix fields become part of the visible name,
while reordered fields can make the OX/mailbox.org UI show `undefined`.

## Safety

Preview and dry-run never mutate a server. By default, preview targets the
current active script name so provider GUIs continue to recognize the script.
Deployment checks the exact candidate and active-script snapshot. Replacing the
active script first stores a server-side `srtx-backup-*` copy, then replaces it
by default; non-active targets are uploaded and activated by default. After
successful deployment, SieveRuler prunes only inactive SieveRuler-owned
`srtx-*` and `srtx-backup-*` history, keeping the oldest backup plus the
configured newest history count. If deployment starts from no active script, it
stores an inactive `srtx-backup-*-no-active` marker so history restore can later
restore the original unmanaged/no-active state. `history restore` always creates
a fresh backup or no-active marker before changing active filtering.
`history delete` may remove only inactive SieveRuler-owned history.
`history prune` removes all inactive SieveRuler-owned history, including the
oldest original backup or no-active marker, while keeping the active script and
non-SieveRuler scripts. Rollback restores the server-side backup for v3 plans or
reactivates the v1/v2 source script without deleting scripts. Plaintext
credentials are prohibited.

## Commands

```bash
dotnet restore Transiever.SieveRuler.slnx
dotnet build Transiever.SieveRuler.slnx
dotnet test Transiever.SieveRuler.slnx
dotnet run --project src/Transiever.SieveRuler.Cli -- --help
```

Docker integration tests skip when Docker is unavailable. Keep the library and
CLI independently documented. Update this file, both READMEs, architecture, JSON
schema, tests, and changelog when their contracts change.

Until publication, the library has one temporary sibling project reference to
`Transiever.ManageSieve`. Replace it with a versioned package before release.
