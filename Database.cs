using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;

namespace PersistenceServer
{
    public class DatabaseCharacterInfo
    {
        public int AccountId { get; set; }
        public int CharId { get; set; }
        public string Name { get; set; } = "";
        public int Permissions { get; set; }
        public int? Guild { get; set; }
        public int? GuildRank { get; set; }
        public string Prefix { get; set; } = "";

        // Propriétés du système de niveaux
        public int Level { get; set; } = 1;
        public long Experience { get; set; } = 0;
        public long ExperienceToNextLevel { get; set; } = 100;

        // Nouvelles propriétés individuelles (remplacent SerializedCharacter)
        public string Class { get; set; } = "";
        public string Species { get; set; } = "";
        public string Gender { get; set; } = "";
        public string Appearance { get; set; } = "{}"; // JSON pour les détails d'apparence
        public string Stats { get; set; } = "{}"; // JSON pour les statistiques
        public string Inventory { get; set; } = "{}"; // JSON pour l'inventaire
        public string Equipment { get; set; } = "{}"; // JSON pour l'équipement
        public string Abilities { get; set; } = "{}"; // JSON pour les capacités
        public string Quests { get; set; } = "{}"; // JSON pour les quêtes
        public string Zone { get; set; } = "";
        public float PositionX { get; set; } = 0f;
        public float PositionY { get; set; } = 0f;
        public float PositionZ { get; set; } = 0f;
        public float RotationYaw { get; set; } = 0f;
        public bool IsNewCharacter { get; set; } = false;

        // Méthode pour convertir en JSON (pour compatibilité avec le code existant)
        public string ToSerializedJson()
        {
            var statsObj = string.IsNullOrEmpty(Stats) || Stats == "{}" ? new { } : Newtonsoft.Json.JsonConvert.DeserializeObject(Stats);
            var appearanceObj = string.IsNullOrEmpty(Appearance) || Appearance == "{}" ? new { } : Newtonsoft.Json.JsonConvert.DeserializeObject(Appearance);
            var inventoryObj = string.IsNullOrEmpty(Inventory) || Inventory == "{}" ? new { } : Newtonsoft.Json.JsonConvert.DeserializeObject(Inventory);
            var equipmentObj = string.IsNullOrEmpty(Equipment) || Equipment == "{}" ? new { } : Newtonsoft.Json.JsonConvert.DeserializeObject(Equipment);
            var abilitiesObj = string.IsNullOrEmpty(Abilities) || Abilities == "{}" ? new { } : Newtonsoft.Json.JsonConvert.DeserializeObject(Abilities);
            var questsObj = string.IsNullOrEmpty(Quests) || Quests == "{}" ? new { } : Newtonsoft.Json.JsonConvert.DeserializeObject(Quests);

            return Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Class = this.Class,
                Species = this.Species,
                Gender = this.Gender,
                Appearance = appearanceObj,
                Stats = statsObj,
                Inventory = inventoryObj,
                Equipment = equipmentObj,
                Abilities = abilitiesObj,
                Quests = questsObj,
                Zone = this.Zone,
                Position = new { X = this.PositionX, Y = this.PositionY, Z = this.PositionZ },
                Rotation = new { Yaw = this.RotationYaw },
                NewCharacter = this.IsNewCharacter
            });
        }

        // Méthode pour parser depuis JSON (pour la migration et la sauvegarde)
        public static DatabaseCharacterInfo FromSerializedJson(string json, int accountId, int charId, string name, int permissions, int? guild, int? guildRank, string prefix)
        {
            var info = new DatabaseCharacterInfo
            {
                AccountId = accountId,
                CharId = charId,
                Name = name,
                Permissions = permissions,
                Guild = guild,
                GuildRank = guildRank,
                Prefix = prefix
            };

            try
            {
                var jsonObject = JObject.Parse(json);

                info.Class = jsonObject["Class"]?.ToString() ?? "";
                info.Species = jsonObject["Species"]?.ToString() ?? "";
                info.Gender = jsonObject["Gender"]?.ToString() ?? "";

                // Parser les sous-objets JSON
                info.Appearance = jsonObject["Appearance"]?.ToString() ?? "{}";
                info.Stats = jsonObject["Stats"]?.ToString() ?? "{}";
                info.Inventory = jsonObject["Inventory"]?.ToString() ?? "{}";
                info.Equipment = jsonObject["Equipment"]?.ToString() ?? "{}";
                info.Abilities = jsonObject["Abilities"]?.ToString() ?? "{}";
                info.Quests = jsonObject["Quests"]?.ToString() ?? "{}";

                info.Zone = jsonObject["Zone"]?.ToString() ?? "";

                if (jsonObject["Position"] != null)
                {
                    info.PositionX = jsonObject["Position"]?["X"]?.Value<float>() ?? 0f;
                    info.PositionY = jsonObject["Position"]?["Y"]?.Value<float>() ?? 0f;
                    info.PositionZ = jsonObject["Position"]?["Z"]?.Value<float>() ?? 0f;
                }

                if (jsonObject["Rotation"] != null)
                {
                    info.RotationYaw = jsonObject["Rotation"]?["Yaw"]?.Value<float>() ?? 0f;
                }

                info.IsNewCharacter = jsonObject["NewCharacter"]?.Value<bool>() ?? false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors du parsing du JSON pour le personnage {charId}: {ex.Message}");
            }

            return info;
        }
    }

    public class DatabaseAccountInfo
    {
        public int AccountId;
        public string? Name;
        public string? SteamId;
        public int Status;
        public string? LastIp;
        public string? LastMac;
    }

    public abstract class Database
    {
        protected string ConnectionParams;
        protected readonly string Pepper = "$2a$11$46Z/ZIevW5fGpZFXJK5CMe";
        protected string GetIdentitySqlCommand;

#pragma warning disable CS8618, IDE0060
        protected Database(SettingsReader settings) { }
#pragma warning restore CS8618, IDE0060

        public abstract Task CheckCreateDatabase(SettingsReader settings);
        public abstract Task<Guild?> CreateGuild(string guildName, int charId);
        public abstract Task SaveServerInfo(string serializedServerInfo, int port, string level);
        public abstract Task SavePersistentObject(string level, int port, int objectId, string jsonString);
        protected abstract Task<DbConnection> GetConnection(string parameters);
        protected abstract DbCommand GetCommand(string parameters, DbConnection? connection);
        protected virtual DbCommand GetCommand(string parameters) => GetCommand(parameters, null);

        protected async Task<DataTable> RunQuery(string cmdParams, string overrideConnectionParams)
        {
            await using var conn = await GetConnection(overrideConnectionParams);
            await using var cmd = GetCommand(cmdParams, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            DataTable dt = new();
            dt.Load(reader);
            return dt;
        }

        protected async Task<DataTable> RunQuery(string cmdParams)
        {
            await using var conn = await GetConnection(ConnectionParams);
            await using var cmd = GetCommand(cmdParams, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            DataTable dt = new();
            dt.Load(reader);
            return dt;
        }

        protected async Task<DataTable> RunQuery(DbCommand command)
        {
            await using var conn = await GetConnection(ConnectionParams);
            command.Connection = conn;
            await using var reader = await command.ExecuteReaderAsync();
            DataTable dt = new();
            dt.Load(reader);
            await command.DisposeAsync();
            return dt;
        }

        protected async Task<int> RunNonQuery(string cmdParams, string overrideConnectionParams)
        {
            await using var conn = await GetConnection(overrideConnectionParams);
            await using var cmd = GetCommand(cmdParams, conn);
            return await cmd.ExecuteNonQueryAsync();
        }

        protected async Task<int> RunNonQuery(string cmdParams)
        {
            await using var conn = await GetConnection(ConnectionParams);
            await using var cmd = GetCommand(cmdParams, conn);
            return await cmd.ExecuteNonQueryAsync();
        }

        protected async Task<int> RunNonQuery(DbCommand command)
        {
            await using var conn = await GetConnection(ConnectionParams);
            command.Connection = conn;
            int rowsAffected = await command.ExecuteNonQueryAsync();
            await command.DisposeAsync();
            return rowsAffected;
        }

        protected async Task<int> RunInsert(DbCommand command)
        {
            await using var conn = await GetConnection(ConnectionParams);
            command.Connection = conn;

            // Exécuter l'insertion
            await command.ExecuteNonQueryAsync();

            // Récupérer l'ID inséré avec une commande séparée
            var idCmd = GetCommand(GetIdentitySqlCommand, conn);
            object? obj = await idCmd.ExecuteScalarAsync();
            await command.DisposeAsync();
            await idCmd.DisposeAsync();

            return (int)Convert.ChangeType(obj!, typeof(int));
        }

        public async Task HelloWorld()
        {
            var cmd = GetCommand(@"SET @helloWorldStr = ""Hello World!"";SELECT @helloWorldStr AS ""My Row"";");
            var dt = await RunQuery(cmd);
            Debug.Assert(dt.HasRows());
            Console.WriteLine(dt.Rows[0]["My Row"].ToString());
        }

        /******************** Below are gameplay related DB requests ****************/

        public virtual async Task<bool> DoesAccountExist(string accountName)
        {
            var cmd = GetCommand("SELECT * FROM accounts WHERE name = @accountName");
            cmd.AddParam("@accountName", accountName);
            var dt = await RunQuery(cmd);
            return dt.HasRows();
        }

        public virtual async Task<int> CreateUserAccount(string accountName, string password, string? ipAddress = null, string? macAddress = null)
        {
            string salt = BCrypt.Net.BCrypt.GenerateSalt();
            var cmd = GetCommand("INSERT INTO `accounts` (`id`, `name`, `steamid`, `password`, `salt`, `email`, `status`, `last_ip`, `last_mac`) VALUES (NULL, @accountName, NULL, @password, @salt, NULL, 0, @lastIp, @lastMac);");
            cmd.AddParam("@accountName", accountName);
            cmd.AddParam("@password", BCrypt.Net.BCrypt.HashPassword(password, salt + Pepper));
            cmd.AddParam("@salt", salt);
            cmd.AddParam("@lastIp", ipAddress);
            cmd.AddParam("@lastMac", macAddress);
            int lastInsertedId = await RunInsert(cmd);
            return lastInsertedId;
        }

        public virtual async Task<int> CreateSteamAccount(string steamId, string? ipAddress = null, string? macAddress = null)
        {
            var cmd = GetCommand("INSERT INTO `accounts` (`id`, `name`, `steamid`, `password`, `salt`, `email`, `status`, `last_ip`, `last_mac`) VALUES (NULL, NULL, @steamid, NULL, NULL, NULL, 0, @lastIp, @lastMac);");
            cmd.AddParam("@steamid", steamId);
            cmd.AddParam("@lastIp", ipAddress);
            cmd.AddParam("@lastMac", macAddress);
            int lastInsertedId = await RunInsert(cmd);
            return lastInsertedId;
        }

        public abstract Task<int> LoginUser(string accountName, string password);

        public virtual async Task<int> LoginSteamUser(string steamId, string? ipAddress = null, string? macAddress = null)
        {
            var cmd = GetCommand("SELECT id, status FROM accounts WHERE steamid = @steamId");
            cmd.AddParam("@steamId", steamId);
            var dt = await RunQuery(cmd);

            if (!dt.HasRows())
            {
                int newId = await CreateSteamAccount(steamId, ipAddress, macAddress);
                return newId;
            }
            else
            {
                var id = (int)dt.GetInt(0, "id")!;
                var status = dt.GetInt(0, "status")!;

                if (status == -1)
                {
                    return -1;
                }

                if (ipAddress != null || macAddress != null)
                {
                    await UpdateLoginInfo(id, ipAddress, macAddress);
                }

                return id;
            }
        }

        public virtual async Task UpdateLoginInfo(int accountId, string? ipAddress, string? macAddress)
        {
            var cmd = GetCommand("UPDATE `accounts` SET `last_ip` = @lastIp, `last_mac` = @lastMac WHERE `id` = @accountId");
            cmd.AddParam("@accountId", accountId);
            cmd.AddParam("@lastIp", ipAddress);
            cmd.AddParam("@lastMac", macAddress);
            await RunNonQuery(cmd);
        }

        public virtual async Task<DatabaseAccountInfo?> GetAccountInfo(int accountId)
        {
            var cmd = GetCommand("SELECT id, name, steamid, status, last_ip, last_mac FROM accounts WHERE id = @accountId");
            cmd.AddParam("@accountId", accountId);
            var dt = await RunQuery(cmd);
            if (!dt.HasRows()) return null;
            var row = dt.Rows[0];

            DatabaseAccountInfo accountInfo = new()
            {
                AccountId = (int)row.GetInt("id")!,
                Name = row.GetString("name"),
                SteamId = row.GetString("steamid"),
                Status = (int)row.GetInt("status")!,
                LastIp = row.GetString("last_ip"),
                LastMac = row.GetString("last_mac")
            };
            return accountInfo;
        }

        public virtual async Task<bool> DoesCharnameExist(string charName)
        {
            var cmd = GetCommand("SELECT * FROM characters WHERE name = @charName");
            cmd.AddParam("@charName", charName);
            var dt = await RunQuery(cmd);
            return dt.HasRows();
        }

        public virtual async Task<int> CreateCharacter(string charName, int ownerAccountId, bool gmCharacter, DatabaseCharacterInfo charInfo)
        {
            var cmd = GetCommand(@"
                INSERT INTO characters 
                (id, name, owner, guild, guildrank, permissions, prefix,
                 level, experience, experience_to_next_level,
                 class, species, gender, appearance, stats, inventory, equipment,
                 abilities, quests, zone, position_x, position_y, position_z,
                 rotation_yaw, is_new_character)
                VALUES 
                (NULL, @charName, @ownerAccountId, NULL, NULL, @permissions, '',
                 @level, @experience, @expToNext,
                 @class, @species, @gender, @appearance, @stats, @inventory, @equipment,
                 @abilities, @quests, @zone, @posX, @posY, @posZ,
                 @rotYaw, @isNew)
            ");

            cmd.AddParam("@charName", charName);
            cmd.AddParam("@ownerAccountId", ownerAccountId);
            cmd.AddParam("@permissions", gmCharacter ? 11 : 0);
            cmd.AddParam("@level", charInfo.Level);
            cmd.AddParam("@experience", charInfo.Experience);
            cmd.AddParam("@expToNext", charInfo.ExperienceToNextLevel);
            cmd.AddParam("@class", charInfo.Class);
            cmd.AddParam("@species", charInfo.Species);
            cmd.AddParam("@gender", charInfo.Gender);
            cmd.AddParam("@appearance", charInfo.Appearance);
            cmd.AddParam("@stats", charInfo.Stats);
            cmd.AddParam("@inventory", charInfo.Inventory);
            cmd.AddParam("@equipment", charInfo.Equipment);
            cmd.AddParam("@abilities", charInfo.Abilities);
            cmd.AddParam("@quests", charInfo.Quests);
            cmd.AddParam("@zone", charInfo.Zone);
            cmd.AddParam("@posX", charInfo.PositionX);
            cmd.AddParam("@posY", charInfo.PositionY);
            cmd.AddParam("@posZ", charInfo.PositionZ);
            cmd.AddParam("@rotYaw", charInfo.RotationYaw);
            cmd.AddParam("@isNew", charInfo.IsNewCharacter);

            int lastInsertedId = await RunInsert(cmd);
            return lastInsertedId;
        }

        public virtual async Task<List<DatabaseCharacterInfo>> GetCharacters(int accountId)
        {
            List<DatabaseCharacterInfo> result = new();

            var cmd = GetCommand(@"
                SELECT id, owner, name, permissions, guild, guildrank, prefix,
                       level, experience, experience_to_next_level,
                       class, species, gender, appearance, stats, inventory, equipment,
                       abilities, quests, zone, position_x, position_y, position_z,
                       rotation_yaw, is_new_character
                FROM characters 
                WHERE owner = @accountId
            ");
            cmd.AddParam("@accountId", accountId);
            var dt = await RunQuery(cmd);

            foreach (var row in dt.Rows.OfType<DataRow>())
            {
                DatabaseCharacterInfo charInfo = new()
                {
                    AccountId = (int)row.GetInt("owner")!,
                    CharId = (int)row.GetInt("id")!,
                    Name = row.GetString("name")!,
                    Permissions = (int)row.GetInt("permissions")!,
                    Guild = row.GetInt("guild"),
                    GuildRank = row.GetInt("guildrank"),
                    Prefix = row.GetString("prefix") ?? "",
                    Level = (int)(row.GetInt("level") ?? 1),
                    Experience = (long)(row.GetInt("experience") ?? 0),
                    ExperienceToNextLevel = (long)(row.GetInt("experience_to_next_level") ?? 100),

                    Class = row.GetString("class") ?? "",
                    Species = row.GetString("species") ?? "",
                    Gender = row.GetString("gender") ?? "",
                    Appearance = row.GetString("appearance") ?? "{}",
                    Stats = row.GetString("stats") ?? "{}",
                    Inventory = row.GetString("inventory") ?? "{}",
                    Equipment = row.GetString("equipment") ?? "{}",
                    Abilities = row.GetString("abilities") ?? "{}",
                    Quests = row.GetString("quests") ?? "{}",
                    Zone = row.GetString("zone") ?? "",
                    PositionX = row["position_x"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_x"]),
                    PositionY = row["position_y"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_y"]),
                    PositionZ = row["position_z"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_z"]),
                    RotationYaw = row["rotation_yaw"] == DBNull.Value ? 0f : Convert.ToSingle(row["rotation_yaw"]),
                    IsNewCharacter = row.GetInt("is_new_character") == 1
                };
                result.Add(charInfo);
            }

            return result;
        }

        public virtual async Task<DatabaseCharacterInfo?> GetCharacter(int charId, int accountId)
        {
            var cmd = GetCommand(@"
                SELECT id, owner, name, permissions, guild, guildrank, prefix, 
                       level, experience, experience_to_next_level,
                       class, species, gender, appearance, stats, inventory, equipment, 
                       abilities, quests, zone, position_x, position_y, position_z, 
                       rotation_yaw, is_new_character
                FROM characters 
                WHERE id = @charId AND owner = @accountId
            ");
            cmd.AddParam("@charId", charId);
            cmd.AddParam("@accountId", accountId);
            var dt = await RunQuery(cmd);
            if (!dt.HasRows()) return null;
            var row = dt.Rows[0];

            DatabaseCharacterInfo character = new()
            {
                AccountId = (int)row.GetInt("owner")!,
                CharId = (int)row.GetInt("id")!,
                Name = row.GetString("name")!,
                Permissions = (int)row.GetInt("permissions")!,
                Guild = row.GetInt("guild"),
                GuildRank = row.GetInt("guildrank"),
                Prefix = row.GetString("prefix") ?? "",
                Level = (int)(row.GetInt("level") ?? 1),
                Experience = (long)(row.GetInt("experience") ?? 0),
                ExperienceToNextLevel = (long)(row.GetInt("experience_to_next_level") ?? 100),

                // Nouvelles colonnes
                Class = row.GetString("class") ?? "",
                Species = row.GetString("species") ?? "",
                Gender = row.GetString("gender") ?? "",
                Appearance = row.GetString("appearance") ?? "{}",
                Stats = row.GetString("stats") ?? "{}",
                Inventory = row.GetString("inventory") ?? "{}",
                Equipment = row.GetString("equipment") ?? "{}",
                Abilities = row.GetString("abilities") ?? "{}",
                Quests = row.GetString("quests") ?? "{}",
                Zone = row.GetString("zone") ?? "",
                PositionX = row["position_x"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_x"]),
                PositionY = row["position_y"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_y"]),
                PositionZ = row["position_z"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_z"]),
                RotationYaw = row["rotation_yaw"] == DBNull.Value ? 0f : Convert.ToSingle(row["rotation_yaw"]),
                IsNewCharacter = row.GetInt("is_new_character") == 1
            };
            return character;
        }

        public virtual async Task<DatabaseCharacterInfo?> GetCharacterByName(string charName, int accountId)
        {
            var cmd = GetCommand(@"
                SELECT id, owner, name, permissions, guild, guildrank, prefix,
                       level, experience, experience_to_next_level,
                       class, species, gender, appearance, stats, inventory, equipment,
                       abilities, quests, zone, position_x, position_y, position_z,
                       rotation_yaw, is_new_character
                FROM characters 
                WHERE name = @charName AND owner = @accountId
            ");
            cmd.AddParam("@charName", charName);
            cmd.AddParam("@accountId", accountId);
            var dt = await RunQuery(cmd);
            if (!dt.HasRows()) return null;
            var row = dt.Rows[0];

            DatabaseCharacterInfo character = new()
            {
                AccountId = (int)row.GetInt("owner")!,
                CharId = (int)row.GetInt("id")!,
                Name = row.GetString("name")!,
                Permissions = (int)row.GetInt("permissions")!,
                Guild = row.GetInt("guild"),
                GuildRank = row.GetInt("guildrank"),
                Prefix = row.GetString("prefix") ?? "",
                Level = (int)(row.GetInt("level") ?? 1),
                Experience = (long)(row.GetInt("experience") ?? 0),
                ExperienceToNextLevel = (long)(row.GetInt("experience_to_next_level") ?? 100),

                Class = row.GetString("class") ?? "",
                Species = row.GetString("species") ?? "",
                Gender = row.GetString("gender") ?? "",
                Appearance = row.GetString("appearance") ?? "{}",
                Stats = row.GetString("stats") ?? "{}",
                Inventory = row.GetString("inventory") ?? "{}",
                Equipment = row.GetString("equipment") ?? "{}",
                Abilities = row.GetString("abilities") ?? "{}",
                Quests = row.GetString("quests") ?? "{}",
                Zone = row.GetString("zone") ?? "",
                PositionX = row["position_x"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_x"]),
                PositionY = row["position_y"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_y"]),
                PositionZ = row["position_z"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_z"]),
                RotationYaw = row["rotation_yaw"] == DBNull.Value ? 0f : Convert.ToSingle(row["rotation_yaw"]),
                IsNewCharacter = row.GetInt("is_new_character") == 1
            };
            return character;
        }

        public virtual async Task<DatabaseCharacterInfo?> GetCharacterForPieWindow(int pieWindowId)
        {
            var cmd = GetCommand(@"
                SELECT id, owner, name, permissions, guild, guildrank, prefix,
                       level, experience, experience_to_next_level,
                       class, species, gender, appearance, stats, inventory, equipment,
                       abilities, quests, zone, position_x, position_y, position_z,
                       rotation_yaw, is_new_character
                FROM characters 
                ORDER BY id ASC 
                LIMIT @pieWindowId,1
            ");
            cmd.AddParam("@pieWindowId", pieWindowId);
            var dt = await RunQuery(cmd);
            if (!dt.HasRows()) return null;
            var row = dt.Rows[0];

            DatabaseCharacterInfo charInfo = new()
            {
                AccountId = (int)row.GetInt("owner")!,
                CharId = (int)row.GetInt("id")!,
                Name = row.GetString("name")!,
                Permissions = (int)row.GetInt("permissions")!,
                Guild = row.GetInt("guild"),
                GuildRank = row.GetInt("guildrank"),
                Prefix = row.GetString("prefix") ?? "",
                Level = (int)(row.GetInt("level") ?? 1),
                Experience = (long)(row.GetInt("experience") ?? 0),
                ExperienceToNextLevel = (long)(row.GetInt("experience_to_next_level") ?? 100),

                Class = row.GetString("class") ?? "",
                Species = row.GetString("species") ?? "",
                Gender = row.GetString("gender") ?? "",
                Appearance = row.GetString("appearance") ?? "{}",
                Stats = row.GetString("stats") ?? "{}",
                Inventory = row.GetString("inventory") ?? "{}",
                Equipment = row.GetString("equipment") ?? "{}",
                Abilities = row.GetString("abilities") ?? "{}",
                Quests = row.GetString("quests") ?? "{}",
                Zone = row.GetString("zone") ?? "",
                PositionX = row["position_x"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_x"]),
                PositionY = row["position_y"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_y"]),
                PositionZ = row["position_z"] == DBNull.Value ? 0f : Convert.ToSingle(row["position_z"]),
                RotationYaw = row["rotation_yaw"] == DBNull.Value ? 0f : Convert.ToSingle(row["rotation_yaw"]),
                IsNewCharacter = row.GetInt("is_new_character") == 1
            };
            return charInfo;
        }

        public async Task SaveCharacter(int charId, DatabaseCharacterInfo charInfo)
        {
            var cmd = GetCommand(@"
                UPDATE characters SET 
                    class = @class,
                    species = @species,
                    gender = @gender,
                    appearance = @appearance,
                    stats = @stats,
                    inventory = @inventory,
                    equipment = @equipment,
                    abilities = @abilities,
                    quests = @quests,
                    zone = @zone,
                    position_x = @posX,
                    position_y = @posY,
                    position_z = @posZ,
                    rotation_yaw = @rotYaw,
                    is_new_character = @isNew
                WHERE id = @charId
            ");

            cmd.AddParam("@charId", charId);
            cmd.AddParam("@class", charInfo.Class);
            cmd.AddParam("@species", charInfo.Species);
            cmd.AddParam("@gender", charInfo.Gender);
            cmd.AddParam("@appearance", charInfo.Appearance);
            cmd.AddParam("@stats", charInfo.Stats);
            cmd.AddParam("@inventory", charInfo.Inventory);
            cmd.AddParam("@equipment", charInfo.Equipment);
            cmd.AddParam("@abilities", charInfo.Abilities);
            cmd.AddParam("@quests", charInfo.Quests);
            cmd.AddParam("@zone", charInfo.Zone);
            cmd.AddParam("@posX", charInfo.PositionX);
            cmd.AddParam("@posY", charInfo.PositionY);
            cmd.AddParam("@posZ", charInfo.PositionZ);
            cmd.AddParam("@rotYaw", charInfo.RotationYaw);
            cmd.AddParam("@isNew", charInfo.IsNewCharacter);

            int result = await RunNonQuery(cmd);
            if (result == 1)
                Console.WriteLine($"{DateTime.Now:HH:mm} Character with id {charId} was saved to DB.");
            else
                Console.WriteLine($"{DateTime.Now:HH:mm} Character wasn't saved: {charId}!");
        }

        public async virtual Task<Dictionary<int, Guild>> GetGuilds()
        {
            var result = new Dictionary<int, Guild>();

            var cmd = GetCommand(@"
                SELECT guilds.id as guildId, guilds.name as guildName, characters.id as charId, characters.name as charName, characters.guildRank as guildRank FROM guilds
                LEFT JOIN characters
                ON guilds.id = characters.guild
            ");
            var dt = await RunQuery(cmd);
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
                    Console.WriteLine($"Info: guild \"{guildName}\" (id: {guildId}) is parentless and memberless");
                    continue;
                }
                result[guildId].PopulateMember((int)charId, charName, (int)guildRank!);
            }

            return result;
        }

        public async Task PlayerLeavesGuild(int charId)
        {
            var cmd = GetCommand("UPDATE `characters` SET `guild` = NULL, `guildRank` = NULL WHERE `characters`.`id` = @charId; ");
            cmd.AddParam("@charId", charId);
            await RunNonQuery(cmd);
        }

        public async Task DeleteGuild(int guildId)
        {
            var cmd = GetCommand("DELETE FROM `guilds` WHERE `guilds`.`id` = @guildId");
            cmd.AddParam("@guildId", guildId);
            await RunNonQuery(cmd);
        }

        public async Task<int> MakeNewGuildMaster(int guildId)
        {
            var cmd = GetCommand("SELECT `id` FROM `characters` where `guild` = @guildId ORDER BY `guildrank` ASC LIMIT 0,1 ");
            cmd.AddParam("@guildId", guildId);
            var dt = await RunQuery(cmd);
            var charId = (int)dt.Rows[0].GetInt("id")!;

            var cmd2 = GetCommand("UPDATE `characters` SET `guildRank` = '0' WHERE `id` = @charId");
            cmd2.AddParam("@charId", charId);
            await RunNonQuery(cmd2);

            return charId;
        }

        public async Task UpdateGuildRank(int charId, int rank)
        {
            var cmd = GetCommand("UPDATE `characters` SET `guildRank` = @rank WHERE `id` = @charId");
            cmd.AddParam("@rank", rank);
            cmd.AddParam("@charId", charId);
            await RunNonQuery(cmd);
        }

        public async Task DisbandGuild(int guildId)
        {
            var cmd = GetCommand("UPDATE `characters` SET `guild` = NULL, `guildRank` = NULL WHERE `guild` = @guildId");
            cmd.AddParam("@guildId", guildId);
            await RunNonQuery(cmd);

            var cmd2 = GetCommand("DELETE FROM `guilds` WHERE `guilds`.`id` = @guildId");
            cmd2.AddParam("@guildId", guildId);
            await RunNonQuery(cmd2);
        }

        public async Task AddGuildMember(int guildId, int memberId, int rank)
        {
            var updateCharCmd = GetCommand("UPDATE `characters` SET `guild` = @guildId, `guildRank` = @rank WHERE `characters`.`id` = @charId; ");
            updateCharCmd.AddParam("@guildId", guildId);
            updateCharCmd.AddParam("@rank", rank);
            updateCharCmd.AddParam("@charId", memberId);
            await RunNonQuery(updateCharCmd);
        }

        public async Task RemoveGuildMember(int memberId)
        {
            var updateCharCmd = GetCommand("UPDATE `characters` SET `guild` = NULL, `guildRank` = NULL WHERE `characters`.`id` = @charId; ");
            updateCharCmd.AddParam("@charId", memberId);
            await RunNonQuery(updateCharCmd);
        }

        public async Task<bool> DeleteCharacter(string charName, int accountId)
        {
            var deleteCharCmd = GetCommand("DELETE FROM `characters` WHERE `name` = @charName AND `owner` = @accountId;");
            deleteCharCmd.AddParam("@charName", charName);
            deleteCharCmd.AddParam("@accountId", accountId);
            int result = await RunNonQuery(deleteCharCmd);
            return (result == 1);
        }

        public virtual async Task<string?> GetServerInfo(int port, string level)
        {
            var cmd = GetCommand("SELECT serialized FROM servers WHERE port = @port and level = @level");
            cmd.AddParam("@port", port);
            cmd.AddParam("@level", level);
            var dt = await RunQuery(cmd);
            if (!dt.HasRows()) return null;
            var row = dt.Rows[0];
            return row.GetString("serialized");
        }

        public struct PersistentObject
        {
            public int ObjectId;
            public string JsonString;
            public PersistentObject(int inObjectId, string inJsonString)
            {
                ObjectId = inObjectId;
                JsonString = inJsonString;
            }
        }

        public virtual async Task<List<PersistentObject>> GetPersistentObjects(int port, string level)
        {
            List<PersistentObject> result = new();
            var cmd = GetCommand("SELECT objectId, serialized FROM persistentobjs WHERE port = @port and level = @level");
            cmd.AddParam("@port", port);
            cmd.AddParam("@level", level);
            var dt = await RunQuery(cmd);
            foreach (DataRow row in dt.Rows.OfType<DataRow>())
            {
                result.Add(new PersistentObject((int)row.GetInt("objectId")!, (string)row.GetString("serialized")!));
            }
            return result;
        }

        public virtual async Task DeletePersistentObject(string level, int port, int objectId)
        {
            var cmd = GetCommand("DELETE FROM `persistentobjs` WHERE level = @level and port = @port and objectId = @objectId");
            cmd.AddParam("@level", level);
            cmd.AddParam("@port", port);
            cmd.AddParam("@objectId", objectId);
            await RunNonQuery(cmd);
        }

        public async Task<bool> IsCharactersTableEmpty()
        {
            var cmd = GetCommand("SELECT EXISTS(SELECT 1 FROM characters LIMIT 1) as result;");
            var dt = await RunQuery(cmd);
            if (!dt.HasRows()) return false;
            var result = (int)dt.Rows[0].GetInt("result")!;
            return (result == 0);
        }

        /******************** SYSTÈME DE NIVEAUX - MÉTHODES ****************/

        /// <summary>
        /// Récupère les informations de niveau d'un personnage
        /// </summary>
        public virtual async Task<CharacterLevelInfo?> GetCharacterLevel(int charId)
        {
            var cmd = GetCommand("SELECT level, experience, experience_to_next_level FROM characters WHERE id = @charId");
            cmd.AddParam("@charId", charId);
            var dt = await RunQuery(cmd);

            if (!dt.HasRows()) return null;
            var row = dt.Rows[0];

            return new CharacterLevelInfo(
                charId,
                (int)row.GetInt("level")!,
                (long)row.GetInt("experience")!,
                (long)row.GetInt("experience_to_next_level")!
            );
        }

        /// <summary>
        /// Met à jour le niveau et l'expérience d'un personnage
        /// </summary>
        public virtual async Task UpdateCharacterLevel(int charId, int newLevel, long newExp, long expToNext)
        {
            var cmd = GetCommand(
                "UPDATE `characters` SET `level` = @level, `experience` = @experience, `experience_to_next_level` = @expToNext WHERE `id` = @charId"
            );
            cmd.AddParam("@level", newLevel);
            cmd.AddParam("@experience", newExp);
            cmd.AddParam("@expToNext", expToNext);
            cmd.AddParam("@charId", charId);

            int result = await RunNonQuery(cmd);

            if (result == 1)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Niveau du personnage {charId} mis à jour: niveau {newLevel}, XP {newExp}/{expToNext}");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} AVERTISSEMENT: Échec de la mise à jour du niveau pour le personnage {charId}!");
            }
        }

        /// <summary>
        /// Récupère la configuration de niveau depuis la base de données
        /// </summary>
        public virtual async Task<Dictionary<int, long>> GetLevelConfiguration()
        {
            var result = new Dictionary<int, long>();

            var cmd = GetCommand("SELECT level, experience_required FROM level_config ORDER BY level");
            var dt = await RunQuery(cmd);

            foreach (DataRow row in dt.Rows.OfType<DataRow>())
            {
                int level = (int)row.GetInt("level")!;
                long expRequired = (long)row.GetInt("experience_required")!;
                result[level] = expRequired;
            }

            return result;
        }

        /// <summary>
        /// Initialise la table de configuration des niveaux si elle est vide
        /// </summary>
        public virtual async Task InitializeLevelConfiguration()
        {
            // Vérifier si la table existe et est vide
            try
            {
                var checkCmd = GetCommand("SELECT COUNT(*) as count FROM level_config");
                var dt = await RunQuery(checkCmd);

                if (dt.HasRows() && (int)dt.GetInt(0, "count")! > 0)
                {
                    Console.WriteLine("Configuration des niveaux déjà initialisée.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AVERTISSEMENT: Impossible de vérifier la table level_config: {ex.Message}");
                Console.WriteLine("Assurez-vous d'avoir exécuté le script SQL AddLevelSystem.sql");
                return;
            }

            Console.WriteLine("Initialisation de la configuration des niveaux...");

            // Créer le système de niveaux et insérer les données
            var levelSystem = new LevelSystem();

            for (int level = 1; level <= LevelSystem.MAX_LEVEL; level++)
            {
                long expRequired = levelSystem.GetExperienceRequiredForLevel(level);

                try
                {
                    var insertCmd = GetCommand(
                        "INSERT INTO level_config (level, experience_required) VALUES (@level, @expRequired)"
                    );
                    insertCmd.AddParam("@level", level);
                    insertCmd.AddParam("@expRequired", expRequired);

                    await RunNonQuery(insertCmd);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur lors de l'insertion du niveau {level}: {ex.Message}");
                }
            }

            Console.WriteLine($"Configuration des niveaux initialisée pour {LevelSystem.MAX_LEVEL} niveaux.");
        }
    }
}