namespace PersistenceServer.RPCs
{
    public class TeleportToPlayer : BaseRpc
    {
        public TeleportToPlayer()
        {
            RpcType = RpcType.RpcTeleportToPlayer; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string targetPlayerName = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(() => ProcessTeleport(targetPlayerName, connection));
        }

        private void ProcessTeleport(string targetPlayerName, UserConnection senderConn)
        {
            var sender = Server!.GameLogic.GetPlayerByConnection(senderConn);
            if (sender == null) return; // le joueur a pu se déconnecter entre-temps, donc on abandonne l'opération

            // Nettoyer le nom du joueur cible (enlever espaces)
            targetPlayerName = targetPlayerName.Trim();

            // DEBUG: Afficher tous les joueurs en ligne
            Console.WriteLine($"{DateTime.Now:HH:mm} [Téléport DEBUG] Joueurs en ligne:");
            var allPlayers = Server!.GameLogic.GetAllPlayerConnections();
            foreach (var conn in allPlayers)
            {
                var p = Server!.GameLogic.GetPlayerByConnection(conn);
                if (p != null)
                {
                    Console.WriteLine($"  - '{p.Name}' (CharId: {p.CharId}, Permissions: {p.Permissions})");
                }
            }
            Console.WriteLine($"{DateTime.Now:HH:mm} [Téléport DEBUG] Recherche de: '{targetPlayerName}'");

            // Vérifier les permissions (4 ou supérieur)
            if (sender.Permissions < 4)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [Téléport] {sender.Name} a tenté de se téléporter sans permissions suffisantes (Permissions: {sender.Permissions})");
                byte[] msgNoPermission = MergeByteArrays(ToBytes(RpcType.RpcTeleportToPlayer), ToBytes(false), ToBytes(0)); // 0 = pas de permission
                senderConn.Send(msgNoPermission);
                return;
            }

            // Vérifier que le joueur cible existe et est en ligne
            // Recherche insensible à la casse
            var targetPlayer = Server!.GameLogic.GetPlayerByName(targetPlayerName);
            if (targetPlayer == null)
            {
                // Essayer une recherche insensible à la casse
                targetPlayer = Server!.GameLogic.GetPlayerByNameCaseInsensitive(targetPlayerName);
            }

            if (targetPlayer == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [Téléport] {sender.Name} a tenté de se téléporter vers '{targetPlayerName}' mais le joueur n'est pas en ligne");
                byte[] msgPlayerNotFound = MergeByteArrays(ToBytes(RpcType.RpcNoSuchPlayer));
                senderConn.Send(msgPlayerNotFound);
                return;
            }

            Console.WriteLine($"{DateTime.Now:HH:mm} [Téléport DEBUG] Joueur trouvé: '{targetPlayer.Name}' (CharId: {targetPlayer.CharId})");

            // Vérifier que le joueur ne se téléporte pas vers lui-même
            if (sender.CharId == targetPlayer.CharId)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [Téléport] {sender.Name} a tenté de se téléporter vers lui-même");
                byte[] msgSelfTeleport = MergeByteArrays(ToBytes(RpcType.RpcTeleportToPlayer), ToBytes(false), ToBytes(1)); // 1 = téléportation vers soi-même
                senderConn.Send(msgSelfTeleport);
                return;
            }

            // Récupérer le serveur où se trouve le joueur cible
            var targetServer = Server!.GameLogic.GetPlayerServer(targetPlayer.CharId);
            if (targetServer == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [Téléport] {sender.Name} a tenté de se téléporter vers {targetPlayerName} mais le serveur du joueur est introuvable");
                byte[] msgServerNotFound = MergeByteArrays(ToBytes(RpcType.RpcTeleportToPlayer), ToBytes(false), ToBytes(2)); // 2 = serveur introuvable
                senderConn.Send(msgServerNotFound);
                return;
            }

            Console.WriteLine($"{DateTime.Now:HH:mm} [Téléport] {sender.Name} se téléporte vers {targetPlayerName}");

            // Envoyer une commande de téléportation au serveur où se trouve l'expéditeur
            // Le serveur devra gérer la téléportation côté gameplay
            // Paramètres : CharId de l'expéditeur, CharId du joueur cible, succès
            byte[] msgToServer = MergeByteArrays(
                ToBytes(RpcType.RpcTeleportToPlayer), 
                ToBytes(sender.CharId), 
                ToBytes(targetPlayer.CharId),
                ToBytes(true) // succès
            );
            
            // Trouver le serveur où se trouve l'expéditeur et lui envoyer le message
            var senderServer = Server!.GameLogic.GetPlayerServer(sender.CharId);
            if (senderServer != null)
            {
                senderServer.Conn.Send(msgToServer);
            }

            // Optionnel : envoyer une confirmation au joueur qui se téléporte
            byte[] msgToSender = MergeByteArrays(
                ToBytes(RpcType.RpcTeleportToPlayer), 
                ToBytes(true),
                ToBytes(3), // 3 = téléportation en cours
                WriteMmoString(targetPlayerName)
            );
            senderConn.Send(msgToSender);

            // Optionnel : notifier le joueur cible
            byte[] msgToTarget = MergeByteArrays(
                ToBytes(RpcType.RpcTeleportToPlayer),
                ToBytes(true),
                ToBytes(4), // 4 = un joueur se téléporte vers vous
                WriteMmoString(sender.Name)
            );
            targetPlayer.Conn.Send(msgToTarget);
        }
    }
}
