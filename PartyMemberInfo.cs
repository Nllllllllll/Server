using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistenceServer
{
    // ceci est l'info que le GameServer envoie au Persistence Server toutes les quelques secondes
    // on renvoie cette info aux autres membres du groupe quand ils la demandent (ce qu'ils font aussi toutes les quelques secondes)
    // voir Party dans la documentation
    public class PartyMemberInfo
    {
        public string Name;
        public int CurHp;
        public int MaxHp;

        public PartyMemberInfo(string inName)
        {
            Name = inName;
            CurHp = 1;
            MaxHp = 1;
        }
    }
}
