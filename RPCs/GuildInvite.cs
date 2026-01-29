namespace PersistenceServer.RPCs
{
    internal class GuildInvite : BaseRpc
    {
        public GuildInvite()
        {
            RpcType = RpcType.RpcGuildInvite; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string charName = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(charName, connection));
        }

        // 0. vérifier si l'expéditeur a l'autorité pour inviter dans une guilde (est officier de guilde)  
        // 1. vérifier si le joueur cible est en ligne  
        // 2. vérifier si le joueur cible n'appartient à aucune guilde  
        // 3. vérifier si le joueur cible a déjà une invitation en attente  
        // 3. envoyer l'invitation au joueur si toutes les conditions sont remplies *(note : duplication du numéro d'étape dans le code original)*  
        // 4. envoyer une confirmation à l'expéditeur si toutes les conditions sont remplies
        private void ProcessMessage(string recipientName, UserConnection senderConn)
        {
            var sender = Server!.GameLogic.GetPlayerByConnection(senderConn);
            if (sender == null) return; // le joueur a pu se déconnecter entre-temps, donc on abandonne l'opération
            var guild = Server!.GameLogic.GetPlayerGuild(senderConn);
            if (guild == null) return;
            if (sender.GuildRank > Server.Settings.GuildOfficerRank) return; // seul un Officier peut inviter
            var senderName = sender.Name;

            // tentative de trouver le destinataire
            var recipient = Server!.GameLogic.GetPlayerByName(recipientName);
            if (recipient == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Invitation de guilde de {senderName} à {recipientName} échec : aucun joueur trouvé");
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcNoSuchPlayer)); // envoyer « aucun joueur de ce nom »
                senderConn.Send(msgFail);
                return;
            }

            if (recipient.GuildId != -1)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Invitation de guilde de {senderName} à {recipientName} échec : le joueur est déjà dans une guilde");
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcGuildInvite), ToBytes(false), ToBytes(0)); // envoyer « le joueur est déjà dans une guilde »
                senderConn.Send(msgFail);
                return;
            }

            if (recipient.HasPendingInvite())
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Invitation de guilde de {senderName} à {recipientName} échec : le joueur a une invitation en attente");
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcGuildInvite), ToBytes(false), ToBytes(1)); // envoyer « le joueur est occupé »
                senderConn.Send(msgFail);
                return;
            }

            Console.WriteLine($"{DateTime.Now:HH:mm} Invitation de guilde de {senderName} à {recipientName} a été envoyé");

            recipient.SetInviteToGuild(sender.Name, sender.GuildId);

            byte[] msgToRecipient = MergeByteArrays(ToBytes(RpcType.RpcGuildInvite), ToBytes(true), WriteMmoString(senderName), WriteMmoString(guild.Name)); // envoyer « X vous invite à rejoindre la guilde Y »
            recipient.Conn.Send(msgToRecipient);

            byte[] msgConfirmToSender = MergeByteArrays(ToBytes(RpcType.RpcGuildInvite), ToBytes(false), ToBytes(2)); // envoyer « invitation envoyée avec succès »
            senderConn.Send(msgConfirmToSender);
        }
    }
}