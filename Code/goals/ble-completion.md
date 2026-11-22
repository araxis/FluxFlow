# BLE completion audit

Reviewed 2026-11-02 against the existing Windows companion and shared Studio UI.
The detailed standards and platform matrix is in
[CAPABILITIES.md](../src/Plugins/FluxFlow.Plugins.Bluetooth/CAPABILITIES.md).

| Checklist | Existing implementation | Completion work |
| --- | --- | --- |
| Standards and host capabilities | SIG references, Windows-managed services/descriptors, explicit unsupported features | Rechecked primary sources; corrected obsolete package overview |
| Multi-service Visual/JSON | Legacy loading, detached drafts, properties, protection, custom read-only descriptors, unknown fields | Extend multi-service template save/reopen and Undo/Redo coverage |
| UUID catalog and templates | Searchable curated catalog, custom UUID generation, Battery templates | Add validated read-only Tx Power structure using the existing codec/editor |
| Binary representations | Hex/Base64/strict UTF-8; Battery, Tx Power and selected string codecs | Verify exact original bytes and invalid conversions |
| Workflow integration | Dynamic client Read/Write, bounded Notify/Indicate source, server updates and incoming writes | Run existing cancellation, disconnect, cleanup, retention and isolation tests |
| Advertising | Separate editor; connectable/discoverable only | Retain host limits; no unsupported raw-advertising controls |
| Infrastructure/topology | Host-owned adapter/peripheral entries, locked snapshots, compatible bindings, read-only capability checks | Run existing binding/snapshot tests |
| Native lifecycle | Reverse rollback, pending request drain, late event rejection, explicit unconfirmed Stop failure | Extend late multi-service acquisition, clean rerun and combined startup/cleanup failure proof |

No change to Core or WFENG. Existing configuration, snapshots, telemetry and
Undo/Redo remain the source of truth. A standard UUID or template does not imply
complete profile behavior or Bluetooth qualification.

Physical delivery, pairing/protection interoperability, notification unsubscribe
over the air and actual advertising shutdown remain unverified without an
identified independent BLE peer. The known unconfirmed GATT Stop condition is
retained as a cleanup failure and prevents another server start in that companion.
No radio setting is changed by this audit.

## Verification — 2026-11-02

- BLE component tests: 38 passed across .NET 8 and 10.
- Studio BLE/editor and matching regression tests: 148 passed across both targets.
- Infrastructure catalog tests: 30 passed across both targets.
- BLE companion/explorer and RabbitMQ original-log shutdown checks: 20 passed
  across both targets.
- Windows lifecycle proof: 13 hardware-independent checks passed on each target.
  The `--no-hardware` result deliberately remains non-success for the unverified
  physical portion; no native Stop success is inferred from these checks.
- Updated WebAssembly UI and Windows companion published successfully. An initial
  host publish encountered files held by an existing preview; publishing into a
  separate directory succeeded without stopping or changing that preview.
- Browser trial in a separate temporary project: Battery and Tx Power services,
  signed Tx Power encoding (-20 to exact byte EC), Visual/JSON round-trip,
  Save/Undo/Redo, invalid JSON rejection and Cancel/reopen all passed.

Existing tabs and project settings were left intact. The browser trial did not
start advertising, scan for peripherals or change the radio state.
