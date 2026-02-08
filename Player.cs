using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersistenceServer
{
    public class Player
    {
        public string Name;
        public UserConnection Conn;
        public int CharId;
        public int AccountId;
        public int GuildId;
        public int GuildRank;
        public int Permissions;
        public string Prefix;  // NOUVEAU
        public string Title;  // NOUVEAU
        public int Level { get; set; } = 1;
        public long Experience { get; set; } = 0;
        public long ExperienceToNextLevel { get; set; } = 100;
        public Party? PartyRef;
        public string PartyId { get { return PartyRef != null ? PartyRef.Id : ""; } }

        private PendingInvitation? invite = null;

        public Player(UserConnection conn, DatabaseCharacterInfo dbPlayer)
        {
            Name = dbPlayer.Name;
            Conn = conn;
            AccountId = dbPlayer.AccountId;
            CharId = dbPlayer.CharId;
            GuildId = dbPlayer.Guild ?? -1;
            GuildRank = dbPlayer.GuildRank ?? -1;
            Permissions = dbPlayer.Permissions;
            Prefix = dbPlayer.Prefix ?? "";  // NOUVEAU
            Level = dbPlayer.Level;
            Experience = dbPlayer.Experience;
            ExperienceToNextLevel = dbPlayer.ExperienceToNextLevel;
        }

        public bool IsGm()
        {
            return Permissions >= 10; // MOD ou GM
        }

        public bool IsMod()
        {
            return Permissions == 10;
        }

        public bool IsAdmin()
        {
            return Permissions >= 11;
        }

        // NOUVELLE MÉTHODE
        public string GetRolePrefix()
        {
            return Prefix;
        }


        // si la dernière invitation est valide et a été émise il y a moins de 30 secondes, le joueur a une invitation en attente
        public bool HasPendingInvite()
        {
            return invite != null && (DateTime.Now - invite.timeInvited).TotalSeconds < 30;
        }

        public PendingInvitation? GetPendingInvite()
        {
            return invite;
        }

        public void SetInviteToGuild(string inviterName, int guildId)
        {
            invite = new GuildInvitation(inviterName, guildId);
        }

        public void SetInviteToParty(string inviterName, string partyId)
        {
            invite = new PartyInvitation(inviterName, partyId);
        }

        public void ClearPendingInvite()
        {
            invite = default;
        }
    }

    public abstract class PendingInvitation
    {
        public DateTime timeInvited = default; // la valeur par défaut est 01/01/0001 00:00:00
        public string inviterName = "";
    }

    public class GuildInvitation : PendingInvitation
    {
        public int guildId;
        public GuildInvitation(string inInviter, int inGuildId)
        {
            timeInvited = DateTime.Now;
            inviterName = inInviter;
            guildId = inGuildId;
        }
    }

    public class PartyInvitation : PendingInvitation
    {
        public string partyId; // l'id du groupe est une chaîne vide si l'invitant n'est pas encore dans un groupe
        public PartyInvitation(string inInviter, string inPartyId) {
            timeInvited = DateTime.Now;
            inviterName = inInviter;
            partyId = inPartyId;
        }
    }
}
