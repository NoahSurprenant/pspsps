# Rooms and looks: the content workflow

The database is the truth for rooms, looks, recipes, overlays and prop designs. Git holds the changes for review.

## Changing content

1. Edit `src/Pspsps.Application/Rooms/ContentDrafts.json` (the shape of the admin export) in a pull request. CI checks
   that its rooms form a valid book with the starting rooms, that every look can be drawn, and that prop designs are valid.
2. After the merge deploys, it is loaded as drafts for you: the Content import workflow (`content-import.yml`) runs after each merge to master that changed the file, waits until the game runs that file (`GET /api/content/version` is its SHA-256) and imports it. Load it by hand any time: the admin page's **Load the reviewed content as drafts**, or the workflow below.
   Rooms become drafts, never live; new looks, recipes and prop designs are saved; a new overlay of a published or draft room is saved (an overlay of a room that has a draft is kept with that draft and goes live
   in the same publish as its layout, never before; an overlay of a room with no draft is live at once; overlays show in their own time window); an overlay of a room that is neither published nor a draft is a problem. A look whose room is not published yet is replaced when the file's version differs, so a draft room can keep changing across loads. A look variant, an overlay or a floor-and-wall painter recipe that differs from the live one is replaced by the file's (the file is the source); anything else that differs from what is live is reported as a problem and left alone.
3. Review the drafts in the editor, then publish (now, or scheduled). Publishing is never automatic.

## Without a browser: the Content workflow

`gh workflow run content.yml -f action=list|export|import|publish|retire` (Actions tab, "Content"). It calls `/api/content`:

| action | does | needs |
| --- | --- | --- |
| `list` | the rooms and which have a draft | |
| `export` | the live content as `export.json`, kept as an artifact for 30 days | |
| `import` | loads the deployed image's `ContentDrafts.json` as drafts; fails if it reports problems | the change merged and deployed |
| `publish` | publishes a room's draft | `room`; `with` (rooms that go live together, e.g. a new room and the doors to it); `retire` (live rooms taken out of the game in the same step); `at` (ISO time, to schedule) |
| `retire` | takes a live room out of the game (its version is archived, so it stays in the history) | `room`; `with` (more rooms to retire together); fails if a room that stays has a door to it |

### Setup (once)

- The game's Secret (`existingSecret`) gets `Content__ApiToken` (a random string of 32+ characters) and `Content__ActAs`
  (the player ID of an admin, on the admin list: the room audit trail shows that player). Without both, `/api/content`
  answers 404.
- The repository gets the secret `CONTENT_TOKEN` (the same token). The workflow runs on the in-cluster runner `pspsps-k8s` and calls the game's Service (`http://pspsps.pspsps.svc.cluster.local`), because Cloudflare's challenge answers GitHub-hosted runners with 403. No `GAME_URL` variable is needed.

The token reaches only `/api/content/{export,rooms,version,import,rooms/{id}/publish,rooms/{id}/retire}`: not moderation, bans or players.
It carries no cookie, so the same-site check the admin page's changes need does not apply to it.
