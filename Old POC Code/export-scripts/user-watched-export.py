import sqlite3
import os
import json

def main():
    # --- Configuration: SET THESE PATHS CAREFULLY ---
    # Paths for the OLD (source) Jellyfin instance
    old_jellyfin_db_path = "C:\\ProgramData\\Jellyfin\\Server\\data\\jellyfin.db"
    old_library_db_path = "C:\\ProgramData\\Jellyfin\\Server\\data\\library.db"
    output_json_path = "C:\\Users\\Sam\\Documents\\jellyfin-mig-v2\\export-files\\watched_data.json"

    # --- Pre-run Checks ---
    for path in [old_jellyfin_db_path, old_library_db_path]:
        if not os.path.exists(path):
            print(f"Error: Database file not found at '{path}'")
            return

    # --- Database Connection ---
    try:
        # Connection to OLD library.db
        old_lib_conn = sqlite3.connect(old_library_db_path)
        old_lib_cursor = old_lib_conn.cursor()

        # Attach the old jellyfin.db to the old library.db connection to run the join query
        old_lib_conn.execute(f"ATTACH DATABASE '{old_jellyfin_db_path}' AS jf")

    except sqlite3.Error as e:
        print(f"Database connection error: {e}")
        return

    print("Successfully connected to the old databases.")

    # --- SQL Query to get watched data from the OLD system ---
    # This query joins tables across the attached databases (library.db and jellyfin.db)
    query = """
    SELECT
        i.Name,
        i.Path,
        d.PlaybackPositionTicks,
        d.LastPlayedDate,
        u.Username,
        REPLACE(d.Key, '-', '') AS PresentationUniqueKey
    FROM TypedBaseItems i
    JOIN UserDatas d ON i.PresentationUniqueKey = REPLACE(d.Key, '-', '')
    JOIN jf.Users u ON d.UserId = u.InternalId
    WHERE i.Path IS NOT NULL;
    """

    try:
        print("Fetching watched data from the old database...")
        old_lib_cursor.execute(query)
        watched_data_rows = old_lib_cursor.fetchall()
        print(f"Found {len(watched_data_rows)} watched items to export.")

        exported_data = []
        for row in watched_data_rows:
            item_name, old_path, position, last_played, username, pres_key = row
            exported_data.append({
                "ItemName": item_name,
                "Path": old_path,
                "PlaybackPositionTicks": position,
                "LastPlayedDate": last_played,
                "Username": username,
                "PresentationUniqueKey": pres_key
            })

        # --- Write data to JSON file ---
        with open(output_json_path, 'w') as f:
            json.dump(exported_data, f, indent=4)
        
        print(f"\nExport complete! Successfully saved watched status for {len(exported_data)} items to '{output_json_path}'.")

    except sqlite3.Error as e:
        print(f"An error occurred during data extraction: {e}")
    finally:
        # --- Close all connections ---
        old_lib_conn.close()
        print("Database connection closed.")

if __name__ == "__main__":
    main()