# Transiever.SieveRuler

Cross-platform .NET library and CLI for turning a provider-neutral JSON rule
document into Sieve, optimizing compatible rules, reconciling an existing
script, and safely deploying changes through ManageSieve.

`Transiever.SieveRuler` is intended as the common engine behind source adapters
such as `Transiever.OutlookResiever` and future Thunderbird or
provider-specific importers.

The rule model and generated Sieve are provider-agnostic in intent, but current
provider UI metadata has been tested and validated against mailbox.org and the
Open-Xchange implementation it uses.

## Projects

```text
src/Transiever.SieveRuler/                 Packable library
src/Transiever.SieveRuler.Cli/             `srtx` CLI and future .NET tool
src/Transiever.SieveRuler.UnitTest/        Pure engine tests
src/Transiever.SieveRuler.Cli.UnitTest/    CLI tests
src/Transiever.SieveRuler.IntegrationTest/ Docker-backed ManageSieve test
```

The public JSON contract is
[`schemas/sieveruler.rules.schema.json`](schemas/sieveruler.rules.schema.json)
with schema ID `urn:sieveruler:rules:v2`.

## Development

```bash
dotnet build Transiever.SieveRuler.slnx
dotnet test Transiever.SieveRuler.slnx
dotnet run --project src/Transiever.SieveRuler.Cli -- --help
```

Preview writes separate review artifacts for ownership state
(`reconciled-rules.json`) and rendered candidate rules
(`candidate-rules.json`). ManageSieve preview preserves the current active
script name by default, deployment creates a server-side backup before
replacing that active script, and `rollback` restores the backup or reactivates
the previous source script for legacy plans. Generated managed Sieve includes
Open-Xchange-compatible `## Flag:` comments so provider UIs can display rule
names, and deploy prunes inactive SieveRuler history by default while retaining
the oldest backup and the newest 5 history scripts. `srtx history` can list,
show, restore, delete, and prune retained SieveRuler versions, including the
original backup or the original no-active state when that marker exists. Manual
history prune deletes all inactive SieveRuler-owned history while keeping the
active script and non-SieveRuler scripts.

The current development build references the sibling `Transiever.ManageSieve`
project. This
must become a versioned package reference before independent publication.

See the [CLI guide](src/Transiever.SieveRuler.Cli/README.md), the
[library guide](src/Transiever.SieveRuler/README.md), and the
[architecture](docs/architecture.md).
