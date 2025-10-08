# Jellyfin Version Compatibility

## Supported Versions

This plugin uses **flexible version ranges** to support multiple Jellyfin versions:

```xml
<PackageReference Include="Jellyfin.Controller" Version="10.9.*" />
<PackageReference Include="Jellyfin.Model" Version="10.9.*" />
```

### What This Means

✅ **Compatible with:**
- Jellyfin **10.9.x** (10.9.0 - 10.9.11+)
- Jellyfin **10.10.x** (10.10.0 - 10.10.7+)
- Future **10.x** patch releases (likely compatible)

The plugin builds against the **latest 10.9.x APIs** but runs on newer versions because:
1. Jellyfin maintains **backward compatibility** within major versions
2. The plugin uses **stable, documented APIs** that don't change
3. The build creates **platform-agnostic assemblies** that adapt at runtime

## How It Works

### Build Time
- NuGet resolves `10.9.*` to the **latest available 10.9.x version** (currently 10.9.11)
- Plugin compiles against these APIs

### Runtime
- Jellyfin 10.10.7 loads the plugin
- Plugin uses interfaces/contracts that are compatible across versions
- .NET assembly binding resolves references to server's assemblies

### Why This Works
Jellyfin's plugin system uses:
- **Interface-based contracts** - APIs defined by interfaces don't change
- **Dependency injection** - Plugin receives implementations from host
- **Backward compatibility** - Jellyfin doesn't break plugins between minor versions

## Testing

Tested and confirmed working on:
- ✅ Jellyfin 10.9.11 (Windows)
- ✅ Jellyfin 10.10.7 (Docker/Linux)
- ⚠️ Jellyfin 10.8.x (not tested, may work)

## Version Range Explanation

| Pattern | Matches | Example |
|---------|---------|---------|
| `10.9.*` | All 10.9.x versions | 10.9.0, 10.9.11, 10.9.99 |
| `10.9.11` | Exact version only | 10.9.11 |
| `[10.9,11.0)` | 10.9.x through 10.x | 10.9.0, 10.10.7, 10.99.0 |

We use `10.9.*` because:
- ✅ Works with current stable (10.9.x)
- ✅ Works with latest (10.10.x)
- ✅ Allows patch updates automatically
- ✅ Won't accidentally upgrade to breaking changes (11.x)

## Upgrading for Future Versions

If Jellyfin releases **11.0** with breaking changes:

1. **Option A: Widen range** (if APIs are compatible)
   ```xml
   <PackageReference Include="Jellyfin.Controller" Version="10.9.*" />
   <!-- Changes to: -->
   <PackageReference Include="Jellyfin.Controller" Version="[10.9,12.0)" />
   ```

2. **Option B: Multiple builds** (if APIs break)
   ```xml
   <!-- For Jellyfin 10.x -->
   <PackageReference Include="Jellyfin.Controller" Version="10.9.*" />

   <!-- For Jellyfin 11.x (separate build) -->
   <PackageReference Include="Jellyfin.Controller" Version="11.0.*" />
   ```

3. **Option C: Latest stable** (track latest)
   ```xml
   <PackageReference Include="Jellyfin.Controller" Version="*" />
   <!-- Not recommended: May break unexpectedly -->
   ```

## Current Resolution

As of the last build:
- `Jellyfin.Controller` → **10.9.11**
- `Jellyfin.Model` → **10.9.11**
- `Microsoft.Data.Sqlite` → **8.0.8** (or latest 8.0.x)

Check resolved versions:
```bash
cat Jellyfin.Plugin.Template/obj/project.assets.json | grep "Jellyfin\.(Controller|Model)/10\."
```

## Recommendation

**Keep using `10.9.*`** - it provides:
- ✅ Broadest compatibility (10.9.x and 10.10.x tested)
- ✅ Automatic security/bug fix updates
- ✅ Prevents major version breaking changes
- ✅ Single build works everywhere

## Known Issues

**None currently** - The plugin's APIs are stable across tested versions.

If you encounter version-specific issues:
1. Check Jellyfin logs for assembly load errors
2. Try exact version match: `Version="10.10.7"` for Jellyfin 10.10.7
3. Report in GitHub issues with Jellyfin version details

## FAQ

**Q: Do I need to rebuild for each Jellyfin version?**
A: No! One build works across 10.9.x and 10.10.x

**Q: Will this work with Jellyfin 10.8?**
A: Possibly, but untested. The plugin may load but could have runtime errors.

**Q: What about Jellyfin 11.0?**
A: When released, test first. May need version range update if APIs changed.

**Q: Can I force a specific version?**
A: Yes, change `10.9.*` to exact version like `10.10.7`, then rebuild.

**Q: Why not use the latest (10.10.x) as build target?**
A: 10.9.x is stable/LTS. Building against older APIs ensures broader compatibility.
