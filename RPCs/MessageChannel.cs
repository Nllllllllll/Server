namespace PersistenceServer.RPCs
{
    public class MessageChannel : BaseRpc
    {
        public MessageChannel()
        {
            RpcType = RpcType.RpcMessageChannel; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            int channel = reader.ReadInt32();
            string message = reader.ReadMmoString();
            bool talkAsGM = reader.ReadBoolean();
            int maxLength = 255;
            // Tronquer le message à maxLength (255 caractères)
            message = message.Length <= maxLength ? message : message[..maxLength]; // .. est l'opérateur de plage (Range Operator) de C# 8.0 https://www.codeguru.com/csharp/c-8-0-ranges-and-indices-types/
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(channel, message, talkAsGM, connection));
        }

        private void ProcessMessage(int channel, string message, bool talkAsGM, UserConnection connection)
        {
            var sender = Server!.GameLogic.GetPlayerByConnection(connection);
            if (sender == null) return; // le joueur a pu se déconnecter entre-temps, donc on abandonne l'opération
            var charName = sender.Name;

            // si talkAsGM est vrai mais que l'expéditeur n'est pas un MJ/MOD, réinitialise simplement la valeur à false
            if (talkAsGM && !sender.IsGm())
            {
                talkAsGM = false;
            }

            // Say correspond au canal 0  
            // « Say » (Dire) ne doit être affiché qu'aux alentours de la personne qui parle, donc nous demandons au serveur de jeu  
            // de le diffuser en multicast depuis le personnage, dont la distance de culling réseau déterminera la portée d'écoute
            if (channel == 0)
            {
                string rolePrefix = talkAsGM ? sender.GetRolePrefix() : ""; // MODIFIÉ
                Console.WriteLine($"{DateTime.Now:HH:mm} [Dit] {rolePrefix}{charName}: \"{message}\"");
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcMessageChannel), WriteMmoString(charName), WriteMmoString(message), ToBytes(talkAsGM));
                // @TODO : optimiser en n'envoyant qu'au serveur approprié
                foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
                {
                    serverConn.Send(msg);
                }
            }

            // Global correspond au canal 1
            if (channel == 1)
            {
                string rolePrefix = talkAsGM ? sender.GetRolePrefix() : "";
                Console.WriteLine($"{DateTime.Now:HH:mm} [Global] {rolePrefix}{charName}: \"{message}\"");

                // Envoyer le préfixe séparément
                byte[] msg = MergeByteArrays(
                    ToBytes(RpcType.RpcMessageChannel),
                    ToBytes(channel),
                    WriteMmoString(charName),
                    WriteMmoString(message),
                    ToBytes(talkAsGM),
                    WriteMmoString(rolePrefix) // NOUVEAU
                );

                var players = Server!.GameLogic.GetAllPlayerConnections();
                foreach (var player in players)
                {
                    player.Send(msg);
                }
            }
        }
    }
}