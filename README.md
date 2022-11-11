# TraderLadder
Current version: 0.4.1 ([release notes](https://github.com/OrderFlowTools/TraderLadder/blob/main/RELEASE_NOTES.md))

Free SuperDOM Ladder for Order Flow traders on Ninjatrader.

TraderLadder is a free order flow ladder for Ninjatrader v8. The SuperDOM column can display the following:

- Last trades
- Buys / Sells in a configurable sliding time window (defaults to 60 seconds) + Imbalance detection
- Last prints or Largest prints at price in sliding window 
- Session Buys / Sells + Imbalance detection
- Potential ICE execution
- Bid/Ask + Bid/Ask updates
- Bid/Ask histogram 
- Large resting Bid/Ask orders (highlighted)
- Volume histogram
- If in a position, Current P/L + Session P/L + Account cash value
- Notes from an external URL, OR a local file

## Disclaimer
This software is provided for free, including the source code for it. At some point, Ninjatrader will (fingers-crossed) provide an OrderFlow DOM of their own. Until then, hopefully this can help fill in the gap - in the free software domain :)

## Documentation
Please refer to the documentation here: https://github.com/OrderFlowTools/TraderLadder/wiki

## Screenshots
![DarkScreenShot_1](https://github.com/OrderFlowTools/screenshots/blob/main/traderladder/v0.3.3/full.PNG)

![DarkScreenShot_1](https://github.com/OrderFlowTools/screenshots/blob/main/traderladder/v0.3.6/ladder-size-highlight.PNG)

The screenshot below shows the **largest trades** that occurred at each price within the sliding window time period. 
This information can be viewed by using SHIFT + Left-Click (left-click while holding down the Shift Key). To toggle back to the original view, simply use SHIFT + Left-Click again.

![DarkScreenShot_1](https://github.com/OrderFlowTools/screenshots/blob/main/traderladder/v0.3.3/largest-trades.PNG)

The screenshot below shows the **latest trades** that occurred at each price within the sliding window time period. 
This information can be viewed by using CTRL + Left-Click (left-click while holding down the Control/CTRL Key). To toggle back to the original view, simply use CTRL + Left-Click again.

![DarkScreenShot_1](https://github.com/OrderFlowTools/screenshots/blob/main/traderladder/v0.3.3/last-prints.PNG)
