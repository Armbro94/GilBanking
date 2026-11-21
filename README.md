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
  Retainer sales are detected from the retainer's gil increasing (read-only, no item names available).
- **NPC shops and repairs:** purchases/sales at NPC vendors and repair costs are tracked as expenses/income.
- **Market price tooltip:** hovering an item shows prices from [Universalis](https://universalis.app)
  (lowest price NQ/HQ, averages, sales per day) or "Not tradable", plus your own average sale price.
  Price source selectable: own world, own data center, all of Europe or a custom world/DC/region.
  For wide sources it also shows the price on your own world and how much you would save.
  Results cached for 5 minutes.
- **Data protection:** option to never delete sales data (on by default) plus a daily backup of `ledger.json`.
- **Multiple characters:** filter per character or "All"
- **CSV export**
- Commands: `/gil`, `/gilbanking`, `/gilbanking settings`


## Install

In-game, open Dalamud settings (`/xlsettings`) → **Experimental** → **Custom Plugin Repositories**, then add:

https://raw.githubusercontent.com/Available5673/GilBanking/refs/heads/main/repo.json

Save, then open the plugin installer (`/xlplugins`) and search for **Gil-Banking**.
