using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistenceServer
{
    public class GuildJson
    {
        [JsonProperty]
        public GuildMember[] Members { get; set; }
        public GuildJson(GuildMember[] members) { Members = members; }
    }
    public class GuildMember
    {
        [JsonProperty]
        public int Id { get; set; }
        [JsonProperty]
        public string MemberName { get; set; }
        [JsonProperty]
        public int GuildRank { get; set; }
        [JsonProperty]
        public bool Online { get; set; }
        public GuildMember(int id, string name, int guildRank, bool online)
        {
            Id = id;
            MemberName = name;
            GuildRank = guildRank;
            Online = online;            
        }
    }

    public class Guild
    {
        public int Id;
        public string Name;
        readonly Dictionary<int, GuildMember> Members;
        readonly HashSet<Player> OnlineMembers; // ça duplique l'information, mais c'est facile de récupérer les membres en ligne de cette façon sans itérer sur les collections à chaque fois

        public Guild(int id, string name)
        {
            Id = id;
            Name = name;
            Members = new();
            OnlineMembers = new();
        }

        public int MembersCount { get => Members.Count; }

        // ajoute un membre quand les guildes sont récupérées depuis la bdd au démarrage
        public void PopulateMember(int memberId, string memberName, int guildRank)
        {
            Members.Add(memberId, new GuildMember(memberId, memberName, guildRank, false));
        }

        public HashSet<Player> GetOnlineMembers()
        {
            return OnlineMembers;
        }

        public HashSet<Player> GetOnlineOfficers(int officerRankRequired)
        {
            return OnlineMembers.Where(x => x.GuildRank <= officerRankRequired).ToHashSet();
        }

        public void UpdateMemberRank(int charId, int newRank)
        {
            Members[charId].GuildRank = newRank;
        }

        public Dictionary<int, GuildMember> GetGuildMembers()
        {
            return Members;
        }

        public GuildMember? GetGuildMemberByName(string name)
        {
            foreach (var pair in Members)
            {
                if (pair.Value.MemberName == name) return pair.Value;
            }
            return null;
        }

        public void OnPlayerConnected(Player newPlayer)
        {
            Members[newPlayer.CharId].Online = true;
            OnlineMembers.Add(newPlayer);
        }

        public void OnPlayerDisconnected(Player player)
        {
            Members[player.CharId].Online = false;
            OnlineMembers.Remove(player);
        }

        public void RemoveMember(Player player)
        {
            Members.Remove(player.CharId);
            OnlineMembers.Remove(player);
        }

        // suppose que le joueur n'est pas en ligne, donc on a seulement besoin de le retirer de Members
        public void RemoveMemberById(int id)
        {
            Members.Remove(id);
        }

        public GuildMember[] GetAllMembers()
        {
            return Members.Values.ToArray();
        }

        public int GuildMastersCount
        {
            get
            {
                int result = 0;
                foreach (var member in Members)
                {
                    if (member.Value.GuildRank == 0)
                        result++;
                }
                return result;
            }
        }

        public string GetGuildMembersJson()
        {
            return JsonConvert.SerializeObject(new GuildJson(GetAllMembers()));
        }
    }
}
