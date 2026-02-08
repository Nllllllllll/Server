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
            // obtenir le nombre de tables dans la base de données (depuis une table spéciale appelée sqlite_master)
            var getTablesQuery = await RunQuery($"SELECT count(*) FROM sqlite_master WHERE type = 'table';");
            // s'il y a 0 tables, ça signifie que la base de données est nouvelle, donc on les crée
            if (getTablesQuery.GetBigInt(0, "count(*)") == 0) // count retourne BigInt
            {
                Console.Write("Base de données non trouvée ou vide : création...");

                // créer les tables
                await RunNonQuery(
                    @"CREATE TABLE ""accounts"" (
	                    ""id""	INTEGER NOT NULL UNIQUE,
	                    ""name""	TEXT UNIQUE,
                        ""steamid""	TEXT UNIQUE,
	                    ""password""	TEXT,
	                    ""salt""	TEXT,
	                    ""email""	TEXT,
	                    ""status""	INTEGER,
	                    ""last_ip""	TEXT,
	                    ""last_mac""	TEXT,
	                    PRIMARY KEY(""id"" AUTOINCREMENT),
	                    UNIQUE(""name"")
                    );
                    CREATE UNIQUE INDEX accname 
                    ON accounts(name);
                    CREATE UNIQUE INDEX accsteamid 
                    ON accounts(steamid);
                    CREATE TABLE ""guilds"" (
	                    ""id""	INTEGER NOT NULL UNIQUE,
	                    ""name""	TEXT UNIQUE,
                        ""serialized""	TEXT,
	                    PRIMARY KEY(""id"" AUTOINCREMENT),
	                    UNIQUE(""name"")
                    );
                    CREATE UNIQUE INDEX guildname 
                    ON guilds(name);
                    CREATE TABLE ""characters"" (
	                    ""id""	INTEGER NOT NULL UNIQUE,
	                    ""name""	TEXT UNIQUE,
                        ""owner""	INTEGER,
	                    ""guild""	INTEGER,
	                    ""guildrank""	INTEGER,
                        ""permissions"" INTEGER NOT NULL DEFAULT 0,
	                    ""serialized""	TEXT,
	                    ""prefix""	TEXT,
	                    PRIMARY KEY(""id"" AUTOINCREMENT),
	                    UNIQUE(""name""),
                        FOREIGN KEY(""owner"") REFERENCES ""accounts""(""id"") ON UPDATE CASCADE ON DELETE SET NULL,
                        FOREIGN KEY(""guild"") REFERENCES ""guilds""(""id"") ON UPDATE CASCADE ON DELETE SET NULL
                    );
                    CREATE UNIQUE INDEX charname
                    ON characters(name);
                    CREATE INDEX charowner
                    ON characters(owner);
                    CREATE INDEX charguild
                    ON characters(guild);
                    CREATE TABLE ""servers"" (
                        ""id""  INTEGER NOT NULL UNIQUE,
                        ""port""    INTEGER NOT NULL,
                        ""level""   TEXT NOT NULL,
                        ""serialized""  TEXT,
                        PRIMARY KEY(""id"" AUTOINCREMENT)
                    );
                    CREATE UNIQUE INDEX ServerPortLevel ON servers (port, level);
                    CREATE TABLE ""persistentobjs"" (
                        ""id""  INTEGER NOT NULL UNIQUE,
                        ""level""   TEXT NOT NULL,
                        ""port""    INTEGER NOT NULL,
                        ""objectId""    INTEGER NOT NULL,
                        ""serialized""  TEXT NOT NULL,
                        PRIMARY KEY(""id"" AUTOINCREMENT)
                    );
                    CREATE UNIQUE INDEX PersistentObjIndex ON persistentobjs (objectId, port, level);
                    ");
                // ~créer les tables

                Console.WriteLine("Fait.");
            }
            else
            {
                Console.WriteLine($"Base de données trouvée: {settings.SqliteFilename}");
                
                // Vérifier si la colonne prefix existe et l'ajouter si nécessaire
                var checkColumnQuery = await RunQuery("PRAGMA table_info(characters);");
                bool hasPrefix = false;
                foreach (DataRow row in checkColumnQuery.Rows)
                {
                    if (row["name"].ToString() == "prefix")
                    {
                        hasPrefix = true;
                        break;
                    }
                }
                
                if (!hasPrefix)
                {
                    Console.Write("Ajout de la colonne 'prefix' à la table characters...");
                    await RunNonQuery("ALTER TABLE characters ADD COLUMN prefix TEXT;");
                    Console.WriteLine("Fait.");
                }
            }
        }

        public override async Task<int> LoginUser(string accountName, string password)
        {
            var cmd = GetCommand("SELECT id, password, salt, status FROM accounts WHERE name = @accountName");
            cmd.AddParam("@accountName", accountName);
            var dt = await RunQuery(cmd);

            // si aucun compte avec ce nom n'est trouvé
            if (!dt.HasRows())
            {
                return -1;
            }

            var id = (int)dt.GetBigInt(0, "id")!;
            var status = dt.GetBigInt(0, "status");
            var passwordInDb = dt.GetString(0, "password");
            var salt = dt.GetString(0, "salt");

            // si le statut est banni
            if (status == -1)
            {
                return -1;
            }

            // si mauvais mot de passe
            if (passwordInDb != BCrypt.Net.BCrypt.HashPassword(password, salt + Pepper))
            {
                return -1;
            }

            // si tout est en ordre, autoriser la connexion en retournant l'id de l'utilisateur
            return id;
        }

        public override async Task<Guild?> CreateGuild(string guildName, int charId)
        {
            // vérifier si une guilde avec ce nom existe
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

        /*
        * En raison d'un bug dans Microsoft.Data.Sqlite, DataTable.Load préserve les contraintes UNIQUE des requêtes JOIN
        * Cela rend impossible d'avoir deux lignes avec le même nom de guilde et ça lance une erreur
        * J'ai soumis un rapport de bug https://github.com/dotnet/efcore/issues/30765
        * En attendant, on crée manuellement les colonnes DataTable pour cette requête spécifiquement, ce qui nous permet de contourner le bug
        */
        public async override Task<Dictionary<int, Guild>> GetGuilds()
        {
            var result = new Dictionary<int, Guild>();

            /*
             * Un exemple de ce qu'on peut s'attendre à recevoir en retour :
             * 
             * guildId	    guildName			charId		charName 	
             *    1 		Diamond Dogs 		1 			Arthur Pendragon
             *    1         Diamond Dogs        2           Raven
             *    2 		No Dogs 			NULL 		NULL 
             * 
             * Dans cet exemple "Diamond Dogs" a deux membres : Arthur Pendragon et Raven
             * La guilde "No Dogs" est sans membres. Ça ne devrait pas arriver, mais si c'est le cas, on affichera un avertissement.
             */
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

            //Console.WriteLine("GuildId, GuildName, CharId, CharName, GuildRank");
            foreach (var row in dt.Rows.OfType<DataRow>())
            {
                var guildId = (int)row.GetInt("guildId")!;
                var guildName = (string)row.GetString("guildName")!;
                var charId = row.GetInt("charId");
                var charName = row.GetString("charName");
                var guildRank = row.GetInt("guildRank");
                //Console.WriteLine($"{guildId}, {guildName}, {charId}, {charName}, {guildRank}");
                // si la guilde n'a pas encore été initialisée, le faire maintenant
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

        // Parce que sqlite et mysql divergent lors de la gestion des opérations upsert (insert ou update), on a deux fonctions différentes
        // L'identifiant ici est level+port
        public override async Task SaveServerInfo(string serializedServerInfo, int port, string level)
        {
            var cmd = GetCommand("INSERT OR REPLACE INTO `servers` (`id`, `port`, `level`, `serialized`) VALUES (NULL, @port, @level, @serialized)");
            cmd.AddParam("@port", port);
            cmd.AddParam("@level", level);
            cmd.AddParam("@serialized", serializedServerInfo);
            await RunNonQuery(cmd);
            Console.WriteLine($"{DateTime.Now:HH:mm} Serveur Info ({port}-{level}) a été enregistré dans la base de données.");
        }

        // Parce que sqlite et mysql divergent lors de la gestion des opérations upsert (insert ou update), on a deux fonctions différentes
        // L'identifiant dans la BDD est une combinaison de level+port+objectId
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