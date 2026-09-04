# Desk Handoff architecture

Desk Handoff coordinates DisplayCue profiles across trusted Windows computers. It is designed to complement input-sharing software such as Mouse Without Borders: DisplayCue owns display topology and monitor-input changes, while the input-sharing application continues to own keyboard, mouse, and clipboard transport.

Version 1.3 implements the first two-computer phase: a local profile may invoke a named profile on one paired computer before applying locally. The transactional multi-device model below remains the direction for later releases.

## Model

- A **local profile** describes the Windows displays that should be active on one computer and any physical monitor inputs that computer can control.
- A **trusted device group** contains two or more explicitly paired DisplayCue installations.
- A **handoff scene** references one local profile per participating computer. Participants may be required or optional.
- A **control route** records which paired computer can reach a physical monitor over DDC/CI in each input state. The route is measured during calibration rather than inferred from whether a cable appears direct or docked.

This separation allows local profiles to work without networking and prevents a shared scene from duplicating hardware-specific VCP values.

## Transaction

1. Discover required peers and reject the handoff if any required participant is unavailable.
2. Ask every participant to validate its referenced profile and capture its current topology and affected monitor inputs.
3. Prepare destination display signals before changing physical monitor inputs.
4. Commit local profiles and route DDC input changes through the peer proven to control each monitor in that state.
5. Confirm that required peers reached their target states.
6. Commit the transaction, or restore every captured state if a required action fails or times out.

Transactions use unique identifiers and expire automatically. Repeated commit and rollback messages must be idempotent.

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

Automatic matching uses EDID manufacturer, product and serial data. When identical monitors omit usable serial numbers, DisplayCue asks the user to identify and name them. Users normally configure input ownership (for example, Desktop or Laptop); they do not configure which cable is "direct."

## Capability levels

- **Fully automatic:** every transition and recovery route has passed calibration.
- **Automatic using source detection:** monitor auto-source behavior is required and has been tested.
- **Manual input required:** DisplayCue coordinates Windows layouts but prompts for a physical input change.

DisplayCue must never claim a scene is fully automatic until both directions and the rollback path have succeeded.

## Tested development setup

Two Samsung Odyssey G5 G51F displays reported MCCS 2.1 and input-source VCP `0x60` with HDMI 1 (`0x11`), HDMI 2 (`0x12`), and DisplayPort 1 (`0x0F`). Both continued accepting read and set commands from the desktop after switching away from the desktop input, allowing that desktop to act as a permanent controller. These results describe the development hardware only and are not hard-coded product assumptions.
