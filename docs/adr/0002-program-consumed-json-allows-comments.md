# Program-consumed JSON allows comments

JSON that the programs read may contain comments: `findjobhelper.config.json`,
everything under `data/` (including `data/*/metadata.json`), and
`master-config.json`. `global.json` stays strict JSON. Comments in
`data/*/metadata.json` must survive WebUI state changes: the WebUI accepts
comments when reading it and preserves them when rewriting its state fields.
Both `.editorconfig` files (this repo and the template) document the
convention, and VS Code gets JSONC file associations for `data/**/*.json`,
`master-config.json`, and `findjobhelper.config.json` so editors highlight
comments correctly; the template `.editorconfig` gets the same treatment.
