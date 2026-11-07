# MyAPRS

MyAPRS logs your station's position reports from APRS-IS and exports date ranges
to Google Earth KMZ. It runs on Windows x64.

## Install

Download `MyAPRS-Setup.exe` from the installer build and run it. Approve the
administrator prompt, enter your callsign including its SSID, and choose the
Yaesu Mic-E altitude setting for your radio. MyAPRS uses a receive-only
APRS-IS login. You can import an old `aprs_log.csv` for the selected station
if that station's new archive does not already exist. Close the old tracker
before installing.

Setup installs a native Windows service named **MyAPRS**, starts it at boot,
and sets Windows to restart it if it fails. The logger also retries APRS-IS
connections when the network drops. No separate .NET or NSSM installation is
required.

## Use

The tray icon starts at sign-in and shows the logger's status. Double-click
the icon to open the status and export window. Choose the first and last local
dates to include, then select **Export KMZ**. Closing the window, exiting the
tray app, or signing out leaves the service running.

Station CSVs are kept under `%ProgramData%\MyAPRS\data` and can be read by
local Windows users. New samples have UTC timestamps. Legacy timestamps
without offsets are interpreted in the PC's local time zone during export.
The decoder carries over the previous uncompressed and Mic-E support.
To change the station or altitude setting, rerun Setup.

## Uninstall

Open **Settings → Apps → Installed apps → MyAPRS → Uninstall**. Setup stops
and removes the service, closes the tray app, and removes the sign-in startup
entry. It keeps the archive and settings under `%ProgramData%\MyAPRS`.

## Build

The **Build MyAPRS installer** workflow publishes the app, compiles the Inno
Setup installer, and checks installation, crash recovery, and uninstall on
Windows. Download the resulting **MyAPRS-Setup** artifact from the workflow run
and extract the installer EXE.
