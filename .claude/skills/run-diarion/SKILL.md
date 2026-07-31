---
name: run-diarion
description: Launch Diarion on Windows and drive it — screenshot the window, click, scroll. Use when a change needs to be seen working in the real app rather than only in the test suite, or when asked to run, start, or screenshot Diarion.
---

# Running Diarion

The suite is fast and green and still says nothing about whether a control is on screen. Layout faults —
a button pushed past the bottom edge, a gesture that fires on the wrong surface — are only visible by
looking. This is how to look.

## Read this first: it is the user's real diary

The Windows build opens the user's actual encrypted database. Everything below is safe to do; the two
things that are not are **saving anything** and **clicking blind**.

- **Never tap Save, never create a task, never tick a mood.** Open screens, toggle controls that live in
  the ViewModel, close with ✕ or the back arrow. Nothing is written until a save.
- **Take a fresh screenshot immediately before every click.** Coordinates from an earlier shot are a
  guess about a screen that has since moved. A stale coordinate once landed on the mood row and wrote a
  mood into the user's diary.
- **Startup applies pending migrations** to the real database. That is normally fine — it would happen on
  the user's next launch anyway — but say so in your report when the branch adds one.
- If the change to be checked cannot be seen without saving, stop and ask the user first.

## Launch

`WindowsPackageType` is `None`, so there is a plain executable and no packaging step.

```bash
dotnet build Diarion.csproj -f net10.0-windows10.0.19041.0
cd bin/Debug/net10.0-windows10.0.19041.0/win-x64 && ./Diarion.exe
```

Start it with `run_in_background: true` and keep the task id — you will need it to stop the app, and you
will need to stop it (see below).

## Drive it

Three helpers in `scripts/`, run from the repo root. Plain `-File`, **no `-ExecutionPolicy Bypass`** —
the bypass flag is refused as an endpoint-policy override, and it is not needed.

```bash
powershell -NoProfile -File .claude/skills/run-diarion/scripts/shot.ps1 -Out shot01.png
powershell -NoProfile -File .claude/skills/run-diarion/scripts/click.ps1 -X 1141 -Y 306
powershell -NoProfile -File .claude/skills/run-diarion/scripts/scroll.ps1 -X 798 -Y 500 -Notches -5
```

`shot.ps1` waits up to a minute for the window, brings it to the front, and prints the origin:

```
OK left=78 top=78 w=1440 h=753
```

**That origin is the whole trick.** Read a coordinate off the screenshot with the Read tool, then click
`left + x`, `top + y`. Forgetting the offset puts the cursor most of a title bar away from the target,
which on a dense screen is a different control rather than a miss.

Then `Read` the PNG. A blank or black frame means the app never painted — that is a launch failure, not a
layout result.

## Stopping it, and why you must

A running Diarion holds `Diarion.Core.dll` open, and the next build fails with MSB3021 / MSB3027 "being
used by another process". Stop the background task before rebuilding. Nothing about the failure says
"your app is still open", so it costs a confusing minute every time it is forgotten.

## Cleaning up

Screenshots are scratch. Delete the PNGs when done; they are large, and a stale one in the tree is
exactly the thing the rule above warns about.

## Worked example

Checking that the task form's Save button survives the recurrence picker being open:

1. Build, launch in the background.
2. `shot.ps1` → read it → find the **Планувальник** tab.
3. Click the tab (offset applied), `shot.ps1` again, read it.
4. Click a task row in the description column — avoid the checkbox on the left and the ✕ on the right,
   both of which act rather than navigate.
5. `shot.ps1`, read: is Save on screen with the picker expanded?
6. `scroll.ps1` over the card, `shot.ps1`, read: do the fields below reach, and does Save stay put?
7. Close with ✕. Stop the background task. Delete the PNGs.

## When the fallback is wrong

These helpers drive the desktop, so they assume Diarion is the foreground window and nothing else is
covering it. If the screenshots come back showing another application, the click went somewhere else too
— stop, do not retry blind, and tell the user.
