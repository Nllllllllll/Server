using Microsoft.AspNetCore.Hosting.Server;
using System.Net;

namespace PersistenceServer
{
    class Program
    {
        static async Task Main(/*string[] args*/)
        {
            // Lit depuis settings.ini
            SettingsReader settings = new();
            Database database;
            if (settings.SqlType == SqlType.MySql)
                database = new DatabaseMysql(settings);
            else if (settings.SqlType == SqlType.Sqlite)
                database = new DatabaseSqlite(settings);
            else
                throw new Exception("Type SQL non défini dans settings.ini");
            await database.CheckCreateDatabase(settings);

            // Créer un nouveau serveur basé sur TCP. IPAddress.Any = les gens peuvent se connecter depuis n'importe quelle ip.
            var server = new MmoWsServer(settings, database);
            int guildsTotal = await server.RequestGuilds();
            Console.WriteLine($"Guildes reçues: {guildsTotal}");
            server.Start();
            Console.WriteLine($"Le serveur a démarré sur le port: {settings.Port}");

            Console.WriteLine("Tapez « Q » pour quitter.");
            Console.WriteLine("Tappuyez sur '!' pour redémarrer le serveur.");            
            Console.WriteLine("Tapez « admin <message> » pour diffuser un message système à tous les joueurs.");

            // Effectuer la saisie de texte
            for (; ; )
            {
                string? line = Console.ReadLine();
                if (string.IsNullOrEmpty(line))
                    continue;

                if (line.ToLower() == "q")
                    break;

                // Redémarrer le serveur
                if (line == "!")
                {
                    Console.Write("Redémarrage du serveur...");
                    await server.Stop();
                    server = new MmoWsServer(settings, database);
                    guildsTotal = await server.RequestGuilds();
                    Console.WriteLine($"Guildes reçues: {guildsTotal}");
                    server.Start();
                    Console.WriteLine("Fait!");
                    continue;
                }

                if (line.StartsWith("admin ")) // Diffuser un message système à toutes les sessions
                {                    
                    server.BroadcastAdminMessage(line[6..]); // supprime "admin " du message
                    continue;
                }
            }

            // Arrêter le serveur
            Console.Write("Arrêt du serveur...");
            await server.Stop();
            Console.WriteLine("Fait!");
        }
    }
}