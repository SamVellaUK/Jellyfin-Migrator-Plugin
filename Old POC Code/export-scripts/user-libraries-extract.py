import json
import requests

# --- Configuration ---
# Replace with your Jellyfin server URL
JELLYFIN_URL = "http://localhost:8096"
# Replace with your Jellyfin API key
JELLYFIN_API_KEY = "5346b466a4ec42739071aa10671f22d4"
# --- End of Configuration ---


def get_jellyfin_data(endpoint):
    """
    Fetches data from a specified Jellyfin API endpoint.
    """
    headers = {
        "Authorization": f'MediaBrowser Token="{JELLYFIN_API_KEY}"',
        "Content-Type": "application/json",
    }
    try:
        response = requests.get(f"{JELLYFIN_URL}{endpoint}", headers=headers, timeout=10)
        response.raise_for_status()
        return response.json()
    except requests.exceptions.RequestException as e:
        print(f"An error occurred while fetching data from {endpoint}: {e}")
        return None


def create_user_library_mapping():
    """
    Connects to a Jellyfin instance, retrieves user and library information,
    and creates a JSON array of objects with username and library name.
    """
    # 1. Fetch library information
    print("Fetching library information from Jellyfin...")
    libraries_data = get_jellyfin_data("/Library/VirtualFolders")
    if not libraries_data:
        return

    # Create a dictionary to map library ItemID to library name
    library_id_to_name = {
        library.get("ItemId"): library.get("Name") for library in libraries_data
    }

    # 2. Fetch user information
    print("Fetching user information from Jellyfin...")
    users_data = get_jellyfin_data("/Users")
    if not users_data:
        return

    # 3. Create the desired output by mapping users to their accessible libraries
    user_library_output = []
    for user in users_data:
        username = user.get("Name")
        policy = user.get("Policy", {})
        enabled_folder_ids = policy.get("EnabledFolders", [])

        for folder_id in enabled_folder_ids:
            library_name = library_id_to_name.get(folder_id)
            if library_name:
                user_library_output.append(
                    {"username": username, "library_name": library_name}
                )

    # 4. Write the output to a new JSON file
    output_filename = "user_library_mapping.json"
    try:
        with open(output_filename, "w") as outfile:
            json.dump(user_library_output, outfile, indent=4)
        print(
            "Successfully wrote user to library mapping to"
            f" {output_filename}"
        )
    except IOError as e:
        print(f"An error occurred while writing the output file: {e}")


if __name__ == "__main__":
    create_user_library_mapping()