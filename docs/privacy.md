# Privacy: networks

What the server keeps about where players play from, for network bans (architecture.md, "Network bans").

- **What:** for each player, the networks they played from: a keyed hash of each (HMAC-SHA256 with the server's secret
  `Moderation:AddressKey`) and when they were last seen on it. Never the address. An IPv4 address is hashed whole; an
  IPv6 address by its first 64 bits (its /64), so it says no more than the household or phone network. Without the key,
  a hash can't be turned back into an address, nor checked against one.
- **When:** as a player makes a guest, signs in with Discord or Google, or connects to the game.
- **How long:** 7 days after the player was last seen on that network; older ones are deleted every hour. Turning
  network bans off deletes them all within the hour.
- **Bans:** a banned network keeps its hash, the banned player it was banned with, why, by whom and until when (a day or
  a week at most), until it ends or is lifted; then it is deleted too. The admins' audit trail keeps a label for it: the
  first 8 hex digits of the hash.
- **Who sees it:** admins, on a banned player's card and on `/admin`, by label only, with how many players were on each
  network this week. Nobody sees an address.
- **Logs and telemetry:** network bans log no address and no hash (a label at most). Separately, the server's request
  logs carry the client address (the `ClientIp` property on each request's log events) for as long as the deployment
  keeps its logs.
