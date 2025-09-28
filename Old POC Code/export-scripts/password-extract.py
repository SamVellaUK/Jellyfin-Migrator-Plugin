import sqlite3
import os
import json

# --- Configuration ---
# Set the full path to your OLD Jellyfin database file
OLD_DB_PATH = "C:\\ProgramData\\Jellyfin\\Server\\data\\jellyfin.db"

# Set the desired output path for the user data file
EXPORT_FILE_PATH = "C:\\Users\\Sam\\Documents\\jellyfin-mig-v2\\export-files\\jellyfin_users.json"


def export_user_data(db_path, output_path):
    """
    Connects to a Jellyfin SQLite database, extracts user data,
    and saves it to a JSON file.
    """
    # --- Safety Check ---
    if not os.path.exists(db_path):
        print(f"Error: Database file not found at '{db_path}'")
        return

    conn = None
    exported_count = 0
    skipped_count = 0
    users_data = []

    try:
        # --- Connect to the database ---
        conn = sqlite3.connect(db_path)
        print(f"Successfully connected to the database at '{db_path}'.")
        cursor = conn.cursor()

        # --- Fetch users from the database ---
        print("Fetching users from the database...")
        cursor.execute("SELECT Username, Password FROM Users")
        
        for row in cursor.fetchall():
            username, password_hash = row
            # --- Exclude the 'Jellyfin' user ---
            if username.lower() == 'jellyfin':
                print(f"- Skipping excluded user: '{username}'")
                skipped_count += 1
                continue
            
            users_data.append({
                "username": username,
                "password_hash": password_hash
            })
            exported_count += 1

        # --- Write data to JSON file ---
        with open(output_path, 'w') as f:
            json.dump(users_data, f, indent=4)
        print(f"\nSuccessfully exported data for {exported_count} user(s) to '{output_path}'.")

    except sqlite3.Error as e:
        print(f"\nA database error occurred: {e}")
    except IOError as e:
        print(f"\nAn error occurred while writing to the file: {e}")
    finally:
        # --- Close connection ---
        if conn:
            conn.close()
        print(f"\nSummary:")
        print(f"  - Exported {exported_count} user(s).")
        print(f"  - Skipped {skipped_count} user(s).")
        print("Database connection closed.")


def main():
    """Main function to run the script."""
    print("--- Jellyfin User Data Export Script ---")
    export_user_data(OLD_DB_PATH, EXPORT_FILE_PATH)


if __name__ == "__main__":
    main()