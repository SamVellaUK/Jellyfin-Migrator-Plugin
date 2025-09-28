# export_devices.py
import sqlite3
import os
import json

def main():
    # --- Configuration: SET THESE PATHS CAREFULLY ---
    old_jellyfin_db_path = "C:\\ProgramData\\Jellyfin\\Server\\data\\jellyfin.db"
    export_file_path = "C:\\Users\\Sam\\Documents\\jellyfin-mig-v2\\export-files\\devices.json"

    # --- Pre-run Checks ---
    if not os.path.exists(old_jellyfin_db_path):
        print(f"Error: Database file not found at '{old_jellyfin_db_path}'")
        return

    # --- Database Connection ---
    try:
        old_conn = sqlite3.connect(old_jellyfin_db_path)
        # Use a Row factory to get column names in the output, which is great for JSON
        old_conn.row_factory = sqlite3.Row
        old_cursor = old_conn.cursor()

    except sqlite3.Error as e:
        print(f"Database connection error: {e}")
        return

    print("Successfully connected to the old database.")

    # --- Query to get device data from the OLD system ---
    query = """
    SELECT
        u.Username,
        d.AccessToken,
        d.AppName,
        d.AppVersion,
        d.DeviceName,
        d.DeviceId,
        d.IsActive,
        d.DateCreated,
        d.DateModified,
        d.DateLastActivity
    FROM Devices d
    JOIN Users u ON d.UserId = u.Id;
    """

    try:
        print("Fetching device data from the old database...")
        old_cursor.execute(query)
        results = old_cursor.fetchall()

        # Convert the sqlite3.Row objects to a list of dictionaries
        device_data = [dict(row) for row in results]
        
        print(f"Found {len(device_data)} devices to export.")

        # --- Write data to JSON file ---
        with open(export_file_path, 'w', encoding='utf-8') as f:
            json.dump(device_data, f, indent=4, ensure_ascii=False)
        
        print(f"Successfully exported device data to '{export_file_path}'")

    except sqlite3.Error as e:
        print(f"An error occurred during the data fetch: {e}")
    except IOError as e:
        print(f"An error occurred writing to the file: {e}")
    finally:
        # --- Close the connection ---
        old_conn.close()
        print("Database connection closed.")

if __name__ == "__main__":
    main()