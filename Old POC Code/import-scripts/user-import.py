import json
import random
import string
import requests

# --- Configuration ---
# Replace with your Jellyfin server URL
JELLYFIN_URL = "http://localhost:8096"
# Replace with your Jellyfin API key
API_KEY = "6e1552625c1b446b9b6c1eebca7a7aa0"
# Path to the JSON file containing user details
USERS_FILE = "C:\\Users\\Sam\\Documents\\jellyfin-mig-v2\\export-files\\users_simplified.json"

def create_jellyfin_user(username, password):
    """
    Creates a new user in Jellyfin.
    """
    headers = {
        "Content-Type": "application/json",
        "Authorization": f'MediaBrowser Token="{API_KEY}"',
    }
    # The API endpoint for creating a new user
    url = f"{JELLYFIN_URL}/Users/New"
    payload = {
        "Name": username,
        "Password": password,
    }

    try:
        response = requests.post(url, headers=headers, json=payload)
        response.raise_for_status()  # Raise an exception for bad status codes (4xx or 5xx)
        print(f"Successfully created user: {username}")
        return response.json()
    except requests.exceptions.HTTPError as errh:
        print(f"Http Error for user {username}: {errh}")
        print(f"Response content: {errh.response.content.decode()}")
    except requests.exceptions.ConnectionError as errc:
        print(f"Error Connecting for user {username}: {errc}")
    except requests.exceptions.Timeout as errt:
        print(f"Timeout Error for user {username}: {errt}")
    except requests.exceptions.RequestException as err:
        print(f"Something else went wrong for user {username}: {err}")
    return None

def generate_random_password(length=12):
    """
    Generates a random password.
    """
    characters = string.ascii_letters + string.digits + string.punctuation
    return ''.join(random.choice(characters) for i in range(length))

def main():
    """
    Main function to read users from the file and create them in Jellyfin.
    """
    try:
        with open(USERS_FILE, 'r') as f:
            users_to_create = json.load(f)
    except FileNotFoundError:
        print(f"Error: The file '{USERS_FILE}' was not found.")
        return
    except json.JSONDecodeError:
        print(f"Error: Could not decode JSON from the file '{USERS_FILE}'.")
        return

    for user in users_to_create:
        username = user.get("username")
        if not username:
            print("Skipping user with no username.")
            continue

        random_password = generate_random_password()
        print(f"Creating user '{username}' with a random password.")
        create_jellyfin_user(username, random_password)
        # You can optionally store the generated passwords if needed
        # print(f"Username: {username}, Password: {random_password}")

if __name__ == "__main__":
    main()

