# Set up DisplayCue

Use this guide to save a screen layout, share monitors between two Windows computers, and optionally switch layouts when you dock a laptop. Start with manual switching. Enable automation after switching and Undo both work.

## 1. Install and find the app

1. Download `DisplayCue-win-x64.zip` from the [GitHub releases page](https://github.com/ttpears/DisplayCue/releases/latest).
2. Extract it to a folder you want to keep. Run `DisplayCue.exe` from that folder.
3. Find DisplayCue in the Windows notification area, next to the clock. You may need to open the hidden-icons arrow.
4. Right-click its icon and choose **Settings and profiles**.

If you use two computers, install the same version on both. Paired switching requires version 1.5 or later on both computers.

## 2. Save a screen layout

A profile saves which screens Windows uses, their arrangement, and optionally which cable input each monitor shows.

1. Open Windows **Settings > System > Display**.
2. Enable the screens you want. Choose **Extend** for multiple screens, arrange them, and select your main display. Use a resolution and refresh rate that already work with your cables and dock.
3. Open DisplayCue's **Settings and profiles > Display profiles**. If you changed the connected screens while Settings was open, close and reopen it first.
4. Choose **New profile**, then give it a useful name, such as `Office`.
5. Choose **Capture current layout**. This fills in the active-screen selection and saves the arrangement, main display, resolution, orientation, and refresh rate.
6. Leave **Profile to run on paired computer** blank for a layout that affects only this PC.
7. Choose **Save profile**, then close Settings.
8. Right-click the tray icon and select the saved profile. If a confirmation appears, check the screens and choose **Keep** within 15 seconds. Choose **Revert**, or let the timer expire, if the result is wrong.
9. Select **Undo last change / test recovery** from the tray menu. Check that the previous layout returns.

For one computer, this is enough to switch layouts manually. Repeat for other layouts. To give two profiles keyboard shortcuts, open **Quick switching**, assign the quick actions and shortcuts, then choose **Save changes**.

## 3. Connect shared monitors to both computers

Skip to step 7 if you only want automatic laptop layouts and do not share monitors with another PC.

Each shared monitor needs a video connection from each computer. A laptop's connection can come through its dock. The two cables must use different input sockets on that monitor.

Write down your actual connections before configuring input switching:

| Shared monitor | Input connected to desktop | Input connected to laptop or dock |
| --- | --- | --- |
| First monitor | Fill in the socket label | Fill in the socket label |
| Second monitor, if present | Fill in the socket label | Fill in the socket label |

Use labels such as **HDMI 1**, **HDMI 2**, or **DisplayPort 1** from the monitor's menu. The ports do not have to match across monitors.

1. Use each monitor's own input menu to show the desktop, then the laptop. Confirm that both computers can display a working extended layout.
2. In the monitor's own settings, enable **DDC/CI** if that option exists. This lets DisplayCue ask the monitor to change inputs.
3. Disable automatic input/source selection while setting up and testing, so the monitor does not make an unexpected switch.
4. Leave the video cables attached to both computers throughout setup.

Screens used only by one computer are not shared. Include them in that computer's layouts as appropriate, but select **Do not change** for their monitor inputs.

The profiles below keep at least one screen available on each PC: the laptop's built-in screen and a separate desktop screen. DisplayCue requires at least one active display in every profile. If your desktop has only the shared monitors, this example does not provide a fully parked, screenless desktop profile.

## 4. Pair the computers

On **each** computer, open **Settings and profiles > Paired computer**:

1. Enable **Allow authenticated peer control**.
2. Enter the other PC's hostname or private-network address in **Paired computer address**.
3. Make sure this PC's **Peer port** matches the other PC's **Listen port**. You can keep the defaults on both PCs.
4. On one PC, choose **Generate** beside **Pairing key**. Copy that key to the same field on the other PC. Both must use the same key.
5. Choose **Save**. If Windows Firewall asks, allow DisplayCue on your private network.
6. Choose **Test connection** on both PCs. Each test should identify the other computer.

Keep DisplayCue running on both PCs. Close Settings on both before applying a paired profile; an open Settings window can block incoming changes.

## 5. Create the four shared-monitor profiles

These example names can be copied exactly. You may rename them, but the paired-profile fields must match the names you actually save.

| Create on | Profile name | Screens Windows keeps active | Shared monitors show | Profile to run on paired computer |
| --- | --- | --- | --- | --- |
| Desktop | `Desktop desk` | Shared monitors and desktop-only screen(s) | Desktop inputs | Leave blank |
| Desktop | `Desktop parked` | Desktop-only screen(s) | Laptop inputs | Leave blank |
| Laptop | `Home laptop` | Built-in screen and shared monitors | Laptop inputs | `Desktop parked` |
| Laptop | `Home desktop` | Built-in screen | Desktop inputs | `Desktop desk` |

**Active screens and monitor inputs are separate settings.** A parked profile can tell a shared monitor to show the other PC while removing that screen from its own Windows desktop.

### Save the profiles that use all shared screens

1. Set the monitors to the desktop inputs using their own menus. In Windows, enable and arrange the desktop's shared and private screens.
2. On the desktop, create `Desktop desk` using step 2 above.
3. Before saving, open **Monitor inputs**. For each shared monitor, change **Do not change** to its desktop input from your connection table.
4. Give each shared monitor a different **Peer ID**, such as `left` and `right`. Use **Identify displays** to establish which is which. Write down the IDs; they must match on the other PC.
5. Leave desktop-only screens set to **Do not change**. Choose **Use these inputs**, then **Save profile**.
6. Set the shared monitors to the laptop inputs. Enable and arrange the laptop's built-in screen and shared screens in Windows.
7. On the laptop, create `Home laptop`. Capture its layout, select the laptop inputs, and assign the same Peer IDs to the same physical monitors. Choose **Use these inputs** to close the input editor.
8. Enter `Desktop parked` in **Profile to run on paired computer**, then save. You will create that desktop profile next; do not apply this paired profile yet.

### Save the profiles that give up the shared screens

DisplayCue initially discovers monitor-input controls through active screens. Save their input assignments **before** capturing a smaller layout:

1. Use the monitor buttons to show the desktop and enable its shared screens in Windows. Then create `Desktop parked`.
2. In **Monitor inputs**, select the **laptop inputs** for the shared monitors and use their existing Peer IDs. Choose **Use these inputs**. For this initial save, select the currently active screens and capture their layout. Leave the paired-profile field blank, then save. Do not apply this temporary profile.
3. Close DisplayCue Settings. In Windows Display settings, disconnect the shared screens from the desktop's layout, keeping its private screen(s) active. Leave the cables plugged in.
4. Reopen DisplayCue Settings, select `Desktop parked`, choose **Capture current layout**, and save again. Keep the previously saved monitor-input assignments, even if the shared monitors are now unavailable in the input editor.
5. Use the monitor buttons to show the laptop and enable its shared screens in Windows. Repeat this process for `Home desktop`: first save assignments pointing to the **desktop inputs** while the shared screens are active, then capture a layout with only the built-in screen active.
6. Set `Home desktop`'s paired-profile field to `Desktop desk` and save.

For each destination, both PCs' profiles must agree on the input for every shared monitor. For example, `Home laptop` and `Desktop parked` both select the laptop's inputs.

## 6. Test switching and recovery

Close Settings on both PCs before each test. Use the tray menu to apply profiles.

1. On the desktop, apply `Desktop desk`. Choose **Keep** if prompted, then run **Undo last change / test recovery**. Check that the previous inputs and layout return. Apply `Desktop desk` again to start with the desktop using the shared screens.
2. On the laptop, apply `Home laptop`. Check that the shared screens show the laptop and that the desktop still has its private screen(s). Choose **Keep**, then run **Undo**. Check that both PCs' layouts and the monitor inputs return to the desktop state.
3. Apply `Home laptop` again. Then apply `Home desktop` from the laptop. Check that the shared screens return to the desktop and the laptop uses its built-in screen. Choose **Keep** and test **Undo** for this configuration too.

Undo is available for five minutes, until another change replaces it. A successful **Keep** and full **Undo** test, with a captured layout, are required before that profile can be automated. Editing a layout, input assignment, or pairing configuration requires another test.

## 7. Turn on automatic docking

Configure automatic desk rules on the **laptop**. For shared monitors, the desktop only needs to keep DisplayCue running and have its tested local return profile saved.

### Shared home desk

1. Connect the laptop to the home dock and apply `Home laptop` so the shared monitors are available for recognition.
2. From the laptop's tray menu, open **Automatic desks** and choose **New desk**.
3. Enter `Home` for **Desk name**, select `Home laptop` for **Profile to apply on this computer**, and choose **Recognize this desk**.
4. Under **Dock connection**, select a connection belonging to the dock. Version 1.5 lists supported Lenovo USB connections; other docks can use monitor recognition, but cannot use this automatic desktop-return feature without a supported dock connection.
5. Enter `Desktop desk` in **Desktop profile to restore after undocking**. Leave automatic switching unchecked and choose **Save desk** while you verify the dock marker.
6. Unplug the dock, reopen **Automatic desks**, and select this rule. The chosen marker should read **Saved dock connection (disconnected)**. Reconnect and reopen the dialog; it should show the connected device again. A device that remains present while undocked is the wrong marker.
7. With the dock connected, check **Enable automatic switching for this desk**, then choose **Save desk**. If DisplayCue asks for a recovery test, finish the manual switching and recovery tests above before enabling the rule.
8. Close the dialog. Disconnect and reconnect the dock to test the rule. Takeover normally starts after about nine seconds of a stable connection. Confirm that undocking returns the shared monitors to the desktop.

Both apps must remain running. If the laptop disappears from the network, the desktop returns after about 30 seconds without a connection heartbeat. A network interruption can therefore return the monitors even while the laptop is docked.

### Office or another desk without a paired PC

1. Connect its monitors and save a profile such as `Office`, following step 2. Leave its paired-profile field blank.
2. Apply the profile, choose **Keep**, and test **Undo**.
3. In **Automatic desks**, create an `Office` rule, select that profile, and choose **Recognize this desk** while its external monitors are connected.
4. Use **Recognize by connected monitors**, or select a supported dock connection if needed. Leave the desktop-return field blank.
5. Enable the rule, save, close the dialog, and test a disconnect/reconnect.

For an automatic shared-desk takeover without a supported dock connection, use monitor recognition and leave the desktop-return field blank. Return the monitors manually with `Home desktop` before disconnecting the laptop.

To keep the desktop using the shared monitors for one docking session, select **Leave monitors on desktop this session** from the laptop's tray menu. Disconnecting the dock clears this override. Add DisplayCue to Windows startup if you want it available after signing in.

## If something does not work

| What you see | What to check |
| --- | --- |
| Pairing test fails | Both apps are running, the key matches, the address and ports are correct, and private-network firewall access is allowed. |
| A paired change says Settings is open or the other PC is busy | Close Settings and any pending confirmation or error dialog on both PCs, then retry. |
| A monitor is absent from **Monitor inputs** | Enable it in Windows and select that PC's input on the monitor. Reopen Settings. Check DDC/CI and the cable/dock connection. |
| Wrong input, ambiguous identity, or input could not be confirmed | Check the actual socket labels and matching Peer IDs. Re-save the assignments while the monitors are active. Some monitors or docks cannot provide reliable input control. |
| A saved layout cannot be restored | First make it work in Windows, using a supported resolution and refresh rate. Capture and save it again, then repeat Keep/Undo. |
| Automation will not enable | Capture the layout and finish Keep/Undo. Automatic return also needs a supported dock marker and a tested desktop profile whose paired-profile field is blank. |
| Automation does not run | Close Settings and Automatic desks, check that the rule is enabled and both apps are running, and verify the saved monitors/dock match this desk. |

If recovery is incomplete, use the monitor buttons to select the intended inputs, restore a usable layout in Windows, and repeat the manual tests before enabling automation. Reconnect missing displays first.

For diagnostics, inspect `%LocalAppData%\DisplayCue\peer.log` on both PCs. You can also run `DisplayCue.exe --ddc-report ddc-report.txt` to create a read-only monitor compatibility report. Review logs and reports before sharing them: they can contain computer/network details and monitor identifiers.

Before relying on automation daily, also test a sleeping peer, a missing monitor, a network interruption, and unplugging during a switch. Confirm that private screens stay usable and windows return to the screens their computer still owns.
