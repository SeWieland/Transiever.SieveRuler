# Transiever.SieveRuler

Cross-platform .NET library and CLI for turning provider-neutral JSON rule documents into Sieve.
It optimizes compatible rules, reconciles existing scripts, and deploys changes safely through ManageSieve.

`Transiever.SieveRuler` is the common engine behind source adapters such as `Transiever.OutlookResiever`.
It is also intended for future Thunderbird or other provider-specific importers.

The rule model and generated Sieve are provider-agnostic in intent.
Provider UI metadata has only been tested and validated against [mailbox.org] and the [Open-Xchange] implementation it uses.

## Install

Install the self-contained Windows build with Scoop:

```powershell
scoop bucket add transiever https://github.com/SeWieland/Transiever.ScoopBucket
scoop install transiever/srtx
```

## Documentation Map

Start here, then follow the component-specific guides:

* [CLI guide](src/Transiever.SieveRuler.Cli/README.md) for CLI commands, environment variables, and workflow artifacts.
* [library guide](src/Transiever.SieveRuler/README.md) for the public model, serializer, reconciliation, and typed workflow APIs.
* [architecture](docs/architecture.md) for system boundaries and responsibility split.
* [rules and metadata](docs/rules-and-metadata.md) for the rules contract, composition, and generated `## Flag:` compatibility metadata.
* [synchronization policy](docs/synchronization-policy.md) for preview, deployment, rollback, and retained-history behavior.
* [rules schema](schemas/sieveruler.rules.schema.json) for the v1 JSON contract.

## Repository Layout

```text
src/Transiever.SieveRuler/                 Packable library
src/Transiever.SieveRuler.Cli/             `srtx` CLI and future .NET tool
src/Transiever.SieveRuler.UnitTest/        Pure engine tests
src/Transiever.SieveRuler.Cli.UnitTest/    CLI tests
src/Transiever.SieveRuler.IntegrationTest/ Docker-backed ManageSieve test
```

The Docker-backed integration test uses a pinned Dovecot/Pigeonhole container.
It pins the container certificate through the ManageSieve internal test seam.

The public JSON contract is [`schemas/sieveruler.rules.schema.json`](schemas/sieveruler.rules.schema.json).
Its schema ID is `urn:sieveruler:rules:v1`.

## Feature Summary

* Provider-neutral v1 rules schema and serializer.
* Explicit conditions, exceptions, and actions for stable server-side Sieve behavior.
* Strict Sieve import, source-aware reconciliation, and compatible-rule optimization.
* ManageSieve preview and deployment workflows with active-script-preserving defaults.
* Rollback and retained-history operations for SieveRuler-managed deployments.

Operational details intentionally live in the linked component guides instead of being repeated here.

## Development

```bash
dotnet build Transiever.SieveRuler.slnx
dotnet test Transiever.SieveRuler.slnx
dotnet run --project src/Transiever.SieveRuler.Cli -- --help
```

## Publication Note

GitHub Actions produce releases.
Stable releases come from `main`.
Beta prereleases come from `dev` and may be unstable.

Releases publish the library and `srtx` .NET tool packages to NuGet.org.
They also attach self-contained `srtx` assets for `win-x64`, `win-x86`, and `linux-x64`.

[mailbox.org]: https://mailbox.org/
[Open-Xchange]: https://www.open-xchange.com/
