using System;

namespace PersistenceServer.RPCs
{
    public class LoginServer : BaseRpc
    {
        public LoginServer()
        {
            RpcType = RpcType.RpcLoginServer; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string pw = reader.ReadMmoString();
            int port = reader.ReadInt32();
            string level = reader.ReadMmoString();
            string zone = reader.ReadMmoString();
            if (Server!.Settings.ServerPassword == pw)
            {
#if DEBUG
                Console.WriteLine($"{DateTime.Now:HH:mm} Serveur connecté. Carte: {level}, IP: 127.0.0.1 (en raison de DEBUG), Port: {port}");
#else
                Console.WriteLine($"{DateTime.Now:HH:mm} Server logged in. Map: {level}, IP: {connection.Ip}, Port: {port}");
#endif
                Server!.Processor.ConQ.Enqueue(async () => await ProcessLoginServer(port, level, zone, connection));
            }
            else
            {
                Console.WriteLine($"Refus de connexion au serveur : mot de passe incorrect. Le serveur a essayé: {pw}");
                _ = connection.Disconnect(); // non attendu (non awaited)
            }
        }

        private async Task ProcessLoginServer(int port, string level, string zone, UserConnection conn)
        {
            GameServer newServer = new GameServer(port, level, zone, conn);
            Server!.GameLogic.ServerConnected(conn, newServer);

            // récupérer tous les objets persistants
            var persistentObjects = await Server!.Database.GetPersistentObjects(port, level);

            // récupérer les données sérialisées du serveur et les renvoyer au serveur
            string? serverInfo = await Server!.Database.GetServerInfo(port, level);

            Console.WriteLine("Serveur connecté, attribution du Guid: " + newServer.Conn.Id.ToString());
            // envoyer le message de connexion
            byte[] loginMsg = MergeByteArrays(ToBytes(RpcType.RpcLoginServer), WriteMmoString(newServer.Conn.Id.ToString()), WriteMmoString(serverInfo ?? ""), ToBytes(persistentObjects.Count)) ;
            conn.Send(loginMsg);

            // envoyer tous les objets persistants de la base de données au serveur identifié par son port et son niveau          
            foreach (var obj in persistentObjects)
            {
                byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcSavePersistentObject), ToBytes(obj.ObjectId), WriteMmoString(obj.JsonString));
                conn.Send(msg);
            }
        }
    }
}