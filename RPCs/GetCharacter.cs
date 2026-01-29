namespace PersistenceServer.RPCs
{
    public class GetCharacter : BaseRpc
    {
        public GetCharacter()
        {
            RpcType = RpcType.RpcGetCharacter; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string cookie = reader.ReadMmoString();
            int charId = reader.ReadInt32();
#if DEBUG
            if (Server!.Settings.UniversalCookie == cookie)
            {
                Server!.Processor.ConQ.Enqueue(async () => await ProcessGetCharacterForPie(charId, connection));
            }
            else
            {
                Server!.Processor.ConQ.Enqueue(async () => await ProcessGetCharacter(cookie, charId, connection));
            }
#else
            Server!.Processor.ConQ.Enqueue(async () => await ProcessGetCharacter(cookie, charId, connection));
#endif
        }

        private async Task ProcessGetCharacter(string cookie, int charId, UserConnection connection)
        {
            if (!Server!.GameLogic.IsServer(connection))
            {
                Console.WriteLine("Action illégale : un client (et non un serveur !) a tenté une requête RPC GetCharacter. Cela ne doit jamais se produire : vérifiez si cela se produit.");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcGetCharacter), ToBytes(false));
                connection.Send(msg);
                return;
            }

            var accountId = Server!.GameLogic.GetAccountIdByCookie(cookie);
            if (accountId < 0)
            {
                Console.WriteLine("GetCharacter a échoué : l'utilisateur a fourni un mauvais cookie !");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcGetCharacter), ToBytes(false)); // cela indiquera au serveur de jeu de déconnecter cet utilisateur
                connection.Send(msg);
                return;
            }

            var charInfo = await Server!.Database.GetCharacter(charId, accountId);
            if (charInfo == null)
            {
                Console.WriteLine("GetCharacter a échoué : l'utilisateur a fourni un identifiant de caractère incorrect !");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcGetCharacter), ToBytes(false)); // cela indiquera au serveur de jeu de déconnecter cet utilisateur
                connection.Send(msg);
                return;
            }

            //@TODO: ajouter un booléen allowMultipleCharacters et, s'il est à false ou si on est en DEBUG, demander aux serveurs de jeu de déconnecter le personnage le plus ancien associé à l'ID de compte

            Console.WriteLine($"GetCharacter traité pour: {charInfo.Name}");
            Server!.GameLogic.SetPlayersServer(charInfo.CharId, connection.Id.ToString());
            SendCharinfoToConnection(charInfo, connection);
        }

        private async Task ProcessGetCharacterForPie(int pieWindowId, UserConnection connection)
        {
            var charInfo = await Server!.Database.GetCharacterForPieWindow(pieWindowId);
            if (charInfo == null)
            {
                Console.WriteLine($"Échec de LoginWithCookie pour le client : pas assez de caractères dans la base de données pour la fenêtre PIE: {pieWindowId}");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcGetCharacter), ToBytes(false)); // cela indiquera au serveur de jeu de déconnecter cet utilisateur
                connection.Send(msg);
                return;
            }
            Console.WriteLine($"GetCharacter traité pour le compte : {charInfo.AccountId}, character: {charInfo.Name}");
            Server!.GameLogic.SetPlayersServer(charInfo.CharId, connection.Id.ToString());
            SendCharinfoToConnection(charInfo, connection);
        }

        private void SendCharinfoToConnection(DatabaseCharacterInfo charInfo, UserConnection connection)
        {
            byte[] binAccountId = ToBytes(charInfo.AccountId);
            byte[] binCharId = ToBytes(charInfo.CharId);
            byte[] binCharname = WriteMmoString(charInfo.Name);
            byte[] binSerialized = WriteMmoString(charInfo.SerializedCharacter);
            byte[] binPermissions = ToBytes(charInfo.Permissions);
            byte[] binGuild = ToBytes(charInfo.Guild ?? -1);
            byte[] binGuildrank = ToBytes(charInfo.GuildRank ?? -1);            
            byte[] msgSuccess = MergeByteArrays(ToBytes(RpcType.RpcGetCharacter), ToBytes(true), binAccountId, binCharId, binCharname, binPermissions, binSerialized, binGuild, binGuildrank);
            connection.Send(msgSuccess);
        }
    }
}