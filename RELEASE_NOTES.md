# Release Notes

## v0.4.3

New Delta column that displays buy/sell delta in the sliding window
New Sliding Volume column that displays developing volume in the sliding window# Release Notes

## v0.4.2
- Fixed the issue with the ladder not working with Level I feeds.

## v0.4.1
- Potential ICE execution (trades that exceed bid/ask sizes). These columns display cumulative executions (within sliding window timeframe) that exceed the corresponding bid and ask sizes.
- Histograms for:
  - Session buys/sells
  - Sliding window buys/sells
  - Highlighting to indicate strength
- Configuration options organized into better grouping

## v0.4.0
- Bid/Ask stacking/pulling is now displayed persistently instead of in a fleeting moment. _With extremely fast changes, only the last update will be visible._
- The Notes column is now able to display notes from either:
   - a local file (ex. D:\temp\data.csv), or
   - a hosted file on a web server (ex. http://myserver.com/files/levels.csv)

## v0.3.9
- BUG FIX: Added a configurable Notes Delimiter to address price formatting (comma instead of dot in prices. Ex. 4120,75 instead of 4120.75). Thanks Hermann! (https://github.com/OrderFlowTools/TraderLadder/issues/16)

## v0.3.8
- Added extended support for full instrument names (ex. ESU22, MNQU22 etc) in the notes CSV (from external URL).

## v0.3.7
- Added a Notes column, fed by an external URL (CSV file over http(s))

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
