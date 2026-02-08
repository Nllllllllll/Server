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
            GetIdentitySqlCommand = "SELECT LAST_INSERT_ID();";
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
            // Commande qui vérifie si notre base de données existe
            string cmdStr = $"SHOW DATABASES LIKE '{settings.MysqlDatabase}';";
            string firstTimeConnectionStr = $"Server={settings.MysqlHost};Port={settings.MysqlPort};Uid={settings.MysqlUser};Pwd={settings.MysqlPassword};";
            var doesDbExistQuery = await RunQuery(cmdStr, firstTimeConnectionStr);

            if (!doesDbExistQuery.HasRows())
            {
                Console.Write("Base de données non trouvée : création... ");
                string collation = settings.MysqlAccentSensitiveCollation ? "utf8mb4_0900_as_ci" : "utf8mb4_0900_ai_ci";
                cmdStr = $"CREATE DATABASE {settings.MysqlDatabase} CHARACTER SET utf8mb4 COLLATE {collation};";
                await RunNonQuery(cmdStr, firstTimeConnectionStr);
                Console.WriteLine("done.");
            }
            else
            {
                Console.WriteLine("Database found: " + doesDbExistQuery.GetString(0, 0));
            }

            // Les tables sont créées si la BDD ne les a pas
            await RunNonQuery(@"
                CREATE TABLE IF NOT EXISTS accounts (
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
                    prefix varchar(50),
                    level int NOT NULL DEFAULT '1',
                    experience bigint NOT NULL DEFAULT '0',
                    experience_to_next_level bigint NOT NULL DEFAULT '100',
                    class varchar(50) DEFAULT '',
                    species varchar(50) DEFAULT '',
                    gender varchar(20) DEFAULT '',
                    appearance text,
                    stats text,
                    inventory text,
                    equipment text,
                    abilities text,
                    quests text,
                    zone varchar(100) DEFAULT '',
                    position_x float DEFAULT 0,
                    position_y float DEFAULT 0,
                    position_z float DEFAULT 0,
                    rotation_yaw float DEFAULT 0,
                    is_new_character boolean DEFAULT FALSE,
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
                ) ENGINE = InnoDB;
                
                CREATE TABLE IF NOT EXISTS level_config (
                    level int NOT NULL,
                    experience_required bigint NOT NULL,
                    PRIMARY KEY (level)
                ) ENGINE = InnoDB;
            ");

            // Vérifier et ajouter la colonne prefix si nécessaire
            var checkPrefixQuery = await RunQuery($@"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = '{settings.MysqlDatabase}' 
                AND TABLE_NAME = 'characters' 
                AND COLUMN_NAME = 'prefix';
            ");

            if (!checkPrefixQuery.HasRows())
            {
                Console.Write("Ajout de la colonne 'prefix'...");
                await RunNonQuery("ALTER TABLE characters ADD COLUMN prefix varchar(50);");
                Console.WriteLine("Fait.");
            }

            // Vérifier et ajouter les colonnes du système de niveaux si nécessaires
            var checkLevelQuery = await RunQuery($@"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = '{settings.MysqlDatabase}' 
                AND TABLE_NAME = 'characters' 
                AND COLUMN_NAME = 'level';
            ");

            if (!checkLevelQuery.HasRows())
            {
                Console.Write("Ajout des colonnes du système de niveaux...");
                await RunNonQuery(@"
                    ALTER TABLE characters 
                    ADD COLUMN level int NOT NULL DEFAULT '1',
                    ADD COLUMN experience bigint NOT NULL DEFAULT '0',
                    ADD COLUMN experience_to_next_level bigint NOT NULL DEFAULT '100';
                ");
                Console.WriteLine("Fait.");
            }

            // Vérifier et ajouter les nouvelles colonnes individuelles si nécessaires
            var checkClassQuery = await RunQuery($@"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = '{settings.MysqlDatabase}' 
                AND TABLE_NAME = 'characters' 
                AND COLUMN_NAME = 'class';
            ");

            if (!checkClassQuery.HasRows())
            {
                Console.Write("Ajout des nouvelles colonnes de personnage...");
                await RunNonQuery(@"
                    ALTER TABLE characters 
                    ADD COLUMN class VARCHAR(50) DEFAULT '',
                    ADD COLUMN species VARCHAR(50) DEFAULT '',
                    ADD COLUMN gender VARCHAR(20) DEFAULT '',
                    ADD COLUMN appearance TEXT,
                    ADD COLUMN stats TEXT,
                    ADD COLUMN inventory TEXT,
                    ADD COLUMN equipment TEXT,
                    ADD COLUMN abilities TEXT,
                    ADD COLUMN quests TEXT,
                    ADD COLUMN zone VARCHAR(100) DEFAULT '',
                    ADD COLUMN position_x FLOAT DEFAULT 0,
                    ADD COLUMN position_y FLOAT DEFAULT 0,
                    ADD COLUMN position_z FLOAT DEFAULT 0,
                    ADD COLUMN rotation_yaw FLOAT DEFAULT 0,
                    ADD COLUMN is_new_character BOOLEAN DEFAULT FALSE;
                ");
                Console.WriteLine("Fait.");
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

            var id = (int)dt.GetInt(0, "id")!;
            var status = dt.GetInt(0, "status")!;
            var passwordInDb = Encoding.UTF8.GetString(dt.GetBinaryArray(0, "password"));
            var salt = Encoding.UTF8.GetString(dt.GetBinaryArray(0, "salt"));

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

        public override async Task SaveServerInfo(string serializedServerInfo, int port, string level)
        {
            var cmd = GetCommand("INSERT INTO `servers` (`id`, `port`, `level`, `serialized`) VALUES (NULL, @port, @level, @serialized) ON DUPLICATE KEY UPDATE serialized = VALUES(serialized);");
            cmd.AddParam("@port", port);
            cmd.AddParam("@level", level);
            cmd.AddParam("@serialized", serializedServerInfo);
            await RunInsert(cmd);
            Console.WriteLine($"{DateTime.Now:HH:mm} Serveur Info ({port}-{level}) a été enregistré dans la base de données");
        }

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