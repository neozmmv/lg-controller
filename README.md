# LG Controller

Control an LG ThinQ air conditioner from the Windows system tray.

## Requirements

- Windows 10 or 11 (the translucent background needs Windows 11 22H2 or newer)
- .NET 10 Desktop Runtime
- An LG air conditioner already registered in the ThinQ app

## First run

1. Create a personal access token at https://connect-pat.lgthinq.com with your LG account.
2. Open `LgController.exe`, paste the token, enter your country and click Connect.
3. Pick the device and save.

The token is stored in `%APPDATA%\LgController\settings.json`, encrypted for your Windows user.

## Usage

- Click the tray icon to open the panel.
- Right-click it to turn the unit on or off, start with Windows, reconfigure or exit.

## Development

```powershell
dotnet test
dotnet run --project src/LgController
dotnet publish src/LgController -c Release -o publish
```

`tools/Probe` is a console app for testing the API directly against the device. Set `LG_THINQ_PAT` and run
`dotnet run --project tools/Probe -- list`.

Unhandled errors go to `%APPDATA%\LgController\log.txt`.
