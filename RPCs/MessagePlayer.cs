namespace PersistenceServer.RPCs
{
    public class MessagePlayer : BaseRpc
    {
        public MessagePlayer()
        {
            RpcType = RpcType.RpcMessagePlayer; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string recipient = reader.ReadMmoString();
            string message = reader.ReadMmoString();
            bool talkAsGM = reader.ReadBoolean();
            int maxLength = 255;
            // Tronquer le message à maxLength (255 caractères)
            message = message.Length <= maxLength ? message : message[..maxLength];
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(recipient, message, talkAsGM, connection));
        }

        private void ProcessMessage(string recipientName, string message, bool talkAsGM, UserConnection senderConn)
        {
            var sender = Server!.GameLogic.GetPlayerByConnection(senderConn);
            if (sender == null) return; // le joueur a pu se déconnecter entre-temps, donc on abandonne l'opération
            var senderName = sender.Name;

            // si talkAsGM est vrai mais que l'expéditeur n'est pas un MJ (Maître du Jeu), réinitialise simplement la valeur à false
            if (talkAsGM && !sender.IsGm())
            {
                talkAsGM = false;
            }
            string GM = talkAsGM ? "<GM>" : "";

            // tentative de trouver le destinataire
            var recipient = Server!.GameLogic.GetPlayerByName(recipientName);
            // si le destinataire est introuvable
            if (recipient == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [Privé (échec)] {GM}{senderName} à {recipientName}: \"{message}\"");
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcNoSuchPlayer));
                senderConn.Send(msgFail);
                return;
            }

            Console.WriteLine($"{DateTime.Now:HH:mm} [Privé] {GM}{senderName} à {recipientName}: \"{message}\"");

            byte[] msgToSender = MergeByteArrays(
                ToBytes(RpcType.RpcMessagePlayer), 
                ToBytes(true), // true pour signifier « c'est votre message »
                WriteMmoString(recipientName), 
                WriteMmoString(message),
                ToBytes(talkAsGM)
            );
            senderConn.Send(msgToSender);

            byte[] msgToRecipient = MergeByteArrays(
                ToBytes(RpcType.RpcMessagePlayer),
                ToBytes(false), // true pour signifier « ce n'est pas votre message »
                WriteMmoString(senderName),
                WriteMmoString(message),
                ToBytes(talkAsGM)
            );
            recipient.Conn.Send(msgToRecipient);            
        }
    }
}