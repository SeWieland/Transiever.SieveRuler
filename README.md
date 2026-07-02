# Transiever.SieveRuler

Cross-platform .NET library and CLI for turning provider-neutral JSON rule documents into Sieve.
It optimizes compatible rules, reconciles existing scripts, and deploys changes safely through ManageSieve.

`Transiever.SieveRuler` is the common engine behind source adapters such as `Transiever.OutlookResiever`.
It is also intended for future Thunderbird or other provider-specific importers.

The rule model and generated Sieve are provider-agnostic in intent.
Provider UI metadata has only been tested and validated against [mailbox.org] and the [Open-Xchange] implementation it uses.

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

The current development build consumes `Transiever.ManageSieve` through a versioned NuGet package.

[mailbox.org]: https://mailbox.org/
[Open-Xchange]: https://www.open-xchange.com/
