# import_devices.py
import sqlite3
import os
import json

def get_user_map(new_db_cursor):
    """
    Creates a mapping of usernames to new user IDs from the new jellyfin.db.
    """
    user_map = {}
    try:
        new_db_cursor.execute("SELECT Username, Id FROM Users")
        for row in new_db_cursor.fetchall():
            user_map[row[0]] = row[1]
    except sqlite3.Error as e:
        print(f"Error reading users from the new database: {e}")
    return user_map

def device_exists(new_db_cursor, device_id, new_user_id):
    """
    Checks if a specific device ID already exists for a specific user ID.
    """
    new_db_cursor.execute("SELECT 1 FROM Devices WHERE DeviceId = ? AND UserId = ?", (device_id, new_user_id))
    return new_db_cursor.fetchone() is not None

def main():
    # --- Configuration: SET THESE PATHS CAREFULLY ---
    new_jellyfin_db_path = "F:\\media-stack\\jellyfin\\server\\config\\data\\jellyfin.db"
    import_file_path = "C:\\Users\\Sam\\Documents\\jellyfin-mig-v2\\export-files\\devices.json"

    # --- Pre-run Checks ---
    for path in [new_jellyfin_db_path, import_file_path]:
        if not os.path.exists(path):
            print(f"Error: Required file not found at '{path}'")
            return

    # --- Database Connection ---
    try:
        new_conn = sqlite3.connect(new_jellyfin_db_path)
        new_cursor = new_conn.cursor()

    except sqlite3.Error as e:
        print(f"Database connection error: {e}")
        return

    print("Successfully connected to the new database.")

    # --- Load Data from JSON ---
    try:
        with open(import_file_path, 'r', encoding='utf-8') as f:
            device_data = json.load(f)
        print(f"Loaded {len(device_data)} devices from '{import_file_path}'.")
    except (IOError, json.JSONDecodeError) as e:
        print(f"Error reading or parsing the JSON file: {e}")
        new_conn.close()
        return

    # --- Prepare for Import ---
    user_map = get_user_map(new_cursor)
    if not user_map:
        print("Could not find any users in the new database. Aborting.")
        new_conn.close()
        return
        
    print(f"Found {len(user_map)} users in the new database: {list(user_map.keys())}")

    try:
        migrated_count = 0
        skipped_count = 0
        for device in device_data:
            username = device['Username']
            device_id = device['DeviceId']
            device_name = device['DeviceName']

            # --- Map Username to New User ID ---
            new_user_id = user_map.get(username)
            if not new_user_id:
                # print(f"Skipping device '{device_name}' for user '{username}' - user not found in new database.")
                skipped_count += 1
                continue

            # --- Check if this device for this user already exists ---
            if device_exists(new_cursor, device_id, new_user_id):
                # print(f"Skipping device '{device_name}' for user '{username}' - already exists in new database.")
                skipped_count += 1
                continue

            # --- Insert Device Data in New DB ---
            insert_query = """
            INSERT INTO Devices (
                AccessToken, AppName, AppVersion, DeviceName, DeviceId,
                IsActive, DateCreated, DateModified, DateLastActivity, UserId
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """

            new_cursor.execute(insert_query, (
                device['AccessToken'], device['AppName'], device['AppVersion'],
                device['DeviceName'], device['DeviceId'], device['IsActive'],
                device['DateCreated'], device['DateModified'], device['DateLastActivity'],
                new_user_id
            ))
            migrated_count += 1

        # Commit changes to the new jellyfin.db
        new_conn.commit()
        print(f"\nMigration complete!")
        print(f"Successfully migrated {migrated_count} devices.")
        print(f"Skipped {skipped_count} devices (user not found or device already exists).")

    except sqlite3.Error as e:
        print(f"An error occurred during migration: {e}")
        new_conn.rollback()  # Rollback any partial changes on error
    finally:
        # --- Close the connection ---
        new_conn.close()
        print("Database connection closed.")

if __name__ == "__main__":
    main()