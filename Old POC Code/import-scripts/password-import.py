import sqlite3
import os

# --- Configuration ---
# Set the full path to your OLD Jellyfin database file
OLD_DB_PATH = "C:\\ProgramData\\Jellyfin\\Server\\data\\jellyfin.db"

# Set the full path to your NEW Jellyfin database file
# This is the file that will be MODIFIED.
NEW_DB_PATH = "F:\\media-stack\\jellyfin\\server\\config\\data\\data\\jellyfin.db"


def transfer_password_hashes(old_db, new_db):
    """
    Connects to two Jellyfin SQLite databases and transfers password hashes
    from the old to the new, excluding the 'Jellyfin' user.
    """
    # --- Safety Checks ---
    if not os.path.exists(old_db):
        print(f"Error: Old database file not found at '{old_db}'")
        return
    if not os.path.exists(new_db):
        print(f"Error: New database file not found at '{new_db}'")
        return

    old_conn = None
    new_conn = None
    updated_count = 0
    skipped_count = 0

    try:
        # --- Connect to both databases ---
        old_conn = sqlite3.connect(old_db)
        new_conn = sqlite3.connect(new_db)
        print("Successfully connected to both databases.")

        old_cursor = old_conn.cursor()
        new_cursor = new_conn.cursor()

        # --- 1. Fetch all users and their password hashes from the OLD database ---
        print("Fetching users from the old database...")
        old_cursor.execute("SELECT Username, Password FROM Users")
        old_users_data = {row[0]: row[1] for row in old_cursor.fetchall()}
        print(f"Found {len(old_users_data)} users in the old database.")

        # --- 2. Fetch all usernames from the NEW database for validation ---
        new_cursor.execute("SELECT Username FROM Users")
        # Use a set for efficient lookup
        new_users_set = {row[0] for row in new_cursor.fetchall()}

        # --- 3. Iterate through old users and update the new database ---
        print("\nStarting password migration process...")
        for username, password_hash in old_users_data.items():
            # --- Exclude the 'Jellyfin' user ---
            if username.lower() == 'jellyfin':
                print(f"- Skipping excluded user: '{username}'")
                continue

            # Check if the user exists in the new database
            if username in new_users_set:
                print(f"- Updating password for user: '{username}'")
                try:
                    # Use parameterized query to prevent SQL injection
                    new_cursor.execute(
                        "UPDATE Users SET Password = ? WHERE Username = ?",
                        (password_hash, username)
                    )
                    updated_count += 1
                except sqlite3.Error as e:
                    print(f"  - Error updating password for '{username}': {e}")
            else:
                print(f"- Skipping user '{username}' (not found in the new database).")
                skipped_count += 1

        # --- 4. Commit the changes to the new database ---
        new_conn.commit()
        print("\nMigration complete. Changes have been committed to the new database.")

    except sqlite3.Error as e:
        print(f"\nAn error occurred: {e}")
        print("No changes were committed. Your new database remains untouched.")

    finally:
        # --- 5. Close connections ---
        if old_conn:
            old_conn.close()
        if new_conn:
            new_conn.close()
        print(f"\nSummary:")
        print(f"  - Passwords updated for {updated_count} user(s).")
        print(f"  - Skipped {skipped_count} user(s) not present in the new database.")
        print("Database connections closed.")


def main():
    """Main function to run the script."""
    print("--- Jellyfin Password Hash Migration Script ---")
    print("IMPORTANT: Ensure your NEW Jellyfin server is STOPPED before proceeding.")
    # Simple confirmation prompt
    if input("Have you stopped the server and backed up your new database? (yes/no): ").lower() != 'yes':
        print("Aborting. Please stop the server and create a backup before running this script.")
        return

    transfer_password_hashes(OLD_DB_PATH, NEW_DB_PATH)


if __name__ == "__main__":
    main()