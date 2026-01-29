namespace PersistenceServer.RPCs
{
    public class LoginPassword : BaseRpc
    {
        public LoginPassword()
        {
            RpcType = RpcType.RpcLoginPassword; // set it to the RpcType you want to catch
        }

        // Read message from the reader, then enqueue an Action on the concurrent queue server.Processor.ConQ
        // For example: Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("like this"));
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string accountName = reader.ReadMmoString();
            string password = reader.ReadMmoString();
            string macAddress = reader.ReadMmoString(); // NOUVEAU

            connection.SetMacAddress(macAddress); // NOUVEAU

#if DEBUG
            Console.WriteLine($"(thread {Environment.CurrentManagedThreadId}): Se connecter avec un mot de passe");
#endif
            Server!.Processor.ConQ.Enqueue(async () => await ProcessLogin(accountName, password, connection));
        }

        private async Task ProcessLogin(string accountName, string password, UserConnection connection)
        {
            int accountId = await Server!.Database.LoginUser(accountName, password); // returns -1 if login failed

            // if account exists
            if (accountId >= 0)
            {
                // Mettre à jour les informations de connexion
                string? ipAddress = connection.GetIpAddress();
                string? macAddress = connection.GetMacAddress();
                await Server!.Database.UpdateLoginInfo(accountId, ipAddress, macAddress);

                var cookie = BCrypt.Net.BCrypt.GenerateSalt();
                Console.WriteLine($"Connexion: '{accountName}', id: {accountId}, cookie: {cookie}");
                Server!.GameLogic.UserLoggedIn(accountId, cookie, connection);

                // sending true to signify "success", plus a cookie
                // the cookie will allow a reconnection later, when the user changes the level to enter the game server
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginPassword), ToBytes(true), WriteMmoString(cookie));
                connection.Send(msg);
            }
            // if account doesn't exist or is banned
            else
            {
                Console.WriteLine($"Connexion pour '{accountName}' échec : mauvaises informations d'identification");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcLoginPassword), ToBytes(false)); // sending false to signify "failure"
                connection.Send(msg);
            }
        }
    }
}