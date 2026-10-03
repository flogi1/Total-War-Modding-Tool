# Total War Modding Tool

A tool that makes modding Total War games easier.

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