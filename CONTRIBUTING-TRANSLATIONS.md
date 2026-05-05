# Contributing translations

Thanks for helping localize Rust+ Desktop. The UI strings live in JSON files under `RustPlusDesktop/Languages/`, one file per language. Translators do not need to touch any C# or XAML.

## Add or improve a language

1. Pick a language code. Use the same code that ships in your locale (`ru`, `es`, `zh`, `pt`, `fr`, `de`, etc.). Two-letter ISO 639-1 codes are preferred.
2. Open `RustPlusDesktop/Languages/<code>.json`. If the file does not exist, copy `en.json` and rename it.
3. Update the `__meta` block so the language picker shows the right names:
   ```json
   "__meta": {
     "code": "es",
     "display_name": "Español",
     "english_name": "Spanish",
     "completion": "100%"
   }
   ```
4. Translate every key whose value still starts with `@@TODO`. Keys themselves never change.
5. Save the file as UTF-8 with no BOM.

## Things to watch for

- **Placeholders.** Strings like `"Tracking: {0}"` use `{0}`, `{1}`, etc. Keep these tokens exactly. Their order can change, but every placeholder that exists in English must exist in the translation.
- **Newlines.** `\n` in a value renders as a real line break. Preserve them where the layout depends on it (especially in confirmation dialogs).
- **Length.** WPF buttons and tooltips have fixed widths. If a translation is much longer than English, the tester should re-check that nothing clips. Shorter translations are fine.
- **Keep the JSON structure.** No nested objects, no comments, no trailing commas.

## Validate before opening a PR

From the repo root:

```bash
python3 RustPlusDesktop/Tools/validate-languages.py
```

The script reports, per language file:
- missing keys (present in `en.json`, absent in your file)
- extra keys (present in your file, not in `en.json`)
- untranslated values (still start with `@@TODO`)
- placeholder mismatches (e.g. English uses `{0}` but the translation dropped it)

The script exits 0 when there are no structural problems. `@@TODO` entries are reported but do not fail the run — you can ship a partially-translated language file and it will fall back to English for any untranslated key.

For a stricter check that fails on untranslated keys:

```bash
python3 RustPlusDesktop/Tools/validate-languages.py --strict
```

## Adding new strings (developers only)

When the app gains a new user-facing string:

1. Add the key to `en.json` (the reference file).
2. Use it in XAML via `{l:Loc some.key}` or in C# via `Loc.T("some.key")`.
3. Run `validate-languages.py` — it will list the new key as missing in every other language. Translators can pick it up from there.
4. To find strings that have not yet been migrated, run `python3 RustPlusDesktop/Tools/extract-strings.py`. This scans XAML and C# for hardcoded literals and prints candidates.

## How language switching works at runtime

`LocalizationManager` (in `RustPlusDesktop/Localization/`) is a singleton that loads the user's chosen language from `Languages/<code>.json` next to the executable. The chosen code is persisted alongside other settings. Switching the language at runtime updates all bound XAML elements without restarting the app: the `LocExtension` markup binds to a property indexer on `LocalizationManager`, and `PropertyChanged("Item[]")` is raised on every language change so WPF refreshes every binding.

If a key is missing from the active language file, the manager falls back to `en.json`. If it is missing from both, the manager renders the key surrounded by brackets (`[some.key]`) so it is obvious during development.
