# Rooms and looks: the content workflow

The database is the truth for rooms, looks, recipes, overlays and prop designs. Git holds the changes for review.

## Changing content

1. Edit `src/Pspsps.Application/Rooms/ContentDrafts.json` (the shape of the admin export) in a pull request. CI checks
   that its rooms form a valid book with the starting rooms, that every look can be drawn, and that prop designs are valid.
2. After the merge deploys, load it: the admin page's **Load the reviewed content as drafts**, or the workflow below.
   Rooms become drafts, never live; new looks, recipes and prop designs are saved; a new overlay is saved (overlays go
   live in their own time window). Anything that differs from what is live is reported as a problem and left alone.
3. Review the drafts in the editor, then publish (now, or scheduled).

## Without a browser: the Content workflow

`gh workflow run content.yml -f action=list|export|import|publish` (Actions tab, "Content"). It calls `/api/content`:

| action | does | needs |
| --- | --- | --- |
| `list` | the rooms and which have a draft | |
| `export` | the live content as `export.json`, kept as an artifact for 30 days | |
| `import` | loads the deployed image's `ContentDrafts.json` as drafts; fails if it reports problems | the change merged and deployed |
| `publish` | publishes a room's draft | `room`; `with` (rooms that go live together, e.g. a new room and the doors to it); `at` (ISO time, to schedule) |

### Setup (once)

- The game's Secret (`existingSecret`) gets `Content__ApiToken` (a random string of 32+ characters) and `Content__ActAs`
  (the player ID of an admin, on the admin list: the room audit trail shows that player). Without both, `/api/content`
  answers 404.
- The repository gets the secret `CONTENT_TOKEN` (the same token) and the variable `GAME_URL` (`https://host`).

The token reaches only `/api/content/{export,rooms,import,rooms/{id}/publish}`: not moderation, bans or players.
It carries no cookie, so the same-site check the admin page's changes need does not apply to it.
