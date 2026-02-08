using System;

namespace PersistenceServer.RPCs
{
    /// <summary>
    /// RPC appelé par les clients pour récupérer leurs informations de niveau
    /// </summary>
    public class GetLevel : BaseRpc
    {
        public GetLevel()
        {
            RpcType = RpcType.RpcGetLevel;
        }

        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            int charId = reader.ReadInt32();
            Server!.Processor.ConQ.Enqueue(async () => await ProcessGetLevel(charId, connection));
        }

        private async Task ProcessGetLevel(int charId, UserConnection conn)
        {
            // Vérifier que le joueur demande bien ses propres informations
            var player = Server!.GameLogic.GetPlayerByConnection(conn);
            if (player == null || player.CharId != charId)
            {
                Console.WriteLine($"GetLevel: tentative d'accès non autorisé aux infos de niveau du personnage {charId}");
                return;
            }

            // Récupérer les informations de niveau
            var levelInfo = await Server!.Database.GetCharacterLevel(charId);
            if (levelInfo == null)
            {
                Console.WriteLine($"GetLevel: personnage {charId} non trouvé");
                byte[] msgFail = MergeByteArrays(ToBytes(RpcType.RpcGetLevel), ToBytes(false));
                conn.Send(msgFail);
                return;
            }

            // Envoyer les informations au client
            byte[] msg = MergeByteArrays(
                ToBytes(RpcType.RpcGetLevel),
                ToBytes(true),
                ToBytes(levelInfo.Level),
                ToBytes(levelInfo.Experience),
                ToBytes(levelInfo.ExperienceToNextLevel)
            );
            conn.Send(msg);

            Console.WriteLine($"{DateTime.Now:HH:mm} GetLevel pour {player.Name}: niveau {levelInfo.Level}, XP {levelInfo.Experience}/{levelInfo.ExperienceToNextLevel}");
        }
    }
}
