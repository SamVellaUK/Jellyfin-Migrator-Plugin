# GitHub Release Installation Instructions

## What You Have

You now have two files ready for GitHub Releases:

1. **`jellyfin-migrator-1.0.0.0.zip`** - The plugin package
2. **`manifest.json`** - Plugin repository manifest

## Step-by-Step: Publish to GitHub

### 1. Create GitHub Release

1. Go to your repository: `https://github.com/YOUR-USERNAME/Jellyfin-Migrator-Plugin`
2. Click **Releases** → **Create a new release**
3. **Tag:** `v1.0.0.0` (create new tag)
4. **Title:** `Jellyfin Migrator Plugin v1.0.0.0`
5. **Description:**
   ```
   Cross-platform Jellyfin plugin for exporting user data and migration.

   ## Features
   - Export user data, passwords, and library permissions
   - Works on Windows, Linux, and Docker
   - Compatible with Jellyfin 10.9.x - 10.10.x+

   ## Installation
   See installation instructions below.
   ```

### 2. Upload Files

Drag and drop these files to the release:
- `jellyfin-migrator-1.0.0.0.zip`
- `manifest.json`

### 3. Update manifest.json

**BEFORE publishing**, edit `manifest.json` and replace:

```json
"owner": "YOUR-GITHUB-USERNAME",
```
with your actual GitHub username, e.g.:
```json
"owner": "Sam",
```

Also replace:
```json
"sourceUrl": "https://github.com/YOUR-GITHUB-USERNAME/Jellyfin-Migrator-Plugin/releases/download/v1.0.0.0/jellyfin-migrator-1.0.0.0.zip",
```
with your actual URL.

**The MD5 hash is already filled in:** `cf479a5f4d05aa800d9c92f20a9dd424`

### 4. Publish Release

Click **Publish release**

---

## How Users Install Your Plugin

### Method 1: Via Plugin Repository (Easiest for Users)

Users add your repository to Jellyfin:

1. Jellyfin Web UI → Dashboard → Plugins → Repositories
2. Click **"+ Add Repository"**
3. **Name:** `Migrator Plugin`
4. **URL:** `https://github.com/YOUR-USERNAME/Jellyfin-Migrator-Plugin/releases/download/v1.0.0.0/manifest.json`
5. Save
6. Go to **Catalog** tab
7. Find and install **"Migrator"**
8. Restart Jellyfin

### Method 2: Direct ZIP Download

1. Download `jellyfin-migrator-1.0.0.0.zip` from your GitHub Release
2. Extract ZIP
3. Copy files to Jellyfin plugins directory:

**Windows:**
```powershell
Copy-Item -Path .\* -Destination "C:\ProgramData\Jellyfin\Server\plugins\Migrator_1.0.0.0\" -Recurse
Restart-Service JellyfinServer
```

**Linux:**
```bash
sudo mkdir -p /var/lib/jellyfin/plugins/Migrator_1.0.0.0
sudo cp -r ./* /var/lib/jellyfin/plugins/Migrator_1.0.0.0/
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/Migrator_1.0.0.0
sudo systemctl restart jellyfin
```

**Docker:**
```bash
docker cp ./ jellyfin:/config/plugins/Migrator_1.0.0.0/
docker exec jellyfin chown -R abc:abc /config/plugins/Migrator_1.0.0.0
docker restart jellyfin
```

---

## Alternative: No GitHub Account?

If you don't want to use GitHub, you can use **raw.githubusercontent.com** or any static file host:

### Free Alternatives:
1. **GitHub Gist** - Create a gist with manifest.json
2. **Pastebin** (raw mode)
3. **Your own web server**
4. **Dropbox/Google Drive** (with public sharing)

Just make sure the manifest.json URL is **publicly accessible** and returns **raw JSON** (not HTML).

---

## Testing

After publishing, test the installation:

1. **Add repository** in Jellyfin using the manifest URL
2. **Check catalog** - "Migrator" should appear
3. **Install** and verify it works

---

## Updating the Plugin

When you release a new version (e.g., 1.0.1.0):

1. Build: `./build.ps1`
2. Create ZIP: `Compress-Archive -Path ./publish/* -DestinationPath ./jellyfin-migrator-1.0.1.0.zip`
3. Update `manifest.json` - add new version to the "versions" array (keep old versions!)
4. Create new GitHub Release with tag `v1.0.1.0`
5. Upload both files

Users who added your repository will see the update automatically in their plugin catalog!

---

## Questions?

- **Where do I get a GitHub account?** https://github.com/signup
- **How do I create a repository?** Click "+" in GitHub → New repository
- **Can I use a private repository?** No, releases must be public for Jellyfin to access them

---

Good luck! 🎉
