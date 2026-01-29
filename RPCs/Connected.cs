namespace PersistenceServer.RPCs
{
    public class Connected : BaseRpc
    {
        public Connected()
        {
            RpcType = RpcType.RpcConnected; // définis-le sur le type de Rpc que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis mets en file d'attente une Action dans la queue concurrente server.Processor.ConQ
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
#if DEBUG
            Console.Write($"(thread {Environment.CurrentManagedThreadId}) ");
#endif
            Console.WriteLine($"Utilisateur connecté. ID de session: {connection.Id}");
        }
    }
}