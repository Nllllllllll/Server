namespace PersistenceServer.RPCs
{
    public class MoveToMe : BaseRpc
    {
        public MoveToMe()
        {
            RpcType = RpcType.RpcMoveToMe;
        }

        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string targetPlayerName = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(() => ProcessMoveToMe(targetPlayerName, connection));
        }

        private void ProcessMoveToMe(string targetPlayerName, UserConnection senderConn)
        {
            var sender = Server!.GameLogic.GetPlayerByConnection(senderConn);
            if (sender == null) return;

            targetPlayerName = targetPlayerName.Trim();

            Console.WriteLine($"{DateTime.Now:HH:mm} [MoveToMe DEBUG] {sender.Name} veut téléporter '{targetPlayerName}' vers lui");

            // Vérifier les permissions (4 ou supérieur)
            if (sender.Permissions < 4)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [MoveToMe] {sender.Name} n'a pas les permissions (Permissions: {sender.Permissions})");
                return;
            }

            // Trouver le joueur cible
            var targetPlayer = Server!.GameLogic.GetPlayerByName(targetPlayerName);
            if (targetPlayer == null)
            {
                targetPlayer = Server!.GameLogic.GetPlayerByNameCaseInsensitive(targetPlayerName);
            }

            if (targetPlayer == null)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [MoveToMe] Joueur '{targetPlayerName}' non trouvé");
                byte[] msgPlayerNotFound = MergeByteArrays(ToBytes(RpcType.RpcNoSuchPlayer));
                senderConn.Send(msgPlayerNotFound);
                return;
            }

            if (sender.CharId == targetPlayer.CharId)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [MoveToMe] {sender.Name} a tenté de se téléporter vers lui-même");
                return;
            }

            Console.WriteLine($"{DateTime.Now:HH:mm} [MoveToMe] {sender.Name} téléporte {targetPlayer.Name} vers lui");

            // Envoyer au GameServer
            // targetPlayer.CharId = celui qui sera téléporté
            // sender.CharId = la destination (l'admin)
            byte[] msgToServer = MergeByteArrays(
                ToBytes(RpcType.RpcMoveToMe),
                ToBytes(targetPlayer.CharId),  // Celui qui sera téléporté
                ToBytes(sender.CharId),         // Destination (l'admin)
                ToBytes(true)
            );

            var senderServer = Server!.GameLogic.GetPlayerServer(sender.CharId);
            if (senderServer != null)
            {
                senderServer.Conn.Send(msgToServer);
                Console.WriteLine($"{DateTime.Now:HH:mm} [MoveToMe] Message envoyé au GameServer");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [MoveToMe] Erreur: GameServer introuvable");
            }
        }
    }
}
