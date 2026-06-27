# Transiever.SieveRuler library

The library exposes:

* `RuleDocument` and `RuleDefinition` as the JSON API;
* `JsonRuleSerializer` with file and stream APIs;
* rule optimization and Sieve generation;
* strict Sieve import, byte-preserving composition, and source-aware
  reconciliation;
* typed preview, deployment, rollback, and history workflows over
  `Transiever.ManageSieve`.

The package includes the v1 JSON schema.
All asynchronous I/O accepts `CancellationToken`.

Expected preview, deployment, rollback, and history outcomes use typed statuses.
Malformed inputs, stale state, I/O, and protocol failures throw.

## Policy Notes

The library owns the provider-neutral workflow behavior behind preview, deployment, rollback, and retained-history operations.
The canonical workflow description lives in [../../docs/synchronization-policy.md](../../docs/synchronization-policy.md).
Rules contract and provider metadata details live in [../../docs/rules-and-metadata.md](../../docs/rules-and-metadata.md).

In short:

* preview writes separate reconciled ownership and rendered candidate rule documents;
* deployment plans are version 1 and preserve the current active script name by default;
* active-script replacement creates a server-side backup before mutation;
* rollback restores that backup, or reactivates the recorded source script when no backup was created;
* generated managed rules include Open-Xchange-compatible `## Flag:` comments with stable IDs and rule names for provider UIs;
* deployment can prune inactive SieveRuler-owned history, keeping the oldest backup plus the newest 5 remaining history scripts by default;
* history restore creates a fresh backup before changing active filtering and can restore the original unmanaged or no-active state when a retained marker exists.

SieveRuler is designed to stay Sieve-provider agnostic.
However, provider UI metadata compatibility is currently only validated against mailbox.org's Open-Xchange implementation.

The library does not read environment variables or access `Console`.
