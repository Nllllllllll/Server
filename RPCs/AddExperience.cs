using System;

namespace PersistenceServer.RPCs
{
    /// <summary>
    /// RPC appelé par les serveurs de jeu pour ajouter de l'expérience à un personnage
    /// </summary>
    public class AddExperience : BaseRpc
    {
        public AddExperience()
        {
            RpcType = RpcType.RpcAddExperience;
        }

        protected override void ReadRpc(UserConnection connection, BinaryReader reader)
        {
            int charId = reader.ReadInt32();
            long expGained = reader.ReadInt64();
            
            Server!.Processor.ConQ.Enqueue(async () => await ProcessAddExperience(charId, expGained, connection));
        }

        private async Task ProcessAddExperience(int charId, long expGained, UserConnection conn)
        {
            // Vérifier que c'est un serveur qui fait la requête
            if (!Server!.GameLogic.IsServer(conn))
            {
                Console.WriteLine("Action illégale : un client (et non un serveur !) a tenté RpcAddExperience.");
                return;
            }

            // Vérifier que le gain d'expérience est valide
            if (expGained <= 0)
            {
                Console.WriteLine($"AddExperience: gain d'expérience invalide ({expGained}) pour le personnage {charId}");
                return;
            }

            // Récupérer les informations actuelles du personnage depuis la DB
            var levelInfo = await Server!.Database.GetCharacterLevel(charId);
            if (levelInfo == null)
            {
                Console.WriteLine($"AddExperience: personnage {charId} non trouvé");
                return;
            }

            // Calculer le nouveau niveau et l'expérience
            var levelSystem = new LevelSystem();
            var (newLevel, newExp, expToNext) = levelSystem.CalculateLevel(
                levelInfo.Level, 
                levelInfo.Experience, 
                expGained
            );

            int levelsGained = levelSystem.GetLevelsGained(levelInfo.Level, newLevel);
            bool hasLeveledUp = levelsGained > 0;

            // Mettre à jour dans la base de données
            await Server!.Database.UpdateCharacterLevel(charId, newLevel, newExp, expToNext);

            // Log
            if (hasLeveledUp)
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Personnage {charId}: +{expGained} XP, niveau {levelInfo.Level} → {newLevel} (+{levelsGained} niveau{(levelsGained > 1 ? "x" : "")})");
            }
            else
            {
                Console.WriteLine($"{DateTime.Now:HH:mm} Personnage {charId}: +{expGained} XP (niveau {newLevel}, {newExp}/{expToNext})");
            }

            // Envoyer la mise à jour au serveur qui a fait la demande
            byte[] msgToServer = MergeByteArrays(
                ToBytes(RpcType.RpcAddExperience),
                ToBytes(charId),
                ToBytes(newLevel),
                ToBytes(newExp),
                ToBytes(expToNext),
                ToBytes(hasLeveledUp),
                ToBytes(levelsGained)
            );
            conn.Send(msgToServer);

            // Si le joueur est en ligne, envoyer aussi la mise à jour à son client
            var playerConn = Server!.GameLogic.GetConnectionByCharId(charId);
            if (playerConn != null)
            {
                byte[] msgToClient = MergeByteArrays(
                    ToBytes(RpcType.RpcLevelUpdate),
                    ToBytes(newLevel),
                    ToBytes(newExp),
                    ToBytes(expToNext),
                    ToBytes(hasLeveledUp),
                    ToBytes(levelsGained)
                );
                playerConn.Send(msgToClient);
            }
        }
    }
}
