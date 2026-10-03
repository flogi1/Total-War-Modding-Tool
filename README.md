# Total War Modding Tool

A tool that makes modding Total War games easier.

## What it can do now

The app finds your Total War game through Steam (or you choose the game folder in the settings) and lists its packs by origin: the game, mods in the data folder and workshop mods. Expand a pack to see its files, and open a file in a tab: DB tables and loc files as a read-only grid, text files as text, anything else as an info tab. Open tabs come back after a restart. In the settings you can also choose a folder with RPFM schema files.

The app only reads. It never changes the game, the workshop folders or your packs; it writes only its own settings, session and log below `%APPDATA%\TotalWarModTool` and `%LocalAppData%\TotalWarModTool`.

## Commands

- `build.bat` builds the whole solution.
- `test.bat` runs all tests of the solution.
- `run.bat` starts the app.

## Tests on real game data

Some tests read the real game files. They run only when these environment variables are set (in the same shell as `test.bat`); without them they are skipped:

- `TWMT_WH3_DIR`: the Total War WARHAMMER III install folder (the one that contains `data`).
- `TWMT_RPFM_CLI`: the full path of `rpfm_cli.exe`, used by tests that compare against RPFM.

## License

MIT