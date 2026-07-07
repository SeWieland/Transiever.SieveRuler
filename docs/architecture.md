# Transiever.SieveRuler Architecture

This document is the canonical description of the SieveRuler system boundary and responsibility split.

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

## Responsibilities

`Transiever.SieveRuler` owns:

* The provider-neutral JSON rules contract.
* Conditions, exceptions, actions, and Sieve capability requirements.
* Source-aware reconciliation and Sieve composition.
* Compatibility metadata for generated managed rules.
* Synchronization planning, deployment validation, rollback, and retained-history policy.

`Transiever.ManageSieve` does not own deployment policy.
It provides the protocol primitives that SieveRuler composes into those workflows.

## Focused Docs

Use the focused docs instead of restating the same policy in multiple places:

* [rules-and-metadata](rules-and-metadata.md) for schema, composition, and `## Flag:` compatibility constraints.
* [synchronization-policy](synchronization-policy.md) for preview artifacts, deployment semantics, rollback behavior, and history retention.
