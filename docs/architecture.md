# Transiever.SieveRuler Architecture

This document is the canonical description of the SieveRuler system boundary and responsibility split.
The root [README](../README.md) is the repo entry point.
The CLI-specific command surface lives in [../src/Transiever.SieveRuler.Cli/README.md](../src/Transiever.SieveRuler.Cli/README.md).
The library API summary lives in [../src/Transiever.SieveRuler/README.md](../src/Transiever.SieveRuler/README.md).
Rules contract and provider metadata live in [rules-and-metadata](rules-and-metadata.md).
Preview, deployment, rollback, and retained-history policy live in [synchronization-policy](synchronization-policy.md).

## System Boundary

```text
source adapters
    -> Transiever.SieveRuler
        -> Transiever.ManageSieve
```

Source adapters discover rules and write `Transiever.SieveRuler` JSON.
`Transiever.SieveRuler` owns the rule language, optimization, Sieve semantics, reconciliation, preservation, and deployment policy.
`Transiever.ManageSieve` owns only RFC 5804 protocol execution.

The main library is cross-platform `net10.0`, with no console, environment, or source-specific dependency.
The CLI owns argument parsing, environment configuration, prompts, presentation, and exit codes.

## Design Responsibilities

`Transiever.SieveRuler` owns:

* The provider-neutral JSON rules contract.
* Source-aware reconciliation and Sieve composition.
* Compatibility metadata for generated managed rules.
* synchronization planning, deployment validation, rollback, and retained-history policy.

`Transiever.ManageSieve` does not own deployment policy.
It provides the protocol primitives that SieveRuler composes into those workflows.

## Canonical References

Use the focused docs instead of restating the same policy in multiple places:

* [rules-and-metadata](rules-and-metadata.md) for schema, composition, and `## Flag:` compatibility constraints.
* [synchronization-policy](synchronization-policy.md) for preview artifacts, deployment semantics, rollback behavior, and history retention.
