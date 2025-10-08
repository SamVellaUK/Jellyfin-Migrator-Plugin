# Docker Installation Guide

## Quick Install (Easiest)

### ⚠️ Important: LinuxServer.io Docker Image Issue

The LinuxServer.io Jellyfin image has **plugin loading issues** with manual file copies. Use **Option 3** (Web UI) or follow this workaround:

### Option 1: Install via Web UI (RECOMMENDED for Docker)

1. **Build and package**:
   ```powershell
   ./build.ps1
   # Create a ZIP
   Compress-Archive -Path ./publish/* -DestinationPath migrator-plugin.zip
   ```

2. **In Jellyfin Web UI**:
   - Go to **Dashboard** → **Plugins** → **Repositories**
   - Add this repository: `https://your-repo-url/manifest.json`
   - Or use "Install from ZIP" if available in your version

3. **Restart Jellyfin**

### Option 2: Copy into Running Container (May Not Work on LinuxServer Image!)

1. **Build the plugin**:
   ```powershell
   ./build.ps1
   ```

2. **Copy to correct location**:
   ```bash
   # Create versioned folder
   docker exec jellyfin mkdir -p /config/data/plugins/Migrator_1.0.0.0

   # Copy DLLs
   docker cp ./publish/Jellyfin.Plugin.Template.dll jellyfin:/config/data/plugins/Migrator_1.0.0.0/
   docker cp ./publish/Microsoft.Data.Sqlite.dll jellyfin:/config/data/plugins/Migrator_1.0.0.0/
   docker cp ./publish/SQLitePCLRaw.batteries_v2.dll jellyfin:/config/data/plugins/Migrator_1.0.0.0/
   docker cp ./publish/SQLitePCLRaw.core.dll jellyfin:/config/data/plugins/Migrator_1.0.0.0/
   docker cp ./publish/SQLitePCLRaw.provider.e_sqlite3.dll jellyfin:/config/data/plugins/Migrator_1.0.0.0/
   ```

3. **Restart**:
   ```bash
   docker restart jellyfin
   ```

**Note:** LinuxServer Jellyfin may ignore manually copied plugins. If this doesn't work, use Option 3.

### Option 2: Volume Mount (Persistent)

If you want the plugin to persist across container recreations:

1. **Build the plugin**:
   ```bash
   ./build.ps1
   ```

2. **Copy to your Jellyfin config directory** (the one mounted to `/config`):
   ```bash
   # Find your mounted config directory
   docker inspect <container-name> | grep -A 5 "Mounts"

   # Example: if /path/to/jellyfin/config is mounted to /config
   cp -r ./publish/* /path/to/jellyfin/config/plugins/Migrator/
   ```

3. **Restart container**:
   ```bash
   docker restart <container-name>
   ```

### Option 3: Docker Compose (Best for Permanent Setup)

If using `docker-compose.yml`:

1. **Build the plugin**:
   ```bash
   ./build.ps1
   ```

2. **Update your docker-compose.yml** to mount plugins folder:
   ```yaml
   services:
     jellyfin:
       image: jellyfin/jellyfin
       volumes:
         - /path/to/config:/config
         - /path/to/cache:/cache
         - ./jellyfin-plugins:/config/plugins  # Add this line
   ```

3. **Copy plugin to host plugins folder**:
   ```bash
   mkdir -p ./jellyfin-plugins/Migrator
   cp -r ./publish/* ./jellyfin-plugins/Migrator/
   ```

4. **Restart**:
   ```bash
   docker-compose restart jellyfin
   ```

## Docker Container Paths

Jellyfin Docker containers typically use these paths:

| Description | Container Path | Typical Host Mount |
|-------------|---------------|-------------------|
| Config (includes plugins) | `/config` | `/path/to/jellyfin/config` |
| Plugins specifically | `/config/plugins` | `/path/to/jellyfin/config/plugins` |
| Cache | `/cache` | `/path/to/jellyfin/cache` |

## Step-by-Step Example

### Using Official Jellyfin Docker Image

```bash
# 1. Build plugin on your Windows/Linux machine
cd /path/to/Jellyfin-Migrator-Plugin
pwsh ./build.ps1

# 2. Find your container name
docker ps | grep jellyfin

# 3. Create plugin directory in container
docker exec jellyfin mkdir -p /config/plugins/Migrator

# 4. Copy plugin files
docker cp ./publish/. jellyfin:/config/plugins/Migrator/

# 5. Verify files copied
docker exec jellyfin ls -la /config/plugins/Migrator/

# 6. Restart Jellyfin
docker restart jellyfin

# 7. Check logs
docker logs -f jellyfin
```

## Windows PowerShell Example

```powershell
# 1. Build
./build.ps1

# 2. Copy to Docker container (replace 'jellyfin' with your container name)
docker cp ./publish/. jellyfin:/config/plugins/Migrator/

# 3. Restart
docker restart jellyfin
```

## Verification

1. **Check files in container**:
   ```bash
   docker exec jellyfin ls -la /config/plugins/Migrator/
   ```

   You should see:
   - `Jellyfin.Plugin.Template.dll`
   - `Microsoft.Data.Sqlite.dll`
   - `SQLitePCLRaw.*.dll`

2. **Check Jellyfin logs**:
   ```bash
   docker logs jellyfin | grep -i migrator
   ```

3. **Check in Web UI**:
   - Open Jellyfin web interface
   - Go to **Dashboard** → **Plugins**
   - Look for **"Jellyfin Migrator Plugin"**

## Troubleshooting

### Plugin Not Loading

**Check permissions**:
```bash
docker exec jellyfin ls -la /config/plugins/Migrator/
docker exec jellyfin chown -R jellyfin:jellyfin /config/plugins/Migrator/
docker restart jellyfin
```

### Can't Find Container

**List all containers**:
```bash
docker ps -a
docker ps -a | grep jellyfin
```

### Files Not Persisting

Your `/config` needs to be mounted to a host volume:
```bash
# Check mounts
docker inspect <container-name> | grep -A 10 "Mounts"

# If not mounted, recreate container with volume
docker run -d \
  --name jellyfin \
  -v /path/to/config:/config \
  -v /path/to/cache:/cache \
  jellyfin/jellyfin
```

### Permission Denied in Container

```bash
# Fix ownership
docker exec jellyfin chown -R jellyfin:jellyfin /config/plugins

# Or run as root temporarily
docker exec -u root jellyfin chown -R jellyfin:jellyfin /config/plugins
```

## Alternative: Build Docker Image with Plugin

Create a custom Dockerfile:

```dockerfile
FROM jellyfin/jellyfin:latest

# Copy plugin files
COPY ./publish /config/plugins/Migrator/

# Set ownership
RUN chown -R jellyfin:jellyfin /config/plugins/Migrator/
```

Build and run:
```bash
docker build -t jellyfin-with-migrator .
docker run -d --name jellyfin jellyfin-with-migrator
```

## Quick Reference

| Task | Command |
|------|---------|
| Build plugin | `./build.ps1` or `pwsh ./build.ps1` |
| Copy to container | `docker cp ./publish/. <container>:/config/plugins/Migrator/` |
| Restart container | `docker restart <container>` |
| View logs | `docker logs -f <container>` |
| Check files | `docker exec <container> ls -la /config/plugins/Migrator/` |
| Fix permissions | `docker exec <container> chown -R jellyfin:jellyfin /config/plugins` |

## One-Line Install Script

**Linux/macOS:**
```bash
pwsh ./build.ps1 && docker cp ./publish/. jellyfin:/config/plugins/Migrator/ && docker restart jellyfin
```

**Windows PowerShell:**
```powershell
./build.ps1; docker cp ./publish/. jellyfin:/config/plugins/Migrator/; docker restart jellyfin
```

---

**That's it!** The plugin is now installed in your Dockerized Jellyfin instance.
