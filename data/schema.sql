
            CREATE TABLE IF NOT EXISTS accounts (
                id INTEGER PRIMARY KEY,
                account_key TEXT NOT NULL UNIQUE,
                nickname TEXT NOT NULL,
                coins INTEGER NOT NULL CHECK(coins BETWEEN 0 AND 2147483647),
                diamonds INTEGER NOT NULL CHECK(diamonds BETWEEN 0 AND 2147483647),
                created_at INTEGER NOT NULL,
                revision INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS catalog (
                str_id TEXT PRIMARY KEY,
                item_id INTEGER NOT NULL UNIQUE,
                item_group TEXT NOT NULL,
                currency TEXT NOT NULL CHECK(currency IN ('coins','diamonds')),
                price INTEGER NOT NULL CHECK(price BETWEEN 0 AND 2147483647),
                consumable INTEGER NOT NULL CHECK(consumable IN (0,1)),
                data_json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS purchases (
                id INTEGER PRIMARY KEY,
                account_id INTEGER NOT NULL REFERENCES accounts(id),
                request_key TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                result_json TEXT,
                created_at INTEGER NOT NULL,
                UNIQUE(account_id, request_key)
            );
            CREATE TABLE IF NOT EXISTS inventory (
                account_id INTEGER NOT NULL REFERENCES accounts(id),
                str_id TEXT NOT NULL REFERENCES catalog(str_id),
                quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 2147483647),
                transaction_id INTEGER NOT NULL REFERENCES purchases(id),
                time_buy INTEGER NOT NULL,
                PRIMARY KEY(account_id, str_id)
            );
            CREATE TABLE IF NOT EXISTS credentials (
                account_id INTEGER PRIMARY KEY REFERENCES accounts(id),
                guid TEXT NOT NULL UNIQUE,
                token_hash TEXT NOT NULL UNIQUE
            );
            CREATE TABLE IF NOT EXISTS stat_packets (
                account_id INTEGER NOT NULL REFERENCES accounts(id),
                packet_guid TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                received_at INTEGER NOT NULL,
                PRIMARY KEY(account_id, packet_guid)
            );
            CREATE TABLE IF NOT EXISTS saved_profiles (
                account_id INTEGER NOT NULL REFERENCES accounts(id),
                block TEXT NOT NULL,
                item_key TEXT NOT NULL,
                subject TEXT NOT NULL,
                data_json TEXT NOT NULL,
                PRIMARY KEY(account_id,block,item_key),
                UNIQUE(account_id,block,subject)
            );


        CREATE TABLE IF NOT EXISTS mission_catalog (
            name TEXT PRIMARY KEY,
            event_id INTEGER NOT NULL UNIQUE,
            previous_name TEXT,
            currency TEXT NOT NULL,
            reward INTEGER NOT NULL,
            mode TEXT NOT NULL,
            max_reward INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS mission_runs (
            id INTEGER PRIMARY KEY,
            account_id INTEGER NOT NULL REFERENCES accounts(id),
            mission_name TEXT NOT NULL REFERENCES mission_catalog(name),
            run_token TEXT NOT NULL UNIQUE,
            started_at REAL NOT NULL,
            status TEXT NOT NULL,
            finished_at REAL,
            finish_fingerprint TEXT,
            receipt_json TEXT
        );
        CREATE INDEX IF NOT EXISTS mission_active ON mission_runs(account_id,status);
        CREATE TABLE IF NOT EXISTS mission_progress (
            account_id INTEGER NOT NULL REFERENCES accounts(id),
            mission_name TEXT NOT NULL REFERENCES mission_catalog(name),
            wins INTEGER NOT NULL,
            first_win INTEGER NOT NULL,
            last_win INTEGER NOT NULL,
            best_seconds REAL NOT NULL,
            last_run INTEGER NOT NULL REFERENCES mission_runs(id),
            PRIMARY KEY(account_id,mission_name)
        );
        CREATE TABLE IF NOT EXISTS mission_requests (
            account_id INTEGER NOT NULL REFERENCES accounts(id),
            route TEXT NOT NULL,
            request_key TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            response_json TEXT NOT NULL,
            PRIMARY KEY(account_id,route,request_key)
        );
