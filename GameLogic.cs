using Microsoft.AspNetCore.Hosting.Server;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace PersistenceServer
{
    public class GameLogic
    {
        // Quand le joueur se connecte pour la première fois, il reçoit un cookie de connexion, et on le sauvegarde comme <cookie, accountId>
        // Il sélectionne ensuite un personnage avec lequel se connecter, puis charge un nouveau niveau, ce qui provoque une déconnexion
        // Une fois qu'il se reconnecte, il enverra son id de personnage désiré et un cookie au serveur de jeu
        // Le serveur de jeu le transmettra au serveur de persistance
        // Et on vérifiera si l'id de personnage existe sur l'accountId - si c'est le cas, on enverra le personnage au serveur de jeu
        // Et le serveur de jeufera apparaître le personnage pour le joueur
        // Et quand le joueur se reconnecte au serveur de persistance, il enverra exactement la même chose - cookie et id de personnage
        // Et on le connectera, si tout est en ordre
        // D'où, _connectionCookies survit aux déconnexions
        readonly Dictionary<string, int> _connectionCookies; // <cookie, account id>
        readonly Dictionary<UserConnection, GameServer> _gameServers;
        readonly Dictionary<string, List<GameServer>> _gameServersByZone; // toutes les instances hébergeant une zone particulière
        readonly Dictionary<string, GameServer> _gameServersByGuid;
        readonly Dictionary<string, Party> _partiesByGuid;                
        Dictionary<int, Guild> _guildsById; // toutes les guildes, même si les joueurs desdites guildes ne se sont pas connectés cette session
        readonly Dictionary<int, string> _playersLastServerId; // quand un serveur envoie un RPC GetCharacter, on enregistre ce serveur comme dernier "propriétaire" connu d'un joueur
        Dictionary<int, string> _storedPlayerPartyId; // quand un joueur se déconnecte, on stocke son id de groupe précédemment connu pour qu'il puisse être reconnecté au groupe quand il revient

        // Les dictionnaires ci-dessous ne survivent pas aux reconnexions, ils doivent être repeuplés lors des reconnexions
        readonly Dictionary<UserConnection, int> _accountIdByConnection;
        readonly Dictionary<int, UserConnection> _connectionByAccountId;
        readonly Dictionary<UserConnection, int> _charIdByConnection;
        readonly Dictionary<int, UserConnection> _connectionByCharId;
        readonly Dictionary<string, Player> _playersByName;
        readonly Dictionary<int, Player> _playersById;
        readonly Dictionary<UserConnection, Player> _playersByConnection;

        public GameLogic()
        {
            _connectionCookies = new();
            _accountIdByConnection = new();
            _connectionByAccountId = new();
            _charIdByConnection = new();
            _connectionByCharId = new();
            _playersByName = new();
            _playersById = new();
            _playersByConnection = new();
            _gameServers = new();
            _gameServersByZone = new();
            _gameServersByGuid = new();
            _guildsById =  new();
            _partiesByGuid = new();
            _playersLastServerId = new();
            _storedPlayerPartyId = new();
        }

        public int GetAccountId(UserConnection conn)
        {
            if(_accountIdByConnection.TryGetValue(conn, out var id))
                return id;
            return -1;
        }

        // Appelé quand l'utilisateur se connecte via le menu initial - il n'est pas dans le monde du jeu et n'a pas encore de personnage
        public void UserLoggedIn(int accountId, string cookie, UserConnection conn)
        {
            conn.Cookie = cookie;
            _connectionCookies.Add(cookie, accountId);
            if (_accountIdByConnection.Remove(conn)) // supprimer si la clé existe
            {
                Console.WriteLine("L'utilisateur a essayé de se connecter deux fois, nous ne devons jamais arriver ici");
                // essayer de récupérer
            }
            _accountIdByConnection.Add(conn, accountId);
            // en release, ne pas autoriser plusieurs connexions depuis un même compte
            // mais en debug, ce n'est pas inattendu, car on peut ouvrir deux fenêtres en PIE et obtenir deux personnages du même compte
            // @TODO: peut-être qu'on devrait créer un bool allowMultipleCharacters et changer ConnectionByAccountId en Dictionary<int, List<UserConnection>>
#if RELEASE
            if (_connectionByAccountId.TryGetValue(accountId, out var oldConn))
            {
                InvalidateCookieForConnection(oldConn);
                DisconnectPlayerFromAllGameServers(oldConn);
                _ = oldConn.Disconnect(); // not awaited
                UserDisconnected(oldConn); // fire this immediately, because otherwise it would fire too late due to threads jumping
            }
#else
            if (!_connectionByAccountId.ContainsKey(accountId))
                _connectionByAccountId.Add(accountId, conn);
#endif
        }
        
        public int GetAccountIdByCookie(string cookie)
        {
            return _connectionCookies.TryGetValue(cookie, out var connectionValue) ? connectionValue : -1;
        }

        // IMPORTANT : ne pas l'appeler pour les personnages connectés, car deux personnages connectés du même compte en PIE obtiendront la même connexion,
        // ce qui causera probablement des bugs. Sauf si vous interdisez explicitement la fonctionnalité qui y est associée en PIE.
        public UserConnection? GetConnectionByAccountId(int accountId)
        {
            return _connectionByAccountId.TryGetValue(accountId, out var connection) ? connection : null;
        }

        public void UserDisconnected(UserConnection conn)
        {
            if (_gameServers.TryGetValue(conn, out GameServer? gameServer))
            {                
                if (_gameServersByZone.TryGetValue(gameServer.Zone, out List<GameServer>? zoneInstances))
                {
                    zoneInstances.Remove(gameServer);
                }
                _gameServersByGuid.Remove(conn.Id.ToString());
                _gameServers.Remove(conn);
                Console.WriteLine($"{DateTime.Now:HH:mm} Serveur de jeu déconnecté");
            }
            if (_charIdByConnection.TryGetValue(conn, out var playerId))
            {
                _connectionByCharId.Remove(playerId);
                _charIdByConnection.Remove(conn);
            }
            if (_accountIdByConnection.TryGetValue(conn, out var userid))
            {
                _connectionByAccountId.Remove(userid);
                _accountIdByConnection.Remove(conn);
            }
            if (_playersByConnection.TryGetValue(conn, out var player))
            {
                var guildId = player.GuildId;
                if (guildId != -1 && _guildsById.TryGetValue(guildId, out var guild)) {
                    guild.OnPlayerDisconnected(player);
                }

                var charname = player.Name;
                var charId = player.CharId;
                Console.WriteLine($"{DateTime.Now:HH:mm} Joueur déconnecté : {charname}");
                _playersByConnection.Remove(conn);
                _playersByName.Remove(charname);
                _playersById.Remove(charId);
            }            
        }

        // Appelé quand l'utilisateur se connecte depuis le monde du jeu - il a maintenant un personnage
        public Player UserReconnected(UserConnection newConn, DatabaseCharacterInfo charInfo)
        {            
            if (_connectionByAccountId.TryGetValue(charInfo.AccountId, out var oldConn))
            {
                // En RELEASE, on n'autorise pas plusieurs personnages d'un même compte
                // Mais en debug, ce n'est pas inattendu, car on peut ouvrir deux fenêtres en PIE et obtenir deux personnages du même compte
                // @TODO : peut-être qu'on devrait créer un bool allowMultipleCharacters et changer ConnectionByAccountId en Dictionary<int, List<UserConnection>>
#if RELEASE
                InvalidateCookieForConnection(oldConn);
                DisconnectPlayerFromAllGameServers(oldConn);
                _ = oldConn.Disconnect(); // not awaited
                UserDisconnected(oldConn); // fire this immediately, because otherwise it would fire too late due to threads jumping

                _accountIdByConnection.Add(newConn, charInfo.AccountId);
                _connectionByAccountId.Add(charInfo.AccountId, newConn);
#endif
            }
            else {
                _accountIdByConnection.Add(newConn, charInfo.AccountId);
                _connectionByAccountId.Add(charInfo.AccountId, newConn);
            }

            _connectionByCharId.Add(charInfo.CharId, newConn);
            _charIdByConnection.Add(newConn, charInfo.CharId);
            Player newPlayer = new(newConn, charInfo);
            _playersByConnection.Add(newConn, newPlayer);
            _playersByName.Add(charInfo.Name, newPlayer);
            _playersById.Add(charInfo.CharId, newPlayer);
            if (charInfo.Guild != null)
            {
                if (_guildsById.TryGetValue((int)charInfo.Guild, out var guild))
                    guild.OnPlayerConnected(newPlayer);
                else
                    Console.WriteLine("Un joueur est connecté avec un identifiant de guilde inexistant. Cela ne devrait jamais arriver, vérifiez la situation.");
            }
            return newPlayer;
        }

        public void ServerConnected(UserConnection conn, GameServer server)
        {
            _gameServers.Add(conn, server);
            _gameServersByGuid.Add(conn.Id.ToString(), server);
            if (_gameServersByZone.TryGetValue(server.Zone, out List<GameServer>? serverInstancesForZone))
            {
                serverInstancesForZone.Add(server);
            } else
            {
                _gameServersByZone.Add(server.Zone, new List<GameServer> { server });
            }
        }

        public bool IsServer(UserConnection conn)
        {
            return _gameServers.ContainsKey(conn);
        }

        public async Task<GameServer> GetOrStartServerForZone(string zone)
        {
            //@TODO : lancer une instance s'il n'y a pas de serveur exécutant une zone particulière, ou si on est au-dessus de la limite de joueurs dans toutes les instances
            // mais pour l'instant, retournons simplement la première instance
            if (_gameServersByZone.TryGetValue(zone, out List<GameServer>? serverInstances))
            {
                //@TODO : vérifier s'il y a une instance avec suffisamment d'emplacements joueurs libres, sinon en lancer une, etc
                return serverInstances[0];
            }
            else
            {
                // attend temporairement Task.CompletedTask pour éviter l'avertissement du compilateur dans une méthode incomplète
                await Task.CompletedTask;
                throw new NotImplementedException("Cette méthode n'est pas encore implémentée.");
            }
        }

        public UserConnection[] GetAllPlayerConnections()
        {
            return _playersByConnection.Keys.ToArray();
        }

        public Player? GetPlayerByName(string name)
        {
            return _playersByName.TryGetValue(name, out var player) ? player : null;
        }

        public Player? GetPlayerById(int charId)
        {
            return _playersById.TryGetValue(charId, out var player) ? player : null;
        }

        public Player? GetPlayerByConnection(UserConnection conn)
        {
            return _playersByConnection.TryGetValue(conn, out var player) ? player : null;
        }

        public UserConnection? GetConnectionByCharId(int charId)
        {
            return _connectionByCharId.TryGetValue(charId, out var conn) ? conn : null;
        }

        public int GetPlayersOnline()
        {
            return _playersByConnection.Count;
        }

        public GameServer? GetServerByConnection(UserConnection conn)
        {
            return _gameServers.TryGetValue(conn, out var server) ? server : null;
        }

        public GameServer? GetServerByGuid(string guid)
        {
            return _gameServersByGuid.TryGetValue(guid, out var server) ? server : null;
        }

        public string GetPlayerName(UserConnection conn)
        {
            return _playersByConnection.TryGetValue(conn, out var player) ? player.Name : "";
        }

        public UserConnection[] GetAllServerConnections()
        {
            return _gameServers.Keys.ToArray();
        }

        public void DisconnectPlayerFromAllGameServers(UserConnection conn)
        {            
            if (_charIdByConnection.TryGetValue(conn, out var oldCharId))
            {
                byte[] msgToServers = BaseRpc.MergeByteArrays(BaseRpc.ToBytes(RpcType.RpcForceDisconnectPlayer), BaseRpc.ToBytes(oldCharId));
                foreach (var serverConn in GetAllServerConnections())
                {
                    serverConn.Send(msgToServers);
                }
            }
        }

        public void InvalidateCookieForConnection(UserConnection conn)
        {
            _connectionCookies.Remove(conn.Cookie);
        }

        public Guild? GetPlayerGuild(UserConnection conn)
        {
            if (_playersByConnection.TryGetValue(conn, out var player))
            {
                var guildId = player.GuildId;
                return _guildsById.TryGetValue(guildId, out var guild) ? guild : null;
            }
            return null;
        }

        public Guild? GetGuildById(int guildId)
        {
            return _guildsById.TryGetValue(guildId, out var guild) ? guild : null;
        }

        public void AssignGuilds(Dictionary<int, Guild> guilds)
        {
            _guildsById = guilds;
        }

        public void CreateGuild(Guild createdGuild, Player guildLeader)
        {
            _guildsById.Add(createdGuild.Id, createdGuild);
            createdGuild.PopulateMember(guildLeader.CharId, guildLeader.Name, 0);
            createdGuild.OnPlayerConnected(guildLeader);
            guildLeader.GuildId = createdGuild.Id;
            guildLeader.GuildRank = 0; // 0 est le chef
        }

        public void DeleteGuild(int guildId)
        {
            _guildsById.Remove(guildId);
        }

        public void SetPlayersServer(int charId, string serverId)
        {
            if (_playersLastServerId.ContainsKey(charId))
            {
                _playersLastServerId[charId] = serverId;
            } else
            {
                _playersLastServerId.Add(charId, serverId);
            }
        }

        public GameServer? GetPlayerServer(int charId)
        {
            if (_playersLastServerId.TryGetValue(charId, out var serverGuid))
            {
                return GetServerByGuid(serverGuid);
            }
            return null;
        }

        public void AddParty(Party party)
        {
            _partiesByGuid.Add(party.Id, party);
        }

        public void RemoveParty(Party party)
        {
            _partiesByGuid.Remove(party.Id);
        }

        public Party? GetPartyById(string Id)
        {
            return _partiesByGuid.TryGetValue(Id, out var party) ? party : null;
        }

        // stocker l'id du groupe pour un personnage déconnecté
        // si le personnage se reconnecte avant d'être expulsé, on pourra lui réassigner sa référence de groupe
        public void StoreDisconnectedPlayerPartyId(int charId, string partyId)
        {
            _storedPlayerPartyId[charId] = partyId;
        }

        public void UnstoreDisconnectedPlayerPartyId(int charId)
        {
            _storedPlayerPartyId.Remove(charId);
        }

        public string? GetStoredDisconnectedPlayerPartyId(int charId)
        {
            return _storedPlayerPartyId.TryGetValue(charId, out var partyId) ? partyId : null;
        }

#pragma warning disable CA1822 // supprimer l'avertissement "make it static", AddGuildMember pourrait avoir besoin d'opérer sur des champs plus tard
        public void AddGuildMember(Guild guild, Player player, int rank)
#pragma warning restore CA1822
        {
            guild.PopulateMember(player.CharId, player.Name, rank);
            guild.OnPlayerConnected(player);
            player.GuildId = guild.Id;
            player.GuildRank = rank;
        }

#pragma warning disable CA1822 // supprimer l'avertissement "make it static", DeleteGuildMember pourrait avoir besoin d'opérer sur des champs plus tard
        // la suppression suppose que le joueur n'est pas en ligne, donc on a seulement besoin de le retirer de Members
        public void DeleteGuildMember(Guild guild, int charId)
#pragma warning restore CA1822
        {
            guild.RemoveMemberById(charId);
        }

#pragma warning disable CA1822 // supprimer l'avertissement "make it static", RemoveGuildMember pourrait avoir besoin d'opérer sur des champs plus tard
        public void RemoveGuildMember(Guild guild, int charId, Player? player)
#pragma warning restore CA1822
        {
            if (player != null)
            {
                guild.OnPlayerDisconnected(player);
                player.GuildId = -1;
                player.GuildRank = -1;
            }
            guild.RemoveMemberById(charId);
        }
    }
}
