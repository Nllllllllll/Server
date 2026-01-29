namespace PersistenceServer.RPCs
{
    internal class GuildLeave : BaseRpc
    {
        public GuildLeave()
        {
            RpcType = RpcType.RpcGuildLeave; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            Server!.Processor.ConQ.Enqueue(async () => await ProcessMessage(connection));
        }

        private async Task ProcessMessage(UserConnection playerConn)
        {
            // si le personnage n'est pas dans une guilde, quitter la fonction  
            // si c'est le maître de guilde, quitter la guilde et nommer un autre membre maître de guilde  
            // si c'est un simple membre, quitter simplement la guilde  
            // si la guilde ne comporte plus aucun membre après cela, la dissoudre

            var player = Server!.GameLogic.GetPlayerByConnection(playerConn);
            if (player == null) return;
            var guild = Server!.GameLogic.GetPlayerGuild(playerConn);
            if (guild == null) return;

            bool wasPlayerGuildMaster = player.GuildRank == 0;

            // mettre à jour la base de données pour indiquer que ce personnage n'appartient plus à aucune guilde
            await Server!.Database.PlayerLeavesGuild(player.CharId);
            // mettre à jour les données de jeu pour indiquer que ce personnage n'appartient plus à aucune guilde
            player.GuildId = -1;
            player.GuildRank = -1;
            guild.RemoveMember(player);

            // envoyer un message aux serveurs de jeu indiquant qu'un personnage avec un identifiant donné n'appartient plus à aucune guilde  
            // Pour le serveur, les paramètres sont : identifiant du personnage, nom de la guilde, identifiant de la guilde, rang dans la guilde
            byte[] msgToServers = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(player.CharId), WriteMmoString(""), ToBytes(-1), ToBytes(-1));
            foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
            {
                // une fois que le serveur aura mis à jour l'ID de guilde du personnage et que cette modification sera répliquée vers le client, celui-ci affichera la fenêtre de guilde en tant que « sans guilde »
                serverConn.Send(msgToServers);
            }

            // envoyer un message au joueur qui quitte pour confirmer son départ (principalement pour un message système)
            byte[] msgToOwner = MergeByteArrays(ToBytes(RpcType.RpcGuildLeave), ToBytes(true)); // true signifie que c'est VOUS qui avez quitté, et non les autres membres
            player.Conn.Send(msgToOwner);

            Console.WriteLine($"{DateTime.Now:HH:mm} {player.Name} a quitté la guilde \"{guild.Name}\"");

            // s'il ne reste plus aucun membre dans cette guilde, la supprimer simplement de la base de données
            if (guild.MembersCount == 0)
            {
                await Server!.Database.DeleteGuild(guild.Id);
                Server!.GameLogic.DeleteGuild(guild.Id);
                return;
            }

            // si le joueur qui quitte la guilde était le maître de guilde ET qu'il n'y avait aucun autre maître de guilde, nous devons désigner un nouveau maître de guilde  
            // nous rechercherons un joueur dans cette guilde ayant le rang le plus bas (plus le rang est bas, mieux c'est ; le maître de guilde a le rang 0) et en ferons le nouveau maître  
            // s'il existe plusieurs joueurs avec le rang le plus bas, nous en choisirons un au hasard
            if (wasPlayerGuildMaster && guild.GuildMastersCount == 0)
            {
                // définir le rang du nouveau maître de guilde et l'envoyer aux serveurs, au cas où il serait en ligne et aurait besoin de mettre à jour son widget de guilde pour afficher les outils de maître de guilde
                var newGuildMasterId = await Server!.Database.MakeNewGuildMaster(guild.Id);                
                guild.UpdateMemberRank(newGuildMasterId, 0);
                // Pour le serveur, les paramètres sont : identifiant du personnage, nom de la guilde, identifiant de la guilde, rang dans la guilde
                byte[] updateRankMsg = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(newGuildMasterId), WriteMmoString(guild.Name), ToBytes(guild.Id), ToBytes(0));
                foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
                {
                    serverConn.Send(updateRankMsg);
                }
            }

            // répliquer à tous les membres restants de la guilde la nouvelle liste des membres de la guilde  
            // si vous souhaitez optimiser, vous pourriez le faire de manière additive, c'est-à-dire envoyer simplement un événement « MemberLeft » avec son identifiant, de même pour MemberJoined, MemberOnline, MemberOffline et RankUpdate  
            // mais je ne pense pas que cela en vaille la peine
            byte[] msgNewRoster = MergeByteArrays(ToBytes(RpcType.RpcGuildAllMembersUpdate), WriteMmoString(guild.GetGuildMembersJson()));
            byte[] msgCharLeft = MergeByteArrays(ToBytes(RpcType.RpcGuildLeave), ToBytes(false), WriteMmoString(player.Name)); // false signifie qu'un autre joueur est parti, et la chaîne correspond à son nom
            foreach (var member in guild.GetOnlineMembers())
            {
                member.Conn.Send(msgNewRoster);
                member.Conn.Send(msgCharLeft);
            }            
        }
    }
}