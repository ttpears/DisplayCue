# Home and Office setup

This workflow uses an X1 Carbon Gen 12, a Lenovo 40AY USB-C dock, two Samsung G51F monitors, and a desktop that retains a separate TV. No additional video switch is required if DDC input switching works over the existing connections.

## Connections

Each PC uses one HDMI connection and one DisplayPort connection for the shared G5s. The connections cross:

| Display | Desktop | Laptop dock |
| --- | --- | --- |
| Left G5 | DisplayPort | HDMI |
| Right G5 | HDMI | DisplayPort |
| TV | Existing desktop cable | None |

Reverse left/right if that matches the actual cabling. Select the actual HDMI socket (HDMI 1 or HDMI 2) for each assignment. Do not assign the same input to both monitors merely because they are identical. Keep the TV's physical input outside all monitor-input assignments.

Begin with both laptop monitors at 2560 × 1440, 60 Hz. Desktop and laptop profiles can retain different refresh rates. Higher laptop refresh rates remain subject to the dock/laptop/cable combination and need a separate physical test.

During calibration, disable the monitors' automatic source switching so enabling a destination connection does not switch inputs behind DisplayCue's recovery snapshot.

## Install and pair

1. Run the same DisplayCue version (1.5 or later) on both computers. Existing 1.4 peers cannot participate in the new handoff protocol.
2. Configure the paired computer address, ports, shared pairing key and private-network firewall access. Test the connection in both directions.
3. In monitor-input settings on both computers, assign `left` and `right` Peer IDs to the same physical monitors. Use Identify while each PC owns the monitors to establish the mapping.

Profile creation starts by manually arranging and activating the desired screens in Windows. Use **Capture current layout**, select input assignments, then **Save profile**. Full layout capture requires an extended desktop with one primary display.

## Save the states

On the desktop:

- **Desktop owns desk:** both G5s and TV active, desktop inputs assigned for both G5s. Leave the paired-profile field blank so this profile works after the laptop disappears.
- **Laptop owns desk:** TV active, both G5 input assignments point to the laptop's ports. Leave the paired-profile field blank; this profile is the passive half of the laptop's takeover.

On the laptop:

- **Home laptop:** laptop panel and both G5s active; G5 assignments point to the laptop's ports; paired profile is `Laptop owns desk`.
- **Home desktop:** laptop panel active; G5 assignments point to the desktop's ports; paired profile is `Desktop owns desk`.
- **Office:** laptop panel and office monitors active; leave peer profile blank when no desktop participates.

The monitor-input editor initially discovers active monitors. To create a smaller state, first save its G5 assignments while the G5s are active. Then manually enable only the TV or laptop panel in Windows, reopen that saved profile, capture the smaller layout and save it. Keep the G5 cables attached throughout. The input editor retains unavailable saved mappings and offers an explicit Remove button. This allows DisplayCue to switch those monitors' inputs before disabling their Windows paths.

The paired profiles must agree on each monitor's destination input. Assign each scene's inputs consistently on both PCs.

## Test before automating

1. On the desktop, apply **Desktop owns desk**, choose **Keep**, and select **Undo last change / test recovery**. Apply it again to return to the desktop state. This validates the standalone return profile.
2. On the laptop, apply **Home laptop**, choose **Keep**, then test **Undo**. Verify both G5 inputs and both computers' layouts return correctly.
3. Apply **Home laptop** again and manually apply **Home desktop** to test the other handoff direction. Choose Keep and test Undo for any new configuration.
4. At the office, capture the Office layout, apply it, choose Keep and test Undo.

If a switch fails, inspect `%LocalAppData%\DisplayCue\peer.log` on both computers. A missing or ambiguous DDC route, unreadable previous input, failed input readback, unavailable monitor, or unsupported saved refresh rate must be resolved before enabling automation. No simulator test certifies the actual monitors or dock.

## Enable automatic desks

On the laptop, open **Automatic desks**:

1. With the home monitors connected, create **Home**, select **Home laptop**, and choose **Recognize this desk**.
2. Select a Lenovo USB connection associated with the dock. Unplug and reconnect once, reopening the dialog as needed, to verify that this marker actually disappears and returns. A permanently connected laptop device is not a suitable marker.
3. Enter `Desktop owns desk` as the desktop return profile and enable the Home rule. The app checks that the desktop return profile has passed its local Keep/Undo test.
4. At work, create **Office**, select **Office**, recognize its monitors, and enable it. A dock marker is optional when monitor identities distinguish the office. Leave desktop return blank.

Keep DisplayCue running on both PCs. Add it to Windows startup if desired. Automatic takeover begins after three stable connection observations (about nine seconds). The first new configuration still needs a manual confirmation; edits to layout, inputs or pairing invalidate calibration.

To keep the desktop in charge for this docking session, use **Leave monitors on desktop this session** before or after takeover. Resume and monitor-change events do not override that choice. Disconnecting the dock resets the session.

On undock, the laptop stops renewing its connection lease. If it can still reach the desktop, it requests return after debounce; otherwise the desktop acts when the 30-second lease expires. A network outage while still docked can also return the monitors. A later explicit desktop change cancels the previous lease, preventing stale laptop heartbeats from overriding it.

## Physical acceptance checks

Before relying on the setup daily, test takeover, return, a sleeping desktop, a missing monitor, a network interruption, and unplugging during a handoff. Check that the TV remains available and that no windows are stranded on screens owned by the other PC. If the laptop has physically lost its external displays during recovery, DisplayCue retains its connected panel and reports incomplete input recovery; reconnect and repeat the full recovery test before enabling automation.
