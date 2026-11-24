# Gil-Banking – gil and sales tracker for FFXIV (Dalamud)

A **tracking plugin** that only reads and analyses. There is **no automation**
(no auto-posting, no auto-buying, no simulated input).
Languages: English, Deutsch, Français (automatic from Dalamud's language, changeable in the settings).

## Features

- **Overview:** gil, retainer gil, total wealth, session plus/minus, revenue, expenses, profit,
  profit-per-day chart, wealth history
- **Sales:** searchable table of all sales/purchases, manual entries
- **Items:** per-item statistics (revenue, quantity, average prices, profit)
- **Retainers:** one card per retainer with gil, income in the chosen period and last income.
  Retainer sales are detected from the retainer's gil increasing. The items are found by comparing the retainer's
  market listings with the list saved at the last visit (read-only): what is missing was sold, the price is estimated
  from the gil received (split by market price when several items sold). Works for sales while you were offline.
- **Universalis access:** one shared, throttled client for tooltip, deals and retainer estimates (max. 2 requests at a time,
  pause after HTTP 429). Home-world prices use the light `aggregated` endpoint, sale history is only fetched on demand.
- **Deals:** finds items that are cheaper on another world than on your home world (Universalis), with profit
  after 5 % tax, margin and sales per day. Manual reload with a cooldown, only a few requests per scan.
- **Watchlist (in the Deals tab):** search items by name, set a wish price and quality; every reload checks the cheapest
  matching listing in your search range and highlights hits.
- **Offline price cache:** Universalis prices are also saved to a file; if Universalis is down, the last values are shown
  and marked as outdated. Storage folder and retention are selectable.
- **Own market board reading (Alpha, off by default):** while you browse a market board search, the visible price list is read
  (read-only, nothing clicked or uploaded) and the lowest price is stored with a time stamp. The tooltip shows whether your own
  observation is newer than Universalis. Storage folder selectable. Tip: also run the official Universalis plugin.
- **NPC shops, market board purchases:** purchases/sales at NPC vendors and market board purchases are detected
  from gil changes while the matching window is open (read-only).
- **Expenses & other:** repairs plus every other gil change (quests, mail, trades, items …) in its own tab.
  Moves to/from retainers are recognised and ignored. Other gil is not part of revenue/profit.
- **Today:** the daily gil change survives relogs (it only resets on a new day).
- **Market price tooltip:** hovering an item shows prices from [Universalis](https://universalis.app)
  (lowest price NQ/HQ, averages, sales per day) or "Not tradable", plus your own average sale price.
  Price source selectable: own world, own data center, all of Europe or a custom world/DC/region.
  The home world is fixed (setting, or your character's home world) and does not change when you visit another world.
  For other sources it adds a block for your home world (NQ and HQ separately) and compares the quality of the
  hovered item: cheaper elsewhere, resale margin, or "you'd be the only seller".
  Results cached for 5 minutes.
- **Data protection:** option to never delete sales data (on by default) plus a daily backup of `ledger.json`.
- **Multiple characters:** filter per character or "All"
- **CSV export**
- Commands: `/gil`, `/gilbanking`, `/gilbanking settings`


## Install

In-game, open Dalamud settings (`/xlsettings`) → **Experimental** → **Custom Plugin Repositories**, then add:

https://raw.githubusercontent.com/Available5673/GilBanking/refs/heads/main/repo.json

Save, then open the plugin installer (`/xlplugins`) and search for **Gil-Banking**.

## Project layout

- `Tracking/` – reading the game: `GilTracker`, `ChatTracker`, `GilMessages`, `MarketReader`, `SalePricing`
- `Data/` – `Ledger`, `Models`, `Configuration` (stays in namespace `GilBanking`, Dalamud stores its type name), `MarketStore` (price cache, own market data)
- `Market/` – `Universalis` (shared client), `Deals`, `Worlds`, `Items`
- `Windows/` code lives in `Ui/` – `MainWindow`, `ItemTooltip`, `Ui`, `Loc`
- `Plugin.cs`, `GlobalUsings.cs` in the root
