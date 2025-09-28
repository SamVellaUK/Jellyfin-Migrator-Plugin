import json
import requests

# --- Configuration ---
# Replace with your Jellyfin server URL
JELLYFIN_URL = "http://localhost:8096"
# Replace with your Jellyfin API key
JELLYFIN_API_KEY = "5346b466a4ec42739071aa10671f22d4"
# --- End of Configuration ---


def extract_simplified_library_info():
    """
    Connects to a Jellyfin instance, retrieves library information,
    and saves a simplified version to a JSON file.
    """
    headers = {
        "Authorization": f'MediaBrowser Token="{JELLYFIN_API_KEY}"',
        "Content-Type": "application/json",
    }

    try:
        # Get the full library information from the Jellyfin API
        print("Fetching library information from Jellyfin...")
        libraries_response = requests.get(f"{JELLYFIN_URL}/Library/VirtualFolders", headers=headers, timeout=10)
        libraries_response.raise_for_status()
        full_libraries_data = libraries_response.json()
        print("Successfully fetched data.")

        # --- New Processing Logic ---
        # Process the full data to extract only the required fields
        simplified_libraries = []
        for library in full_libraries_data:
            simplified_info = {
                "name": library.get("Name"),
                "path": library.get("Locations"),
                "type": library.get("CollectionType"),
                "ItemID": library.get("ItemId")
            }
            simplified_libraries.append(simplified_info)
        # --- End of New Processing Logic ---

        # Write the simplified library information to a new JSON file
        output_filename = "libraries_simplified.json"
        with open(output_filename, "w") as outfile:
            json.dump(simplified_libraries, outfile, indent=4)
        print(f"Successfully wrote simplified library information to {output_filename}")

    except requests.exceptions.RequestException as e:
        print(f"An error occurred: {e}")
    except Exception as e:
        print(f"An unexpected error occurred: {e}")

if __name__ == "__main__":
    extract_simplified_library_info()