import requests
import json
import time

# --- User Configuration ---
# Replace with your Jellyfin server URL
JELLYFIN_URL = "http://localhost:8096"
# Replace with your Jellyfin API key
API_KEY = "6e1552625c1b446b9b6c1eebca7a7aa0"
# Path to your libraries JSON file
LIBRARIES_FILE = "C:\\Users\\Sam\\Documents\\jellyfin-mig-v2\\export-files\\libraries_simplified.json"

SCAN_CHECK_INTERVAL = 15

# --- Path Remapping ---
# The script will replace this Windows path prefix...
WINDOWS_BASE_PATH = "F:\\Media\\"
# ...with this Linux path prefix.
LINUX_BASE_PATH = "/media/"

# --- Script ---

def get_jellyfin_headers():
    """Returns the headers required for Jellyfin API requests."""
    # Note: Content-Type is not strictly needed for this request type,
    # but the API token is essential.
    return {
        "X-Emby-Token": API_KEY,
    }

def convert_path_to_linux(windows_path):
    """Converts a Windows-style file path to a Linux-style path."""
    # Replace the base part of the path
    new_path = windows_path.replace(WINDOWS_BASE_PATH, LINUX_BASE_PATH)
    # Replace any remaining backslashes with forward slashes
    new_path = new_path.replace('\\', '/')
    return new_path

def check_library_scan_completion():
    """
    Periodically checks the Jellyfin server to see if a library scan is running.
    This is done by checking the state of the "Scan Media Library" scheduled task.
    """
    print("Waiting for library scan to complete...")
    while True:
        try:
            response = requests.get(f"{JELLYFIN_URL}/ScheduledTasks", headers=get_jellyfin_headers())
            response.raise_for_status()
            tasks = response.json()

            # Find the library scan task and check if it's running
            is_scanning = False
            for task in tasks:
                if task.get('Name') == "Scan Media Library" and task.get('State') == "Running":
                    is_scanning = True
                    break

            if is_scanning:
                print(f"Library scan is still in progress. Checking again in {SCAN_CHECK_INTERVAL} seconds.")
                time.sleep(SCAN_CHECK_INTERVAL)
            else:
                print("Library scan has completed.")
                # Give a small buffer for Jellyfin to finalize everything
                time.sleep(5)
                return
        except requests.exceptions.RequestException as e:
            print(f"An error occurred while checking scan status: {e}")
            print("Will try again in a moment.")
            time.sleep(SCAN_CHECK_INTERVAL)
        except Exception as e:
            print(f"An unexpected error occurred: {e}")
            break


def create_and_scan_libraries():
    """
    Reads library configurations, converts paths, creates libraries in Jellyfin,
    and scans them sequentially.
    """
    try:
        with open(LIBRARIES_FILE, 'r') as f:
            libraries = json.load(f)
    except FileNotFoundError:
        print(f"Error: The file '{LIBRARIES_FILE}' was not found.")
        return
    except json.JSONDecodeError:
        print(f"Error: Could not decode JSON from the file '{LIBRARIES_FILE}'.")
        return

    for i, library in enumerate(libraries):
        name = library.get("name")
        windows_paths = library.get("path")
        library_type = library.get("type")

        if not all([name, windows_paths, library_type]):
            print(f"Skipping a library due to missing data: {library}")
            continue

        print(f"\nProcessing library: {name}")

        # --- Convert Paths ---
        linux_paths = [convert_path_to_linux(p) for p in windows_paths]
        print(f"  Original Windows path: {windows_paths[0]}")
        print(f"  Converted Linux path:  {linux_paths[0]}")


        # --- **CORRECTED PAYLOAD AS QUERY PARAMETERS** ---
        # Based on the documentation, this endpoint expects URL parameters, not a JSON body.
        # The `requests` library handles lists in params correctly.
        params_payload = {
            'name': name,
            'collectionType': library_type,
            'paths': linux_paths,
            'refreshLibrary': True # Ask Jellyfin to scan the library upon creation
        }

        try:
            # Create the library and trigger a scan
            print(f"Sending request to create and scan library '{name}'...")
            response = requests.post(
                f"{JELLYFIN_URL}/Library/VirtualFolders",
                headers=get_jellyfin_headers(),
                params=params_payload  # Use `params` to send as URL query parameters
            )
            response.raise_for_status()
            print(f"Successfully sent request for library: {name}")

            # Wait for the scan triggered by `refreshLibrary=True` to complete
            check_library_scan_completion()

        except requests.exceptions.RequestException as e:
            print(f"An error occurred while processing library '{name}': {e}")
            if e.response is not None:
                print(f"Response from server ({e.response.status_code}): {e.response.text}")
            continue # Move to the next library on error

    print("\nAll libraries have been processed.")


if __name__ == "__main__":
    create_and_scan_libraries()