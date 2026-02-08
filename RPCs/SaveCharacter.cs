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


            // Écrasé le Title selon les permissions avant sauvegarde en BDD
            var player = Server!.GameLogic.GetPlayerById(charId);
            if (player != null)
            {
                var jsonObject = JObject.Parse(serializedCharacter);

                // Ajoute du prefix dans le Json
                
                jsonObject["Stats"]!["prefix"] = player.Permissions switch
                {
                    >= 10 => "MOD",
                    _ => ""
                };

                jsonObject["Stats"]!["title"] = player.Permissions switch
                {
                    >= 11 => "Fondateur",
                    >= 10 => "Administrateur",
                    >= 6 => "Modérateur",
                    >= 5 => "Maître du jeu",
                    _ => ""
                };
                serializedCharacter = jsonObject.ToString();
            }

            await Server!.Database.SaveCharacter(charId, serializedCharacter);
        }
    }
}