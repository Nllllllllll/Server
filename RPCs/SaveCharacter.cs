namespace PersistenceServer.RPCs
{
    public class SaveCharacter : BaseRpc
    {
        public SaveCharacter()
        {
            RpcType = RpcType.RpcSaveCharacter; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            int charId = reader.ReadInt32();
            string serialized = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(async () => await ProcessSaveCharacter(charId, serialized, connection));
        }

        private async Task ProcessSaveCharacter(int charId, string serializedCharacter, UserConnection conn)
        {
            if (!Server!.GameLogic.IsServer(conn))
            {
                Console.WriteLine("Action illégale : aucun serveur n'a tenté de SaveCharacter RPC. Cela ne doit jamais se produire : enquêtez si cela se produit.");
                return;
            }

            await Server!.Database.SaveCharacter(charId, serializedCharacter);
        }
    }
}