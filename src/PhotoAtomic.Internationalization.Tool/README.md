# PhotoAtomic.Internationalization.Tool

The CLI companion of
[PhotoAtomic.Internationalization](https://www.nuget.org/packages/PhotoAtomic.Internationalization),
packaged as a dotnet tool. It opens your csproj with MSBuildWorkspace (Razor components
included), extracts the translation catalog from the `T($"...")` call sites, and lets you
manage translations **before** shipping instead of at runtime.

## Installing and running

```
dotnet tool install -g PhotoAtomic.Internationalization.Tool

pai18n MyApp.csproj --csv translations.csv    # extract the catalog and pre-translate
                                              # missing entries (AI-assisted)
pai18n MyApp.csproj --csv translations.csv --verify
                                              # CI-friendly: fails when the catalog has holes
```

The first argument is a csproj (opened via MSBuildWorkspace, Razor included) or a compiled
assembly. Filling uses the same AI translation pipeline as
[PhotoAtomic.Internationalization.AI](https://www.nuget.org/packages/PhotoAtomic.Internationalization.AI)
(configure the provider via appsettings / user secrets / environment variables); `--verify` is
meant for pipelines, so a missing translation breaks the build instead of surprising a user.

Part of [PhotoAtomic.Libraries](https://github.com/PhotoAtomic/PhotoAtomic.Libraries).
