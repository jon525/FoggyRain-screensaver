# Foggy Rain

A Windows screen saver that freezes whatever is on your screen. The glass slowly fogs over like a cold window while raindrops land, bead up and wiggle their way down, wiping clear trails through the fog.

- Each drop acts as a tiny lens with an upside-down view of the frozen screen, plus a dark rim and a bright glint
- Drops merge as they meet, catch and pause, then zig-zag on down the glass
- The trails they leave fog over again slowly
- Optional lightning flashes, synced across every monitor
- Each monitor gets its own window at native resolution
- A settings window with presets and lots of sliders

## Install

You don't need to install anything: `build.bat` uses the C# compiler that comes with Windows (.NET Framework 4).

1. Download this repository (**Code → Download ZIP**) and unzip it somewhere permanent. Windows runs the screen saver from that folder, so don't move it after installing.
2. Double-click **`build.bat`**. It creates `FoggyRain.scr`.
3. Right-click **`FoggyRain.scr`** and choose **Install**. On Windows 11 you may need **Show more options** first.
4. Choose the **Wait** time and click **OK**.

To try it straight away, right-click `FoggyRain.scr` and choose **Test**. Move the mouse, click or press any key to exit.

## Settings

Right-click `FoggyRain.scr` and choose **Configure**, or click **Settings…** in Windows' Screen Saver Settings.

| Tab | What you can change |
|---|---|
| **Fog** | Time to fog over, thickness, unevenness, blur, tint colour and strength, grain, trail re-fog time, darkening the screen, wiping the glass every N minutes |
| **Rain** | Start delay, starting and full intensity, build-up time, drop size, biggest drop size, size before a drop runs, running speed, stop-and-go, wiggle, distance between turns, wiggle variety, jitter, wind, beads left behind |
| **Condensation** | Bead formation rate, maximum beads, bead size |
| **Drops** | Lens magnification, highlight brightness, edge darkness, bottom glow, running drop stretch |
| **Lightning** | On/off, time between flashes, flash brightness |
| **General** | Frame rate, render resolution, CPU cores, which monitors, exiting on mouse movement and how far, hiding the pointer, diagnostic log |

Presets: Default, Light drizzle, Heavy downpour, Storm, Thick fog, Night window, Calm and slow, Windy, Quick demo.

**Test** saves your changes and runs the screen saver. Settings are stored in `HKEY_CURRENT_USER\Software\FoggyRain`.

## Updating

Replace `FoggyRain.cs` and run `build.bat` again. You don't need to reinstall.

## Uninstall

1. In Screen Saver Settings, pick another screen saver or **(None)**.
2. Delete the folder.
3. Optional: delete `HKEY_CURRENT_USER\Software\FoggyRain` in regedit.

## Troubleshooting

- **Slow or stuttering:** on the General tab, lower **Render resolution** or **Frame rate**.
- **Small preview picture is blank:** that's normal; use the **Preview** button instead.
- **Frozen screen is black after the lock screen:** untick *On resume, display logon screen*.
- **`build.bat` can't find `csc.exe`:** turn on *.NET Framework 4.8 Advanced Services* under *Turn Windows features on or off*.
- **Anything else:** run it once and check `FoggyRain.log` next to the `.scr`.
