using MySqlConnector;
using System.Data.Common;
using System.Text;

namespace PersistenceServer
{
    public class DatabaseMysql : Database
    {
        public DatabaseMysql(SettingsReader settings) : base(settings)
        {
            ConnectionParams = $"Server={settings.MysqlHost};" +
                $"Port={settings.MysqlPort};" +
                $"Uid={settings.MysqlUser};" +
                $"Pwd={settings.MysqlPassword};" +
                $"Database={settings.MysqlDatabase}; Allow User Variables=True;";
            GetIdentitySqlCommand = "SELECT @@IDENTITY;";
        }

        protected override async Task<DbConnection> GetConnection(string parameters)
        {
            var connection = new MySqlConnection(parameters);
            await connection.OpenAsync();
            return connection;
        }

        protected override DbCommand GetCommand(string parameters, DbConnection? connection) => new MySqlCommand(parameters, (MySqlConnection?)connection);

        public override async Task CheckCreateDatabase(SettingsReader settings)
        {
            // commande qui vérifie si notre base de données existe
            string cmdStr = $"SHOW DATABASES LIKE '{settings.MysqlDatabase}';";
            // cas particulier pour la chaîne de connexion : on ne spécifie pas la base de données, car elle peut ne pas encore exister
            string firstTimeConnectionStr = $"Server={settings.MysqlHost};Port={settings.MysqlPort};Uid={settings.MysqlUser};Pwd={settings.MysqlPassword};";
            var doesDbExistQuery = await RunQuery(cmdStr, firstTimeConnectionStr);
            // s'il n'y a pas de base de données, en créer une
            if (!doesDbExistQuery.HasRows())
            {
                Console.Write("Base de données non trouvée : création... ");

                // créer la base de données
                string collation = settings.MysqlAccentSensitiveCollation ? "utf8mb4_0900_as_ci" : "utf8mb4_0900_ai_ci";
                cmdStr = $"CREATE DATABASE {settings.MysqlDatabase} CHARACTER SET utf8mb4 COLLATE {collation};";
                await RunNonQuery(cmdStr, firstTimeConnectionStr);
                // ~créer la base de données

                Console.WriteLine("done.");
            }
            else
            {
                Console.WriteLine("Database found: " + doesDbExistQuery.GetString(0, 0));
            }

            // les tables sont créées si la BDD ne les a pas
            await RunNonQuery(
                    @"CREATE TABLE IF NOT EXISTS accounts (
	                    id int NOT NULL AUTO_INCREMENT,
	                    name varchar(50),
                        steamid varchar(20),
	                    password BINARY(60),
	                    salt BINARY(60),
	                    email varchar(255),
	                    status int,
	                    last_ip varchar(45),
	                    last_mac varchar(17),
	                    PRIMARY KEY (id),
	                    UNIQUE INDEX NAME (name),
                        UNIQUE INDEX STEAMID (steamid)
                    ) ENGINE = InnoDB;
                    CREATE TABLE IF NOT EXISTS guilds (
	                    id int NOT NULL AUTO_INCREMENT,
	                    name varchar(50) NOT NULL,
                        serialized text,
	                    PRIMARY KEY (id),
	                    UNIQUE INDEX NAME (name)
                    ) ENGINE = InnoDB;
                    CREATE TABLE IF NOT EXISTS characters (
	                    id int NOT NULL AUTO_INCREMENT,
	                    name varchar(50) NOT NULL,
                        owner int,
	                    guild int,
	                    guildrank int,
                        permissions int NOT NULL DEFAULT '0',
	                    serialized text,
	                    prefix varchar(50),
	                    PRIMARY KEY (id),
	                    UNIQUE INDEX NAME (name),
                        INDEX OWNER (owner),
                        INDEX GUILD (guild),
                        CONSTRAINT character_owner_fk FOREIGN KEY (owner) REFERENCES accounts(id) ON UPDATE CASCADE ON DELETE SET NULL,
                        CONSTRAINT character_guild_fk FOREIGN KEY (guild) REFERENCES guilds(id) ON UPDATE CASCADE ON DELETE SET NULL
                    ) ENGINE = InnoDB;
                    CREATE TABLE IF NOT EXISTS servers (
                        id int NOT NULL AUTO_INCREMENT,
                        port int NOT NULL,
                        level text NOT NULL,
                        serialized text,
                        PRIMARY KEY (id),
                        UNIQUE INDEX PORT_LEVEL (`port`, `level`(100))
                    ) ENGINE = InnoDB;
                    CREATE TABLE IF NOT EXISTS persistentobjs (
                        id int NOT NULL AUTO_INCREMENT,
                        level text NOT NULL,
                        port int NOT NULL,
                        objectId int NOT NULL,
                        serialized text NOT NULL,
                        PRIMARY KEY (id),
                        UNIQUE INDEX PERSISTENT_OBJ_INDEX (`objectId`, `port`, `level`(100))
                    ) ENGINE = InnoDB;"
                );
            // ~créer les tables
            
            // Vérifier si la colonne prefix existe et l'ajouter si nécessaire
            var checkColumnQuery = await RunQuery($@"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = '{settings.MysqlDatabase}' 
                AND TABLE_NAME = 'characters' 
                AND COLUMN_NAME = 'prefix';
            ");
            
            if (!checkColumnQuery.HasRows())
            {
                Console.Write("Ajout de la colonne 'prefix' à la table characters...");
                await RunNonQuery("ALTER TABLE characters ADD COLUMN prefix varchar(50);");
                Console.WriteLine("Fait.");
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

            var id = (int)dt.GetInt(0, "id")!;
            var status = dt.GetInt(0, "status")!;
            var passwordInDb = Encoding.UTF8.GetString(dt.GetBinaryArray(0, "password"));
            var salt = Encoding.UTF8.GetString(dt.GetBinaryArray(0, "salt"));

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
            // si le nom est pris, l'insertion échouera silencieusement, retournant 0 lignes insérées
            var cmd = GetCommand("INSERT IGNORE INTO `guilds` (`id`, `name`) VALUES (NULL, @guildName);");
            cmd.AddParam("@guildName", guildName);
            int lastInsertedId = await RunInsert(cmd);
            if (lastInsertedId == 0)
            {
                return null;
            }
            var cmd2 = GetCommand("UPDATE `characters` SET `guild` = @guildId, `guildRank` = '0' WHERE `characters`.`id` = @charId; ");
            cmd2.AddParam("@guildId", lastInsertedId);
            cmd2.AddParam("@charId", charId);
            await RunNonQuery(cmd2);
            return new Guild(lastInsertedId, guildName);
        }

        // Parce que sqlite et mysql divergent lors de la gestion des opérations upsert (insert ou update), on a deux fonctions différentes
        // L'identifiant ici est level+port
        public override async Task SaveServerInfo(string serializedServerInfo, int port, string level)
        {
            var cmd = GetCommand("INSERT INTO `servers` (`id`, `port`, `level`, `serialized`) VALUES (NULL, @port, @level, @serialized) ON DUPLICATE KEY UPDATE serialized = VALUES(serialized);");
            cmd.AddParam("@port", port);
            cmd.AddParam("@level", level);
            cmd.AddParam("@serialized", serializedServerInfo);
            await RunInsert(cmd);
            Console.WriteLine($"{DateTime.Now:HH:mm} Serveur Info ({port}-{level}) a été enregistré dans la base de données");
        }

        // Parce que sqlite et mysql divergent lors de la gestion des opérations upsert (insert ou update), on a deux fonctions différentes
        // L'identifiant dans la BDD est une combinaison de level+port+objectId
        public override async Task SavePersistentObject(string level, int port, int objectId, string jsonString)
        {
            var cmd = GetCommand("INSERT INTO `persistentobjs` (`id`, `level`, `port`, `objectId`, `serialized`) VALUES (NULL, @level, @port, @objectId, @serialized) ON DUPLICATE KEY UPDATE serialized = VALUES(serialized);");
            cmd.AddParam("@level", level); 
            cmd.AddParam("@port", port);
            cmd.AddParam("@objectId", objectId);
            cmd.AddParam("@serialized", jsonString);
            await RunInsert(cmd);
        }
    }
}