import json
import requests
from collections import defaultdict

# --- Configuration ---
# Replace with your Jellyfin server URL
JELLYFIN_URL = "http://localhost:8096"
# Replace with your Jellyfin API key
API_KEY = "6e1552625c1b446b9b6c1eebca7a7aa0"
# Path to the JSON file containing user details
MAPPING_FILE = "C:\\Users\\Sam\\Documents\\jellyfin-mig-v2\\export-files\\user_library_mapping.json"

# --- Headers for API Requests ---
headers = {
    "Content-Type": "application/json",
    "X-Emby-Token": API_KEY, # Note: Jellyfin can also use X-Emby-Token for the API key
}

def get_all_users():
    """Fetches all users from Jellyfin and returns a mapping of username to user ID."""
    try:
        response = requests.get(f"{JELLYFIN_URL}/Users", headers=headers)
        response.raise_for_status()
        users = response.json()
        return {user['Name']: user['Id'] for user in users}
    except requests.exceptions.RequestException as e:
        print(f"Error fetching users: {e}")
        return None

def get_all_libraries():
    """Fetches all libraries from Jellyfin and returns a mapping of library name to library ID."""
    try:
        # This endpoint gets all top-level library folders
        response = requests.get(f"{JELLYFIN_URL}/Items", headers=headers, params={"Recursive": "true", "IncludeItemTypes": "CollectionFolder"})
        response.raise_for_status()
        items = response.json().get('Items', [])
        return {lib['Name']: lib['Id'] for lib in items}
    except requests.exceptions.RequestException as e:
        print(f"Error fetching libraries: {e}")
        return None

def get_user_data(user_id):
    """Gets the full data object for a specific user, which includes their policy."""
    try:
        response = requests.get(f"{JELLYFIN_URL}/Users/{user_id}", headers=headers)
        response.raise_for_status()
        return response.json()
    except requests.exceptions.RequestException as e:
        print(f"Error fetching data for user {user_id}: {e}")
        return None

def update_user_library_access(user_id, library_ids):
    """Updates a user's policy to grant access to a specific list of libraries."""
    user_data = get_user_data(user_id)
    if not user_data:
        print(f"Could not retrieve user data for {user_id}. Skipping policy update.")
        return

    # Get the current policy from the user data
    policy = user_data.get('Policy', {})

    # Update the policy with the new list of accessible library IDs
    # This explicitly denies access to all folders and then adds the ones we want.
    policy['EnableAllFolders'] = False
    policy['EnabledFolders'] = library_ids

    try:
        # POST the updated policy back to the server
        response = requests.post(f"{JELLYFIN_URL}/Users/{user_id}/Policy", headers=headers, json=policy)
        response.raise_for_status() # This will now check for errors on the POST request
        print(f"Successfully updated library access for user ID: {user_id}")
    except requests.exceptions.RequestException as e:
        print(f"Error updating policy for user {user_id}: {e}")
        if hasattr(e, 'response') and e.response is not None:
            print(f"Response status: {e.response.status_code}")
            print(f"Response content: {e.response.content.decode()}")

def main():
    """Main function to orchestrate the user-library association."""
    # 1. Read the mapping file
    try:
        with open(MAPPING_FILE, 'r') as f:
            mappings = json.load(f)
    except FileNotFoundError:
        print(f"Error: The mapping file '{MAPPING_FILE}' was not found.")
        return
    except json.JSONDecodeError:
        print(f"Error: Could not decode JSON from the file '{MAPPING_FILE}'.")
        return

    # 2. Get all users and libraries from Jellyfin
    print("Fetching users and libraries from Jellyfin...")
    user_map = get_all_users()
    library_map = get_all_libraries()

    if not user_map or not library_map:
        print("Could not retrieve users or libraries. Exiting.")
        return

    # 3. Aggregate library access for each user from the mapping file
    user_to_libraries = defaultdict(list)
    for mapping in mappings:
        username = mapping.get("username")
        library_name = mapping.get("library_name")

        if username and library_name:
            # Check if user and library exist in Jellyfin before adding to the update list
            if username in user_map and library_name in library_map:
                library_id = library_map[library_name]
                user_to_libraries[username].append(library_id)
            else:
                if username not in user_map:
                    print(f"Warning: User '{username}' from mapping file not found in Jellyfin.")
                if library_name not in library_map:
                    print(f"Warning: Library '{library_name}' from mapping file not found in Jellyfin.")

    # 4. Update each user's library access policy
    for username, library_ids in user_to_libraries.items():
        user_id = user_map.get(username)
        if user_id:
            print(f"\nUpdating access for user '{username}'...")
            update_user_library_access(user_id, library_ids)

if __name__ == "__main__":
    main()