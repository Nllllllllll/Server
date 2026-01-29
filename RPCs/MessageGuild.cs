using System.Threading.Channels;

namespace PersistenceServer.RPCs
{
    internal class MessageGuild : BaseRpc
    {
        public MessageGuild()
        {
            RpcType = RpcType.RpcMessageGuild; // définis-le sur le RpcType que tu veux intercepter
        }

        // Lis le message depuis le lecteur, puis ajoute une Action dans la file d'attente concurrente server.Processor.ConQ  
        // Par exemple : Server!.Processor.ConQ.Enqueue(() => Console.WriteLine("comme ceci"));  
        // Consulte les autres RPC pour plus d'exemples.
        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            string message = reader.ReadMmoString();
            int maxLength = 255;
            // Tronquer le message à maxLength (255 caractères)
            message = message.Length <= maxLength ? message : message[..maxLength]; // .. est l'opérateur de plage (Range Operator) de C# 8.0 https://www.codeguru.com/csharp/c-8-0-ranges-and-indices-types/
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(message, connection));
        }

        private void ProcessMessage(string message, UserConnection connection)
        {
            var sender = Server!.GameLogic.GetPlayerByConnection(connection); // AJOUTÉ pour obtenir l'objet Player
            if (sender == null) return;

            var charName = sender.Name; // MODIFIÉ (était Server!.GameLogic.GetPlayerName(connection))

            var guild = Server!.GameLogic.GetPlayerGuild(connection);
            if (guild == null)
            {
                Console.WriteLine($"Joueur {charName} a tenté un message de guilde, mais n'est pas dans une guilde");
                return;
            }

            // NOUVEAU : Déterminer si le message doit afficher un préfixe de rôle
            bool isStaff = sender.IsGm();
            string rolePrefix = isStaff ? sender.GetRolePrefix() : "";

            Console.WriteLine($"{DateTime.Now:HH:mm} [Guilde ({guild.Id})] {rolePrefix}{charName}: \"{message}\"");
            // Le canal 5 est le canal de guilde, voir EChatMsgChannel dans UE5
            byte[] msg = MergeByteArrays(ToBytes(RpcType.RpcMessageChannel), ToBytes(5), WriteMmoString(charName), WriteMmoString(message), ToBytes(isStaff) /* MODIFIÉ */);
            var players = guild.GetOnlineMembers();
            foreach (var player in players)
            {
                player.Conn.Send(msg);
            }
        }
    }
}