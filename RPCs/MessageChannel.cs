namespace PersistenceServer.RPCs
{
    public class MessageChannel : BaseRpc
    {
        public MessageChannel()
        {
            RpcType = RpcType.RpcMessageChannel;
        }

        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            int channel = reader.ReadInt32();
            string message = reader.ReadMmoString();
            bool talkAsGM = reader.ReadBoolean();
            int maxLength = 255;
            message = message.Length <= maxLength ? message : message[..maxLength];
            Server!.Processor.ConQ.Enqueue(() => ProcessMessage(channel, message, talkAsGM, connection));
        }

        private void ProcessMessage(int channel, string message, bool talkAsGM, UserConnection connection)
        {
            var sender = Server!.GameLogic.GetPlayerByConnection(connection);
            if (sender == null) return;
            var charName = sender.Name;

            if (talkAsGM && !sender.IsGm())
            {
                talkAsGM = false;
            }

            string rolePrefix = talkAsGM ? sender.GetRolePrefix() : "";

            // LOG DE DEBUG - à supprimer après test
            Console.WriteLine($"DEBUG: talkAsGM={talkAsGM}, Permissions={sender.Permissions}, Prefix='{sender.Prefix}', rolePrefix='{rolePrefix}'");

            // Say (canal 0)
            if (channel == 0)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [Dit] {rolePrefix}{charName}: \"{message}\"");

                byte[] msg = MergeByteArrays(
                    ToBytes(RpcType.RpcMessageChannel),
                    ToBytes(channel),
                    WriteMmoString(charName),
                    WriteMmoString(message),
                    ToBytes(talkAsGM),
                    WriteMmoString(rolePrefix)
                );

                foreach (var serverConn in Server!.GameLogic.GetAllServerConnections())
                {
                    serverConn.Send(msg);
                }
            }

            // Global (canal 1)
            if (channel == 1)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} [Global] {rolePrefix}{charName}: \"{message}\"");

                byte[] msg = MergeByteArrays(
                    ToBytes(RpcType.RpcMessageChannel),
                    ToBytes(channel),
                    WriteMmoString(charName),
                    WriteMmoString(message),
                    ToBytes(talkAsGM),
                    WriteMmoString(rolePrefix)
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
