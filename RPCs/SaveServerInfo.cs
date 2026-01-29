using System;

namespace PersistenceServer.RPCs
{
    internal class SaveServerInfo : BaseRpc
    {
        public SaveServerInfo()
        {
            RpcType = RpcType.RpcSaveServerInfo; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string serialized = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(async () => await ProcessSaveServerInfo(serialized, connection));
        }

        private async Task ProcessSaveServerInfo(string serializedServerInfo, UserConnection conn)
        {
            var gameServer = Server!.GameLogic.GetServerByConnection(conn);
            if (gameServer == null)
            {
                Console.WriteLine("Action illégale : aucun serveur n'a tenté SaveServerInfo RPC. Cela ne doit jamais se produire : enquêtez si cela se produit.");
                return;
            }

            await Server!.Database.SaveServerInfo(serializedServerInfo, gameServer.Port, gameServer.Level);
        }
    }
}