namespace PersistenceServer.RPCs
{
    internal class PartyDisband : BaseRpc
    {
        public PartyDisband()
        {
            RpcType = RpcType.RpcPartyDisband; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(connection));
        }

        private void ProcessMessage(UserConnection connection)
        {
            var player = Server!.GameLogic.GetPlayerByConnection(connection);
            if (player == null) return;
            if (player.PartyRef == null) return;

            // vous n'avez pas les droits
            if (player.PartyRef.PartyLeaderId != player.CharId)
            {
                byte[] msgYourNotLeader = MergeByteArrays(ToBytes(RpcType.RpcPartyChangeLeader), ToBytes(false)); // RpcPartyChangeLeader + false correspond au message « Vous n'êtes pas le chef du groupe »
                connection.Send(msgYourNotLeader);
                return;
            }

            player.PartyRef.DisbandParty(true);
        }
    }
}