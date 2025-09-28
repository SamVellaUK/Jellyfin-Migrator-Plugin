import json
import requests

# --- Configuration ---
# Replace with your Jellyfin server URL
JELLYFIN_URL = "http://localhost:8096"
# Replace with your Jellyfin API key
JELLYFIN_API_KEY = "5346b466a4ec42739071aa10671f22d4"
# --- End of Configuration ---

def extract_and_simplify_user_data():
    """
    Connects to a Jellyfin instance, retrieves all user data,
    and saves a simplified version directly to a JSON file.
    """
    headers = {
        "Authorization": f'MediaBrowser Token="{JELLYFIN_API_KEY}"',
        "Content-Type": "application/json",
    }

    try:
        # 1. Get the full user information from the Jellyfin API
        print("Fetching user information from Jellyfin...")
        response = requests.get(f"{JELLYFIN_URL}/Users", headers=headers, timeout=10)
        response.raise_for_status()  # This will raise an exception for HTTP errors
        full_users_data = response.json()
        print("Successfully fetched user data.")

        # 2. Process the full data to extract only the required fields
        simplified_users = []
        for user in full_users_data:
            policy = user.get("Policy", {})
            simplified_info = {
                # Rename 'Name' to 'username' for clarity
                "username": user.get("Name"),
                "id": user.get("Id"),
                # Get 'EnabledFolders' from the nested 'Policy' object
                "folders": policy.get("EnabledFolders", [])
            }
            simplified_users.append(simplified_info)

        # 3. Write the simplified user information to a new JSON file
        output_filename = "users_simplified.json"
        with open(output_filename, "w") as outfile:
            json.dump(simplified_users, outfile, indent=4)
        print(f"Successfully wrote simplified user information to {output_filename}")

    except requests.exceptions.HTTPError as e:
        print(f"An HTTP error occurred: {e}")
        print(f"Response body: {e.response.text}")
    except requests.exceptions.RequestException as e:
        print(f"A connection error occurred: {e}")
    except Exception as e:
        print(f"An unexpected error occurred: {e}")

if __name__ == "__main__":
    extract_and_simplify_user_data()