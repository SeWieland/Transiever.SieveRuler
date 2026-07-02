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
Schema v1 includes explicit `actions` and `exceptions`.
Keep `targetFolder` as the simple `FileInto` shortcut while existing tools still benefit from it.

Do not carry pre-public migration paths for old development artifacts.
New rules output, deployment plans, and managed Sieve markers all use version 1.

Reconciliation removes obsolete managed rules only for the authoritative source being updated.
It must preserve other sources and opaque Sieve content.

Generated managed rules emit Open-Xchange-compatible `## Flag:` metadata for provider rule editors.
Compatible imports prefer `Rulename:` from those flags.
Opaque provider flag comments outside adopted spans must stay byte-preserved.
The exact metadata shape is documented in `docs/rules-and-metadata.md`.

Capability preview must combine ManageSieve-advertised extensions with capabilities imported from the active script's leading `require` block.
`CHECKSCRIPT` remains the final server-side validation before deployment.

## Safety

Preview and dry-run never mutate a server.
Preview, deployment, rollback, and history behavior are intentionally documented once in `docs/synchronization-policy.md`.
Operator-facing details live in `src/Transiever.SieveRuler.Cli/README.md`.
Keep those files authoritative when policy changes.
Plaintext credentials are prohibited.
Use `TRANSIEVER_SIEVE_*` for shared ManageSieve server configuration.
Accept `--sieve-*` CLI options as command-level overrides.

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

## GitHub CI and Releases

GitHub Actions are repository-local because this repository must publish and build independently from the umbrella workspace.

`ci.yml` runs restore, Release build, and tests on pull requests and pushes to `main` and `dev`.
Docker-backed integration tests skip when Docker is unavailable.

`pr-title.yml` validates pull request titles as Conventional Commits so squash merges can drive semantic-release versioning.

`release.yml` is manual only.
Run it from `main` for stable releases and from `dev` for `beta` prereleases.
The first public beta is expected to start on `dev` and naturally produce `1.0.0-beta.1`; do not seed a `0.1.0` tag for release automation.

Release publishing uses `semantic-release` and `@droidsolutions-oss/semantic-release-nuget` with `usePackageVersion: true`.
Calculated versions are passed to `dotnet pack` without committing version changes back into project files.

The release also attaches self-contained `srtx` CLI assets for `win-x64`, `win-x86`, and `linux-x64`.
.NET does not define a portable `linux-x86` RID, so do not add a Linux x86 release leg unless .NET adds one.

NuGet.org publishing uses trusted publishing through GitHub OIDC, not a long-lived NuGet API key.
Do not add a `NUGET_TOKEN` secret for this workflow.
Configure the GitHub repository variable `NUGET_USER` to the NuGet.org username or organization that owns `Transiever.SieveRuler` and `Transiever.SieveRuler.Cli`.

The NuGet.org trusted publishing policy for this repository must match:

```text
Repository owner: <GitHub owner or organization>
Repository: SieveRuler
Workflow: release.yml
Environment: release
```

The release workflow must keep `id-token: write`, `environment: release`, and the `NuGet/login@v1` step.
That step exchanges the GitHub OIDC token for a temporary `NUGET_API_KEY`.
The workflow passes that key to the semantic-release NuGet plugin through `tokenEnvVar: "NUGET_API_KEY"`.
