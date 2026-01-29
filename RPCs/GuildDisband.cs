namespace PersistenceServer.RPCs
{
    internal class GuildDisband : BaseRpc
    {
        public GuildDisband()
        {
            RpcType = RpcType.RpcGuildDisband; // définis-le sur le RpcType que tu veux intercepter
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
            // supprimer tous les membres de la guilde  
            // supprimer la guilde de la base de données

            var player = Server!.GameLogic.GetPlayerByConnection(playerConn);
            if (player == null) return;
            var guild = Server!.GameLogic.GetPlayerGuild(playerConn);
            if (guild == null) return;

            if (player.GuildRank != 0)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} {player.Name} a essayé de DISSOUDRE la guilde, mais n'est pas un chef de guilde !");
                return;
            }

            if (guild.GuildMastersCount > 1)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} {player.Name} il a essayé de DISSOUDRE la guilde, mais il n'est pas le seul GM, donc la demande a été rejetée.");
                return;
            }

            // mettre à jour les champs « guild » et « guildrank » du personnage dans la base de données, puis supprimer la guilde elle-même
            await Server!.Database.DisbandGuild(guild.Id);
            // supprimer la guilde de la logique de gestion des guildes (GuildLogic)
            Server!.GameLogic.DeleteGuild(guild.Id);

            // créer une liste des personnages qui doivent être marqués comme sans guilde sur les serveurs UE5
            List<int> affectedCharacterIds = new();
            foreach (var member in guild.GetOnlineMembers())
            {
                affectedCharacterIds.Add(member.CharId);
                member.GuildRank = -1;
                member.GuildId = -1;
            }

            // envoyer un message aux serveurs de jeu indiquant que ces personnages n'appartiennent désormais plus à aucune guilde
            byte[] msgToServers = MergeByteArrays(ToBytes(RpcType.RpcGuildDisband), ToBytes(affectedCharacterIds.Count), ToBytes(affectedCharacterIds.ToArray()));
            foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
            {
                // une fois que le serveur aura mis à jour l'ID de guilde du personnage et que cette modification se sera répliquée vers les clients, ceux-ci afficheront la fenêtre de guilde en mode « sans guilde »
                serverConn.Send(msgToServers);
            }

            // envoyer un message aux membres pour leur indiquer que la guilde a été dissoute (principalement pour un message système)
            byte[] msgToPlayers = MergeByteArrays(ToBytes(RpcType.RpcGuildDisband));
            foreach (var formerMember in guild.GetOnlineMembers())
            {
                formerMember.Conn.Send(msgToPlayers);
            }
            Console.WriteLine($"{DateTime.Now:HH:mm} {player.Name} a dissous la guilde \"{guild.Name}\"");
        }
    }
}