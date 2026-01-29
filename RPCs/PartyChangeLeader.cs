namespace PersistenceServer.RPCs
{
    internal class PartyChangeLeader : BaseRpc
    {
        public PartyChangeLeader()
        {
            RpcType = RpcType.RpcPartyChangeLeader; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            int newLeaderId = reader.ReadInt32();
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(newLeaderId, connection));
        }

        private void ProcessMessage(int newLeaderId, UserConnection connection)
        {
            var player = Server!.GameLogic.GetPlayerByConnection(connection);
            if (player == null) return;
            if (player.PartyRef == null) return;

            // vous n'avez pas les droits
            if (player.PartyRef.PartyLeaderId != player.CharId)
            {
                byte[] msgYourNotLeader = MergeByteArrays(ToBytes(RpcType.RpcPartyChangeLeader), ToBytes(false)); // false correspond au message « Vous n'êtes pas le chef du groupe »
                connection.Send(msgYourNotLeader);
                return; 
            }

            // ce joueur ne fait pas partie du groupe
            if (!player.PartyRef.MemberIds.Contains(newLeaderId)) return;

            // si toutes les vérifications sont valides            
            player.PartyRef.PartyLeaderId = newLeaderId;
            player.PartyRef.SendFullPartyToInvolvedServers();

            string newLeaderName = player.PartyRef.GetMemberName(newLeaderId);
            byte[] msgCharRankAdjusted = MergeByteArrays(ToBytes(RpcType.RpcPartyChangeLeader), ToBytes(true), WriteMmoString(newLeaderName));
            foreach (var conn in player.PartyRef.GetClientConnections())
            {
                conn.Send(msgCharRankAdjusted);
            }
        }
    }
}