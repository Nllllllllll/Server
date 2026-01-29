using System;
using System.Numerics;

namespace PersistenceServer.RPCs
{

    /// Se produit lorsqu’un joueur se connecte à la carte monde et possède déjà un cookie valide issu d’un précédent <see cref="LoginPassword"/> ou <see cref="LoginWithSteam"/>  
    public class LoginClientWithCookie : BaseRpc
    {
        public LoginClientWithCookie()
        {
            RpcType = RpcType.RpcLoginClientWithCookie; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            var cookie = reader.ReadMmoString();
            var charId = reader.ReadInt32();
            if (charId < 0)
            {
                Console.WriteLine("LoginClientWithCookie: ID de caractère incorrect, doit être >= 0. Avez-vous lancé PIE dans un mode réseau incorrect ?");
                return;
            }
#if DEBUG
            if (Server!.Settings.UniversalCookie == cookie)
            {
                Server!.Processor.ConQ.Enqueue(async () => await ProcessLoginClientFromEditor(charId, connection));
            }
            else 
            {
                Server!.Processor.ConQ.Enqueue(async () => await ProcessLoginClientWithCookie(cookie, charId, connection));
            }
#else
            Server!.Processor.ConQ.Enqueue(async () => await ProcessLoginClientWithCookie(cookie, charId, connection));
#endif
        }

        private async Task ProcessLoginClientWithCookie(string cookie, int charId, UserConnection connection)
        {
            var accountId = Server!.GameLogic.GetAccountIdByCookie(cookie);
            if (accountId < 0)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Échec de LoginWithCookie pour le client : cookie incorrect");
                _ = connection.Disconnect(); // non attendu (non awaited)
                return;
            }
            
            var charInfo = await Server!.Database.GetCharacter(charId, accountId);
            if (charInfo != null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Client (IP: {connection.Ip}) reconnecté avec le personnage: {charInfo.Name}");
                ProcessLogin(charInfo, connection);
            }
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} LoginWithCookie échec pour le client : mauvais identifiant de caractère");
                _ = connection.Disconnect(); // non attendu (non awaited)
            }
        }

        // En configuration Debug, on suppose que le client se connecte depuis PIE (Play In Editor) et n'a donc pas de cookie valide  
        // Il n'a pas non plus de charId valide. Il fournira à la place l'ID de la fenêtre PIE, compté à partir de 0.
        private async Task ProcessLoginClientFromEditor(int pieWindowId, UserConnection connection)
        {
            var charInfo = await Server!.Database.GetCharacterForPieWindow(pieWindowId);
            if (charInfo == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} LoginWithCookie échec pour le client : pas assez de caractères dans la base de données pour la fenêtre PIE: {pieWindowId}");
                _ = connection.Disconnect(); // non attendu (non awaited)
                return;
            } 
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} LoginWithCookie: {charInfo.Name} connecté à la fenêtre PIE {pieWindowId}");
            }
            ProcessLogin(charInfo, connection);
        }

        private void ProcessLogin(DatabaseCharacterInfo charInfo, UserConnection connection)
        {
            var player = Server!.GameLogic.UserReconnected(connection, charInfo);
            // si ce personnage appartient à une guilde  
            // 1. lui envoyer la liste complète des membres de la guilde  
            // 2. informer tous les serveurs de l'appartenance de ce joueur à la guilde  
            // 3. envoyer un message aux autres membres de la guilde en ligne pour signaler que ce joueur vient de se connecter
            if (charInfo.Guild != null)
            {
                var guild = Server!.GameLogic.GetPlayerGuild(connection);
                if (guild != null)
                {
                    // 1. lui envoyer la liste complète des membres de la guilde
                    byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcGuildAllMembersUpdate), WriteMmoString(guild.GetGuildMembersJson()));
                    connection.Send(msg);

                    // 2. envoyer un message à tous les serveurs indiquant qu'un personnage avec un identifiant donné appartient à une guilde avec un identifiant et un nom donnés  
                    // Pour le serveur, les paramètres sont : identifiant du personnage, nom de la guilde, identifiant de la guilde, rang au sein de la guilde
                    byte[] msgToServers = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(charInfo.CharId), WriteMmoString(guild.Name), ToBytes(guild.Id), ToBytes((int)charInfo.GuildRank!));
                    foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
                    {
                        serverConn.Send(msgToServers);
                    }

                    // 3. envoyer un message aux autres membres de la guilde en ligne pour les informer que ce joueur vient de se connecter  
                    // pour le client, les paramètres sont : identifiant du personnage, rang dans la guilde, statut en ligne (booléen)
                    byte[] msgToGuildies = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(charInfo.CharId), ToBytes((int)charInfo.GuildRank!), ToBytes(true)); // true pour en ligne
                    foreach (var onlineMember in guild.GetOnlineMembers())
                    {
                        // si c'est notre propre personnage, inutile de lui signaler qu'il vient de se connecter
                        if (onlineMember.Conn == connection) continue;
                        onlineMember.Conn.Send(msgToGuildies);
                    }
                }
            }
            // si ce personnage fait partie d'un groupe, restaurer sa référence de groupe et envoyer les informations complètes du groupe à tous les membres du groupe en ligne
            string? partyId = Server!.GameLogic.GetStoredDisconnectedPlayerPartyId(charInfo.CharId);
            if (partyId != null)
            {
                var partyRef = Server!.GameLogic.GetPartyById(partyId);
                if (partyRef != null)
                {
                    Server!.GameLogic.UnstoreDisconnectedPlayerPartyId(charInfo.CharId);
                    player.PartyRef = partyRef;
                    partyRef.SendFullPartyToInvolvedServers();
                }
            }
        }
    }
}