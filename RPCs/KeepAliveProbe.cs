namespace PersistenceServer.RPCs
{
    internal class KeepAliveProbe : BaseRpc
    {
        public KeepAliveProbe()
        {
            RpcType = RpcType.RpcKeepAliveProbe; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(connection));
        }

        private void ProcessMessage(UserConnection senderConn)
        {
            byte[] msgAcknowledge = MergeByteArrays(ToBytes(RpcType.RpcKeepAliveProbe));
            senderConn.Send(msgAcknowledge);
        }
    }
}