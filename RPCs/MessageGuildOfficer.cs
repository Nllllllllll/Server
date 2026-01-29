namespace PersistenceServer.RPCs
{
    internal class MessageGuildOfficer : BaseRpc
    {
        public MessageGuildOfficer()
        {
            RpcType = RpcType.RpcMessageGuildOfficer; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string message = reader.ReadMmoString();
            int maxLength = 255;
            // Tronquer le message à maxLength (255 caractères)
            message = message.Length <= maxLength ? message : message[..maxLength]; // .. est l'opérateur de plage (Range Operator) de C# 8.0 https://www.codeguru.com/csharp/c-8-0-ranges-and-indices-types/
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(message, connection));
        }

        private void ProcessMessage(string message, UserConnection connection)
        {
            var sender = Server!.GameLogic.GetPlayerByConnection(connection);
            if (sender == null) return;
            var charName = sender.Name;

            var guild = Server!.GameLogic.GetPlayerGuild(connection);
            if (guild == null)
            {
                Console.WriteLine($"Joueur {charName} a tenté un message de guilde, mais n'est pas dans une guilde");
                return;
            }
            if (sender.GuildRank > Server.Settings.GuildOfficerRank)
            {
                Console.WriteLine($"Joueur {charName} a tenté un message d'officier de guilde, mais n'est pas un officier");
                return;
            }

            // NOUVEAU : Déterminer si le message doit afficher un préfixe de rôle
            bool isStaff = sender.IsGm();
            string rolePrefix = isStaff ? sender.GetRolePrefix() : "";

            Console.WriteLine($"{DateTime.Now:HH:mm} [Officier de guilde ({guild.Id})] {rolePrefix}{charName}: \"{message}\"");
            // Le canal 7 est le canal des officiers de guilde, voir EChatMsgChannel dans UE5
            byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcMessageChannel), ToBytes(7), WriteMmoString(charName), WriteMmoString(message), ToBytes(isStaff) /* MODIFIÉ */);
            var players = guild.GetOnlineOfficers(Server.Settings.GuildOfficerRank);
            foreach (var player in players)
            {
                player.Conn.Send(msg);
            }
        }
    }
}