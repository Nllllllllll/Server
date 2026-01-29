using System.Numerics;

namespace PersistenceServer.RPCs
{
    internal class DeleteCharacter : BaseRpc
    {
        public DeleteCharacter()
        {
            RpcType = RpcType.RpcDeleteCharacter; // définis-le sur le type de Rpc que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action à la file d'attente concurrente server.Processor.ConQ
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string charName = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(async () => await ProcessMessage(charName, connection));
        }

        private async Task ProcessMessage(string charName, UserConnection playerConn)
        {
            int accountId = Server!.GameLogic.GetAccountId(playerConn);
            if (accountId == -1)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Un utilisateur non connecté a tenté de supprimer un personnage ? Cela ne doit jamais arriver.");
                _ = playerConn.Disconnect(); // pas utilisé avec await
                return;
            }

            var onlinePlayer = Server!.GameLogic.GetPlayerByConnection(playerConn);
            if (onlinePlayer != null && onlinePlayer.Name == charName)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} AVERTISSEMENT : Compte {accountId} tentative de suppression du caractère EN LIGNE {charName} de DB, refusant !");
                return;
            }

            var characterInDb = await Server!.Database.GetCharacterByName(charName, accountId);
            if (characterInDb == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} AVERTISSEMENT : Compte {accountId} tentative de suppression de caractère {charName} de DB, mais ne le possède pas !");
                return;
            }

            var success = await Server!.Database.DeleteCharacter(charName, accountId);
            if (!success) 
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} AVERTISSEMENT : Compte {accountId} tentative de suppression de caractère {charName} de DB, mais quelque chose s'est mal passé !");
                return;
            }
            
            Console.WriteLine($"{DateTime.Now:HH:mm} Character {charName} a été supprimé de la base de données.");

            // Simule que le propriétaire a demandé des personnages
            byte[] emptyMsg = Array.Empty<byte>();
            BinaryReader reader = new(new MemoryStream(emptyMsg));
            Server.InvokeOnMessageReceived(RpcType.RpcGetCharacters, playerConn, reader);

            // Si le personnage faisait partie d'une guilde
            if (characterInDb.Guild == null) return;
            var guild = Server!.GameLogic.GetGuildById((int)characterInDb.Guild);
            if (guild == null) return;

            // supprimer le personnage de la guilde
            Server!.GameLogic.DeleteGuildMember(guild, characterInDb.CharId);

            // mais s'il était le seul membre, supprime simplement la guilde
            if (guild.MembersCount == 0)
            {
                await Server!.Database.DeleteGuild(guild.Id);
                Server!.GameLogic.DeleteGuild(guild.Id);
                // aucune autre action n'est nécessaire, car aucun membre de la guilde ne peut être en ligne dans ce scénario, donc nous n'avons besoin d'informer personne
            }
            // si le joueur faisait partie d'une guilde et n'était pas le seul membre, envoie un message à tous les membres en ligne pour indiquer que le personnage a quitté la guilde
            else
            {
                // si le joueur quittant la guilde était le maître de guilde, nous devons désigner un nouveau maître de guilde
                // nous chercherons un joueur dans cette guilde avec le rang le plus bas (plus le rang est bas, mieux c'est, GM est rang 0) et le nommerons nouveau GM
                // s'il y a plusieurs joueurs avec le rang le plus bas, nous en choisirons un au hasard
                if (characterInDb.GuildRank == 0)
                {
                    // définis le rang pour le nouveau GM et envoie-le aux serveurs, au cas où il serait en ligne et aurait besoin de mettre à jour son widget de guilde pour afficher les outils du maître de guilde
                    var newGuildMasterId = await Server!.Database.MakeNewGuildMaster(guild.Id);
                    guild.UpdateMemberRank(newGuildMasterId, 0);
                    // Pour le serveur, les paramètres sont : id du personnage, nom de la guilde, id de la guilde, rang dans la guilde
                    byte[] updateRankMsg = MergeByteArrays(ToBytes(RpcType.RpcGuildMemberUpdate), ToBytes(newGuildMasterId), WriteMmoString(guild.Name), ToBytes(guild.Id), ToBytes(0));
                    foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
                    {
                        serverConn.Send(updateRankMsg);
                    }
                }

                // réplique à tous les autres membres de la guilde une nouvelle liste des membres de la guilde
                // si tu veux l'optimiser, tu pourrais le faire de manière additive, c’est-à-dire envoyer uniquement un « MembreParti » avec son identifiant, pareil pour MembreRejoint, MembreEnLigne, MembreHorsLigne et MiseÀJourRang
                // mais je pense pas que ça vaille la peine de se donner autant de mal
                byte[] msgNewRoster = MergeByteArrays(ToBytes(RpcType.RpcGuildAllMembersUpdate), WriteMmoString(guild.GetGuildMembersJson()));
                byte[] msgCharLeft = MergeByteArrays(ToBytes(RpcType.RpcGuildLeave), ToBytes(false), WriteMmoString(charName)); // false means it's another player who left, and the string is his name
                foreach (var member in guild.GetOnlineMembers())
                {
                    member.Conn.Send(msgNewRoster);
                    member.Conn.Send(msgCharLeft);
                }
            }
            
        }
    }
}