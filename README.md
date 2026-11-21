# Gil-Banking – gil and sales tracker for FFXIV (Dalamud)

A **tracking plugin** that only reads and analyses. There is **no automation**
(no auto-posting, no auto-buying, no simulated input).
Languages: English, Deutsch, Français (automatic from Dalamud's language, changeable in the settings).

## Features

- **Overview:** gil, retainer gil, total wealth, session plus/minus, revenue, expenses, profit,
  profit-per-day chart, wealth history, retainer list
- **Sales:** searchable table of all sales/purchases, manual entries, delete with Ctrl+click
- **Items:** per-item statistics (revenue, quantity, average prices, profit)
- **Market price tooltip:** hovering an item shows prices from [Universalis](https://universalis.app)
  (lowest price NQ/HQ, averages, sales per day) or "Not tradable", plus your own average sale price.
  Own world or whole data center; results cached for 5 minutes.
- **Multiple characters:** filter per character or "All"
- **CSV export**
- Commands: `/gil`, `/gilbanking`, `/gilbanking settings`

## Build and test

1. Install the .NET SDK required by the current Dalamud template.
2. Check the SDK version in `GilBanking.csproj` against <https://www.nuget.org/packages/Dalamud.NET.Sdk>
   (the version decides the Dalamud API level).
3. `dotnet build -c Release` in the project folder.
4. In game: `/xlsettings` → Experimental → add the path to the built DLL under "Dev Plugin Locations",
   then enable it in `/xlplugins` → Dev Tools → Installed Dev Plugins.

## Check chat detection

The market board sale messages differ per game language and were **not tested in game**. If sales don't appear:

1. Settings → enable "Log system messages containing gil".
2. Wait for a sale and read the exact message.
3. Enter matching words under "Words for a sale/purchase".

Until then you can add entries manually; the gil history works independently.

## Data and disk

By default everything is stored in `%AppData%\XIVLauncher\pluginConfigs\GilBanking\` (`ledger.json`, `exports\`).
Data and export folders can be changed in the settings (full path, e.g. `D:\Data\GilBanking`).

SSD-friendly:
- written only when something changed, at most every N minutes (default 10, up to 60), in the background
- additionally on logout, folder change and when the plugin unloads
- after a game crash at most the last N minutes are lost

Data from the earlier version (folder `pluginConfigs\GilLedger`) is imported automatically on first start.
