using System.Numerics;

namespace PersistenceServer.RPCs
{
    internal class GuildAdjustRank : BaseRpc
    {
        public GuildAdjustRank()
        {
            RpcType = RpcType.RpcGuildAdjustRank; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            bool increaseRank = reader.ReadBoolean();
            string message = reader.ReadMmoString();
            int maxLength = 255;
            // Tronquer le message à maxLength (255 caractères)
            message = message.Length <= maxLength ? message : message[..maxLength]; // .. est un opérateur de plage (Range Operator) introduit en C# 8.0 https://www.codeguru.com/csharp/c-8-0-ranges-and-indices-types/            
            Server!.Processor.ConQ.Enqueue(async () => await ProcessMessage(message, increaseRank, connection));
        }

        private async Task ProcessMessage(string victimName, bool increaseRank, UserConnection connection)
        {
            var adjustInitiator = Server!.GameLogic.GetPlayerByConnection(connection);
            if (adjustInitiator == null) return;
            var guild = Server!.GameLogic.GetPlayerGuild(connection);
            if (guild == null) return;
            if (adjustInitiator.GuildRank > Server.Settings.GuildOfficerRank) return; // seul un Officier peut ajuster/modifier


            var adjustVictim = guild.GetGuildMemberByName(victimName);
            if (adjustVictim == null) return; // aucun joueur portant ce nom n'a été trouvé dans la guilde, peut-être qu'il ne fait pas partie de cette guilde ou n'appartient à aucune guilde

            int newRank = increaseRank ? adjustVictim.GuildRank - 1 : adjustVictim.GuildRank + 1; // augmenter le rang correspond à -1, car plus la valeur numérique est basse, plus le rang est élevé

            // impossible d'ajuster le rang des membres de rang égal ou supérieur  
            // avec une exception : vous pouvez vous rétrograder vous-même
            if (adjustVictim.GuildRank <= adjustInitiator.GuildRank)
            {
                if (increaseRank || adjustInitiator.CharId != adjustVictim.Id) return;
            }

            // impossible de rétrograder en dessous du rang par défaut de la guilde (DefaultGuildRank)
            if (newRank > Server.Settings.DefaultGuildRank || newRank < 0)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} {adjustInitiator.Name} a tenté de définir un rang en dehors des limites autorisées (de 0 à DefaultGuildRank) : rejet de la demande");
                return; 
            }

            // un MG (Maître de Guilde) ne peut pas se rétrograder lui-même s'il est le seul MG dans la guilde – cela entraînerait une guilde sans MG
            if (adjustInitiator.CharId == adjustVictim.Id && !increaseRank && adjustInitiator.GuildRank == 0 && guild.GuildMastersCount == 1) return;

            string verb = increaseRank ? "promoted" : "demoted";
            Console.WriteLine($"{DateTime.Now:HH:mm} {adjustInitiator.Name} a {verb} {adjustVictim.MemberName} dans la guilde.");

            // peut être null si le joueur n'est pas en ligne
            var adjustVictimOnline = Server!.GameLogic.GetPlayerByName(victimName);

            // ajuster le rang dans la base de données
            await Server!.Database.UpdateGuildRank(adjustVictim.Id, newRank);
            // ajuster le rang sur le serveur persistant
            guild.UpdateMemberRank(adjustVictim.Id, newRank);

            // si le joueur est en ligne, ajuster son rang sur le joueur connecté et envoyer un message à tous les serveurs pour indiquer que ce joueur a un nouveau rang
            if (adjustVictimOnline != null)
            {
                adjustVictimOnline.GuildRank = newRank;
                // Pour le serveur, les paramètres sont : l'ID du personnage, le nom de la guilde, l'ID de la guilde, le rang dans la guilde
                byte[] updateRankMsg = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(adjustVictimOnline.CharId), WriteMmoString(guild.Name), ToBytes(guild.Id), ToBytes(newRank));
                foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
                {
                    serverConn.Send(updateRankMsg);
                }
            }


            // envoyer deux messages à tous les membres de la guilde (clients) :  
            // 1. mettre à jour le membre de la guilde (ce qui inclut son rang)  
            // 2. un simple message indiquant qu’un joueur a été promu/rétrogradé, à afficher dans la fenêtre de discussion
            byte[] msgMemberUpdate = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(adjustVictim.Id), ToBytes(adjustVictim.GuildRank), ToBytes(adjustVictimOnline == null ? false : true));
            byte[] msgCharRankAdjusted = MergeByteArrays(ToBytes(RpcType.RpcGuildAdjustRank), ToBytes(increaseRank), WriteMmoString(adjustVictim.MemberName));
            foreach (var member in guild.GetOnlineMembers())
            {
                member.Conn.Send(msgMemberUpdate);
                member.Conn.Send(msgCharRankAdjusted);
            }
        }
    }
}