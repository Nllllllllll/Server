namespace PersistenceServer.RPCs
{
    internal class PartyLeave : BaseRpc
    {
        public PartyLeave()
        {
            RpcType = RpcType.RpcPartyLeave; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(connection));
        }

        private void ProcessMessage(UserConnection playerConn)
        {
            var player = Server!.GameLogic.GetPlayerByConnection(playerConn);
            if (player == null) return;

            if (player.PartyRef == null) return;
            player.PartyRef.RemoveMember(player, true);
        }
    }
}