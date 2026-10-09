# Upstream issues (dotnet/maui), drafts

Issues found while building the Sailfish backend that only MAUI itself can fix. Each folder has the issue text
(`ISSUE.md`, ready to paste into a GitHub issue) and a self-contained repro (`repro/`, plain `net11.0`,
`dotnet run`, no device or Sailfish package needed). Not filed yet. Once filed, add the link here and in
`docs/maui11-tracker.md` (S45).

**Why they matter for Sailfish, although the head is `net11.0-sailfish`:** MAUI's packages have no assets for our
TFM, so NuGet resolves `lib/net11.0`. The Sailfish head runs exactly the platform-neutral `Microsoft.Maui.dll` /
`Microsoft.Maui.Controls.dll` the repros use (see `obj/project.assets.json` of any Sailfish app). Both bugs hit every
Sailfish app on the phone: `HideSoftInputAsync` throws on every `Entry`, and `Loaded` on the root page and on pages
the app constructs fires with no handler. Until they are fixed upstream the backend works around them
(`SailfishKeyboard`; the porting guide points to `OnAppearing` / `HandlerChanged`).

| # | Issue | Plan / tracker | Filed |
|---|---|---|---|
| 01 | [`SoftInputExtensions` throw `NotSupportedException` on the plain TFM](01-soft-input-throws-on-plain-tfm/ISSUE.md) | M12 / S45 | — |
| 02 | [`Loaded` fires before any handler can exist on the plain TFM](02-loaded-before-handler-on-plain-tfm/ISSUE.md) | M25 / S45 | — |
