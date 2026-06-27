# Transiever.SieveRuler Synchronization Policy

This document is the canonical description of preview, deployment, rollback, and retained-history behavior.

The system boundary lives in [architecture](architecture.md).
Rules contract and generated provider metadata live in [rules-and-metadata](rules-and-metadata.md).
The operator-facing command surface lives in [../src/Transiever.SieveRuler.Cli/README.md](../src/Transiever.SieveRuler.Cli/README.md).

## Preview

Preview reads a source rules document and the active server script.
It imports the strict compatible subset, preserves opaque content, reconciles ownership,
optionally optimizes managed rules, validates capabilities and space, and writes separate review artifacts.

The source document is never replaced during preview.
Combined state is written to `reconciled-rules.json`.
The managed rules actually rendered into the candidate script are written to `candidate-rules.json`.

Deployment plans are version 1.
Preview preserves the provider's current active script name as the default target,
and it records a server-side backup name when deployment will replace that active script.
`--script-name` can override the target.
Non-active targets continue to use inactive staging.

## Deployment

Deployment validates the recorded candidate hash.
It then runs `CHECKSCRIPT` and rechecks the active script name and hash before mutating the server.

Active-script replacement writes the previous active content to a unique `srtx-backup-*` script,
then replaces the active script in place with `PUTSCRIPT` and verifies the active target hash.

Non-active targets are uploaded inactive and then activated by default.
Existing inactive scripts are not overwritten or deleted except by protected history cleanup.

After successful deployment, cleanup may delete only inactive SieveRuler-owned history scripts matching
`srtx-YYYYMMDDHHMMSS-*` and `srtx-backup-YYYYMMDDHHMMSS-*`.
The active script, target, source active script, current plan backup, non-SieveRuler names,
and the oldest `srtx-backup-*` are protected.
Default retention keeps that oldest backup plus the newest 5 remaining history scripts.

If `HAVESPACE` fails before upload, deployment runs the same protected prune pass,
then refreshes server state and retries the space check once.
Cleanup failures are returned as warnings, and they do not convert a successful deployment into failure.

Deployments from a no-active original state create an inactive `srtx-backup-*-no-active` marker,
which allows the unmanaged state to be restored later without guessing.

## Rollback

Rollback reads v1 plans.
It refuses to proceed unless the current active script matches the deployed candidate, except when forced.

Backup rollback fetches and verifies the server-side backup,
then restores that backup into the target script and leaves the backup script in place.

Plans without a server-side backup reactivate the recorded source active script.
If the preview started with no active script, rollback disables active filtering instead.

## History

History operations list and show retained SieveRuler-owned backup and candidate scripts directly from the server.

Restoring a history entry first creates a fresh backup of the current active script,
then writes the selected content into the current active script name.

Restoring the original no-active marker disables active Sieve processing after backing up the current active script.

History delete removes one inactive SieveRuler-owned history script and refuses active scripts.

History prune removes all inactive SieveRuler-owned history scripts,
including the original backup or no-active marker, while keeping the active script and non-SieveRuler names.
