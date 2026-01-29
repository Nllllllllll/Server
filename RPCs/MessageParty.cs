namespace PersistenceServer.RPCs
{
    internal class MessageParty : BaseRpc
    {
        public MessageParty()
        {
            RpcType = RpcType.RpcMessageParty; // définis-le sur le RpcType que tu veux intercepter
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
            var player = Server!.GameLogic.GetPlayerByConnection(connection);
            if (player == null) return;

            if (player.PartyRef == null)
            {
                Console.WriteLine($" Joueur {player.Name} a tenté un message de groupe, mais n'est pas dans un groupe");
                return;
            }

            bool partyLeader = player.PartyRef.PartyLeaderId == player.CharId;

            Console.WriteLine($"{DateTime.Now:HH:mm} [{(partyLeader ? "chef du Groupe" : "Groupe")} ({player.PartyId})] {player.Name}: \"{message}\"");
            // Le canal 8 est le canal de groupe, voir EChatMsgChannel dans UE5
            // Le canal 9 est le canal du chef de groupe
            int channel = partyLeader ? 9 : 8;
            byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcMessageChannel), ToBytes(channel), WriteMmoString(player.Name), WriteMmoString(message), ToBytes(false) /* pas un message de MJ */);
            foreach (var partyMemberConn in player.PartyRef.GetClientConnections())
            {
                partyMemberConn.Send(msg);
            }
        }
    }
}