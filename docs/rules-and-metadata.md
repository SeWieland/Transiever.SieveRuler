# Transiever.SieveRuler Rules and Metadata

This document is the canonical description of the SieveRuler rules contract, composition behavior, and generated provider metadata.

The system boundary lives in [architecture](architecture.md).
Preview, deployment, rollback, and retained-history behavior live in [synchronization-policy](synchronization-policy.md).

## Rules Contract

Schema v1 has a document-level `sourceId`, canonical rules, and diagnostics.
Per-rule source overrides support reconciled documents containing multiple sources.
Ownership is either `Managed` or `External`.

The source passed to reconciliation is authoritative.
Managed rules from that source become obsolete when they disappear from the document.
Managed rules from every other source remain untouched.

## Composition

Composition emits one leading `require [...]` statement.
That statement contains the union of preserved script capabilities and generated rule capabilities.
Earlier leading `require` statements and SieveRuler managed requirements regions are removed during composition.
Managed rule metadata and body hashes remain in the rules region.

## Provider Metadata

Generated managed rule blocks include Open-Xchange-compatible `## Flag:` comments immediately before each `if` command.
These comments let provider rule editors associate names and stable IDs with the generated rules.

The generated format follows the mailbox.org/Open-Xchange parser shape:

```text
## Flag: <flags>|UniqueId:<integer>|Rulename: <name>
```

Compatibility constraints:

* Compatible imports prefer `Rulename:` from those flags.
* Opaque provider flag comments outside adopted spans stay byte-preserved.
* Do not add `LastModified`, `ModifiedBy`, or other fields to generated flags.
* Do not move fields before `Rulename:`.
* Suffix fields become part of the visible name.
* Reordered fields can make the OX/mailbox.org UI show `undefined`.

This is the only provider UI metadata compatibility currently validated end to end.
