# Desk Handoff architecture

Desk Handoff coordinates DisplayCue profiles across trusted Windows computers. It is designed to complement input-sharing software such as Mouse Without Borders: DisplayCue owns display topology and monitor-input changes, while the input-sharing application continues to own keyboard, mouse, and clipboard transport.

Version 1.5 uses handoff protocol 2 and retains the source Windows paths through physical input switching. Saved extended layouts include positions, primary display, orientation, resolution and refresh rate. The trusted-device-group model below remains the direction for extending coordination beyond two computers.

## Model

- A **local profile** describes the Windows displays that should be active on one computer and any physical monitor inputs that computer can control.
- A **trusted device group** contains two or more explicitly paired DisplayCue installations.
- A **handoff scene** references one local profile per participating computer. Participants may be required or optional.
- A **control route** records which paired computer can reach a physical monitor over DDC/CI in each input state. The route is measured during calibration rather than inferred from whether a cable appears direct or docked.

This separation allows local profiles to work without networking and prevents a shared scene from duplicating hardware-specific VCP values.

## Transaction

1. Discover required peers and reject the handoff if any required participant is unavailable.
2. Ask every participant to validate its referenced profile and capture its current topology and affected monitor inputs.
3. Read original inputs through a reachable participant, then activate available destination/control paths while retaining the outgoing paths. Capture any newly reachable originals before the first input write; reject missing originals.
4. Apply each input change through either participant and read the physical input back. Do not overwrite the original snapshot when a fallback route sees an already changed input.
5. Apply and verify both final Windows layouts; only now are outgoing shared paths removed.
6. Commit after confirmation of a new pair of profile signatures. Unchanged validated signatures commit without the dialog. Revert, timeout, failure or Undo prepares original control paths, restores inputs through either participant, and attempts both original layouts even if one recovery action fails.

Transactions use unique identifiers and expire automatically. Repeated commit and rollback messages must be idempotent.

Version 1.5 keeps the current inbound transaction until a newer transaction supersedes it so a five-minute Undo can still restore its snapshot. Incomplete transactions recover after two minutes without phase activity. Recovery validation requires all original displays to be restored; an unplugged laptop may recover only its connected panel without earning calibration.

## Desk automation

Automation defaults to disabled. Saved monitor sets distinguish Home and Office; present Lenovo USB instance IDs can serve as dock anchors while video paths reconnect. Exact matches must be unique. Dock-only fallback is allowed only with no external video paths and one matching rule. A per-session override survives display changes and resume, and is cleared after three observations of physical disconnect.

The desktop can arm a return lease only for the current committed transaction and a calibrated desktop-local profile. The laptop renews that transaction's lease while its saved dock remains present. Explicit undock requests or 30 seconds without renewal expire it. Return runs locally on the desktop, without needing the absent laptop. New manual/peer transitions cancel old leases; stale tokens cannot renew or release a new lease. A network outage can therefore cause return while the laptop remains physically docked.

Calibration stores digests of the saved configuration and the paired configuration. A saved layout, successful Keep and successful full Undo are required before enabling a desk rule. These records represent a user-performed physical test; simulated regression tests never write calibration records for real hardware.

## Pairing and transport requirements

- Pairing requires an explicit action on both computers and an out-of-band secret or authenticated code comparison.
- Each installation has a persistent device identity. Trust can be reviewed and revoked locally.
- Commands are authenticated with HMAC-SHA256, include a timestamp and nonce, and are rejected when replayed. Payload encryption is planned before internet relay or use outside trusted private networks.
- Discovery remains on the local network and reveals no profile contents or monitor details to unpaired devices.
- DisplayCue has no cloud dependency. Internet relay is outside the initial scope.

## DDC calibration

For each physical monitor, calibration records:

- EDID identity and user-facing location name.
- Advertised MCCS version, VCP `0x60` values, and current input.
- The paired computers that can read and set the input.
- Whether a route remains responsive when its video input is inactive.
- Average response time and recent failures.

Automatic matching uses a fingerprint of the monitor EDID, including its hardware serial when supplied. When identical monitors expose indistinguishable EDIDs, the user assigns the same short Peer ID (for example, `left` or `right`) to the corresponding monitor on both PCs. Users configure the destination input, not which computer is permanently "primary" for DDC; reachability is measured for each transition.

## Capability levels

- **Fully automatic:** every transition and recovery route has passed calibration.
- **Automatic using source detection:** monitor auto-source behavior is required and has been tested.
- **Manual input required:** DisplayCue coordinates Windows layouts but prompts for a physical input change.

DisplayCue must never claim a scene is fully automatic until both directions and the rollback path have succeeded.

## Hardware compatibility

Input control must be tested for each monitor, cable, graphics adapter, and dock combination. An advertised input value does not guarantee that the monitor can be controlled after changing inputs. Use the [setup guide](HOME-OFFICE-SETUP.md) to verify both handoff directions and recovery before enabling automatic switching.
