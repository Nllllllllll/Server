namespace PersistenceServer.RPCs
{
    public class GetCharacters : BaseRpc
    {
        public GetCharacters()
        {
            RpcType = RpcType.RpcGetCharacters; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            Server!.Processor.ConQ.Enqueue(async () => await ProcessGetCharacters(connection));
        }

        private async Task ProcessGetCharacters(UserConnection connection)
        {
            int accountId = Server!.GameLogic.GetAccountId(connection);
            if (accountId == -1)
            {
                Console.WriteLine("L'obtention des caractères a échoué : l'utilisateur n'est pas connecté. Cela ne doit jamais arriver.");
                _ = connection.Disconnect(); // non attendu
                return;
            }
            var characters = await Server!.Database.GetCharacters(accountId);

            // Le message est structuré comme suit :
            // Un entier pour indiquer le nombre de personnages sur ce compte
            // Puis pour chaque personnage : id (entier), nom (chaîne de caractères), json (chaîne de caractères)         

            byte[] numCharacters = ToBytes(characters.Count);
            List<byte> allCharacters = new();
            foreach (var toon in characters)
            {
                allCharacters.AddRange(ToBytes(toon.CharId)); // id
                allCharacters.AddRange(WriteMmoString(toon.Name)); // nom
                allCharacters.AddRange(WriteMmoString(toon.SerializedCharacter)); // json
                //@TODO: envoyer également le nom de la guilde et le lire côté client
            }

            byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcGetCharacters), numCharacters, allCharacters.ToArray());
            connection.Send(msg);
        }
    }
}