using System;
using System.Collections.Generic;
using System.Linq;

namespace PersistenceServer
{
    /// <summary>
    /// Gère le système de niveaux et d'expérience pour les personnages
    /// </summary>
    public class LevelSystem
    {
        private readonly Dictionary<int, long> _levelRequirements;
        public const int MAX_LEVEL = 65;
        public const int MIN_LEVEL = 1;

        public LevelSystem()
        {
            _levelRequirements = new Dictionary<int, long>();
            InitializeLevelRequirements();
        }

        /// <summary>
        /// Initialise les prérequis d'expérience pour chaque niveau
        /// Formule: expérience requise = 100 * (niveau^2) + 50 * niveau
        /// </summary>
        private void InitializeLevelRequirements()
        {
            for (int level = 1; level <= MAX_LEVEL; level++)
            {
                long expRequired = CalculateExperienceForLevel(level);
                _levelRequirements[level] = expRequired;
            }
        }

        /// <summary>
        /// Calcule l'expérience totale requise pour atteindre un niveau donné
        /// </summary>
        private long CalculateExperienceForLevel(int level)
        {
            if (level == 1) return 0;

            // Formule progressive: chaque niveau nécessite plus d'expérience
            long baseExp = 100;
            long expRequired = 0;

            for (int i = 2; i <= level; i++)
            {
                expRequired += baseExp * (i - 1) * (i - 1) + 50 * (i - 1);
            }

            return expRequired;
        }

        /// <summary>
        /// Obtient l'expérience totale requise pour atteindre le niveau suivant
        /// </summary>
        public long GetExperienceRequiredForLevel(int level)
        {
            if (level < MIN_LEVEL || level > MAX_LEVEL)
                return 0;

            return _levelRequirements.TryGetValue(level, out long exp) ? exp : 0;
        }

        /// <summary>
        /// Calcule le nouveau niveau et l'expérience restante après avoir gagné de l'expérience
        /// </summary>
        public (int newLevel, long remainingExp, long expToNextLevel) CalculateLevel(int currentLevel, long currentExp, long expGained)
        {
            if (currentLevel >= MAX_LEVEL)
                return (MAX_LEVEL, currentExp, 0);

            long totalExp = currentExp + expGained;
            int newLevel = currentLevel;

            // Vérifier si on monte de niveau
            while (newLevel < MAX_LEVEL)
            {
                long expRequiredForNext = GetExperienceRequiredForLevel(newLevel + 1);
                if (totalExp >= expRequiredForNext)
                {
                    newLevel++;
                }
                else
                {
                    break;
                }
            }

            // Calculer l'expérience restante dans le niveau actuel
            long expForCurrentLevel = GetExperienceRequiredForLevel(newLevel);
            long expNeededForNext = newLevel < MAX_LEVEL ? GetExperienceRequiredForLevel(newLevel + 1) : totalExp;
            long remainingExp = totalExp;
            long expToNextLevel = expNeededForNext - expForCurrentLevel;

            return (newLevel, remainingExp, expToNextLevel);
        }

        /// <summary>
        /// Calcule le pourcentage de progression dans le niveau actuel
        /// </summary>
        public float GetLevelProgress(int level, long currentExp)
        {
            if (level >= MAX_LEVEL)
                return 100f;

            long expForCurrentLevel = GetExperienceRequiredForLevel(level);
            long expRequiredForNext = GetExperienceRequiredForLevel(level + 1);
            long expInCurrentLevel = currentExp - expForCurrentLevel;
            long expNeededForLevel = expRequiredForNext - expForCurrentLevel;

            if (expNeededForLevel <= 0)
                return 100f;

            return (float)expInCurrentLevel / expNeededForLevel * 100f;
        }

        /// <summary>
        /// Vérifie si le personnage a monté de niveau
        /// </summary>
        public bool HasLeveledUp(int oldLevel, int newLevel)
        {
            return newLevel > oldLevel && newLevel <= MAX_LEVEL;
        }

        /// <summary>
        /// Obtient le nombre de niveaux gagnés
        /// </summary>
        public int GetLevelsGained(int oldLevel, int newLevel)
        {
            if (newLevel <= oldLevel || newLevel > MAX_LEVEL)
                return 0;

            return Math.Min(newLevel - oldLevel, MAX_LEVEL - oldLevel);
        }
    }

    /// <summary>
    /// Informations sur le niveau et l'expérience d'un personnage
    /// </summary>
    public class CharacterLevelInfo
    {
        public int CharacterId { get; set; }
        public int Level { get; set; }
        public long Experience { get; set; }
        public long ExperienceToNextLevel { get; set; }

        public CharacterLevelInfo(int charId, int level, long exp, long expToNext)
        {
            CharacterId = charId;
            Level = level;
            Experience = exp;
            ExperienceToNextLevel = expToNext;
        }
    }
}