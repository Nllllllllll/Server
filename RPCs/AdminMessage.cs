using System.Threading.Channels;

namespace PersistenceServer.RPCs
{
    internal class AdminMessage : BaseRpc
    {
        public AdminMessage()
        {
            RpcType = RpcType.RpcAdminMessage; // définis-le sur le type de Rpc que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis mets en file d'attente une Action dans la queue concurrente server.Processor.ConQ
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string message = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(message, connection));
        }

        private void ProcessMessage(string message, UserConnection connection)
        {
            bool isServerMessage = Server!.GameLogic.GetAllServerConnections().Contains(connection);
            if (!isServerMessage) return;

            Console.WriteLine($"{DateTime.Now:HH:mm} [Admin Message]: \"{message}\"");
            byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcAdminMessage), WriteMmoString(message));
            var players = Server!.GameLogic.GetAllPlayerConnections();
            foreach (var player in players)
            {
                player.Send(msg);
            }
        }
    }
}