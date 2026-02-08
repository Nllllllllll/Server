namespace PersistenceServer.RPCs
{
    public class GetCharacters : BaseRpc
    {
        public GetCharacters()
        {
            RpcType = RpcType.RpcGetCharacters;
        }

        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            Server!.Processor.ConQ.Enqueue(async () => await ProcessGetCharacters(connection));
        }

        private async Task ProcessGetCharacters(UserConnection connection)
        {
            int accountId = Server!.GameLogic.GetAccountId(connection);
            if (accountId == -1)
            {
                Console.WriteLine("L'obtention des caractères a échoué : l'utilisateur n'est pas connecté. Cela ne doit jamais arriver.");
                _ = connection.Disconnect();
                return;
            }
            
            var characters = await Server!.Database.GetCharacters(accountId);

            byte[] numCharacters = ToBytes(characters.Count);
            List<byte> allCharacters = new();
            
            foreach (var charInfo in characters)
            {
                string serializedJson = charInfo.ToSerializedJson();
                
                allCharacters.AddRange(ToBytes(charInfo.CharId));
                allCharacters.AddRange(WriteMmoString(charInfo.Name));
                allCharacters.AddRange(WriteMmoString(serializedJson));
            }

            byte[] msg = MergeByteArrays(
                ToBytes(RpcType.RpcGetCharacters), 
                numCharacters, 
                allCharacters.ToArray()
            );
            connection.Send(msg);
        }
    }
}
