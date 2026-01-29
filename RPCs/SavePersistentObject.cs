namespace PersistenceServer.RPCs
{
    internal class SavePersistentObject : BaseRpc
    {
        public SavePersistentObject()
        {
            RpcType = RpcType.RpcSavePersistentObject; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            int objectId = reader.ReadInt32();
            string jsonString = reader.ReadMmoString();

            Server!.Processor.ConQ.Enqueue(async () => await ProcessSavePersistentObject(objectId, jsonString, connection));
        }

        private async Task ProcessSavePersistentObject(int objectId, string jsonString, UserConnection conn)
        {
            var gameServer = Server!.GameLogic.GetServerByConnection(conn);
            if (gameServer == null)
            {
                Console.WriteLine("Action illégale : aucun serveur n'a tenté de RPC SavePersistentObject. Cela ne devrait jamais arriver.");
                return;
            }
            // si c'est un JSON vide, supprimer l'objet
            if (jsonString == "{}")
            {
                await Server!.Database.DeletePersistentObject(gameServer.Level, gameServer.Port, objectId);
            }
            else
            {
                await Server!.Database.SavePersistentObject(gameServer.Level, gameServer.Port, objectId, jsonString);
            }
        }
    }
}