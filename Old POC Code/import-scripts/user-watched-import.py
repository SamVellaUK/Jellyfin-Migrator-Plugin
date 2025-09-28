import sqlite3
import os
import json

def translate_path(windows_path):
    """
    Translates a Windows-style path to a Linux-style path.
    You MUST customize this function to match your specific path mapping.
    """
    # Example: 'F:\Media\Movies\Movie.mkv' -> '/media/Movies/Movie.mkv'
    # Ensure you use double backslashes in the source path for proper string escaping.
    path = windows_path.replace("F:\\Media\\", "/media/")
    path = path.replace("\\", "/")
    return path

def get_user_map(new_jf_cursor):
    """
    Creates a mapping of usernames to new user IDs from the new jellyfin.db.
    """
    user_map = {}
    new_jf_cursor.execute("SELECT Username, Id FROM Users")
    for row in new_jf_cursor.fetchall():
        user_map[row[0]] = row[1]
    return user_map

def get_new_presentation_key(new_lib_cursor, file_path):
    """
    Retrieves the PresentationUniqueKey for a given file path from the new library.db.
    """
    new_lib_cursor.execute("SELECT PresentationUniqueKey FROM TypedBaseItems WHERE Path = ?", (file_path,))
    result = new_lib_cursor.fetchone()
    return result[0] if result else None

def main():
    # --- Configuration: SET THESE PATHS CAREFULLY ---
    # Paths for the NEW (destination) Jellyfin instance
    new_jellyfin_db_path = "F:\\media-stack\\jellyfin\\server\\config\\data\\jellyfin.db"
    new_library_db_path = "F:\\media-stack\\jellyfin\\server\\config\\data\\library.db"
    input_json_path = "C:\\Users\\Sam\\Documents\\jellyfin-mig-v2\\export-files\\watched_data.json"

    # --- Pre-run Checks ---
    for path in [new_jellyfin_db_path, new_library_db_path, input_json_path]:
        if not os.path.exists(path):
            print(f"Error: File not found at '{path}'")
            return

    # --- Database Connections ---
    try:
        # Connections to NEW databases
        new_jf_conn = sqlite3.connect(new_jellyfin_db_path)
        new_jf_cursor = new_jf_conn.cursor()
        new_lib_conn = sqlite3.connect(new_library_db_path)
        new_lib_cursor = new_lib_conn.cursor()

    except sqlite3.Error as e:
        print(f"Database connection error: {e}")
        return

    print("Successfully connected to the new databases.")
    
    # --- Load Data from JSON ---
    try:
        with open(input_json_path, 'r') as f:
            watched_data = json.load(f)
        print(f"Loaded {len(watched_data)} items from '{input_json_path}'.")
    except json.JSONDecodeError as e:
        print(f"Error reading JSON file: {e}")
        return

    user_map = get_user_map(new_jf_cursor)
    print(f"Found {len(user_map)} users in the new database: {list(user_map.keys())}")

    try:
        migrated_count = 0
        for item in watched_data:
            item_name = item.get("ItemName")
            old_path = item.get("Path")
            position = item.get("PlaybackPositionTicks")
            last_played = item.get("LastPlayedDate")
            username = item.get("Username")

            # --- Step 1: Map Username to New User ID ---
            new_user_id = user_map.get(username)
            if not new_user_id:
                print(f"Skipping '{item_name}' for user '{username}' - user not found in new database.")
                continue

            # --- Step 2: Translate Path and Find Media in New DB ---
            new_path = translate_path(old_path)
            new_pres_key = get_new_presentation_key(new_lib_cursor, new_path)

            if not new_pres_key:
                print(f"Skipping '{item_name}' - media not found in new database with path '{new_path}'.")
                continue

            # --- Step 3: Insert or Update Watched Data in New DB ---
            insert_query = """
            INSERT OR REPLACE INTO UserDatas 
                (Key, UserId, Played, PlayCount, IsFavorite, PlaybackPositionTicks, LastPlayedDate, AudioStreamIndex, SubtitleStreamIndex)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?);
            """
            
            # Format the PresentationUniqueKey with dashes for the UserDatas table
            formatted_key = f"{new_pres_key[:8]}-{new_pres_key[8:12]}-{new_pres_key[12:16]}-{new_pres_key[16:20]}-{new_pres_key[20:]}"

            new_lib_cursor.execute(insert_query, (formatted_key, new_user_id, 1, 1, 0, position, last_played, -1, -1))
            migrated_count += 1
        
        # Commit changes to the new library.db
        new_lib_conn.commit()
        print(f"\nMigration complete! Successfully migrated watched status for {migrated_count} items.")

    except sqlite3.Error as e:
        print(f"An error occurred during migration: {e}")
        new_lib_conn.rollback()  # Rollback any partial changes on error
    finally:
        # --- Close all connections ---
        new_jf_conn.close()
        new_lib_conn.close()
        print("All database connections closed.")

if __name__ == "__main__":
    main()