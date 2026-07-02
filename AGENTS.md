# AGENTS.md

## Project

`Transiever.SieveRuler` is a cross-platform .NET rules engine and CLI.
It owns these engine concerns:

* The provider-neutral JSON rules contract.
* Optimization.
* Sieve import and rendering.
* Preservation and reconciliation policy.
* Safe ManageSieve deployment.

It must not reference Outlook, Thunderbird, COM, or another source adapter.
Source-specific repositories produce `Transiever.SieveRuler` rule documents and may wrap its public operations.

## Layout

```text
Transiever.SieveRuler.slnx
schemas/sieveruler.rules.schema.json
docs/architecture.md
docs/rules-and-metadata.md
docs/synchronization-policy.md
src/
  Transiever.SieveRuler/
  Transiever.SieveRuler.Cli/
  Transiever.SieveRuler.UnitTest/
  Transiever.SieveRuler.Cli.UnitTest/
  Transiever.SieveRuler.IntegrationTest/
```

The library targets `net10.0`.
The CLI command is `srtx`.
Neither may assume Windows.

## Contracts

Rules schema v1 uses `$schema: urn:sieveruler:rules:v1`, `schemaVersion: 1`, and a non-empty document `sourceId`.
A rule can override `sourceId`; otherwise it belongs to the document source.

Do not carry pre-public migration paths for old development artifacts.
New rules output, deployment plans, and managed Sieve markers all use version 1.

Reconciliation removes obsolete managed rules only for the authoritative source being updated.
It must preserve other sources and opaque Sieve content.

Generated managed rules emit Open-Xchange-compatible `## Flag:` metadata for provider rule editors.
Compatible imports prefer `Rulename:` from those flags.
Opaque provider flag comments outside adopted spans must stay byte-preserved.
The exact metadata shape is documented in `docs/rules-and-metadata.md`.

## Safety

Preview and dry-run never mutate a server.
Preview, deployment, rollback, and history behavior are intentionally documented once in `docs/synchronization-policy.md`.
Operator-facing details live in `src/Transiever.SieveRuler.Cli/README.md`.
Keep those files authoritative when policy changes.
Plaintext credentials are prohibited.

## Commands

```bash
dotnet restore Transiever.SieveRuler.slnx
dotnet build Transiever.SieveRuler.slnx
dotnet test Transiever.SieveRuler.slnx
dotnet run --project src/Transiever.SieveRuler.Cli -- --help
```

Docker integration tests skip when Docker is unavailable.
They build a pinned Dovecot/Pigeonhole image.
They use the image's bundled test certificate through the ManageSieve internal certificate-validation seam.
They wait on the mapped host port instead of requiring extra socket tooling inside the container.
Keep the library and CLI independently documented.
Update this file, both READMEs, architecture, JSON schema, tests, and changelog when their contracts change.

The library consumes `Transiever.ManageSieve` through a versioned NuGet package.
