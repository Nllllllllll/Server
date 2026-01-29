using System;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace PersistenceServer.RPCs
{
    internal class GuildCreate : BaseRpc
    {
        public GuildCreate()
        {
            RpcType = RpcType.RpcGuildCreate; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string guildName = reader.ReadMmoString();
            int maxLength = 20;
            // Tronquer le message à maxLength (20 caractères)
            guildName = guildName.Length <= maxLength ? guildName : guildName[..maxLength]; // .. est un opérateur de plage (Range Operator) introduit en C# 8.0 https://www.codeguru.com/csharp/c-8-0-ranges-and-indices-types/
            SanitizeGuildName(ref guildName);
            Server!.Processor.ConQ.Enqueue(async () => await ProcessMessage(guildName, connection));
        }

        private async Task ProcessMessage(string guildName, UserConnection playerConn)
        {            
            if (guildName.Length < 2)
            {
                byte[] errMsg = MergeByteArrays(ToBytes(RpcType.RpcGuildCreate), ToBytes(false)); // envoyer false pour signifier un « échec »
                playerConn.Send(errMsg);
                return;
            }

            var player = Server!.GameLogic.GetPlayerByConnection(playerConn);
            if (player == null) return;
            if (player.GuildId != -1) return; // vérifier que le joueur n'appartient pas déjà à une guilde

            var guild = await Server!.Database.CreateGuild(guildName, player.CharId);
            if (guild == null)
            {                
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcGuildCreate), ToBytes(false)); // envoyer un message indiquant l'échec
                playerConn.Send(msgFail);
                return;
            }
            Console.WriteLine($"{DateTime.Now:HH:mm} {player.Name} a créé une guilde \"{guildName}\"");
            Server!.GameLogic.CreateGuild(guild, player);

            // envoyer un message au chef de guilde pour lui indiquer qu'il a réussi à créer la guilde
            byte[] msgToClient = MergeByteArrays(ToBytes(RpcType.RpcGuildCreate), ToBytes(true));
            playerConn.Send(msgToClient);

            // envoyer la liste des membres de la guilde nouvellement créée à tous les membres (dans ce cas, uniquement un personnage : lui-même)
            byte[] msgNewRoster = MergeByteArrays(ToBytes(RpcType.RpcGuildAllMembersUpdate), WriteMmoString(guild.GetGuildMembersJson()));
            foreach (var member in guild.GetOnlineMembers())
            {
                member.Conn.Send(msgNewRoster);
            }

            // envoyer un message à tous les serveurs indiquant qu'un personnage avec un ID donné appartient à une guilde avec un ID et un nom donnés  
            // Pour le serveur, les paramètres sont : ID du personnage, nom de la guilde, ID de la guilde, rang dans la guilde
            byte[] msgToServers = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(player.CharId), WriteMmoString(guild.Name), ToBytes(guild.Id), ToBytes(player.GuildRank));
            foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
            {
                serverConn.Send(msgToServers);
            }
        }

        private void SanitizeGuildName(ref string guildName)
        {
            guildName = Regex.Replace(guildName, "[\\p{S}\\p{C}\\p{P}]", ""); // supprime la ponctuation, les symboles et les caractères de contrôle
            guildName = Regex.Replace(guildName, @"\s+", " "); // remplacer les caractères d'espacement répétitifs par un seul espace
        }
    }
}