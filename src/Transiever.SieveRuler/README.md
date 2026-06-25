# Transiever.SieveRuler library

The library exposes:

* `RuleDocument` and `RuleDefinition` as the JSON API;
* `JsonRuleSerializer` with file and stream APIs plus legacy migration;
* rule optimization and Sieve generation;
* strict Sieve import, byte-preserving composition, and source-aware
  reconciliation;
* typed preview, deployment, rollback, and history workflows over
  `Transiever.ManageSieve`.

The package includes the v2 JSON schema. All asynchronous I/O accepts
`CancellationToken`. Expected preview, deployment, and rollback policy outcomes
use typed statuses; malformed inputs, stale state, I/O, and protocol failures
throw. Preview writes separate reconciled ownership and rendered candidate rule
documents so callers can review both the stable source state and the optimized
candidate. New deployment plans are version 3 and preserve the current active
script name by default; deployment creates a server-side backup before replacing
that active script. Rollback restores that backup or reactivates the previous
source script for legacy v1/v2 plans. Generated managed rules include
Open-Xchange-compatible `## Flag:` comments with stable IDs and rule names for
provider UIs. Deployment can automatically prune inactive SieveRuler-owned
history scripts; the default keeps the oldest `srtx-backup-*` copy plus the
newest 5 remaining `srtx-*`/`srtx-backup-*` history scripts.
History restore can list, show, and restore retained SieveRuler history. It
creates a fresh backup before changing active filtering, and can restore the
original unmanaged/no-active state when a retained no-active marker exists.
History delete removes a single inactive SieveRuler-owned history script;
history prune removes all inactive SieveRuler-owned history while keeping the
active script and non-SieveRuler scripts.

SieveRuler is designed to stay Sieve-provider agnostic, but provider UI
metadata compatibility is currently validated against mailbox.org's
Open-Xchange implementation.

The library does not read environment variables or access `Console`.
