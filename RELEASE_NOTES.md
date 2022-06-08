# Release Notes

## v0.3.6
- Ability to highlight large resting Ask/Bid orders based on configurable size threshold.
- BUG FIX: Fixed issue that causes installs to fail (LadderRow name conflict).

## v0.3.5
- Added totals for the sliding window (total buys/sells, largest buys/sells, last buy/sell) and a summary row at the bottom of ladder.
- Turned off last trades on UI by default. Turned on Bid/Ask ladder by default.
- BUG FIX: Fixed issue that was causing an internal error with bid and ask ladders.

## v0.3.4
- BUG FIX: Fixed issue that was causing the ladder to suddenly stop rendering some columns.

## v0.3.3
- Added ability to view the Largest Trades at Prices in Sliding Window (SHIFT + Left Click)
- Added ability to view the Last Trade at Prices in Sliding Window (CTRL + Left Click)
- Added Bid/Ask volume histogram

## v0.3.2
- Added column name row at top of ladder
- Added additional configuration parameters for column colors
- Added three options (IMBALANCE, BUY_SELL, COMBINED) to calculate orderflow strength bar values
- Turned off orderflow strength bar by default
