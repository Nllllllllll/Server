using Newtonsoft.Json.Linq;

namespace PersistenceServer.RPCs
{
    public class SaveCharacter : BaseRpc
    {
        public SaveCharacter()
        {
            RpcType = RpcType.RpcSaveCharacter;
        }

        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            int charId = reader.ReadInt32();
            string serialized = reader.ReadMmoString();
            Server!.Processor.ConQ.Enqueue(async () => await ProcessSaveCharacter(charId, serialized, connection));
        }

        private async Task ProcessSaveCharacter(int charId, string serializedCharacter, UserConnection conn)
        {
            if (!Server!.GameLogic.IsServer(conn))
            {
                Console.WriteLine("Action illégale : aucun serveur n'a tenté de SaveCharacter RPC. Cela ne doit jamais se produire : enquêtez si cela se produit.");
                return;
            }

            var player = Server!.GameLogic.GetPlayerById(charId);
            if (player == null)
            {
                Console.WriteLine($"SaveCharacter: personnage {charId} non trouvé");
                return;
            }

            // Calcul du prefix et du title selon les permissions
            var prefix = player.Permissions switch
            {
                >= 10 => "MOD",
                _ => ""
            };

            var title = player.Permissions switch
            {
                >= 11 => "Fondateur",
                >= 10 => "Administrateur",
                >= 6 => "Modérateur",
                >= 5 => "Maître du jeu",
                _ => ""
            };

            player.Prefix = prefix;
            player.Title = title;

            // Parser le JSON sans modifier prefix/title dedans
            var charInfo = DatabaseCharacterInfo.FromSerializedJson(
                serializedCharacter,
                player.AccountId,
                charId,
                player.Name,
                player.Permissions,
                player.GuildId == -1 ? null : player.GuildId,
                player.GuildRank == -1 ? null : player.GuildRank,
                prefix
            );

            // Sauvegarder avec les colonnes individuelles
            await Server!.Database.SaveCharacter(charId, charInfo);
        }
    }
}
