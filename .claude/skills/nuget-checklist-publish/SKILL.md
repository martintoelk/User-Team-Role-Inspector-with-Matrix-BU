---
name: nuget-checklist-publish
description: Validate the XrmToolBox Tool Library submission checklist against this repo, then publish a new version to NuGet.org.
disable-model-invocation: true
---

Validates the checklist the XrmToolBox portal enforces (package config + tool behavior), then
publishes via the `Publish NuGet package` GitHub Actions workflow. Publishing is irreversible
(a version can be unlisted but not deleted) — never skip the confirmation step.

Take a target version as an argument (e.g. `/nuget-checklist-publish 1.0.0`). If none is given,
ask the user for one.

## 1. Static checks (repo source)

Read `UserTeamRoleInspector.nuspec`, `UserTeamRoleInspector/Plugin.cs`,
`UserTeamRoleInspector/UserTeamRoleInspector.csproj`, and
`UserTeamRoleInspector/UserTeamRoleInspectorControl.cs`, and check each:

- **Icon**: `<icon>` element present, and its path uses `/` not `\`. (A `.nupkg` is a zip; zip
  entries always use `/`. A backslash here previously made nuget.org's own portal reject the
  package with "Logo Url is not valid" even though the icon file was correctly embedded — see the
  sibling `Modernized BU Security Role Assigner` repo's version of this skill.)
- **No custom `<iconUrl>`**: don't add one. nuget.org computes its own hosted icon URL from
  `<icon>` once indexed — that's the form the portal accepts. A prior attempt in the sibling repo
  pointed `<iconUrl>` at `raw.githubusercontent.com`; nuget.org used that literal value instead of
  computing its own, and the portal rejected it as invalid.
- **Project URL**: `<projectUrl>` present.
- **Plugins folder**: the plugin DLL's `<file target=...>` sits under `lib/net48/Plugins/`.
- **Large/small tile images**: `Plugin.cs` has non-null `SmallImageBase64` and `BigImageBase64`
  in the `ExportMetadata` attributes. (As of this repo's packaging ticket these are solid-color
  placeholders, not real iconography — that's a separate open question on the map, not something
  this checklist item can catch.)
- **Resize adaptivity**: the main layout container docks `Fill` with percent-sized rows/columns
  (not fixed pixel sizing) — check the `.Designer.cs`.
- **Opens without a connection**: the control's constructor only calls `InitializeComponent()`,
  no `Service` access.
- **Connection-required controls open the connection dialog**: click handlers that need a
  connection call `ExecuteMethod(...)` directly, with no manual `if (Service == null)` short
  -circuit that shows a plain message box instead — `ExecuteMethod` already opens the host's
  connection dialog when disconnected, so a manual check just replaces that with a worse UX and
  fails this checklist item.
- **Async long-running work**: user/assignment-loading handlers wrap their org calls in
  `WorkAsync`, not a direct synchronous call.
- **README present**: `<file src="README.md" target="README.md" />` in the nuspec resolves to an
  actual, non-empty `README.md` at the repo root. If it doesn't exist yet, stop here — this is a
  separate ticket ("Write README.md / end-user usage docs"), not something to draft as a
  side-effect of a pack dry run.

## 2. Version sync (build + inspect, don't trust the source alone)

The nuspec version and the plugin DLL's assembly version must match — the portal checks this
directly against the built artifact, not the csproj.

```
dotnet build "UserTeamRoleInspector/UserTeamRoleInspector.csproj" -c Release -p:Version=<target-version>
```

Then read the built DLL's version with PowerShell:

```powershell
$vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path "UserTeamRoleInspector\bin\Release\UserTeamRoleInspector.dll"))
$vi.FileVersion
```

Confirm it reads `<target-version>.0`. If it doesn't, check `.github/workflows/publish-nuget.yml`
still passes `-p:Version=${{ inputs.version }}` into the `dotnet build` step — that's what makes
the shipped DLL track the package version instead of the csproj's hardcoded default.

## 3. Local pack dry run

```
dotnet pack "UserTeamRoleInspector/UserTeamRoleInspector.csproj" -c Release --no-build -p:NuspecFile=../UserTeamRoleInspector.nuspec -p:NuspecProperties="version=<target-version>-test" -o out
```

Unzip the resulting `.nupkg` into a scratch folder and confirm: the `<icon>` path resolves to an
actual file in the archive (forward slash matches the real zip entry name), and the packaged
`UserTeamRoleInspector.dll` has the same version as step 2. Delete `out/` afterward.

## 4. Report findings, then confirm before publishing

Report pass/fail for every item above. Fix anything failing, re-run steps 1-3, and only once
everything passes: confirm with the user (via a real question, not an assumption) that you
should publish `<target-version>` to NuGet.org — this step is irreversible.

## 5. Publish and verify

```
gh workflow run "Publish NuGet package" -f version=<target-version>
```

Watch the run (`gh run watch <run-id> --exit-status`) and confirm the "Push to NuGet.org" step
logs `Your package was pushed` — not silently skipped. The workflow uses `--skip-duplicate`, so
if `<target-version>` was already published (check `gh run view <run-id> --log | grep -i push`),
nothing actually changes on nuget.org even though the run shows green; bump to the next version
and retry.

nuget.org's flatcontainer index lags a few minutes behind a successful push. Poll it rather than
trusting the workflow's success alone:

```
curl -s "https://api.nuget.org/v3-flatcontainer/userteamroleinspector/index.json"
```

Once `<target-version>` appears, fetch its registration leaf's `catalogEntry` and confirm
`iconFile` shows a forward-slash path. Download the live `.nupkg` from
`https://api.nuget.org/v3-flatcontainer/userteamroleinspector/<target-version>/userteamroleinspector.<target-version>.nupkg`
and re-check the packaged DLL's version, the same way as step 3, against the artifact nuget.org
is actually serving — not just what the workflow built.
