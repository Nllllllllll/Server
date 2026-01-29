namespace PersistenceServer.RPCs
{
    internal class PartyInvite : BaseRpc
    {
        public PartyInvite()
        {
            RpcType = RpcType.RpcPartyInvite; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string charName = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(charName, connection));
        }

        // 0. vérifier si l'expéditeur a l'autorité pour inviter dans un groupe (il est soit chef de groupe, soit sans groupe)  
        // 1. vérifier si le joueur cible est en ligne  
        // 2. vérifier si le joueur cible n'a pas de groupe  
        // 3. vérifier que le joueur cible n'est pas l'expéditeur lui-même  
        // 4. vérifier si le joueur cible a déjà une invitation en attente  
        // 5. envoyer l'invitation au joueur si toutes les conditions sont remplies  
        // 6. envoyer une confirmation à l'expéditeur si toutes les conditions sont remplies  
        // N.B. Lors de l'envoi de RpcPartyInvite aux clients, le premier octet (un booléen) vaut true lorsqu'on envoie au destinataire de l'invitation, et false lorsqu'on renvoie à l'expéditeur de l'invitation
        private void ProcessMessage(string recipientName, UserConnection senderConn)
        {
            var sender = Server!.GameLogic.GetPlayerByConnection(senderConn);
            if (sender == null) return; // le joueur a pu se déconnecter entre-temps, donc on abandonne l'opération

            if (sender.PartyRef != null)
            {                
                if (sender.PartyRef.PartyLeaderId != sender.CharId)
                {
                    // pas d'autorité pour inviter, vous n'êtes pas le chef du groupe
                    Console.WriteLine($"{DateTime.Now:HH:mm} Invitation à un groupe par {sender.Name} a {recipientName} échec : le joueur n'a aucune autorité pour inviter");
                    byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcPartyInvite), ToBytes(false), ToBytes(0)); // 0 est un message indiquant « vous n'avez pas l'autorité »
                    senderConn.Send(msgFail);
                    return;
                }

                // impossible d'inviter, le groupe est complet
                if (sender.PartyRef.IsPartyFull())
                {
                    // impossible d'inviter, vous avez atteint le nombre maximum de membres
                    Console.WriteLine($"{DateTime.Now:HH:mm} Invitation à un groupe par {sender.Name} a {recipientName} échec : Groupe complète");
                    byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcPartyInvite), ToBytes(false), ToBytes(1)); // 1 est un message indiquant « groupe complet »
                    senderConn.Send(msgFail);
                    return;
                }
            }

            // tentative de trouver le destinataire en ligne
            var recipient = Server!.GameLogic.GetPlayerByName(recipientName);
            if (recipient == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Invitation à un groupe par {sender.Name} a {recipientName} échec : aucun joueur trouvé");
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcNoSuchPlayer)); // envoyer « joueur introuvable »
                senderConn.Send(msgFail);
                return;
            }

            // impossible de s'inviter soi-même dans un groupe
            if (recipient == sender)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Invitation à un groupe par {sender.Name} a {recipientName} échec : impossible de s'inviter soi-même");
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcNoSuchPlayer)); // envoyer « joueur introuvable »
                senderConn.Send(msgFail);
                return;
            }

            // vérifier si le destinataire n'a pas de groupe (sans groupe)
            if (recipient.PartyRef != null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Invitation à un groupe par {sender.Name} a {recipientName} échec : le joueur est déjà dans le groupe");
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcPartyInvite), ToBytes(false), ToBytes(2)); // 2 est un message indiquant « le joueur est déjà dans un groupe »
                senderConn.Send(msgFail);
                return;
            }

            // vérifier si le destinataire n'est pas occupé (a une invitation en attente)
            if (recipient.HasPendingInvite())
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Invitation à un groupe par {sender.Name} a {recipientName} échec : le joueur a une invitation en attente");
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcPartyInvite), ToBytes(false), ToBytes(3)); // 3 est un message indiquant « le joueur est occupé »
                senderConn.Send(msgFail);
                return;
            }

            Console.WriteLine($"{DateTime.Now:HH:mm} {sender.Name} invites {recipientName} au groupe.");
            recipient.SetInviteToParty(sender.Name, sender.PartyId);

            byte[] msgToInvitee = MergeByteArrays(ToBytes(RpcType.RpcPartyInvite), ToBytes(true), WriteMmoString(sender.Name));
            recipient.Conn.Send(msgToInvitee);            

            byte[] msgToSender = MergeByteArrays(ToBytes(RpcType.RpcPartyInvite), ToBytes(false), ToBytes(4)); // 4 est un message indiquant « invitation envoyée avec succès »
            senderConn.Send(msgToSender);
        }
    }
}