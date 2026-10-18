# Tools

Build, packaging, release, data-import and trace tooling. A package is what players install, so its scope is declared, never inferred.

## Packaging

- Public archives use an explicit allowlist of supported plugins, and package creation fails when an unexpected DLL appears. Never infer package scope from the projects that build or exist on `main`.
- Archives stay portable: relative forward-slash paths, no rooted entries, and only the expected `BepInEx/plugins/` destinations.
- Release rehearsals write only under `artifacts/`, with SHA-256 checksums generated and verified.

## Release verification

- Run the complete validation pipeline before packaging.
- Inspect the raw archive entries and list the exact plugin DLLs included.
- Check package versions against the project files and `PluginIds`.
- Confirm the archive was built from the intended commit with a clean tracked working tree.
- Building a package and publishing it are separate actions. Building and inspecting local artifacts needs no go-ahead; publishing, replacing or deleting a release always needs the user's.

## Replacing a tool

When a tool's job moves to a new tool, delete the old one in the same change, so a maintainer can never run the wrong one.
