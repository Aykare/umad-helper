> ⚠️ **Don't click Fork!**
> 
> This is a GitHub Template repo. If you want to use this for a plugin, [use this template][new-repo] to make a new repo!
>
# SamplePlugin

[![Use This Template badge](https://img.shields.io/badge/Use%20This%20Template-0?logo=github&labelColor=grey)][new-repo]


Simple example plugin for Dalamud.

This is not designed to be the simplest possible example, but it is also not designed to cover everything you might want to do. For more detailed questions, come ask in [the Discord](https://discord.gg/holdshift).

## Current helper: P2 Forsaken

In Sigmascape V4.0, the main window is a compact memory aid: a four-pair Alpha/Beta tower timeline (**AA | AB | BB | BA**) highlights the active tower, followed by an Odd/Even/Past-Future phase row. Below it are the local Alpha/Beta icon and current marker image. Past's End or Future's End remains visible through the even-tower hit and clears on the next odd-tower hit. The Odd phase follows the clones' All Things Ending cast. After the eighth tower, once the last All Things Ending cast has finished, the window returns to "No active mechanic." and the run is saved to History. Your buddy is resolved automatically from the raid positions set in the settings (the buddy pairs are fixed: MT-H1, OT-H2, M1-R1, M2-R2); different initial markers mean Alpha and matching markers mean Beta.

The main window only shows mechanic information while a mechanic is active and returns to an idle message when Forsaken ends, wipes, or you leave the zone. The raid positions (MT, OT, H1, H2, M1, M2, R1, R2; supports take the left side, dps the right) and **Preview** live in the settings window. Positions are saved by character name and start with Celia Nyrell R1, Luna Tancredi H2, M'iriam Yukikazu M1, Nimral Lebanel OT, Ranko Mizuki M2, Cian Avino H1, Vin Enryu R2 and Selene Celeste MT. The Forsaken history shows each player's position and side in the arena tooltips.

**History** has one tab per mechanic. The Forsaken tab draws the arena at the start and at each of the eight towers: the towers that went off (blue circles, radius 4 assumed), who soaked them, and every player as a job icon with the marker they were holding in the corner. The arena is turned for every tower so the towers are always at the bottom (the strategy spots are unaffected). The tower tabs (T1-T8 and Bait) sit in a table whose columns line up with the marker table below the arena; the marker table shows the marker each player held during each tower (gray when that player is not in a tower that round), lists buddies next to each other, joins them with a bracket labelled Alpha or Beta and tints the pair. The window is the only thing that scrolls. Each layer has its own toggle and its own position dots, marking where a player stood when that mechanic hit: **Tower damage** (blue = hit by Path of Light), **Clones** (purple = hit by a clone's bait circle or All Things Ending, plus the clone squares), **Stack** (green), **Spread** (orange) and **Cone** (yellow; one player can have several, and the cone's origin gets a dot too). Players a mechanic did not hit get a gray dot at their position at that moment. Hovering a dot names the player and what the position marks. A further toggle hides the job icons and head markers; a gray head marker means that player's stack, spread or cone did not go off (every player not hit by Path of Light that round, and soakers whose attack was not recorded). In odd towers the Clones layer also shows, per clone, a purple dot with a dashed line to it for where its baiter stood when the clone started casting All Things Ending and fixed its half-room cleave. Runs recorded before the per-layer snapshots existed fall back to the tower-time position for gray dots. The **Mistakes** toggle compares every player's position when the towers hit with their strategy spot (odd/even set, tower side, held marker, or raid position for players not in a tower; spots are the averages of 28 logged runs, expressed relative to the round's towers so rotation and mirroring do not matter). Anyone further away than the tolerance (2.5 yalms for free positions, 3 for tower cone/stack, 4.5 for spreads) gets a red dot where they should have stood, a red arrow from where they actually stood to it (hover the dot for the player's name), and a red line in the list between the arena and the marker table; the run picker counts them. Players without a raid position are skipped when they were not in a tower. A player who had to soak but stood in no tower (an overshoot, for example) is judged against the tower spot they missed, not as a free player: one group of four soaks sets 1 to 3 and 8 and the other sets 4 to 7 (this held in all 37 saved runs), and the tower that still lacks the player's marker is the one they belonged in. The odd sets from T3 on also check the Past's End / Future's End bait: where everyone stood when the clones started casting All Things Ending must be at least max melee out, between the coming towers for Past or on the opposite side for Future (the type is read from the previous even set's bait circles). A **Bait** tab after T8 shows the last All Things Ending, which is always baited toward the waymark that was between the set 8 towers; the tracker saves it as a final pseudo-round, and runs recorded earlier were backfilled from the pull logs. Runs recorded before positions were captured only show the marker table. Runs are saved one JSON file per run in `history\<mechanic>\` inside the plugin's config directory (`forsaken`, `limit-cut`), so they survive closing the game and nothing is ever pruned.

**Preview** plays a scripted Alpha or Beta run based on a real pull, uses a fixed Past's End cast, and lets you step through the tower and clone-lock events. It does not modify live encounter tracking. See the [Kroxy-Rinon guide](https://wtfdig.info/ultimates/umad#kefkabin) for the reference strategy.

## Current helper: P3 Limit Cut

The Kefka clones preview their attacks one by one (Ultima Blaster, action 47843). The first clone's compass direction is the **origin** waymark (markers run clockwise from north: A, 2, B, 3, C, 4, D, 1) and the second clone shows the **rotation**. Each player then gets a numbered head marker (IDs 336-339 are 1-4, 437-440 are 5-8). The origin picks a colour group (D/4 purple, A/1 red, B/2 yellow, C/3 blue) and, with the rotation and your number, the table in `LC-strat` gives your final position, shown as the two waymarks that border it. The history tab lists every player's number and position. The detection and table were checked against all nine logged pulls (72 of 72 players ended at the predicted position).

## Current helper: P4 Kefka Says

The helper reads every party member's status list each frame and, from the first Grand Cross debuff, records each player's debuffs (Compressed Water, Forked Lightning, Cursed Shriek, Acceleration Bomb, Dynamic Fluid, Entropy, Allagan Field, Beyond Death and the Flood wounds) together with the Real/Fake tell of Neo Exdeath or Chaos that was showing when it was handed out. Real/Fake is converted into the task: Water real = stack, Lightning real = spread, Shriek real = stand out (look away), Bomb real = stand still, Entropy real = point-blank, Dynamic Fluid real = donut. Two Fakes cancel into a Real. Kefka's own head markers drive the Mana mechanics (675 fake ice, 676 real ice, 677 fake thunder, 678 real thunder): the first Mana Charge is thunder and the second is ice, and each banks the value of the marker that appears with its Charged status. Mana Release then shows an ice and a thunder marker, and each of those is combined with the bank of the same element (two Fakes make a Real) to give its final value; ice is shown as Stack/Spread like Compressed Water and thunder like Forked Lightning. The Tsunami is just its own Dynamic Fluid value. Flood of Naught uses the wound colour as it was handed out (the later Antilight hit swaps the wound, so only the first one counts): must-die holders go to their own colour, the others to the opposite one.

The main window shows your own debuffs at the top, first as a strip of status icons with a green (real) or red (fake) border and the remaining time underneath, then as a table of debuff icon, real/fake mark (check or cross) and timer; hover an icon for its name. Below that are the six group values (both gaze rounds, Inferno, Tsunami, and the final Mana Release ice and thunder values), your own tasks with countdowns, and the 12-step resolve order with finished steps dimmed and the next one highlighted. The history tab has one button per step; each shows the arena (same size as the Forsaken and Limit Cut tabs) with the waymarks and every player at their position when the step resolved, ring-coloured by real/fake, plus tables of all debuffs and the raw boss tells and Kefka head markers. Step 1 (Flood of Naught) is drawn in more detail: a line states whether the cast was Real or Fake, the half-room areas of White Antilight (purple) and Black Antilight (blue) are shaded, and each player is a position dot (green = stood on the correct side, red = wrong side, gray = unknown) joined by a line to their job icon, with their Flood statuses as they were applied (the wound status the game handed out, and Allagan Field or Beyond Death, using the game's own status icons and names) beside it; hover a dot for the player, their wound and the half they should have been in. The areas need both Antilight casters, which are only recorded for runs made with this version (older runs were backfilled from the pull logs where a log existed). The two Stack / Spread steps (3 and 9) draw the real area around each holder, blue for a stack and purple for a spread (the radii are estimates, the logs do not contain them); supports are expected to stack on A and spread on D, DPS to stack on C and spread on B, and a holder more than 5 yalms off their mark gets a red arrow to it. Saved under `kefka-says`.

Not verified in game: the live recording, the Ice Charged status ID (1484), and that the Mana Release ice and thunder values are the tasks of Compressed Water and Forked Lightning (the markers and their combination with the charges follow your description and match the saved pulls). Boss detection by name assumes an English client. The logs never reach the second half, so Long gazes, Tsunami and Mana Release timings come from the first pulls' extrapolation.

## History: the Decisive Battle

Chaos (action 49890) and Exdeath (49891) cast the Decisive Battle together and every player gets Epic Hero (status 4192, alpha) or Fated Hero (4194, beta). The history tab keeps one entry per pull with an arena view and a table per cast. The arena (same size as the Forsaken tab) shows Chaos (C) and Exdeath (E) where they stood and every player as a position dot with their job icon, joined by a tether to the boss of their status (orange = Epic Hero / Chaos, blue = Fated Hero / Exdeath); hover a player for the status and both distances. The tethers are drawn by that rule, not read from the game; in 479 of 488 logged players the status matched the nearer boss. The table lists player, distance to Chaos, distance to Exdeath (flat yalms, measured when the status appeared) and the status. The game exposes no status-application packet, so the row order is the order in which a frame-by-frame poll saw the statuses; all eight arrive in the same frame and are then listed in party list order. Saved under `decisive-battle`.


## Main Points

* P2 Forsaken marker display with in-game image preview.
* Project organization
  * Copies all necessary plugin files to the output directory
    * Does not copy dependencies that are provided by dalamud
    * Output directory can be zipped directly and have exactly what is required
  * Hides data files from visual studio to reduce clutter
    * Also allows having data files in different paths than VS would usually allow if done in the IDE directly


The intention is less that any of this is used directly in other projects, and more to show how similar things can be done.

## How To Use

### Getting Started

To begin, [clone this template repository][new-repo] to your own GitHub account. This will automatically bring in everything you need to get a jumpstart on development. You do not need to fork this repository unless you intend to contribute modifications to it.

Be sure to also check out the [Dalamud Developer Docs][dalamud-docs] for helpful information about building your own plugin. The Developer Docs includes helpful information about all sorts of things, including [how to submit][submit] your newly-created plugin to the official repository. Assuming you use this template repository, the provided project build configuration and license are already chosen to make everything a breeze.

[new-repo]: https://github.com/new?template_name=SamplePlugin&template_owner=goatcorp
[dalamud-docs]: https://dalamud.dev
[submit]: https://dalamud.dev/plugin-publishing/submission

### Prerequisites

SamplePlugin assumes all the following prerequisites are met:

* XIVLauncher, FINAL FANTASY XIV, and Dalamud have all been installed and the game has been run with Dalamud at least once.
* XIVLauncher is installed to its default directories and configurations.
  * If a custom path is required for Dalamud's dev directory, it must be set with the `DALAMUD_HOME` environment variable.
* A .NET Core 8 SDK has been installed and configured, or is otherwise available. (In most cases, the IDE will take care of this.)

### Building

1. Open up `SamplePlugin.sln` in your C# editor of choice (likely [Visual Studio](https://visualstudio.microsoft.com) or [JetBrains Rider](https://www.jetbrains.com/rider/)).
2. Build the solution. By default, this will build a `Debug` build, but you can switch to `Release` in your IDE.
3. The resulting plugin can be found at `SamplePlugin/bin/x64/Debug/SamplePlugin.dll` (or `Release` if appropriate.)

### Activating in-game

1. Launch the game and use `/xlsettings` in chat or `xlsettings` in the Dalamud Console to open up the Dalamud settings.
    * In here, go to `Experimental`, and add the full path to the `SamplePlugin.dll` to the list of Dev Plugin Locations.
2. Next, use `/xlplugins` (chat) or `xlplugins` (console) to open up the Plugin Installer.
    * In here, go to `Dev Tools > Installed Dev Plugins`, and the `SamplePlugin` should be visible. Enable it.
3. You should now be able to use `/pmycommand` (chat) or `pmycommand` (console)!

Note that you only need to add it to the Dev Plugin Locations once (Step 1); it is preserved afterwards. You can disable, enable, or load your plugin on startup through the Plugin Installer.

### Reconfiguring for your own uses

Replace all references to `SamplePlugin` in all the files and filenames with your desired name, then start building the plugin of your dreams. You'll figure it out 😁

Dalamud will load the JSON file (by default, `SamplePlugin/SamplePlugin.json`) next to your DLL and use it for metadata, including the description for your plugin in the Plugin Installer. Make sure to update this with information relevant to _your_ plugin!

All participation in this repository is governed by our [Code of Conduct](https://dalamud.dev/code-of-conduct). If you used AI tooling at any point, review the [AI Usage Policy](https://dalamud.dev/plugin-publishing/ai-policy) and disclose your level of AI use. Entirely AI-generated submissions will be rejected, and undisclosed AI use may result in a ban.
