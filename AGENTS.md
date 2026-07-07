# AGENTS.md

## Project Boundary

`Transiever.SieveRuler` is a cross-platform .NET rules engine and CLI.
It owns the provider-neutral JSON rules contract, optimization, Sieve import and rendering, source-aware reconciliation, preservation policy, rollback, retained history, and safe ManageSieve deployment.

It must not reference Outlook, Thunderbird, COM, or another source adapter.
Source-specific repositories produce `Transiever.SieveRuler` rule documents and may wrap its public operations.

## Agent Index

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

## Canonical Docs

| Topic                                                                                  | Owner                                                                   |
| -------------------------------------------------------------------------------------- | ----------------------------------------------------------------------- |
| JSON rules contract, composition, optimization notes, and `## Flag:` provider metadata | `docs/rules-and-metadata.md` and `schemas/sieveruler.rules.schema.json` |
| Preview, deployment, rollback, and retained-history policy                             | `docs/synchronization-policy.md`                                        |
| System boundary and responsibility split                                               | `docs/architecture.md`                                                  |
| Public model, serializer, reconciliation, and typed workflow APIs                      | `src/Transiever.SieveRuler/README.md`                                   |
| CLI commands, options, environment variables, and workflow artifacts                   | `src/Transiever.SieveRuler.Cli/README.md`                               |
| Public overview, docs map, feature summary, and development commands                   | `README.md`                                                             |
| Repo boundary, validation, release constraints, and agent workflow                     | `AGENTS.md`                                                             |

Do not update every document by default.
Update the canonical owner for the changed behavior.

## Validation

```bash
dotnet restore Transiever.SieveRuler.slnx
dotnet build Transiever.SieveRuler.slnx
dotnet test Transiever.SieveRuler.slnx
dotnet run --project src/Transiever.SieveRuler.Cli -- --help
```

Docker integration tests skip when Docker is unavailable.
They build a pinned Dovecot/Pigeonhole image and use the ManageSieve internal certificate-validation seam to trust the container certificate.

## Non-Negotiables

Rules schema v1 uses `$schema: urn:sieveruler:rules:v1`, `schemaVersion: 1`, and a non-empty document `sourceId`.
A rule can override `sourceId`; otherwise it belongs to the document source.
Schema v1 includes explicit `actions` and `exceptions`.
Conditions and actions use `values` arrays.
Keep `targetFolder` as the simple `FileInto` shortcut while existing tools still benefit from it.

Do not carry pre-public migration paths for old development artifacts.
New rules output, deployment plans, and managed Sieve markers all use version 1.

Reconciliation removes obsolete managed rules only for the authoritative source being updated.
It must preserve other sources and opaque Sieve content.

Generated managed rules emit Open-Xchange-compatible `## Flag:` metadata for provider rule editors.
Opaque provider flag comments outside adopted spans must stay byte-preserved.

Preview and dry-run never mutate a server.
Capability preview combines ManageSieve-advertised extensions with active-script `require` evidence.
`CHECKSCRIPT` remains the final server-side validation before deployment.
Plaintext credentials are prohibited.

The library consumes `Transiever.ManageSieve` through a versioned NuGet package.

## When Docs Change

Update `AGENTS.md` only for agent workflow, repo boundary, validation, release constraints, or non-negotiable safety rules.
Update focused docs and READMEs through the ownership table above.
Keep documentation accurate, but do not duplicate the same contract across every Markdown file.

GitHub Actions are repository-local.
Releases are manual, stable from `main`, and `beta` from `dev`.
NuGet publishing uses GitHub OIDC trusted publishing; do not add a `NUGET_TOKEN` secret.
Release assets are `srtx` for `win-x64`, `win-x86`, and `linux-x64`; do not add `linux-x86` unless .NET defines a portable RID.
