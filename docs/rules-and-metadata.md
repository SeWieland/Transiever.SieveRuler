# Transiever.SieveRuler Rules and Metadata

This document is the canonical description of the SieveRuler rules contract, composition behavior, and generated provider metadata.

The system boundary lives in [architecture](architecture.md).
Preview, deployment, rollback, and retained-history behavior live in [synchronization-policy](synchronization-policy.md).

## Rules Contract

Schema v1 has a document-level `sourceId`, canonical rules, and diagnostics.
Per-rule source overrides support reconciled documents containing multiple sources.
Ownership is either `Managed` or `External`.
Rules support `conditions`, `exceptions`, explicit `actions`, and the simple `targetFolder` shortcut.
`targetFolder` is still treated as a simple `FileInto` action when `actions` is empty.

The source passed to reconciliation is authoritative.
Managed rules from that source become obsolete when they disappear from the document.
Managed rules from every other source remain untouched.

Supported condition and exception types are:

* `SenderContains`
* `ReceiverContains`
* `SubjectContains`
* `BodyContains`
* `SubjectOrBodyContains`
* `HasAttachment`

Exceptions are blocking tests.
When any exception matches, the generated rule does not run its actions.

Supported action types are:

* `FileInto`
* `CopyInto`
* `Redirect`
* `SetFlags`
* `Stop`

Generated Sieve requires extensions only when the rendered rule needs them.
`BodyContains` and `SubjectOrBodyContains` require `body`.
`FileInto` and `CopyInto` require `fileinto`.
`CopyInto` also requires `copy`.
`SetFlags` requires `imap4flags`.
`HasAttachment` requires `mime`.

## Optimization

Optimization never merges rules with different effective actions or different exceptions.
When `actions` is empty, `targetFolder` is treated as a single `FileInto` action.

`conservative` merges exact single-condition equivalents with the same condition type, actions, and exceptions.
`balanced` also merges action-equivalent single-condition rules across different condition types into one `Any` rule.
`aggressive` keeps the balanced merge behavior and applies broader sender-domain inference.

Redirect-only rules are not merged.
Rules that include redirect may merge only when they also have a delivery folder and the full action list is identical.

## Composition

Composition emits one leading `require [...]` statement when the candidate script needs extensions.
That statement contains the union of preserved active-script capabilities and generated rule capabilities.
Earlier leading `require` statements and SieveRuler managed requirements regions are removed during composition.
Managed rule metadata and body hashes remain in the rules region.

Preview capability checks use the union of server-advertised ManageSieve extensions and capabilities declared by the original active script's leading `require` block.
For example, an active script containing `require ["body", "fileinto", "imap4flags"];` is evidence that those extensions are acceptable for planning and preview.
Upload still depends on the server accepting the full candidate through `CHECKSCRIPT`.

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
* Render subject header tests as `Subject`, not `subject`.

This is the only provider UI metadata compatibility currently validated end to end.
