- v1.8
  - Fixes issue that assigned object ownership too eagerly (possibly causing all kind of weird issues).
  - Fixes vanilla issue that left stale ownership data (now cleaned up periodically).

- v1.7
  - Adds terrain to always loaded objects (possibly fixing relog issue on dungeons and other issues).

- v1.6
  - Fixed for the new game version.

- v1.5
  - Possibly fixes dungeon entering issue.

- v1.4
  - Removes log spam of "Any nearby".

- v1.3
  - Delays unloading of objects by 2.5 seconds to prevent desync issues.

- v1.2
  - Adds the player object to the always sent list so that they properly disappear when entering a dungeon.
  - Fixes the issue of ground creatures not working when one of the players is in the dungeon layer.
