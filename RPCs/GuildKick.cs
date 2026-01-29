using System.Numerics;
using System.Reflection;

namespace PersistenceServer.RPCs
{
    internal class GuildKick : BaseRpc
    {
        public GuildKick()
        {
            RpcType = RpcType.RpcGuildKick; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string charName = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(async () => await ProcessMessage(charName, connection));
        }

        private async Task ProcessMessage(string kickedName, UserConnection playerConn)
        {
            var kickInitiator = Server!.GameLogic.GetPlayerByConnection(playerConn);
            if (kickInitiator == null) return;
            var guild = Server!.GameLogic.GetPlayerGuild(playerConn);
            if (guild == null) return;
            if (kickInitiator.GuildRank > Server.Settings.GuildOfficerRank) return; // seul un Officier peut expulser un membre


            var kickedGuildMember = guild.GetGuildMemberByName(kickedName);
            if (kickedGuildMember == null) return; // aucun joueur avec ce nom trouvé dans la guilde, il ne fait peut-être pas partie de cette guilde ou n'appartient à aucune guilde

            if (kickedGuildMember.GuildRank <= kickInitiator.GuildRank) return; // impossible d'expulser les membres de même rang ou de rang supérieur

            Console.WriteLine($"{DateTime.Now:HH:mm} {kickInitiator.Name} a expulser {kickedGuildMember.MemberName} hors de la guilde.");

            // peut être null si le joueur n'est pas en ligne
            var kickedPlayerOnline = Server!.GameLogic.GetPlayerByName(kickedName);

            // supprimer
            await Server!.Database.RemoveGuildMember(kickedGuildMember.Id);
            Server!.GameLogic.RemoveGuildMember(guild, kickedGuildMember.Id, kickedPlayerOnline);
            
            if (kickedPlayerOnline != null)
            {
                // envoyer un message aux serveurs de jeu indiquant qu'un personnage avec un identifiant donné n'appartient plus à aucune guilde  
                // Pour le serveur, les paramètres sont : identifiant du personnage, nom de la guilde, identifiant de la guilde, rang dans la guilde
                byte[] msgToServers = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(kickedPlayerOnline.CharId), WriteMmoString(""), ToBytes(-1), ToBytes(-1));
                foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
                {
                    // une fois que le serveur aura mis à jour l'ID de guilde du personnage et que cette modification sera répliquée vers le client, celui-ci affichera la fenêtre de guilde en mode « sans guilde »
                    serverConn.Send(msgToServers);
                }
            }

            // répliquer à tous les membres le message concernant l'expulsion  
            // répliquer à tous les membres la nouvelle liste des membres de la guilde
            byte[] msgNewRoster = MergeByteArrays(ToBytes(RpcType.RpcGuildAllMembersUpdate), WriteMmoString(guild.GetGuildMembersJson()));
            byte[] msgCharLeft = MergeByteArrays(ToBytes(RpcType.RpcGuildKick), ToBytes(true), WriteMmoString(kickedGuildMember.MemberName)); // true signifie qu'il s'agit d'un autre joueur et non de vous-même
            foreach (var member in guild.GetOnlineMembers())
            {
                member.Conn.Send(msgNewRoster);
                member.Conn.Send(msgCharLeft);
            }

            // si le joueur est en ligne, lui envoyer un message pour l'informer qu'il a été expulsé
            if (kickedPlayerOnline != null)
            {
                byte[] msgToKicked = MergeByteArrays(ToBytes(RpcType.RpcGuildKick), ToBytes(false), WriteMmoString(kickedGuildMember.MemberName)); // false signifie que c'est vous qui avez été expulsé
                kickedPlayerOnline.Conn.Send(msgToKicked);
            }
        }
    }
}