namespace PersistenceServer
{
    public enum RpcType { 
        RpcUndef, // 0
        RpcConnected, // 1
        RpcDisconnected, // 2
        RpcCreateAccountPassword, //3
        RpcCreateAccountSteam, // 4
        RpcLoginPassword, // 5
        RpcLoginSteam, // 6
        RpcMessageChannel, // 7
        RpcMessagePlayer, // 8
        RpcMessageParty, // 9
        RpcMessageGuild, // 10
        RpcUnused1, // 11
        RpcUnused2, // 12
        RpcUnused3, // 13
        RpcAcceptInvite, // 14
        RpcDeclineInvite, // 15
        RpcGuildCreate, // 16
        RpcGuildInvite, // 17
        RpcGuildDisband, // 18
        RpcGuildLeave, // 19
        RpcCreateCharacter, // 20
        RpcGetCharacters, // 21
        RpcGetIpAndPort, // 22
        RpcGetCharacter, // 23
        RpcLoginServer, // 24
        RpcLoginClientWithCookie, // 25
        RpcSaveCharacter, // 26
        RpcNoSuchPlayer, // 27
        RpcKeepAliveProbe, // 28
        RpcAdminMessage, // 29
        /* Explication de RpcGuildMemberUpdate :
        * Pour les serveurs de jeu : quand un membre rejoint ou quitte une guilde, ou simplement se connecte, nous informons tous les serveurs de jeu de l'id et du nom de sa guilde
        * Pour les clients de jeu : quand un membre change de rang ou de statut en ligne */
        RpcGuildMemberUpdate, // 30
        RpcGuildAllMembersUpdate, // 31 -- quand on envoie une liste complète des joueurs dans la guilde avec leurs rôles et statut en ligne
        RpcGuildMemberJoined, // 32 -- un joueur a rejoint votre guilde
        RpcDeleteCharacter, // 33
        RpcGuildKick, // 34
        RpcMessageGuildOfficer, // 35
        RpcGuildAdjustRank, // 36
        RpcSaveServerInfo, // 37
        RpcSavePersistentObject, // 38
        RpcForceDisconnectPlayer, // 39
        RpcPartyInvite, // 40
        RpcPartyLeave, // 41
        RpcPartyChangeLeader, // 42
        RpcPartyKick, // 43
        RpcPartyDisband, // 44
        RpcPartyFullInfo, // 45
        RpcPartyJoin, // 46
        RpcPartyMembersSync, // 47
    }
}
