# Transiever.SieveRuler Architecture

## Boundary

```text
source adapters
    -> Transiever.SieveRuler
        -> Transiever.ManageSieve
```

Source adapters discover rules and write `Transiever.SieveRuler` JSON.
`Transiever.SieveRuler` owns the rule language, optimization, Sieve semantics,
reconciliation, preservation, and deployment policy.
`Transiever.ManageSieve` owns only RFC 5804 protocol execution.

The main library is cross-platform `net10.0` and has no console, environment, or
source-specific dependency. The CLI owns argument parsing, environment
configuration, prompts, presentation, and exit codes.

## Rules Contract

Schema v2 has a document-level `sourceId`, canonical rules, and diagnostics.
Per-rule source overrides support reconciled documents containing multiple
sources. Ownership is either `Managed` or `External`.

The source passed to reconciliation is authoritative: absent managed rules from
that source become obsolete, while managed rules from every other source remain
untouched.

Legacy `Transiever.OutlookResiever` arrays, schema v1 documents, and managed
regions are accepted as migration inputs. Successful composition replaces
legacy markers with `Transiever.SieveRuler` v2 markers.

Composition emits one leading `require [...]` statement containing the union of
preserved script capabilities and generated rule capabilities. Earlier leading
`require` statements and legacy managed requirements regions are removed during
composition; managed rule metadata and body hashes remain in the rules region.
Generated managed rule blocks also include Open-Xchange-compatible `## Flag:`
comments immediately before each `if` command so provider rule editors can
associate names and stable IDs with the generated rules.
The generated format follows the mailbox.org/Open-Xchange parser shape and is
the only provider UI metadata compatibility currently validated end to end.

## Synchronization

Preview reads a source rules document and the active server script, imports the
strict compatible subset, preserves opaque content, reconciles ownership,
optionally optimizes managed rules, validates capabilities and space, and writes
separate review artifacts.

New deployment plans are version 3. Preview preserves the provider's current
active script name as the default target and records a server-side backup name
when deployment will replace that active script. `--script-name` can override
the target; non-active targets continue to use inactive staging.

Deployment validates the recorded candidate hash, runs `CHECKSCRIPT`, and
rechecks active script name and hash before mutating the server. Active-script
replacement writes the previous active content to a unique `srtx-backup-*`
script, replaces the active script in place with `PUTSCRIPT`, and verifies the
active target hash. Non-active targets are uploaded inactive and then activated
by default. Existing inactive scripts are not overwritten or deleted except by
protected history cleanup. After successful deployment,
cleanup may delete only inactive SieveRuler-owned history scripts matching
`srtx-YYYYMMDDHHMMSS-*` and `srtx-backup-YYYYMMDDHHMMSS-*`. The active script,
target, source active script, current plan backup, non-SieveRuler names, and
the oldest `srtx-backup-*` are protected. Default retention keeps that oldest
backup plus the newest 5 remaining history scripts. If `HAVESPACE` fails before
upload, deployment runs the same protected prune pass, refreshes server state,
and retries the space check once. Cleanup failures are returned as warnings and
do not convert a successful deployment into failure.
Deployments from a no-active original state create an inactive
`srtx-backup-*-no-active` marker so the unmanaged state can later be restored
without guessing.

Rollback reads v1/v2/v3 plans. It refuses to proceed unless the current active
script matches the deployed candidate, except when forced. V3 backup rollback
fetches and verifies the server-side backup, restores it into the target
script, and leaves the backup script in place. Legacy v1/v2 rollback
reactivates the recorded source active script or disables active filtering when
the preview started with no active script.

The source document is never replaced during preview. Combined state is written
to `reconciled-rules.json`; the managed rules actually rendered into the
candidate script are written to `candidate-rules.json`.

History operations list and show retained SieveRuler-owned backup and candidate
scripts directly from the server. Restoring a history entry first creates a
fresh backup of the current active script, then writes selected content into the
current active script name. Restoring the original no-active marker disables
active Sieve processing after backing up the current active script. History
delete removes one inactive SieveRuler-owned history script and refuses active
scripts. History prune removes all inactive SieveRuler-owned history scripts,
including the original backup or no-active marker, while keeping the active
script and non-SieveRuler names.
