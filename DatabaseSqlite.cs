using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;

namespace PersistenceServer
{
    public class DatabaseSqlite : Database
    {
        public DatabaseSqlite(SettingsReader settings) : base(settings)
        {
            ConnectionParams = $"Data Source={settings.SqliteFilename};";
            GetIdentitySqlCommand = "SELECT last_insert_rowid();";
        }

        protected override async Task<DbConnection> GetConnection(string parameters)
        {
            var connection = new SqliteConnection(parameters);
            await connection.OpenAsync();
            return connection;
        }

        protected override DbCommand GetCommand(string parameters, DbConnection? connection) => new SqliteCommand(parameters, (SqliteConnection?)connection);

        public override async Task CheckCreateDatabase(SettingsReader settings)
        {
            var getTablesQuery = await RunQuery($"SELECT count(*) FROM sqlite_master WHERE type = 'table';");
            
            if (getTablesQuery.GetBigInt(0, "count(*)") == 0)
            {
                Console.Write("Base de données non trouvée ou vide : création...");

                await RunNonQuery(@"
                    CREATE TABLE accounts (
                        id INTEGER NOT NULL UNIQUE,
                        name TEXT UNIQUE,
                        steamid TEXT UNIQUE,
                        password TEXT,
                        salt TEXT,
                        email TEXT,
                        status INTEGER,
                        last_ip TEXT,
                        last_mac TEXT,
                        PRIMARY KEY(id AUTOINCREMENT),
                        UNIQUE(name)
                    );
                    CREATE UNIQUE INDEX accname ON accounts(name);
                    CREATE UNIQUE INDEX accsteamid ON accounts(steamid);
                    
                    CREATE TABLE guilds (
                        id INTEGER NOT NULL UNIQUE,
                        name TEXT UNIQUE,
                        serialized TEXT,
                        PRIMARY KEY(id AUTOINCREMENT),
                        UNIQUE(name)
                    );
                    CREATE UNIQUE INDEX guildname ON guilds(name);
                    
                    CREATE TABLE characters (
                        id INTEGER NOT NULL UNIQUE,
                        name TEXT UNIQUE,
                        owner INTEGER,
                        guild INTEGER,
                        guildrank INTEGER,
                        permissions INTEGER NOT NULL DEFAULT 0,
                        prefix TEXT,
                        level INTEGER NOT NULL DEFAULT 1,
                        experience INTEGER NOT NULL DEFAULT 0,
                        experience_to_next_level INTEGER NOT NULL DEFAULT 100,
                        class TEXT DEFAULT '',
                        species TEXT DEFAULT '',
                        gender TEXT DEFAULT '',
                        appearance TEXT DEFAULT '{}',
                        stats TEXT DEFAULT '{}',
                        inventory TEXT DEFAULT '{}',
                        equipment TEXT DEFAULT '{}',
                        abilities TEXT DEFAULT '{}',
                        quests TEXT DEFAULT '{}',
                        zone TEXT DEFAULT '',
                        position_x REAL DEFAULT 0,
                        position_y REAL DEFAULT 0,
                        position_z REAL DEFAULT 0,
                        rotation_yaw REAL DEFAULT 0,
                        is_new_character INTEGER DEFAULT 0,
                        PRIMARY KEY(id AUTOINCREMENT),
                        UNIQUE(name),
                        FOREIGN KEY(owner) REFERENCES accounts(id) ON UPDATE CASCADE ON DELETE SET NULL,
                        FOREIGN KEY(guild) REFERENCES guilds(id) ON UPDATE CASCADE ON DELETE SET NULL
                    );
                    CREATE UNIQUE INDEX charname ON characters(name);
                    CREATE INDEX charowner ON characters(owner);
                    CREATE INDEX charguild ON characters(guild);
                    
                    CREATE TABLE servers (
                        id INTEGER NOT NULL UNIQUE,
                        port INTEGER NOT NULL,
                        level TEXT NOT NULL,
                        serialized TEXT,
                        PRIMARY KEY(id AUTOINCREMENT)
                    );
                    CREATE UNIQUE INDEX ServerPortLevel ON servers (port, level);
                    
                    CREATE TABLE persistentobjs (
                        id INTEGER NOT NULL UNIQUE,
                        level TEXT NOT NULL,
                        port INTEGER NOT NULL,
                        objectId INTEGER NOT NULL,
                        serialized TEXT NOT NULL,
                        PRIMARY KEY(id AUTOINCREMENT)
                    );
                    CREATE UNIQUE INDEX PersistentObjIndex ON persistentobjs (objectId, port, level);
                    
                    CREATE TABLE level_config (
                        level INTEGER NOT NULL,
                        experience_required INTEGER NOT NULL,
                        PRIMARY KEY(level)
                    );
                ");

                Console.WriteLine("Fait.");
            }
            else
            {
                Console.WriteLine($"Base de données trouvée: {settings.SqliteFilename}");
                
                // Vérifier et ajouter les colonnes manquantes si nécessaire
                var checkColumnQuery = await RunQuery("PRAGMA table_info(characters);");
                bool hasPrefix = false;
                bool hasLevel = false;
                bool hasClass = false;
                
                foreach (DataRow row in checkColumnQuery.Rows)
                {
                    string colName = row["name"].ToString() ?? "";
                    if (colName == "prefix") hasPrefix = true;
                    if (colName == "level") hasLevel = true;
                    if (colName == "class") hasClass = true;
                }
                
                if (!hasPrefix)
                {
                    Console.Write("Ajout de la colonne 'prefix'...");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN prefix TEXT;");
                    Console.WriteLine("Fait.");
                }

                if (!hasLevel)
                {
                    Console.Write("Ajout des colonnes du système de niveaux...");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN level INTEGER NOT NULL DEFAULT 1;");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN experience INTEGER NOT NULL DEFAULT 0;");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN experience_to_next_level INTEGER NOT NULL DEFAULT 100;");
                    Console.WriteLine("Fait.");
                }

                if (!hasClass)
                {
                    Console.Write("Ajout des nouvelles colonnes de personnage...");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN class TEXT DEFAULT '';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN species TEXT DEFAULT '';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN gender TEXT DEFAULT '';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN appearance TEXT DEFAULT '{}';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN stats TEXT DEFAULT '{}';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN inventory TEXT DEFAULT '{}';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN equipment TEXT DEFAULT '{}';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN abilities TEXT DEFAULT '{}';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN quests TEXT DEFAULT '{}';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN zone TEXT DEFAULT '';");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN position_x REAL DEFAULT 0;");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN position_y REAL DEFAULT 0;");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN position_z REAL DEFAULT 0;");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN rotation_yaw REAL DEFAULT 0;");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN is_new_character INTEGER DEFAULT 0;");
                    Console.WriteLine("Fait.");
                }
            }
        }

        public override async Task<int> LoginUser(string accountName, string password)
        {
            var cmd = GetCommand("SELECT id, password, salt, status FROM accounts WHERE name = @accountName");
            cmd.AddParam("@accountName", accountName);
            var dt = await RunQuery(cmd);

            if (!dt.HasRows())
            {
                return -1;
            }

            var id = (int)dt.GetBigInt(0, "id")!;
            var status = dt.GetBigInt(0, "status");
            var passwordInDb = dt.GetString(0, "password");
            var salt = dt.GetString(0, "salt");

            if (status == -1)
            {
                return -1;
            }

            if (passwordInDb != BCrypt.Net.BCrypt.HashPassword(password, salt + Pepper))
            {
                return -1;
            }

            return id;
        }

        public override async Task<Guild?> CreateGuild(string guildName, int charId)
        {
            // Vérifier si une guilde avec ce nom existe
            var checkCmd = GetCommand("SELECT * FROM guilds WHERE name = @guildName");
            checkCmd.AddParam("@guildName", guildName);
            var dt = await RunQuery(checkCmd);
            if (dt.HasRows()) {
                return null;
            }

            var createGuildCmd = GetCommand("INSERT INTO `guilds` (`id`, `name`) VALUES (NULL, @guildName);");
            createGuildCmd.AddParam("@guildName", guildName);            
            int lastInsertedId = await RunInsert(createGuildCmd);

            var updateCharCmd = GetCommand("UPDATE `characters` SET `guild` = @guildId, `guildRank` = '0' WHERE `characters`.`id` = @charId; ");
            updateCharCmd.AddParam("@guildId", lastInsertedId);
            updateCharCmd.AddParam("@charId", charId);
            await RunNonQuery(updateCharCmd);
            return new Guild(lastInsertedId, guildName);
        }

        public async override Task<Dictionary<int, Guild>> GetGuilds()
        {
            var result = new Dictionary<int, Guild>();

            await using var conn = await GetConnection(ConnectionParams);

            var cmd = GetCommand(@"
                SELECT guilds.id as guildId, guilds.name as guildName, characters.id as charId, characters.name as charName, characters.guildRank as guildRank FROM guilds
                LEFT JOIN characters
                ON guilds.id = characters.guild
            ", conn);                        
            await using var reader = await cmd.ExecuteReaderAsync();
            DataTable dt = new();
            dt.Columns.Add("guildId", typeof(int));
            dt.Columns.Add("guildName", typeof(string));
            dt.Columns.Add("charId", typeof(int));
            dt.Columns.Add("charName", typeof(string));
            dt.Columns.Add("guildRank", typeof(int));

            dt.Load(reader);
            await cmd.DisposeAsync();

            if (!dt.HasRows()) return result;

            foreach (var row in dt.Rows.OfType<DataRow>())
            {
                var guildId = (int)row.GetInt("guildId")!;
                var guildName = (string)row.GetString("guildName")!;
                var charId = row.GetInt("charId");
                var charName = row.GetString("charName");
                var guildRank = row.GetInt("guildRank");
                
                if (!result.ContainsKey(guildId))
                {
                    result.Add(guildId, new Guild(guildId, guildName));
                }
                if (charId == null || charName == null)
                {
                    Console.WriteLine($"Info : guilde \"{guildName}\" (id: {guildId}) est sans parents et sans membres");
                    continue;
                }                
                result[guildId].PopulateMember((int)charId, charName, (int)guildRank!);
            }

            return result;
        }

        public override async Task SaveServerInfo(string serializedServerInfo, int port, string level)
        {
            var cmd = GetCommand("INSERT OR REPLACE INTO `servers` (`id`, `port`, `level`, `serialized`) VALUES (NULL, @port, @level, @serialized)");
            cmd.AddParam("@port", port);
            cmd.AddParam("@level", level);
            cmd.AddParam("@serialized", serializedServerInfo);
            await RunNonQuery(cmd);
            Console.WriteLine($"{DateTime.Now:HH:mm} Serveur Info ({port}-{level}) a été enregistré dans la base de données.");
        }

        public override async Task SavePersistentObject(string level, int port, int objectId, string jsonString)
        {
            var cmd = GetCommand("INSERT OR REPLACE INTO `persistentobjs` (`id`, `level`, `port`, `objectId`, `serialized`) VALUES (NULL, @level, @port, @objectId, @serialized)");
            cmd.AddParam("@level", level);
            cmd.AddParam("@port", port);
            cmd.AddParam("@objectId", objectId);
            cmd.AddParam("@serialized", jsonString);
            await RunNonQuery(cmd);
        }
    }
}
